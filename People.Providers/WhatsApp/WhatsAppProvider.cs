using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using People.Core.Interfaces;
using People.Core.Interfaces.Repositories;
using People.Core.Models;
using People.Interop;

namespace People.Providers.WhatsApp;

public class WhatsAppProvider : IWhatsAppProvider
{
    public string PlatformId => "WhatsApp";
    public string DisplayName => "WhatsApp";
    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;

    public bool IsAuthenticated
    {
        get
        {
            if (IsTestingMode) return true;
            try
            {
                var dbPath = GetSessionDbPath();
                return WhatsAppNative.wa_is_authenticated(dbPath) == 1;
            }
            catch
            {
                return false;
            }
        }
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ChatUpdatedEventArgs>? ChatUpdated;
    public event EventHandler<string>? QrCodeReceived;
    public event EventHandler<int>? SyncProgressChanged;

    private IntPtr _clientPtr = IntPtr.Zero;
    private readonly ILogger<WhatsAppProvider>? _logger;
    private readonly ISettingsService? _settingsService;
    private readonly IChatRepository? _chatRepository;
    private readonly IMessageRepository? _messageRepository;
    private readonly IContactRepository? _contactRepository;

    // Static instance pointer for unmanaged C callbacks
    private static WhatsAppProvider? _activeInstance;

    // In-memory testing messages cache so sent messages persist during session
    private readonly List<Message> _testingMessages = new();

    public WhatsAppProvider(
        ILogger<WhatsAppProvider>? logger = null, 
        ISettingsService? settingsService = null,
        IChatRepository? chatRepository = null,
        IMessageRepository? messageRepository = null,
        IContactRepository? contactRepository = null)
    {
        _logger = logger;
        _settingsService = settingsService;
        _chatRepository = chatRepository;
        _messageRepository = messageRepository;
        _contactRepository = contactRepository;
        _activeInstance = this;
    }

    private bool IsTestingMode => _settingsService?.GetValue<bool>("WhatsAppTestingMode", false) ?? false;

    public static string GetSessionDbPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "People");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "whatsapp_session.db");
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (ConnectionState == ConnectionState.Connected && _clientPtr != IntPtr.Zero)
        {
            return;
        }

        if (IsTestingMode)
        {
            _logger?.LogInformation("WhatsApp is operating in Testing/Demo Mode.");
            await Task.Delay(50, ct);
            ConnectionState = ConnectionState.Connected;
            OnConnectionStateChanged(ConnectionState);
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                if (_clientPtr == IntPtr.Zero)
                {
                    _clientPtr = WhatsAppNative.wa_create_client();
                }
            }, ct);

            if (_clientPtr != IntPtr.Zero)
            {
                _logger?.LogInformation("Successfully created WhatsApp client backend.");
                var dbPath = GetSessionDbPath();

                unsafe
                {
                    WhatsAppNative.wa_register_message_callback(_clientPtr, &OnNativeMessageReceived);
                    WhatsAppNative.wa_register_sync_progress_callback(_clientPtr, &OnNativeSyncProgress);
                    WhatsAppNative.wa_register_chats_updated_callback(_clientPtr, &OnNativeChatsUpdated);
                }

                await Task.Run(() => WhatsAppNative.wa_load_cache(dbPath), ct);

                if (IsAuthenticated)
                {
                    _logger?.LogInformation("WhatsApp is already authenticated. Connected immediately.");
                    ConnectionState = ConnectionState.Connected;
                    OnConnectionStateChanged(ConnectionState);

                    // Run authentication in background to connect socket & sync
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await AuthenticateAsync(ct);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Background WhatsApp reconnect failed.");
                        }
                    }, ct);
                }
                else
                {
                    ConnectionState = ConnectionState.Connecting;
                    OnConnectionStateChanged(ConnectionState);
                }
            }
            else
            {
                _logger?.LogError("Failed to create WhatsApp native client. Pointer is zero.");
                ConnectionState = ConnectionState.Error;
                OnConnectionStateChanged(ConnectionState);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception connecting to WhatsApp backend.");
            ConnectionState = ConnectionState.Error;
            OnConnectionStateChanged(ConnectionState);
        }
    }

    public async Task AuthenticateAsync(CancellationToken ct = default)
    {
        if (IsTestingMode)
        {
            _logger?.LogInformation("Bypassing authentication in Testing Mode.");
            return;
        }

        if (_clientPtr == IntPtr.Zero)
        {
            _logger?.LogWarning("Attempted to authenticate without client pointer.");
            throw new InvalidOperationException("Client backend is not connected.");
        }

        try
        {
            var dbPath = GetSessionDbPath();
            _logger?.LogInformation("Authenticating WhatsApp with SQLite DB at '{DbPath}'", dbPath);

            await Task.Run(() =>
            {
                unsafe
                {
                    WhatsAppNative.wa_authenticate_qr(
                        _clientPtr,
                        dbPath,
                        &OnNativeQrReceived,
                        &OnNativeAuthSuccess,
                        &OnNativeError);
                }
            }, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception during WhatsApp authentication.");
            throw;
        }
    }

    public async Task ReauthenticateAsync(CancellationToken ct = default)
    {
        _logger?.LogInformation("Starting WhatsApp reauthentication: cleaning session and restarting QR flow.");
        await DisconnectAsync();

        try
        {
            var dbPath = GetSessionDbPath();
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm");
            if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal");
            var cachePath = dbPath + ".cache.json";
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clean some WhatsApp session files.");
        }

        await ConnectAsync(ct);
        await AuthenticateAsync(ct);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeError(IntPtr errPtr)
    {
        if (errPtr == IntPtr.Zero || _activeInstance == null) return;
        string? err = Marshal.PtrToStringUTF8(errPtr);
        _activeInstance._logger?.LogError("WhatsApp backend error: {Error}", err);
        _activeInstance.ConnectionState = ConnectionState.Error;
        _activeInstance.OnConnectionStateChanged(ConnectionState.Error);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeQrReceived(IntPtr qrStrPtr)
    {
        if (qrStrPtr == IntPtr.Zero || _activeInstance == null) return;
        string? qr = Marshal.PtrToStringUTF8(qrStrPtr);
        if (!string.IsNullOrEmpty(qr))
        {
            _activeInstance._logger?.LogInformation("Received QR string from WhatsApp backend.");
            _activeInstance.QrCodeReceived?.Invoke(_activeInstance, qr);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeAuthSuccess()
    {
        if (_activeInstance == null) return;
        _activeInstance._logger?.LogInformation("WhatsApp authenticated successfully.");
        _activeInstance.ConnectionState = ConnectionState.Connected;
        _activeInstance.OnConnectionStateChanged(ConnectionState.Connected);
        _activeInstance.ChatUpdated?.Invoke(_activeInstance, new ChatUpdatedEventArgs());
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeSyncProgress(int progress)
    {
        if (_activeInstance == null) return;
        _activeInstance._logger?.LogInformation("WhatsApp sync progress: {Progress}%", progress);
        _activeInstance.SyncProgressChanged?.Invoke(_activeInstance, progress);
    }

    private static readonly object _chatsUpdateLock = new();
    private static CancellationTokenSource? _chatsUpdateDebounceCts;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeChatsUpdated()
    {
        if (_activeInstance == null) return;
        _activeInstance._logger?.LogInformation("WhatsApp chats updated notification from backend.");

        lock (_chatsUpdateLock)
        {
            _chatsUpdateDebounceCts?.Cancel();
            _chatsUpdateDebounceCts?.Dispose();
            _chatsUpdateDebounceCts = new CancellationTokenSource();
            var token = _chatsUpdateDebounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(400, token);
                    if (token.IsCancellationRequested) return;

                    if (_activeInstance._chatRepository != null && _activeInstance._clientPtr != IntPtr.Zero)
                    {
                        var chats = await _activeInstance.FetchChatsFromNativeAsync(token);
                        if (chats.Count > 0 && !token.IsCancellationRequested)
                        {
                            await _activeInstance._chatRepository.SaveManyAsync(chats);
                        }
                    }

                    if (!token.IsCancellationRequested)
                    {
                        _activeInstance.ChatUpdated?.Invoke(_activeInstance, new ChatUpdatedEventArgs());
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _activeInstance._logger?.LogError(ex, "Failed to persist updated chats to SQLite repository.");
                }
            }, token);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeMessageReceived(IntPtr msgJsonPtr)
    {
        if (msgJsonPtr == IntPtr.Zero || _activeInstance == null) return;
        string? json = Marshal.PtrToStringUTF8(msgJsonPtr);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var reactionsList = new List<string>();
                if (root.TryGetProperty("reactions", out var rProp) && rProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in rProp.EnumerateArray())
                    {
                        var str = elem.GetString();
                        if (!string.IsNullOrEmpty(str)) reactionsList.Add(str);
                    }
                }

                string text = root.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? string.Empty : string.Empty;
                string mediaType = root.TryGetProperty("mediaType", out var mediaProp) ? mediaProp.GetString() ?? "text" : "text";

                // Filter out empty protocol / stub messages
                if (mediaType == "text" && string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                var msg = new Message
                {
                    Id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                    ChatId = root.TryGetProperty("chatId", out var chatProp) ? chatProp.GetString() ?? string.Empty : string.Empty,
                    SenderId = root.TryGetProperty("senderId", out var senderProp) ? senderProp.GetString() ?? string.Empty : string.Empty,
                    SenderName = root.TryGetProperty("senderName", out var sNameProp) ? sNameProp.GetString() : null,
                    PlatformId = "WhatsApp",
                    Content = new MessageContent { Text = text },
                    Timestamp = root.TryGetProperty("timestamp", out var tsProp) ? DateTimeOffset.FromUnixTimeSeconds(tsProp.GetInt64()) : DateTimeOffset.UtcNow,
                    Status = MessageStatus.Delivered,
                    IsOutgoing = root.TryGetProperty("isOutgoing", out var outProp) && outProp.GetBoolean(),
                    QuotedText = root.TryGetProperty("quotedText", out var qTextProp) ? qTextProp.GetString() : null,
                    QuotedSender = root.TryGetProperty("quotedSender", out var qSenderProp) ? qSenderProp.GetString() : null,
                    Reactions = reactionsList
                };

                // Persist to SQLite repository
                if (_activeInstance._messageRepository != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _activeInstance._messageRepository.SaveAsync(msg);
                            if (_activeInstance._chatRepository != null)
                            {
                                var existingChat = await _activeInstance._chatRepository.GetByIdAsync(msg.ChatId);
                                if (existingChat != null)
                                {
                                    existingChat.LastMessage = msg;
                                    existingChat.LastMessageTime = msg.Timestamp;
                                    await _activeInstance._chatRepository.SaveAsync(existingChat);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _activeInstance._logger?.LogError(ex, "Failed to persist live message to SQLite.");
                        }
                    });
                }

                _activeInstance.MessageReceived?.Invoke(_activeInstance, new MessageReceivedEventArgs(msg));
            }
            catch (Exception ex)
            {
                _activeInstance._logger?.LogError(ex, "Failed to parse incoming native message JSON.");
            }
        }
    }

    public async Task DisconnectAsync()
    {
        if (_clientPtr != IntPtr.Zero)
        {
            await Task.Run(() => WhatsAppNative.wa_destroy_client(_clientPtr));
            _clientPtr = IntPtr.Zero;
        }

        ConnectionState = ConnectionState.Disconnected;
        OnConnectionStateChanged(ConnectionState);
    }

    public async Task<IReadOnlyList<Chat>> GetChatsAsync(int offset = 0, int limit = 50, CancellationToken ct = default)
    {
        if (IsTestingMode)
        {
            return GetTestingChats();
        }

        // 1. Try SQLite first (instant startup, sub-millisecond retrieval)
        if (_chatRepository != null)
        {
            try
            {
                var localChats = await _chatRepository.GetByPlatformAsync(PlatformId, offset, limit);
                if (localChats.Count > 0)
                {
                    // Refresh in background if native bridge is connected
                    if (_clientPtr != IntPtr.Zero)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var remoteChats = await FetchChatsFromNativeAsync(ct);
                                if (remoteChats.Count > 0)
                                {
                                    await _chatRepository.SaveManyAsync(remoteChats);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogDebug(ex, "Background chat sync to SQLite completed with warning.");
                            }
                        }, ct);
                    }
                    return localChats;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read chats from SQLite repository.");
            }
        }

        if (_clientPtr == IntPtr.Zero)
        {
            return Array.Empty<Chat>();
        }

        var chats = await FetchChatsFromNativeAsync(ct);
        if (_chatRepository != null && chats.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try { await _chatRepository.SaveManyAsync(chats); } catch { }
            }, ct);
        }
        return chats;
    }

    private async Task<IReadOnlyList<Chat>> FetchChatsFromNativeAsync(CancellationToken ct = default)
    {
        if (_clientPtr == IntPtr.Zero) return Array.Empty<Chat>();

        return await Task.Run(() =>
        {
            var ptr = WhatsAppNative.wa_get_chats(_clientPtr);
            if (ptr == IntPtr.Zero) return (IReadOnlyList<Chat>)new List<Chat>();

            try
            {
                var json = Marshal.PtrToStringUTF8(ptr);
                if (string.IsNullOrEmpty(json)) return new List<Chat>();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dtos = JsonSerializer.Deserialize<List<NativeChatDto>>(json, options) ?? new List<NativeChatDto>();

                var chats = new List<Chat>();
                foreach (var dto in dtos)
                {
                    // Filter out phantom empty non-group chat stubs (no valid last message)
                    if (!dto.IsGroup && (dto.LastMessage == null || string.IsNullOrWhiteSpace(dto.LastMessage.Text)))
                    {
                        continue;
                    }
                    var chat = new Chat
                    {
                        Id = dto.Id ?? string.Empty,
                        PlatformId = "WhatsApp",
                        Title = dto.Title ?? string.Empty,
                        IsGroup = dto.IsGroup,
                        UnreadCount = dto.UnreadCount,
                        PictureUrl = dto.PictureUrl,
                        LastMessageTime = dto.LastMessageTimestamp > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(dto.LastMessageTimestamp)
                            : DateTimeOffset.UtcNow
                    };

                    if (dto.LastMessage != null && !string.IsNullOrWhiteSpace(dto.LastMessage.Text))
                    {
                        chat.LastMessage = new Message
                        {
                            Id = dto.LastMessage.Id ?? Guid.NewGuid().ToString(),
                            ChatId = dto.LastMessage.ChatId ?? chat.Id,
                            PlatformId = "WhatsApp",
                            SenderId = dto.LastMessage.SenderId ?? string.Empty,
                            SenderName = dto.LastMessage.SenderName,
                            IsOutgoing = dto.LastMessage.IsOutgoing,
                            Content = new MessageContent { Text = dto.LastMessage.Text ?? string.Empty },
                            Timestamp = dto.LastMessage.Timestamp > 0
                                ? DateTimeOffset.FromUnixTimeSeconds(dto.LastMessage.Timestamp)
                                : chat.LastMessageTime,
                            Status = dto.LastMessage.IsOutgoing ? MessageStatus.Sent : MessageStatus.Delivered,
                            QuotedText = dto.LastMessage.QuotedText,
                            QuotedSender = dto.LastMessage.QuotedSender,
                            Reactions = dto.LastMessage.Reactions ?? new()
                        };
                    }

                    chats.Add(chat);
                }

                return (IReadOnlyList<Chat>)chats;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to deserialize chats from native bridge.");
                return new List<Chat>();
            }
            finally
            {
                WhatsAppNative.wa_free_string(ptr);
            }
        }, ct);
    }

    private IReadOnlyList<Chat> GetTestingChats()
    {
        return new List<Chat>
        {
            new Chat
            {
                Id = "sample_wa_1",
                PlatformId = PlatformId,
                Title = "Sarah Connor",
                UnreadCount = 1,
                LastMessageTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastMessage = new Message
                {
                    Id = "msg_1",
                    ChatId = "sample_wa_1",
                    PlatformId = PlatformId,
                    SenderId = "sarah",
                    Content = new MessageContent { Text = "Hey! Is the Windows 11 People app ready?" },
                    Timestamp = DateTimeOffset.UtcNow.AddMinutes(-15),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                }
            },
            new Chat
            {
                Id = "sample_wa_2",
                PlatformId = PlatformId,
                Title = "Windows Dev Team",
                IsGroup = true,
                UnreadCount = 3,
                LastMessageTime = DateTimeOffset.UtcNow.AddHours(-1),
                LastMessage = new Message
                {
                    Id = "msg_3",
                    ChatId = "sample_wa_2",
                    PlatformId = PlatformId,
                    SenderId = "dev_alex",
                    Content = new MessageContent { Text = "The Fluent Design Mica backdrop looks amazing!" },
                    Timestamp = DateTimeOffset.UtcNow.AddMinutes(-20),
                    Status = MessageStatus.Read,
                    IsOutgoing = false
                }
            }
        };
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(string chatId, int offset = 0, int limit = 100, CancellationToken ct = default)
    {
        if (IsTestingMode)
        {
            var msgs = _testingMessages.FindAll(m => m.ChatId == chatId);
            if (msgs.Count > 0) return msgs;

            if (chatId == "sample_wa_1")
            {
                return new List<Message>
                {
                    new Message
                    {
                        Id = "msg_1",
                        ChatId = chatId,
                        PlatformId = PlatformId,
                        SenderId = "sarah",
                        Content = new MessageContent { Text = "Hey! Is the Windows 11 People app ready?" },
                        Timestamp = DateTimeOffset.UtcNow.AddMinutes(-15),
                        Status = MessageStatus.Read,
                        IsOutgoing = false
                    },
                    new Message
                    {
                        Id = "msg_2",
                        ChatId = chatId,
                        PlatformId = PlatformId,
                        SenderId = "me",
                        Content = new MessageContent { Text = "Yes, we just implemented the full WhatsApp provider and testing mode!" },
                        Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                        Status = MessageStatus.Delivered,
                        IsOutgoing = true
                    }
                };
            }

            return Array.Empty<Message>();
        }

        // 1. Try SQLite first (instant indexed retrieval)
        if (_messageRepository != null)
        {
            try
            {
                var localMsgs = await _messageRepository.GetByChatIdAsync(chatId, offset, limit);
                if (localMsgs.Count > 0)
                {
                    return localMsgs;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load messages from SQLite for {ChatId}", chatId);
            }
        }

        if (_clientPtr == IntPtr.Zero) return Array.Empty<Message>();

        // 2. Fallback to native bridge if SQLite has no messages for this chat yet
        var bridgeMsgs = await FetchMessagesFromNativeAsync(chatId, 0, limit, ct);
        if (_messageRepository != null && bridgeMsgs.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try { await _messageRepository.SaveManyAsync(bridgeMsgs); } catch { }
            }, ct);
        }
        return bridgeMsgs;
    }

    private async Task<IReadOnlyList<Message>> FetchMessagesFromNativeAsync(string chatId, long beforeTs, int limit, CancellationToken ct = default)
    {
        if (_clientPtr == IntPtr.Zero) return Array.Empty<Message>();

        return await Task.Run(() =>
        {
            var ptr = WhatsAppNative.wa_get_messages_paginated(_clientPtr, chatId, beforeTs, limit);
            if (ptr == IntPtr.Zero) return (IReadOnlyList<Message>)new List<Message>();

            try
            {
                var json = Marshal.PtrToStringUTF8(ptr);
                if (string.IsNullOrEmpty(json)) return new List<Message>();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dtos = JsonSerializer.Deserialize<List<NativeMessageDto>>(json, options) ?? new List<NativeMessageDto>();

                var messages = new List<Message>();
                foreach (var dto in dtos)
                {
                    // Filter empty protocol messages
                    if (dto.MediaType == "text" && string.IsNullOrWhiteSpace(dto.Text))
                    {
                        continue;
                    }

                    messages.Add(new Message
                    {
                        Id = dto.Id ?? Guid.NewGuid().ToString(),
                        ChatId = dto.ChatId ?? chatId,
                        PlatformId = "WhatsApp",
                        SenderId = dto.SenderId ?? string.Empty,
                        SenderName = dto.SenderName,
                        IsOutgoing = dto.IsOutgoing,
                        Content = new MessageContent { Text = dto.Text ?? string.Empty },
                        Timestamp = dto.Timestamp > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(dto.Timestamp)
                            : DateTimeOffset.UtcNow,
                        Status = dto.IsOutgoing ? MessageStatus.Sent : MessageStatus.Delivered,
                        QuotedText = dto.QuotedText,
                        QuotedSender = dto.QuotedSender,
                        Reactions = dto.Reactions ?? new()
                    });
                }

                return (IReadOnlyList<Message>)messages;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to deserialize messages from native bridge.");
                return new List<Message>();
            }
            finally
            {
                WhatsAppNative.wa_free_string(ptr);
            }
        }, ct);
    }

    public async Task<IReadOnlyList<Message>> GetOlderMessagesAsync(string chatId, int count = 25, DateTimeOffset? beforeTimestamp = null, CancellationToken ct = default)
    {
        if (IsTestingMode)
        {
            return Array.Empty<Message>();
        }

        if (string.IsNullOrEmpty(chatId))
        {
            return Array.Empty<Message>();
        }

        // 1. FIRST check SQLite! Do we already have older messages stored locally in SQLite?
        if (_messageRepository != null && beforeTimestamp.HasValue)
        {
            try
            {
                var localOlder = await _messageRepository.GetOlderMessagesBeforeAsync(chatId, beforeTimestamp.Value, count);
                if (localOlder.Count > 0)
                {
                    _logger?.LogInformation("Loaded {Count} older messages from local SQLite storage.", localOlder.Count);
                    return localOlder;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load older messages from SQLite for {ChatId}", chatId);
            }
        }

        if (_clientPtr == IntPtr.Zero)
        {
            return Array.Empty<Message>();
        }

        // 2. Check if native bridge in-memory cache has messages before beforeTimestamp
        long beforeUnix = beforeTimestamp?.ToUnixTimeSeconds() ?? 0;
        var nativeCached = await FetchMessagesFromNativeAsync(chatId, beforeUnix, count, ct);
        if (nativeCached.Count > 0)
        {
            if (_messageRepository != null)
            {
                _ = Task.Run(async () =>
                {
                    try { await _messageRepository.SaveManyAsync(nativeCached); } catch { }
                }, ct);
            }
            return nativeCached;
        }

        // 3. Local storage & bridge cache exhausted -> request on-demand PDO from phone
        int initialCount = await Task.Run(() => WhatsAppNative.wa_get_message_count(_clientPtr, chatId), ct);

        // Fire PDO on-demand request to the phone
        await Task.Run(() =>
        {
            WhatsAppNative.wa_fetch_older_messages(_clientPtr, chatId, count);
        }, ct);

        // Poll for phone response (up to 3 seconds)
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(200, ct);
            int currentCount = await Task.Run(() => WhatsAppNative.wa_get_message_count(_clientPtr, chatId), ct);
            if (currentCount > initialCount)
            {
                break;
            }
        }

        // Fetch newly arrived messages from bridge, save to SQLite, return
        var newlyFetched = await FetchMessagesFromNativeAsync(chatId, beforeUnix, count, ct);
        if (_messageRepository != null && newlyFetched.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try { await _messageRepository.SaveManyAsync(newlyFetched); } catch { }
            }, ct);
        }
        return newlyFetched;
    }

    private async Task<IReadOnlyList<Message>> GetAllMessagesRaw(string chatId, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var ptr = WhatsAppNative.wa_get_messages(_clientPtr, chatId);
            if (ptr == IntPtr.Zero) return (IReadOnlyList<Message>)new List<Message>();

            try
            {
                var json = Marshal.PtrToStringUTF8(ptr);
                if (string.IsNullOrEmpty(json)) return new List<Message>();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dtos = JsonSerializer.Deserialize<List<NativeMessageDto>>(json, options) ?? new List<NativeMessageDto>();

                var messages = new List<Message>();
                foreach (var dto in dtos)
                {
                    // Filter empty protocol messages
                    if (dto.MediaType == "text" && string.IsNullOrWhiteSpace(dto.Text))
                    {
                        continue;
                    }

                    messages.Add(new Message
                    {
                        Id = dto.Id ?? Guid.NewGuid().ToString(),
                        ChatId = dto.ChatId ?? chatId,
                        PlatformId = "WhatsApp",
                        SenderId = dto.SenderId ?? string.Empty,
                        SenderName = dto.SenderName,
                        IsOutgoing = dto.IsOutgoing,
                        Content = new MessageContent { Text = dto.Text ?? string.Empty },
                        Timestamp = dto.Timestamp > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(dto.Timestamp)
                            : DateTimeOffset.UtcNow,
                        Status = dto.IsOutgoing ? MessageStatus.Sent : MessageStatus.Delivered,
                        QuotedText = dto.QuotedText,
                        QuotedSender = dto.QuotedSender,
                        Reactions = dto.Reactions ?? new()
                    });
                }

                return (IReadOnlyList<Message>)messages;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to deserialize messages from native bridge.");
                return new List<Message>();
            }
            finally
            {
                WhatsAppNative.wa_free_string(ptr);
            }
        }, ct);
    }

    public async Task<Message> SendMessageAsync(string chatId, MessageContent content, CancellationToken ct = default)
    {
        var msg = new Message
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            PlatformId = PlatformId,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow,
            Status = MessageStatus.Sent,
            SenderId = "me",
            IsOutgoing = true
        };

        if (IsTestingMode)
        {
            _testingMessages.Add(msg);
            return msg;
        }

        if (_messageRepository != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _messageRepository.SaveAsync(msg);
                    if (_chatRepository != null)
                    {
                        var chat = await _chatRepository.GetByIdAsync(chatId);
                        if (chat != null)
                        {
                            chat.LastMessage = msg;
                            chat.LastMessageTime = msg.Timestamp;
                            await _chatRepository.SaveAsync(chat);
                        }
                    }
                }
                catch { }
            }, ct);
        }

        if (_clientPtr != IntPtr.Zero)
        {
            await Task.Run(() =>
            {
                WhatsAppNative.wa_send_message(_clientPtr, chatId, content.Text);
            }, ct);
        }

        return msg;
    }

    public Task MarkAsReadAsync(string chatId, string messageId, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Contact>> GetContactsAsync(CancellationToken ct = default)
    {
        if (_clientPtr == IntPtr.Zero)
        {
            return Array.Empty<Contact>();
        }

        return await Task.Run(async () =>
        {
            var ptr = WhatsAppNative.wa_get_contacts(_clientPtr);
            if (ptr == IntPtr.Zero)
            {
                return (IReadOnlyList<Contact>)Array.Empty<Contact>();
            }

            try
            {
                var json = Marshal.PtrToStringUTF8(ptr);
                if (string.IsNullOrEmpty(json))
                {
                    return (IReadOnlyList<Contact>)Array.Empty<Contact>();
                }

                var nativeContacts = JsonSerializer.Deserialize<List<NativeContactDto>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (nativeContacts == null || nativeContacts.Count == 0)
                {
                    return (IReadOnlyList<Contact>)Array.Empty<Contact>();
                }

                var result = new List<Contact>();
                foreach (var nc in nativeContacts)
                {
                    if (string.IsNullOrEmpty(nc.Id) || string.IsNullOrEmpty(nc.Name)) continue;
                    var contact = new Contact
                    {
                        Id = nc.Id,
                        Name = nc.Name,
                        PhoneNumber = nc.PhoneNumber,
                        PlatformId = "WhatsApp"
                    };
                    result.Add(contact);
                }

                // Persist to IContactRepository
                try
                {
                    if (_contactRepository != null)
                    {
                        foreach (var c in result)
                        {
                            await _contactRepository.SaveAsync(c);
                        }
                    }
                }
                catch { }

                return (IReadOnlyList<Contact>)result;
            }
            finally
            {
                WhatsAppNative.wa_free_string(ptr);
            }
        }, ct);
    }

    public async Task<string?> GetProfilePictureUrlAsync(string chatId, CancellationToken ct = default)
    {
        if (IsTestingMode || _clientPtr == IntPtr.Zero || string.IsNullOrEmpty(chatId))
        {
            return null;
        }

        return await Task.Run(async () =>
        {
            var ptr = WhatsAppNative.wa_get_profile_picture(_clientPtr, chatId, true);
            if (ptr == IntPtr.Zero) return null;

            try
            {
                var url = Marshal.PtrToStringUTF8(ptr);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    if (_chatRepository != null)
                    {
                        var chat = await _chatRepository.GetByIdAsync(chatId);
                        if (chat != null && chat.PictureUrl != url)
                        {
                            chat.PictureUrl = url;
                            await _chatRepository.SaveAsync(chat);
                        }
                    }
                    return url;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to resolve profile picture for chat {ChatId}", chatId);
                return null;
            }
            finally
            {
                WhatsAppNative.wa_free_string(ptr);
            }
        }, ct);
    }

    protected virtual void OnConnectionStateChanged(ConnectionState state)
    {
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(PlatformId, state));
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }

    private class NativeContactDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? PlatformId { get; set; }
    }

    private class NativeChatDto
    {
        public string? Id { get; set; }
        public string? PlatformId { get; set; }
        public string? Title { get; set; }
        public bool IsGroup { get; set; }
        public int UnreadCount { get; set; }
        public long LastMessageTimestamp { get; set; }
        public NativeMessageDto? LastMessage { get; set; }
        public string? PictureUrl { get; set; }
    }

    private class NativeMessageDto
    {
        public string? Id { get; set; }
        public string? ChatId { get; set; }
        public string? SenderId { get; set; }
        public string? SenderName { get; set; }
        public bool IsOutgoing { get; set; }
        public string? Text { get; set; }
        public string? MediaType { get; set; }
        public string? QuotedText { get; set; }
        public string? QuotedSender { get; set; }
        public List<string>? Reactions { get; set; }
        public long Timestamp { get; set; }
    }
}
