using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
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

    public bool IsClientReady
    {
        get
        {
            if (IsTestingMode) return true;
            try
            {
                return _clientPtr != IntPtr.Zero && WhatsAppNative.wa_is_client_ready(_clientPtr) == 1;
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
    public event EventHandler<string>? ChatMarkedRead;
    public event EventHandler<(string MessageId, long BytesReceived, long TotalBytes)>? MediaDownloadProgress;

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

    // Outbox queue for offline message persistence and background dispatching
    private readonly ConcurrentQueue<Message> _outboxQueue = new();
    private readonly SemaphoreSlim _outboxLock = new(1, 1);
    private System.Threading.Timer? _outboxTimer;

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

        try
        {
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch { }

        _outboxTimer = new System.Threading.Timer(OnOutboxTimerTick, null, 1500, 1500);

        InitializeClientAndCache();
    }

    private void InitializeClientAndCache()
    {
        if (IsTestingMode) return;
        try
        {
            var dbPath = GetSessionDbPath();
            if (File.Exists(dbPath) || File.Exists(dbPath + ".cache.json"))
            {
                // Synchronously load memory cache from disk (~40ms); do NOT create tokio runtime here
                WhatsAppNative.wa_load_cache(dbPath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to initialize native cache on startup.");
        }
        _ = LoadPendingOutboxMessagesAsync();
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
            if (_clientPtr == IntPtr.Zero)
            {
                await Task.Run(() =>
                {
                    _clientPtr = WhatsAppNative.wa_create_client();
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
                        WhatsAppNative.wa_register_chat_read_callback(_clientPtr, &OnNativeChatRead);
                        WhatsAppNative.wa_register_download_progress_callback(_clientPtr, &OnNativeDownloadProgress);
                    }

                    await Task.Run(() => WhatsAppNative.wa_load_cache(dbPath), ct);
                }
            }

            if (_clientPtr != IntPtr.Zero)
            {
                if (IsAuthenticated)
                {
                    _logger?.LogInformation("WhatsApp is already authenticated. Connected immediately.");
                    ConnectionState = ConnectionState.Connected;
                    OnConnectionStateChanged(ConnectionState);
                    _ = LoadPendingOutboxMessagesAsync();
                    _ = ProcessOutboxQueueAsync();

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
                LogDiagnostic("Failed to create WhatsApp native client. Pointer is zero.");
                ConnectionState = ConnectionState.Error;
                OnConnectionStateChanged(ConnectionState, "Failed to create WhatsApp native client.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception connecting to WhatsApp backend.");
            LogDiagnostic($"Exception connecting to WhatsApp backend: {ex}");
            ConnectionState = ConnectionState.Error;
            OnConnectionStateChanged(ConnectionState, ex.Message);
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
            await Task.Run(() => WhatsAppNative.wa_clear_session(dbPath), ct);

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (File.Exists(dbPath)) File.Delete(dbPath);
                    if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm");
                    if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal");
                    var cachePath = dbPath + ".cache.json";
                    if (File.Exists(cachePath)) File.Delete(cachePath);
                    break;
                }
                catch
                {
                    await Task.Delay(50, ct);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clean some WhatsApp session files.");
        }

        ConnectionState = ConnectionState.Connecting;
        OnConnectionStateChanged(ConnectionState);

        await ConnectAsync(ct);
        await AuthenticateAsync(ct);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeError(IntPtr errPtr)
    {
        if (errPtr == IntPtr.Zero || _activeInstance == null) return;
        string? err = Marshal.PtrToStringUTF8(errPtr);
        _activeInstance._logger?.LogError("WhatsApp backend error: {Error}", err);
        LogDiagnostic($"WhatsApp backend error: {err}");
        _activeInstance.ConnectionState = ConnectionState.Error;
        _activeInstance.OnConnectionStateChanged(ConnectionState.Error, err);
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
        _ = _activeInstance.LoadPendingOutboxMessagesAsync();
        _ = _activeInstance.ProcessOutboxQueueAsync();
        _activeInstance.ChatUpdated?.Invoke(_activeInstance, new ChatUpdatedEventArgs());
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeSyncProgress(int progress)
    {
        if (_activeInstance == null) return;
        _activeInstance._logger?.LogInformation("WhatsApp sync progress: {Progress}%", progress);
        _activeInstance.SyncProgressChanged?.Invoke(_activeInstance, progress);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeChatRead(IntPtr chatIdPtr)
    {
        if (chatIdPtr == IntPtr.Zero || _activeInstance == null) return;
        string? rawChatId = Marshal.PtrToStringUTF8(chatIdPtr);
        if (string.IsNullOrWhiteSpace(rawChatId)) return;

        var cleanId = rawChatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? rawChatId[3..] : rawChatId;
        _activeInstance._logger?.LogInformation("WhatsApp chat read notification received from backend for {ChatId}", cleanId);

        _ = Task.Run(async () =>
        {
            try
            {
                if (_activeInstance._chatRepository != null)
                {
                    var chat = await _activeInstance._chatRepository.GetByIdAsync(cleanId)
                        ?? await _activeInstance._chatRepository.GetByIdAsync("wa_" + cleanId);
                    if (chat != null && chat.UnreadCount > 0)
                    {
                        chat.UnreadCount = 0;
                        await _activeInstance._chatRepository.SaveAsync(chat);
                    }
                }
            }
            catch (Exception ex)
            {
                _activeInstance._logger?.LogWarning(ex, "Failed to clear unread count in SQLite for {ChatId}", cleanId);
            }
        });

        _activeInstance.ChatMarkedRead?.Invoke(_activeInstance, cleanId);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnNativeDownloadProgress(IntPtr msgIdPtr, long written, long total)
    {
        if (_activeInstance == null || msgIdPtr == IntPtr.Zero) return;
        try
        {
            var msgId = Marshal.PtrToStringUTF8(msgIdPtr);
            if (!string.IsNullOrEmpty(msgId))
            {
                _activeInstance.MediaDownloadProgress?.Invoke(_activeInstance, (msgId, written, total));
            }
        }
        catch { }
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
                    await Task.Delay(150, token);
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
                string? mediaUrl = root.TryGetProperty("mediaUrl", out var muProp) ? muProp.GetString() : null;
                string? fileName = root.TryGetProperty("fileName", out var fnProp) ? fnProp.GetString() : null;
                long? fileSize = root.TryGetProperty("fileSize", out var fsProp) ? fsProp.GetInt64() : null;
                string? mimeType = root.TryGetProperty("mimeType", out var mtProp) ? mtProp.GetString() : null;
                string? thumbB64 = root.TryGetProperty("thumbnailBase64", out var tbProp) ? tbProp.GetString() : null;
                string? caption = root.TryGetProperty("caption", out var capProp) ? capProp.GetString() : null;
                int? duration = root.TryGetProperty("durationSeconds", out var durProp) ? durProp.GetInt32() : null;

                var contentType = ParseContentType(mediaType);

                // Filter out empty protocol / stub messages (unless it's an alert/notice with text or real media)
                if (contentType == MessageContentType.Text && string.IsNullOrWhiteSpace(text))
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
                    Content = new MessageContent
                    {
                        Text = text,
                        Type = contentType,
                        MediaUrl = mediaUrl,
                        LocalFilePath = mediaUrl,
                        FileName = fileName,
                        FileSize = fileSize,
                        MimeType = mimeType,
                        ThumbnailUrl = thumbB64,
                        Caption = caption,
                        DurationSeconds = duration
                    },
                    Timestamp = root.TryGetProperty("timestamp", out var tsProp) ? DateTimeOffset.FromUnixTimeSeconds(tsProp.GetInt64()) : DateTimeOffset.UtcNow,
                    Status = root.TryGetProperty("status", out var stProp) ? (MessageStatus)stProp.GetInt32() : ((root.TryGetProperty("isOutgoing", out var isOut) && isOut.GetBoolean()) ? MessageStatus.Sent : MessageStatus.Delivered),
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
                            if (_activeInstance._chatRepository != null && msg.Content?.Type != MessageContentType.SystemNotice)
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

        // 1. If native bridge is connected, fetch chats from native cache (resolves latest group titles and previews)
        if (_clientPtr != IntPtr.Zero)
        {
            var remoteChats = await FetchChatsFromNativeAsync(ct);
            if (remoteChats.Count > 0)
            {
                if (_chatRepository != null)
                {
                    try
                    {
                        var localChats = await _chatRepository.GetByPlatformAsync(PlatformId, 0, 500);
                        var picMap = localChats
                            .Where(c => !string.IsNullOrEmpty(c.PictureUrl))
                            .ToDictionary(c => c.Id, c => c.PictureUrl);
                        var unreadMap = localChats.ToDictionary(c => c.Id, c => c.UnreadCount);

                        foreach (var rc in remoteChats)
                        {
                            if (string.IsNullOrEmpty(rc.PictureUrl) && picMap.TryGetValue(rc.Id, out var cachedPic))
                            {
                                rc.PictureUrl = cachedPic;
                            }
                        }
                    }
                    catch { }

                    _ = Task.Run(async () =>
                    {
                        try { await _chatRepository.SaveManyAsync(remoteChats); } catch { }
                    }, ct);
                }
                return remoteChats;
            }
        }

        // 2. Fallback to SQLite if native client is not ready
        if (_chatRepository != null)
        {
            try
            {
                var localChats = await _chatRepository.GetByPlatformAsync(PlatformId, offset, limit);
                if (localChats.Count > 0)
                {
                    return localChats;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read chats from SQLite repository.");
            }
        }

        return Array.Empty<Chat>();
    }

    public string? GetContactName(string chatId)
    {
        if (IsTestingMode || string.IsNullOrEmpty(chatId)) return null;
        try
        {
            var ptr = WhatsAppNative.wa_get_contact_name(chatId);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                WhatsAppNative.wa_free_string(ptr);
            }
        }
        catch
        {
            return null;
        }
    }

    public string? GetPushName(string chatId)
    {
        if (IsTestingMode || string.IsNullOrEmpty(chatId)) return null;
        try
        {
            var ptr = WhatsAppNative.wa_get_push_name(chatId);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                WhatsAppNative.wa_free_string(ptr);
            }
        }
        catch
        {
            return null;
        }
    }

    public string? GetPhoneNumber(string chatId)
    {
        if (IsTestingMode || string.IsNullOrEmpty(chatId)) return null;
        try
        {
            var ptr = WhatsAppNative.wa_get_phone_number(chatId);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                WhatsAppNative.wa_free_string(ptr);
            }
        }
        catch
        {
            return null;
        }
    }

    public void SubscribePresence(string chatId)
    {
        if (IsTestingMode || _clientPtr == IntPtr.Zero || string.IsNullOrEmpty(chatId)) return;
        try
        {
            WhatsAppNative.wa_subscribe_presence(_clientPtr, chatId);
        }
        catch
        {
        }
    }

    public IReadOnlyList<Chat> GetCachedChats()
    {
        if (IsTestingMode) return GetTestingChats();
        return ReadChatsFromNative(_clientPtr);
    }

    public IReadOnlyList<Message> GetCachedMessages(string chatId, int limit = 30)
    {
        if (IsTestingMode)
        {
            var testMsgs = _testingMessages.FindAll(m => m.ChatId == chatId);
            if (testMsgs.Count > 0) return testMsgs;
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

        if (_clientPtr == IntPtr.Zero || string.IsNullOrEmpty(chatId))
        {
            return Array.Empty<Message>();
        }

        var resultList = new List<Message>(ReadMessagesFromNative(_clientPtr, chatId, 0, limit));

        // Merge pending outbox messages
        var cleanChatId = chatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? chatId[3..] : chatId;
        var bareUser = cleanChatId.Split('@')[0].Split(':')[0];
        foreach (var p in _outboxQueue)
        {
            var c = p.ChatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? p.ChatId[3..] : p.ChatId;
            var cBare = c.Split('@')[0].Split(':')[0];
            if (string.Equals(c, cleanChatId, StringComparison.OrdinalIgnoreCase) ||
                (bareUser.Length >= 5 && string.Equals(cBare, bareUser, StringComparison.OrdinalIgnoreCase)))
            {
                if (!resultList.Any(r => r.Id == p.Id) &&
                    !resultList.Any(r => r.IsOutgoing && r.Status != MessageStatus.Sending && r.Content?.Text == p.Content?.Text))
                {
                    resultList.Add(p);
                }
            }
        }

        return resultList.OrderBy(m => m.Timestamp).ToList();
    }

    private async Task<IReadOnlyList<Chat>> FetchChatsFromNativeAsync(CancellationToken ct = default)
    {
        return await Task.Run(() => ReadChatsFromNative(_clientPtr), ct);
    }

    private IReadOnlyList<Chat> ReadChatsFromNative(IntPtr clientPtr)
    {
        var ptr = WhatsAppNative.wa_get_chats(clientPtr);
        if (ptr == IntPtr.Zero) return Array.Empty<Chat>();

        try
        {
            var json = Marshal.PtrToStringUTF8(ptr);
            if (string.IsNullOrEmpty(json)) return Array.Empty<Chat>();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var dtos = JsonSerializer.Deserialize<List<NativeChatDto>>(json, options) ?? new List<NativeChatDto>();

            var chats = new List<Chat>(dtos.Count);
            foreach (var dto in dtos)
            {
                // Filter out phantom empty chat stubs (no valid last message)
                if (dto.LastMessage == null || (dto.LastMessage.MediaType == "text" && string.IsNullOrWhiteSpace(dto.LastMessage.Text)))
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
                        : DateTimeOffset.MinValue
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
                            : (chat.LastMessageTime != DateTimeOffset.MinValue ? chat.LastMessageTime : DateTimeOffset.UnixEpoch),
                        Status = (MessageStatus)dto.LastMessage.Status,
                        QuotedText = dto.LastMessage.QuotedText,
                        QuotedSender = dto.LastMessage.QuotedSender,
                        Reactions = dto.LastMessage.Reactions ?? new()
                    };
                }

                chats.Add(chat);
            }

            return chats;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to deserialize chats from native bridge.");
            return Array.Empty<Chat>();
        }
        finally
        {
            WhatsAppNative.wa_free_string(ptr);
        }
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

        var resultList = new List<Message>();

        // 1. Fetch from native bridge first if ready
        if (_clientPtr != IntPtr.Zero)
        {
            var bridgeMsgs = await FetchMessagesFromNativeAsync(chatId, 0, limit, ct);
            if (bridgeMsgs.Count > 0)
            {
                resultList.AddRange(bridgeMsgs);
                if (_messageRepository != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await _messageRepository.SaveManyAsync(bridgeMsgs); } catch { }
                    }, ct);
                }
            }
        }

        // 2. Fallback to SQLite if native bridge had no messages (e.g. offline, not connected, or empty)
        if (resultList.Count == 0 && _messageRepository != null)
        {
            try
            {
                var localMsgs = await _messageRepository.GetByChatIdAsync(chatId, offset, limit);
                resultList.AddRange(localMsgs);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load messages from SQLite for {ChatId}", chatId);
            }
        }

        // 3. Always merge pending outgoing messages (Status == Sending) from in-memory outbox
        var cleanChatId = chatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? chatId[3..] : chatId;
        var bareUser = cleanChatId.Split('@')[0].Split(':')[0];

        var pendingMessages = new List<Message>();
        foreach (var p in _outboxQueue)
        {
            var c = p.ChatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? p.ChatId[3..] : p.ChatId;
            var cBare = c.Split('@')[0].Split(':')[0];
            if (string.Equals(c, cleanChatId, StringComparison.OrdinalIgnoreCase) ||
                (bareUser.Length >= 5 && string.Equals(cBare, bareUser, StringComparison.OrdinalIgnoreCase)))
            {
                if (!pendingMessages.Any(m => m.Id == p.Id))
                {
                    pendingMessages.Add(p);
                }
            }
        }

        foreach (var p in pendingMessages)
        {
            // Only add if not already in resultList by ID and not replaced by a sent message with matching text
            if (!resultList.Any(r => r.Id == p.Id) &&
                !resultList.Any(r => r.IsOutgoing && r.Status != MessageStatus.Sending && r.Content?.Text == p.Content?.Text))
            {
                resultList.Add(p);
            }
        }

        return resultList.OrderBy(m => m.Timestamp).ToList();
    }

    private async Task<IReadOnlyList<Message>> FetchMessagesFromNativeAsync(string chatId, long beforeTs, int limit, CancellationToken ct = default)
    {
        if (_clientPtr == IntPtr.Zero) return Array.Empty<Message>();
        return await Task.Run(() => ReadMessagesFromNative(_clientPtr, chatId, beforeTs, limit), ct);
    }

    private IReadOnlyList<Message> ReadMessagesFromNative(IntPtr clientPtr, string chatId, long beforeTs, int limit)
    {
        if (clientPtr == IntPtr.Zero || string.IsNullOrEmpty(chatId)) return Array.Empty<Message>();

        var ptr = WhatsAppNative.wa_get_messages_paginated(clientPtr, chatId, beforeTs, limit);
        if (ptr == IntPtr.Zero) return Array.Empty<Message>();

        try
        {
            var json = Marshal.PtrToStringUTF8(ptr);
            if (string.IsNullOrEmpty(json)) return Array.Empty<Message>();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var dtos = JsonSerializer.Deserialize<List<NativeMessageDto>>(json, options) ?? new List<NativeMessageDto>();

            var messages = new List<Message>(dtos.Count);
            foreach (var dto in dtos)
            {
                var contentType = ParseContentType(dto.MediaType);
                // Filter empty protocol messages
                if (contentType == MessageContentType.Text && string.IsNullOrWhiteSpace(dto.Text))
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
                    Content = new MessageContent { 
                        Text = dto.Text ?? string.Empty,
                        Type = contentType,
                        MediaUrl = dto.MediaUrl,
                        LocalFilePath = dto.MediaUrl,
                        FileName = dto.FileName,
                        FileSize = dto.FileSize,
                        MimeType = dto.MimeType,
                        ThumbnailUrl = dto.ThumbnailBase64,
                        Caption = dto.Caption,
                        DurationSeconds = dto.DurationSeconds
                    },
                    Timestamp = dto.Timestamp > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(dto.Timestamp)
                        : DateTimeOffset.UnixEpoch,
                    Status = (MessageStatus)dto.Status,
                    QuotedText = dto.QuotedText,
                    QuotedSender = dto.QuotedSender,
                    Reactions = dto.Reactions ?? new()
                });
            }

            return messages;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to deserialize messages from native bridge.");
            return Array.Empty<Message>();
        }
        finally
        {
            WhatsAppNative.wa_free_string(ptr);
        }
    }

    public async Task<IReadOnlyList<Message>> GetOlderMessagesAsync(
        string chatId, 
        int count = 25, 
        DateTimeOffset? beforeTimestamp = null, 
        string? anchorMessageId = null, 
        bool anchorFromMe = false, 
        CancellationToken ct = default)
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
        var cleanId = chatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? chatId[3..] : chatId;

        string? anchorId = anchorMessageId;
        bool fromMe = anchorFromMe;
        long anchorTsMs = (beforeTimestamp?.ToUnixTimeMilliseconds()) ?? (beforeUnix * 1000);

        if (string.IsNullOrEmpty(anchorId) && _messageRepository != null)
        {
            try
            {
                var oldestSqlite = await _messageRepository.GetOlderMessagesBeforeAsync(chatId, beforeTimestamp ?? DateTimeOffset.UtcNow, 1);
                if (oldestSqlite.Count > 0)
                {
                    anchorId = oldestSqlite[0].Id;
                    fromMe = oldestSqlite[0].IsOutgoing;
                    anchorTsMs = oldestSqlite[0].Timestamp.ToUnixTimeMilliseconds();
                }
            }
            catch { }
        }

        int initialCount = WhatsAppNative.wa_get_message_count(_clientPtr, cleanId);

        // Fire PDO on-demand request to the phone with exact anchor message ID and timestamp
        await Task.Run(() =>
        {
            WhatsAppNative.wa_fetch_older_messages(_clientPtr, cleanId, count, anchorId, fromMe, anchorTsMs);
        }, ct);

        // Poll for phone response with responsive 50ms intervals (up to 4.5 seconds for network/phone latency)
        for (int i = 0; i < 90; i++)
        {
            await Task.Delay(50, ct);
            int currentCount = WhatsAppNative.wa_get_message_count(_clientPtr, cleanId);
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
                    var contentType = ParseContentType(dto.MediaType);
                    // Filter empty protocol messages
                    if (contentType == MessageContentType.Text && string.IsNullOrWhiteSpace(dto.Text))
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
                        Content = new MessageContent { 
                            Text = dto.Text ?? string.Empty,
                            Type = contentType,
                            MediaUrl = dto.MediaUrl,
                            LocalFilePath = dto.MediaUrl,
                            FileName = dto.FileName,
                            FileSize = dto.FileSize,
                            MimeType = dto.MimeType,
                            ThumbnailUrl = dto.ThumbnailBase64,
                            Caption = dto.Caption,
                            DurationSeconds = dto.DurationSeconds
                        },
                        Timestamp = dto.Timestamp > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(dto.Timestamp)
                            : DateTimeOffset.UnixEpoch,
                        Status = (MessageStatus)dto.Status,
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

    public static bool HasInternetAccess()
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
                return false;

            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                {
                    var props = ni.GetIPProperties();
                    if (props != null && props.GatewayAddresses != null &&
                        props.GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.None)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            _logger?.LogInformation("Network connectivity available. Fast-draining outbox queue.");
            _ = Task.Run(async () =>
            {
                // Rapidly poll up to 30 times (every 250ms = 7.5s) until IsClientReady is true, then drain immediately
                for (int i = 0; i < 30; i++)
                {
                    if (!_outboxQueue.IsEmpty && HasInternetAccess() && _clientPtr != IntPtr.Zero && IsClientReady && ConnectionState == ConnectionState.Connected)
                    {
                        await ProcessOutboxQueueAsync();
                        break;
                    }
                    await Task.Delay(250);
                }
            });
        }
    }

    private void OnOutboxTimerTick(object? state)
    {
        if (!_outboxQueue.IsEmpty && HasInternetAccess() && ConnectionState == ConnectionState.Connected)
        {
            _ = ProcessOutboxQueueAsync();
        }
    }

    public async Task LoadPendingOutboxMessagesAsync()
    {
        if (_messageRepository == null) return;
        try
        {
            var pending = await _messageRepository.GetPendingOutgoingMessagesAsync(PlatformId);
            foreach (var msg in pending)
            {
                if (!_outboxQueue.Any(m => m.Id == msg.Id))
                {
                    _outboxQueue.Enqueue(msg);
                    _logger?.LogInformation("Loaded pending outbox message {Id} for chat {ChatId}", msg.Id, msg.ChatId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load pending messages from repository.");
        }
    }

    private void EnqueueOutbox(Message msg)
    {
        if (!_outboxQueue.Any(m => m.Id == msg.Id))
        {
            _outboxQueue.Enqueue(msg);
        }
        _ = ProcessOutboxQueueAsync();
    }

    public async Task ProcessOutboxQueueAsync()
    {
        if (_outboxQueue.IsEmpty) return;
        if (!HasInternetAccess() || _clientPtr == IntPtr.Zero || !IsClientReady || ConnectionState != ConnectionState.Connected)
        {
            return;
        }

        if (!await _outboxLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            while (_outboxQueue.TryPeek(out var msg))
            {
                if (!HasInternetAccess() || _clientPtr == IntPtr.Zero || !IsClientReady || ConnectionState != ConnectionState.Connected)
                {
                    break;
                }

                _logger?.LogInformation("Dispatching queued message {Id} to {ChatId}", msg.Id, msg.ChatId);
                IntPtr realIdPtr = IntPtr.Zero;
                try
                {
                    realIdPtr = await Task.Run(() => WhatsAppNative.wa_send_message(_clientPtr, msg.ChatId, msg.Content.Text));
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error executing wa_send_message in outbox queue");
                }

                if (realIdPtr != IntPtr.Zero)
                {
                    var realId = Marshal.PtrToStringUTF8(realIdPtr);
                    WhatsAppNative.wa_free_string(realIdPtr);

                    if (!string.IsNullOrEmpty(realId))
                    {
                        _outboxQueue.TryDequeue(out _);

                        var oldId = msg.Id;
                        msg.Id = realId;
                        bool isSelf = WhatsAppNative.wa_is_self_chat(_clientPtr, msg.ChatId) == 1;
                        msg.Status = isSelf ? MessageStatus.Read : MessageStatus.Sent;

                        if (_messageRepository != null)
                        {
                            try
                            {
                                await _messageRepository.DeleteAsync(oldId);
                                await _messageRepository.SaveAsync(msg);

                                if (_chatRepository != null)
                                {
                                    var chat = await _chatRepository.GetByIdAsync(msg.ChatId);
                                    if (chat != null)
                                    {
                                        chat.LastMessage = msg;
                                        chat.LastMessageTime = msg.Timestamp;
                                        await _chatRepository.SaveAsync(chat);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError(ex, "Failed to update database for sent message {Id}", realId);
                            }
                        }

                        MessageReceived?.Invoke(this, new MessageReceivedEventArgs(msg));
                        ChatUpdated?.Invoke(this, new ChatUpdatedEventArgs());
                        continue;
                    }
                }

                _logger?.LogInformation("Outbox send did not succeed for message {Id}. Will retry when connection stabilizes.", msg.Id);
                break;
            }
        }
        finally
        {
            _outboxLock.Release();
        }
    }

    public async Task<Message> SendMessageAsync(string chatId, MessageContent content, string? clientMessageId = null, CancellationToken ct = default)
    {
        var msg = new Message
        {
            Id = clientMessageId ?? ("pending_" + Guid.NewGuid().ToString("N")),
            ChatId = chatId,
            PlatformId = PlatformId,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow,
            Status = MessageStatus.Sending,
            SenderId = "me",
            IsOutgoing = true
        };

        if (IsTestingMode)
        {
            msg.Status = MessageStatus.Sent;
            _testingMessages.Add(msg);
            return msg;
        }

        bool sentImmediately = false;
        if (_clientPtr != IntPtr.Zero && IsClientReady && ConnectionState == ConnectionState.Connected && HasInternetAccess())
        {
            try
            {
                var realIdPtr = await Task.Run(() => WhatsAppNative.wa_send_message(_clientPtr, chatId, content.Text), ct);
                if (realIdPtr != IntPtr.Zero)
                {
                    var realId = Marshal.PtrToStringUTF8(realIdPtr);
                    WhatsAppNative.wa_free_string(realIdPtr);
                    if (!string.IsNullOrEmpty(realId))
                    {
                        msg.Id = realId;
                        bool isSelf = WhatsAppNative.wa_is_self_chat(_clientPtr, chatId) == 1;
                        msg.Status = isSelf ? MessageStatus.Read : MessageStatus.Sent;
                        sentImmediately = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Direct send failed for message {Id}; queuing for retry", msg.Id);
            }
        }

        if (_messageRepository != null)
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
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to persist outgoing message {Id}", msg.Id);
            }
        }

        if (!sentImmediately)
        {
            _logger?.LogInformation("Message {Id} for chat {ChatId} enqueued in Outbox.", msg.Id, chatId);
            EnqueueOutbox(msg);
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

        var cleanId = chatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? chatId[3..] : chatId;

        return await Task.Run(async () =>
        {
            var ptr = WhatsAppNative.wa_get_profile_picture(_clientPtr, cleanId, true);
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

    public async Task MarkChatAsReadAsync(string chatId, CancellationToken ct = default)
    {
        if (IsTestingMode || string.IsNullOrWhiteSpace(chatId)) return;

        var cleanId = chatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? chatId[3..] : chatId;
        if (_clientPtr != IntPtr.Zero)
        {
            await Task.Run(() => WhatsAppNative.wa_mark_chat_read(_clientPtr, cleanId), ct);
        }

        if (_chatRepository != null)
        {
            try
            {
                var chat = await _chatRepository.GetByIdAsync(chatId);
                if (chat != null && chat.UnreadCount > 0)
                {
                    chat.UnreadCount = 0;
                    await _chatRepository.SaveAsync(chat);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to persist chat read state to SQLite for {ChatId}", chatId);
            }
        }
    }

    internal static void LogDiagnostic(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "people_connection.log");
            File.AppendAllText(path, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    protected virtual void OnConnectionStateChanged(ConnectionState state, string? errorMessage = null)
    {
        LogDiagnostic($"ConnectionStateChanged -> {state}" + (!string.IsNullOrEmpty(errorMessage) ? $": {errorMessage}" : ""));
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(PlatformId, state, errorMessage));
    }

    private static MessageContentType ParseContentType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "image" => MessageContentType.Image,
        "video" => MessageContentType.Video,
        "audio" => MessageContentType.Audio,
        "document" => MessageContentType.Document,
        "location" => MessageContentType.Location,
        "system_notice" => MessageContentType.SystemNotice,
        _ => MessageContentType.Text
    };

    private static string GetExtensionForMime(string? mimeType, MessageContentType? type = null, string? fileName = null)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var ext = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext) && ext.Length >= 2 && ext.Length <= 7)
            {
                return ext.ToLowerInvariant();
            }
        }

        if (!string.IsNullOrEmpty(mimeType))
        {
            var cleanMime = mimeType.Split(';')[0].Trim().ToLowerInvariant();
            switch (cleanMime)
            {
                case "image/jpeg":
                case "image/jpg":
                    return ".jpg";
                case "image/png":
                    return ".png";
                case "image/webp":
                    return ".webp";
                case "image/gif":
                    return ".gif";
                case "video/mp4":
                    return ".mp4";
                case "video/3gpp":
                case "video/3gp":
                    return ".3gp";
                case "video/quicktime":
                    return ".mov";
                case "video/x-msvideo":
                    return ".avi";
                case "audio/ogg":
                case "audio/opus":
                    return ".ogg";
                case "audio/mp4":
                case "audio/m4a":
                case "audio/x-m4a":
                    return ".m4a";
                case "audio/mpeg":
                case "audio/mp3":
                    return ".mp3";
                case "application/pdf":
                    return ".pdf";
                case "application/vnd.openxmlformats-officedocument.wordprocessingml.document":
                    return ".docx";
                case "application/msword":
                    return ".doc";
                case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":
                    return ".xlsx";
                case "application/vnd.ms-excel":
                    return ".xls";
                case "application/vnd.openxmlformats-officedocument.presentationml.presentation":
                    return ".pptx";
                case "application/vnd.ms-powerpoint":
                    return ".ppt";
                case "application/zip":
                    return ".zip";
                case "text/plain":
                    return ".txt";
            }
        }

        if (type.HasValue)
        {
            switch (type.Value)
            {
                case MessageContentType.Image:
                    return ".jpg";
                case MessageContentType.Video:
                    return ".mp4";
                case MessageContentType.Audio:
                    return ".ogg";
                case MessageContentType.Document:
                    return ".pdf";
            }
        }

        return ".bin";
    }

    public async Task<string?> DownloadMediaAsync(string chatId, string messageId, string? fileName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(messageId)) return null;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var mediaDir = Path.Combine(localAppData, "People", "Media");
        Directory.CreateDirectory(mediaDir);

        Message? existingMsg = null;
        if (_messageRepository != null)
        {
            try { existingMsg = await _messageRepository.GetByIdAsync(messageId); } catch { }
        }

        var mime = existingMsg?.Content?.MimeType;
        var msgType = existingMsg?.Content?.Type;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = existingMsg?.Content?.FileName;
        }

        var ext = GetExtensionForMime(mime, msgType, fileName);

        string safeFileName;
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            safeFileName = string.Concat(fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (!safeFileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && !Path.HasExtension(safeFileName))
            {
                safeFileName += ext;
            }
        }
        else
        {
            safeFileName = msgType switch
            {
                MessageContentType.Image => $"IMG_{messageId}{ext}",
                MessageContentType.Video => $"VID_{messageId}{ext}",
                MessageContentType.Audio => $"AUD_{messageId}{ext}",
                _ => $"DOC_{messageId}{ext}"
            };
        }

        var targetPath = Path.Combine(mediaDir, safeFileName);
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
        {
            return targetPath;
        }

        if (_clientPtr == IntPtr.Zero || !IsClientReady)
        {
            _logger?.LogWarning("Cannot download media: WhatsApp client not ready.");
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                var ptr = WhatsAppNative.wa_download_media(_clientPtr, chatId, messageId, targetPath);
                if (ptr == IntPtr.Zero)
                {
                    _logger?.LogWarning("wa_download_media returned null for {MessageId}", messageId);
                    return null;
                }

                try
                {
                    var resultPath = Marshal.PtrToStringUTF8(ptr);
                    if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath))
                    {
                        // Update SQLite repository asynchronously
                        if (_messageRepository != null)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var msg = await _messageRepository.GetByIdAsync(messageId);
                                    if (msg != null)
                                    {
                                        msg.Content.LocalFilePath = resultPath;
                                        msg.Content.MediaUrl = resultPath;
                                        await _messageRepository.SaveAsync(msg);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogError(ex, "Failed to update downloaded media path in SQLite for {MessageId}", messageId);
                                }
                            });
                        }

                        return resultPath;
                    }
                    return null;
                }
                finally
                {
                    WhatsAppNative.wa_free_string(ptr);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DownloadMediaAsync failed for {MessageId}", messageId);
                return null;
            }
        }, ct);
    }

    public void Dispose()
    {
        try
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }
        catch { }
        _outboxTimer?.Dispose();
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
        public int Status { get; set; }
        public string? MediaUrl { get; set; }
        public string? FileName { get; set; }
        public long? FileSize { get; set; }
        public string? MimeType { get; set; }
        public string? ThumbnailBase64 { get; set; }
        public string? Caption { get; set; }
        public int? DurationSeconds { get; set; }
    }
}
