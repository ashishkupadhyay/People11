using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using People.Core.Interfaces;
using People.Core.Interfaces.Repositories;
using People.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace People.ViewModels;

public partial class ChatListViewModel : ViewModelBase
{
    public ObservableCollection<Chat> Chats { get; } = new();
    public ObservableCollection<Contact> FilteredContacts { get; } = new();
    private readonly List<Contact> _allContacts = new();
    private readonly object _contactsLock = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboardingActive))]
    public partial bool IsWelcomeVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboardingActive))]
    public partial bool IsQrLinkVisible { get; set; }

    partial void OnIsQrLinkVisibleChanged(bool value)
    {
        if (!value)
        {
            StopQrCountdown();
        }
    }

    public bool IsOnboardingActive => IsWelcomeVisible || IsQrLinkVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQrCodeBytes))]
    public partial byte[]? QrCodeBytes { get; set; }

    public bool HasQrCodeBytes => QrCodeBytes != null && QrCodeBytes.Length > 0;

    [ObservableProperty]
    public partial string QrRefreshCountdownText { get; set; } = string.Empty;

    private System.Threading.Timer? _qrCountdownTimer;
    private int _qrSecondsRemaining = 60;
    private const int FirstQrLifetimeSeconds = 60;
    private const int SubsequentQrLifetimeSeconds = 20;
    private bool _isFirstQrInSession = true;
    private int _stalledRefreshSeconds = 0;
    private bool _isRefreshingSession = false;

    public event EventHandler? RequestSlideToQr;
    public event EventHandler? RequestDrillInToSync;
    public event EventHandler? RequestDrillInToMain;

    [ObservableProperty]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    public partial bool ShowQrCode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockingError))]
    [NotifyPropertyChangedFor(nameof(HasNonBlockingError))]
    public partial bool HasError { get; set; }

    public bool HasBlockingError => HasError && Chats.Count == 0;
    public bool HasNonBlockingError => HasError && Chats.Count > 0;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSyncing { get; set; }

    [ObservableProperty]
    public partial int SyncProgress { get; set; }

    [ObservableProperty]
    public partial bool IsSyncIndeterminate { get; set; } = true;

    partial void OnSyncProgressChanged(int value)
    {
        IsSyncIndeterminate = value <= 0;
    }

    [ObservableProperty]
    public partial string SyncStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsInitialSyncing { get; set; }

    [ObservableProperty]
    public partial int InitialSyncProgress { get; set; }

    [ObservableProperty]
    public partial bool IsInitialSyncIndeterminate { get; set; } = true;

    partial void OnInitialSyncProgressChanged(int value)
    {
        IsInitialSyncIndeterminate = value <= 0;
    }

    [ObservableProperty]
    public partial string InitialSyncText { get; set; } = "Downloading your recent messages";

    private readonly IMessagingProvider _provider;
    private readonly IDispatcherService _dispatcherService;
    private readonly ISettingsService _settingsService;
    private readonly IChatRepository? _chatRepository;
    private readonly Dictionary<string, string> _contactNamesByPhoneDigits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _contactNamesById = new(StringComparer.OrdinalIgnoreCase);
    private int _isLoadingChats;
    private volatile bool _needsReloadChats;
    private DateTimeOffset _lastChatUpdateLoadTime = DateTimeOffset.MinValue;
    private readonly object _chatUpdateLock = new();
    private System.Threading.Timer? _syncWatchdogTimer;
    private System.Threading.Timer? _inlineSyncWatchdogTimer;
    private DateTimeOffset _initialSyncStartTime = DateTimeOffset.MinValue;
    private DateTimeOffset _lastInlineSyncActivity = DateTimeOffset.MinValue;
    private DateTimeOffset _chatUpdateFirstPending = DateTimeOffset.MinValue;

    public event EventHandler? RequestCloseDetail;

    public ChatListViewModel(
        IMessagingProvider provider, 
        IDispatcherService dispatcherService,
        ISettingsService settingsService,
        IChatRepository? chatRepository = null)
    {
        _provider = provider;
        _dispatcherService = dispatcherService;
        _settingsService = settingsService;
        _chatRepository = chatRepository;

        _settingsService.SettingChanged += OnSettingChanged;
        _provider.ConnectionStateChanged += OnConnectionStateChanged;
        _provider.ChatUpdated += OnChatUpdated;
        _provider.MessageReceived += OnMessageReceived;

        if (_provider is IWhatsAppProvider waProvider)
        {
            bool isAuth = waProvider.IsAuthenticated;
            if (!isAuth)
            {
                IsWelcomeVisible = true;
                IsQrLinkVisible = false;
            }
            else
            {
                // Synchronously preload cached chats so Chats is populated BEFORE page render
                PreloadCachedChats();
                _ = Task.Run(RebuildContactsAsync);
            }

            waProvider.QrCodeReceived += OnQrCodeReceived;
            waProvider.SyncProgressChanged += OnSyncProgressChanged;
            waProvider.ChatMarkedRead += OnChatMarkedRead;
            waProvider.ChatPictureUpdated += OnChatPictureUpdated;
            _ = ConnectAndAuthenticateAsync(waProvider);
        }
        else
        {
            PreloadCachedChats();
            _ = Task.Run(RebuildContactsAsync);
            LoadMockData();
        }
    }

    private void PreloadCachedChats()
    {
        try
        {
            if (_provider is IWhatsAppProvider waProvider && waProvider.IsAuthenticated)
            {
                var cached = waProvider.GetCachedChats();
                if (cached.Count > 0)
                {
                    var valid = cached.Where(c => !string.IsNullOrWhiteSpace(c.Id)).ToList();
                    foreach (var c in valid)
                    {
                        c.Title = ResolveChatTitle(c);
                    }
                    var sorted = valid.OrderByDescending(c => c.LastMessageTime).ToList();

                    // Preload viewport batch immediately so ListView renders fully with stacking animation
                    var viewportBatch = sorted.Take(25).ToList();
                    foreach (var c in viewportBatch)
                    {
                        Chats.Add(c);
                    }

                    // Schedule remaining chats so initial launch and window presentation are never stalled
                    if (sorted.Count > 25)
                    {
                        var remaining = sorted.Skip(25).ToList();
                        _dispatcherService.Enqueue(() =>
                        {
                            foreach (var c in remaining)
                            {
                                Chats.Add(c);
                            }
                        });
                    }
                }
            }
        }
        catch { }
    }

    private async Task ConnectAndAuthenticateAsync(IWhatsAppProvider provider)
    {
        bool isAuth = provider.IsAuthenticated;

        if (isAuth)
        {
            // Already authenticated: show chat list directly, show sync bar, and connect in background
            _dispatcherService.Enqueue(() =>
            {
                IsWelcomeVisible = false;
                IsQrLinkVisible = false;
                IsConnecting = false;
                ShowQrCode = false;
                HasError = false;
                ErrorMessage = string.Empty;
                IsSyncing = true;
                SyncProgress = 0;
                IsSyncIndeterminate = true;
                SyncStatusText = string.Empty;
            });
            ArmInlineSyncWatchdog(25);

            _ = LoadChatsAsync();

            try
            {
                await provider.ConnectAsync();
            }
            catch (Exception ex)
            {
                DisarmInlineSyncWatchdog();
                _dispatcherService.Enqueue(() =>
                {
                    IsSyncing = false;
                    HasError = true;
                    ErrorMessage = ex.Message;
                });
            }

            // Immediately load all live/cached chats now that native client is connected
            await LoadChatsAsync();
            _dispatcherService.Enqueue(() =>
            {
                var (nativeSyncing, nativeProgress) = provider.GetNativeSyncStatus();
                if (nativeSyncing)
                {
                    IsSyncing = true;
                    if (nativeProgress > 0)
                    {
                        SyncProgress = nativeProgress;
                        IsSyncIndeterminate = false;
                        SyncStatusText = $"Downloading recent messages ({nativeProgress}%)";
                    }
                    else
                    {
                        IsSyncIndeterminate = true;
                        SyncStatusText = "Downloading recent messages...";
                    }
                    ArmInlineSyncWatchdog(12);
                }
                else if (provider.ConnectionState != ConnectionState.Connecting)
                {
                    // Brief grace window in case OfflineSyncPreview arrives shortly after connect
                    ArmInlineSyncWatchdog(5);
                }
            });
            return;
        }

        // First app launch / unauthenticated: Show Welcome screen first
        _dispatcherService.Enqueue(() =>
        {
            IsWelcomeVisible = true;
            IsQrLinkVisible = false;
            IsConnecting = false;
            ShowQrCode = false;
            HasError = false;
            ErrorMessage = string.Empty;
            QrCodeBytes = null;
        });

        // Pre-connect WebSocket in background so it's ready when user taps "Get started"
        try
        {
            await provider.ConnectAsync();
        }
        catch { }
    }

    [RelayCommand]
    public async Task StartLinkingAsync()
    {
        IsWelcomeVisible = false;
        IsQrLinkVisible = true;
        _isFirstQrInSession = true;
        _stalledRefreshSeconds = 0;
        _isRefreshingSession = false;
        RequestSlideToQr?.Invoke(this, EventArgs.Empty);

        if (HasQrCodeBytes)
        {
            StartQrCountdown(FirstQrLifetimeSeconds);
        }

        if (_provider is IWhatsAppProvider waProvider)
        {
            try
            {
                if (_provider.ConnectionState != ConnectionState.Connected)
                {
                    await _provider.ConnectAsync();
                }
                await waProvider.AuthenticateAsync();
            }
            catch (Exception ex)
            {
                _dispatcherService.Enqueue(() =>
                {
                    HasError = true;
                    ErrorMessage = ex.Message;
                });
            }
        }
    }

    public void PrepareForReauthentication()
    {
        _dispatcherService.Enqueue(() =>
        {
            StopQrCountdown();
            _isFirstQrInSession = true;
            _stalledRefreshSeconds = 0;
            _isRefreshingSession = false;
            IsWelcomeVisible = true;
            IsQrLinkVisible = false;
            IsConnecting = false;
            ShowQrCode = false;
            HasError = false;
            ErrorMessage = string.Empty;
            QrCodeBytes = null;
            IsSyncing = false;
            IsInitialSyncing = false;
            Chats.Clear();
        });
    }

    [RelayCommand]
    private async Task RetryConnectAsync()
    {
        if (_provider is IWhatsAppProvider waProvider)
        {
            await waProvider.DisconnectAsync();
            await ConnectAndAuthenticateAsync(waProvider);
        }
    }

    [RelayCommand]
    private async Task UseTestingModeAsync()
    {
        _settingsService.SetValue("WhatsAppTestingMode", true);
        await Task.CompletedTask;
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key == "WhatsAppTestingMode")
        {
            _dispatcherService.Enqueue(async () =>
            {
                await HandleTestingModeToggledAsync();
            });
        }
    }

    private async Task HandleTestingModeToggledAsync()
    {
        bool isTesting = _settingsService.GetValue<bool>("WhatsAppTestingMode", false);
        if (_provider is IWhatsAppProvider waProvider)
        {
            if (isTesting)
            {
                // Testing mode enabled: disconnect live client, clear overlays and load test chats
                try { await waProvider.DisconnectAsync(); } catch { }

                IsWelcomeVisible = false;
                IsQrLinkVisible = false;
                ShowQrCode = false;
                IsInitialSyncing = false;
                IsSyncing = false;
                IsConnecting = false;
                HasError = false;
                ErrorMessage = string.Empty;

                Chats.Clear();
                RequestCloseDetail?.Invoke(this, EventArgs.Empty);

                await LoadChatsAsync();
            }
            else
            {
                // Testing mode disabled: check if real authenticated session exists
                bool isAuth = await Task.Run(() => waProvider.IsAuthenticated);

                Chats.Clear();
                RequestCloseDetail?.Invoke(this, EventArgs.Empty);

                if (isAuth)
                {
                    // Live session exists: reconnect real client and load real chats
                    await ConnectAndAuthenticateAsync(waProvider);
                }
                else
                {
                    // No live session: tear down immediately to unauthenticated Welcome screen
                    IsConnecting = false;
                    ShowQrCode = false;
                    HasError = false;
                    ErrorMessage = string.Empty;
                    QrCodeBytes = null;
                    IsSyncing = false;
                    IsInitialSyncing = false;
                    IsWelcomeVisible = true;
                    IsQrLinkVisible = false;

                    try { await waProvider.DisconnectAsync(); } catch { }
                    _ = Task.Run(() => waProvider.ConnectAsync());
                }
            }
        }
    }

    private void StartSyncWatchdog()
    {
        _initialSyncStartTime = DateTimeOffset.UtcNow;
        _syncWatchdogTimer?.Dispose();
        _syncWatchdogTimer = new System.Threading.Timer(OnSyncWatchdogTick, null, 8000, 2000);
    }

    private void ResetSyncWatchdog()
    {
        if (IsInitialSyncing)
        {
            _syncWatchdogTimer?.Change(6000, 2000);
        }
    }

    private void OnSyncWatchdogTick(object? state)
    {
        if (!IsInitialSyncing)
        {
            _syncWatchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        var elapsedSec = (DateTimeOffset.UtcNow - _initialSyncStartTime).TotalSeconds;
        var cached = (_provider as IWhatsAppProvider)?.GetCachedChats();
        bool hasChats = (cached != null && cached.Count > 0) || Chats.Count > 0;

        // Dismiss if chunks have completed/paused for at least 6s of complete silence and we have cached chats, or if hard timeout (45s) elapsed
        if ((hasChats && elapsedSec >= 6) || elapsedSec >= 45)
        {
            CompleteInitialSync();
        }
    }

    private void CompleteInitialSync()
    {
        _dispatcherService.Enqueue(async () =>
        {
            if (!IsInitialSyncing) return;
            _syncWatchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            InitialSyncProgress = 100;
            InitialSyncText = "Finishing sync...";

            // Ensure chats and recent messages are fully loaded into the collection before dismissing overlay
            await LoadChatsAsync();

            IsInitialSyncing = false;
            IsSyncing = false;
            SyncStatusText = string.Empty;
            RequestDrillInToMain?.Invoke(this, EventArgs.Empty);
        });
    }

    private void ArmInlineSyncWatchdog(int idleSeconds = 15)
    {
        _lastInlineSyncActivity = DateTimeOffset.UtcNow;
        int delayMs = Math.Max(1500, idleSeconds * 1000);
        if (_inlineSyncWatchdogTimer == null)
        {
            _inlineSyncWatchdogTimer = new System.Threading.Timer(OnInlineSyncWatchdogTick, null, delayMs, Timeout.Infinite);
        }
        else
        {
            _inlineSyncWatchdogTimer.Change(delayMs, Timeout.Infinite);
        }
    }

    private void DisarmInlineSyncWatchdog()
    {
        _inlineSyncWatchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnInlineSyncWatchdogTick(object? state)
    {
        if (!IsSyncing || IsInitialSyncing)
        {
            return;
        }

        if (_provider is IWhatsAppProvider waProvider)
        {
            var (nativeSyncing, nativeProgress) = waProvider.GetNativeSyncStatus();
            if (nativeSyncing)
            {
                _dispatcherService.Enqueue(() =>
                {
                    if (!IsSyncing || IsInitialSyncing) return;
                    if (nativeProgress > 0 && nativeProgress > SyncProgress)
                    {
                        SyncProgress = nativeProgress;
                        IsSyncIndeterminate = false;
                        SyncStatusText = $"Downloading recent messages ({nativeProgress}%)";
                    }
                    ArmInlineSyncWatchdog(10);
                });
                return;
            }
        }

        _dispatcherService.Enqueue(async () =>
        {
            if (!IsSyncing || IsInitialSyncing) return;
            IsSyncing = false;
            IsSyncIndeterminate = false;
            SyncProgress = 0;
            SyncStatusText = string.Empty;
            await LoadChatsAsync();
        });
    }

    private void OnSyncProgressChanged(object? sender, int progress)
    {
        _dispatcherService.Enqueue(async () =>
        {
            if (IsInitialSyncing)
            {
                ResetSyncWatchdog();
                if (progress >= 100)
                {
                    SyncProgress = 100;
                    CompleteInitialSync();
                }
                else if (progress == -2)
                {
                    IsInitialSyncIndeterminate = true;
                    InitialSyncText = "Reconnecting to continue sync...";
                }
                else if (progress >= 0)
                {
                    SyncProgress = progress;
                    InitialSyncProgress = progress;
                    InitialSyncText = "Downloading your recent messages";
                }
            }
            else
            {
                if (progress == -2)
                {
                    IsSyncing = true;
                    IsSyncIndeterminate = true;
                    SyncStatusText = "Reconnecting to continue sync...";
                    ArmInlineSyncWatchdog(15);
                }
                else if (progress >= 100 || progress < 0)
                {
                    DisarmInlineSyncWatchdog();
                    IsSyncing = false;
                    IsSyncIndeterminate = false;
                    SyncProgress = 0;
                    SyncStatusText = string.Empty;
                    if (progress >= 100)
                    {
                        await LoadChatsAsync();
                    }
                }
                else if (Chats.Count == 0 && _provider.ConnectionState == ConnectionState.Connected)
                {
                    DisarmInlineSyncWatchdog();
                    IsInitialSyncing = true;
                    StartSyncWatchdog();
                    SyncProgress = progress;
                    InitialSyncProgress = progress;
                    InitialSyncText = "Downloading your recent messages";
                }
                else
                {
                    IsSyncing = true;
                    SyncProgress = progress;
                    IsSyncIndeterminate = progress <= 0;
                    SyncStatusText = progress > 0
                        ? $"Downloading recent messages ({progress}%)"
                        : "Downloading recent messages...";
                    ArmInlineSyncWatchdog(15);
                }
            }
        });
    }

    private void OnChatUpdated(object? sender, ChatUpdatedEventArgs e)
    {
        if (IsInitialSyncing && Chats.Count == 0)
        {
            ResetSyncWatchdog();
            var cached = (_provider as IWhatsAppProvider)?.GetCachedChats();
            if (cached != null && cached.Count > 0)
            {
                _dispatcherService.Enqueue(() =>
                {
                    if (IsInitialSyncing && Chats.Count == 0)
                    {
                        InitialSyncText = $"Downloading your recent messages ({cached.Count} chats)...";
                    }
                });
            }
            return;
        }

        ScheduleDebouncedLoadChats(300);
    }

    private void OnChatMarkedRead(object? sender, string chatId)
    {
        _dispatcherService.Enqueue(() =>
        {
            var existing = Chats.FirstOrDefault(c => ConversationViewModel.ChatIdsMatch(c.Id, chatId));
            if (existing != null)
            {
                existing.UnreadCount = 0;
                existing.RefreshLastMessagePreview();
            }
        });
    }

    private void OnChatPictureUpdated(object? sender, (string ChatId, string? PictureUrl) e)
    {
        if (string.IsNullOrWhiteSpace(e.ChatId)) return;
        _dispatcherService.Enqueue(() =>
        {
            foreach (var c in Chats)
            {
                if (ConversationViewModel.ChatIdsMatch(c.Id, e.ChatId))
                {
                    if (!string.Equals(c.PictureUrl, e.PictureUrl, StringComparison.Ordinal))
                    {
                        c.PictureUrl = e.PictureUrl;
                    }
                }
            }
            foreach (var c in _allMasterChats)
            {
                if (ConversationViewModel.ChatIdsMatch(c.Id, e.ChatId))
                {
                    if (!string.Equals(c.PictureUrl, e.PictureUrl, StringComparison.Ordinal))
                    {
                        c.PictureUrl = e.PictureUrl;
                    }
                }
            }
        });
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (IsInitialSyncing && Chats.Count == 0)
        {
            ResetSyncWatchdog();
            return;
        }

        _dispatcherService.Enqueue(() =>
        {
            var msg = e.Message;
            if (!msg.IsRevoked && (msg.Content == null || (msg.Content.Type == MessageContentType.Text && string.IsNullOrWhiteSpace(msg.Content.Text))))
            {
                return;
            }

            var chat = FindChatForMessage(msg);

            if (chat != null)
            {
                bool matchesExistingLast = chat.LastMessage != null && (
                    chat.LastMessage.Id == msg.Id
                    || (msg.IsOutgoing && chat.LastMessage.IsOutgoing &&
                        (chat.LastMessage.Id.StartsWith("pending_") || chat.LastMessage.Status == MessageStatus.Sending) &&
                        Math.Abs((msg.Timestamp - chat.LastMessage.Timestamp).TotalSeconds) <= 120 &&
                        (chat.LastMessage.Content?.Text == msg.Content?.Text ||
                         (chat.LastMessage.Content?.Type != MessageContentType.Text && chat.LastMessage.Content?.Type == msg.Content?.Type))));

                if (matchesExistingLast && chat.LastMessage != null)
                {
                    chat.LastMessage.Id = msg.Id;
                    if (msg.Status >= chat.LastMessage.Status)
                    {
                        chat.LastMessage.Status = msg.Status;
                    }
                    if (msg.IsRevoked)
                    {
                        chat.LastMessage.IsRevoked = true;
                        chat.LastMessage.IsEdited = false;
                        if (chat.LastMessage.Content != null)
                        {
                            chat.LastMessage.Content.IsRevoked = true;
                            chat.LastMessage.Content.Type = MessageContentType.Text;
                            chat.LastMessage.Content.Text = msg.Content?.Text ?? (chat.LastMessage.IsOutgoing ? "You deleted this message" : "This message was deleted");
                        }
                    }
                    else if (msg.IsEdited && chat.LastMessage.Content != null && msg.Content != null)
                    {
                        chat.LastMessage.Content.Text = msg.Content.Text;
                        chat.LastMessage.Content.Caption = msg.Content.Caption;
                        chat.LastMessage.IsEdited = true;
                    }
                    chat.RefreshLastMessagePreview();
                }
                else if (chat.LastMessage == null || msg.Timestamp >= chat.LastMessageTime)
                {
                    chat.LastMessage = msg;
                    chat.LastMessageTime = msg.Timestamp;
                    var convVm = App.Current.Services.GetService<ConversationViewModel>();
                    bool isCurrentChatActive = convVm?.CurrentChat != null && ConversationViewModel.ChatIdsMatch(convVm.CurrentChat.Id, chat.Id);
                    if (!msg.IsOutgoing && !isCurrentChatActive)
                    {
                        chat.UnreadCount++;
                    }
                    else if (isCurrentChatActive)
                    {
                        chat.UnreadCount = 0;
                    }
                    chat.RefreshLastMessagePreview();
                    SortChatsInPlace();
                }

                if (_chatRepository != null)
                {
                    _ = _chatRepository.SaveAsync(chat);
                }
            }
            else
            {
                // Chat not in list yet (new contact or unmapped chat) - reload chats promptly
                ScheduleDebouncedLoadChats(100);
            }
        });
    }

    private Chat? FindChatForMessage(Message msg)
    {
        if (msg == null) return null;

        // 1. Direct match on ChatId
        var chat = Chats.FirstOrDefault(c => ConversationViewModel.ChatIdsMatch(c.Id, msg.ChatId));
        if (chat != null) return chat;

        // 2. Fast digit matching on phone number
        var msgDigits = ExtractDigits(msg.ChatId);
        if (msgDigits.Length >= 7)
        {
            chat = Chats.FirstOrDefault(c => !c.IsGroup && ExtractDigits(c.Id) == msgDigits);
            if (chat != null) return chat;
        }

        // 3. SenderName match for 1-on-1 chats
        if (!string.IsNullOrEmpty(msg.SenderName) && msg.SenderName != "You")
        {
            chat = Chats.FirstOrDefault(c => !c.IsGroup && string.Equals(c.Title, msg.SenderName, StringComparison.OrdinalIgnoreCase));
            if (chat != null) return chat;
        }

        // 4. SenderId match
        if (!string.IsNullOrEmpty(msg.SenderId) && msg.SenderId != "me")
        {
            chat = Chats.FirstOrDefault(c => ConversationViewModel.ChatIdsMatch(c.Id, msg.SenderId));
            if (chat != null) return chat;
        }

        // 5. Active conversation fallback for outgoing messages
        if (msg.IsOutgoing)
        {
            var convVm = App.Current.Services.GetService<ConversationViewModel>();
            if (convVm?.CurrentChat != null && !convVm.CurrentChat.IsGroup)
            {
                chat = Chats.FirstOrDefault(c => ConversationViewModel.ChatIdsMatch(c.Id, convVm.CurrentChat.Id));
            }
        }

        return chat;
    }

    private static string ExtractDigits(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var userPart = text.Split('@')[0].Split(':')[0];
        return new string(userPart.Where(char.IsDigit).ToArray());
    }

    private readonly object _chatUpdateDebounceLock = new();
    private System.Threading.Timer? _chatUpdateDebounceTimer;

    private void ScheduleDebouncedLoadChats(int delayMs = 250)
    {
        lock (_chatUpdateDebounceLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (_chatUpdateFirstPending == DateTimeOffset.MinValue)
            {
                _chatUpdateFirstPending = now;
            }

            // Cap starvation under continuous sync bursts at 250ms from first dirty signal
            var elapsedMs = (int)(now - _chatUpdateFirstPending).TotalMilliseconds;
            int remainingCapMs = Math.Max(0, 250 - elapsedMs);
            int targetDelayMs = Math.Min(delayMs, remainingCapMs);

            if (_chatUpdateDebounceTimer == null)
            {
                _chatUpdateDebounceTimer = new System.Threading.Timer(_ =>
                {
                    lock (_chatUpdateDebounceLock)
                    {
                        _chatUpdateFirstPending = DateTimeOffset.MinValue;
                    }
                    _ = LoadChatsAsync();
                }, null, targetDelayMs, Timeout.Infinite);
            }
            else if (remainingCapMs == 0 || delayMs < 150)
            {
                _chatUpdateDebounceTimer.Change(targetDelayMs, Timeout.Infinite);
            }
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        if (e.State == ConnectionState.Connected)
        {
            bool wasInSetup = IsQrLinkVisible || ShowQrCode || IsWelcomeVisible;
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                if (IsQrLinkVisible || ShowQrCode || IsWelcomeVisible)
                {
                    IsWelcomeVisible = false;
                    IsQrLinkVisible = false;
                    ShowQrCode = false;
                    IsInitialSyncing = true;
                    InitialSyncProgress = 0;
                    InitialSyncText = "Downloading your recent messages";
                    StartSyncWatchdog();
                    RequestDrillInToSync?.Invoke(this, EventArgs.Empty);
                }
                HasError = false;
            });
            if (!wasInSetup && !IsInitialSyncing)
            {
                if (IsSyncing)
                {
                    // Check native sync status after brief grace window instead of unconditionally hiding early
                    ArmInlineSyncWatchdog(6);
                }
                _ = LoadChatsAsync();
            }
        }
        else if (e.State == ConnectionState.Error)
        {
            DisarmInlineSyncWatchdog();
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                ShowQrCode = false;
                IsSyncing = false;
                IsInitialSyncing = false;
                HasError = true;
                if (!string.IsNullOrEmpty(e.ErrorMessage))
                {
                    ErrorMessage = e.ErrorMessage;
                }
                else if (string.IsNullOrEmpty(ErrorMessage))
                {
                    ErrorMessage = "Unable to connect to WhatsApp backend. Check your network or use Testing Mode.";
                }
            });
        }
        else if (e.State == ConnectionState.Disconnected)
        {
            _dispatcherService.Enqueue(() =>
            {
                IsConnecting = false;
                IsSyncing = false;
                IsInitialSyncing = false;
            });
        }
    }

    public async Task LoadChatsAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _isLoadingChats, 1) != 0)
        {
            _needsReloadChats = true;
            return;
        }

        try
        {
            do
            {
                _needsReloadChats = false;
                await ExecuteLoadChatsAsync();
            } while (_needsReloadChats);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isLoadingChats, 0);
        }
    }

    private async Task ExecuteLoadChatsAsync()
    {
        try
        {
            // 1. Show cached chats immediately from SQLite
            if (Chats.Count == 0 && _chatRepository != null)
            {
                try
                {
                    var cached = await _chatRepository.GetAllAsync(0, 100);
                    var validCached = cached.Where(c => !string.IsNullOrWhiteSpace(c.Id) && (c.LastMessage != null || !string.IsNullOrWhiteSpace(c.Title))).ToList();

                    if (validCached.Count > 0 && Chats.Count == 0)
                    {
                        foreach (var c in validCached)
                        {
                            c.Title = ResolveChatTitle(c);
                        }
                        var sortedCached = validCached.OrderByDescending(c => c.LastMessageTime).ToList();

                        _dispatcherService.Enqueue(() =>
                        {
                            if (Chats.Count == 0)
                            {
                                foreach (var c in sortedCached)
                                {
                                    Chats.Add(c);
                                }
                            }
                        });
                    }
                }
                catch { }
            }

            // Trigger contact rebuild asynchronously in background; never block chat display
            if (_allContacts.Count == 0)
            {
                _ = Task.Run(RebuildContactsAsync);
            }

            var chats = await _provider.GetChatsAsync();
            var validChats = chats.Where(c => !string.IsNullOrWhiteSpace(c.Id) && (c.LastMessage != null || !string.IsNullOrWhiteSpace(c.Title))).ToList();

            // Resolve titles on background thread BEFORE dispatching to UI
            foreach (var incoming in validChats)
            {
                incoming.Title = ResolveChatTitle(incoming);
            }
            var sortedIncoming = validChats.OrderByDescending(c => c.LastMessageTime).ToList();

            _dispatcherService.Enqueue(() =>
            {
                if (Chats.Count == 0)
                {
                    foreach (var incoming in sortedIncoming)
                    {
                        Chats.Add(incoming);
                    }
                }
                else
                {
                    var newChatIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in validChats)
                    {
                        if (!string.IsNullOrEmpty(c.Id))
                        {
                            newChatIds.Add(c.Id);
                            var clean = c.Id.StartsWith("wa_") ? c.Id.Substring(3) : c.Id;
                            newChatIds.Add(clean);
                            newChatIds.Add("wa_" + clean);
                        }
                    }

                    // Remove chats that no longer exist
                    if (validChats.Count > 0)
                    {
                        for (int i = Chats.Count - 1; i >= 0; i--)
                        {
                            var cId = Chats[i].Id;
                            var clean = cId.StartsWith("wa_") ? cId.Substring(3) : cId;
                            bool existsInValid = newChatIds.Contains(cId) || newChatIds.Contains(clean) || newChatIds.Contains("wa_" + clean)
                                || validChats.Any(v => ConversationViewModel.ChatIdsMatch(v.Id, cId));
                            if (!existsInValid)
                            {
                                Chats.RemoveAt(i);
                            }
                        }
                    }

                    // Pre-index existing chats safely for O(1) lookups
                    var existingMap = new Dictionary<string, Chat>(StringComparer.OrdinalIgnoreCase);
                    void IndexChat(Chat c)
                    {
                        if (string.IsNullOrEmpty(c.Id)) return;
                        existingMap[c.Id] = c;
                        var clean = c.Id.StartsWith("wa_") ? c.Id.Substring(3) : c.Id;
                        existingMap[clean] = c;
                        existingMap["wa_" + clean] = c;
                        var noDev = clean.Split(':')[0];
                        if (clean.Contains('@'))
                        {
                            var user = noDev.Split('@')[0];
                            var server = clean.Split('@')[1];
                            existingMap[$"{user}@{server}"] = c;
                            existingMap[$"wa_{user}@{server}"] = c;
                            var digits = new string(user.Where(char.IsDigit).ToArray());
                            if (digits.Length >= 7)
                            {
                                existingMap[digits] = c;
                                existingMap[$"{digits}@s.whatsapp.net"] = c;
                                existingMap[$"wa_{digits}@s.whatsapp.net"] = c;
                            }
                        }
                    }

                    foreach (var c in Chats)
                    {
                        IndexChat(c);
                    }

                    Chat? FindExisting(string id)
                    {
                        if (string.IsNullOrEmpty(id)) return null;
                        if (existingMap.TryGetValue(id, out var found)) return found;
                        var clean = id.StartsWith("wa_") ? id.Substring(3) : id;
                        if (existingMap.TryGetValue(clean, out found)) return found;
                        if (existingMap.TryGetValue("wa_" + clean, out found)) return found;
                        var noDev = clean.Split(':')[0];
                        if (clean.Contains('@'))
                        {
                            var user = noDev.Split('@')[0];
                            var server = clean.Split('@')[1];
                            if (existingMap.TryGetValue($"{user}@{server}", out found)) return found;
                            var digits = new string(user.Where(char.IsDigit).ToArray());
                            if (digits.Length >= 7 && existingMap.TryGetValue(digits, out found)) return found;
                        }
                        return Chats.FirstOrDefault(c => ConversationViewModel.ChatIdsMatch(c.Id, id));
                    }

                    // Update existing or add new
                    foreach (var incoming in validChats)
                    {
                        var existing = FindExisting(incoming.Id);
                        if (existing != null)
                        {
                            if (existing.Title != incoming.Title && incoming.Title != "WhatsApp Contact")
                            {
                                existing.Title = incoming.Title;
                            }
                            if (existing.LastMessage == null || incoming.LastMessageTime >= existing.LastMessageTime)
                            {
                                existing.LastMessageTime = incoming.LastMessageTime;
                                if (incoming.LastMessage != null)
                                {
                                    if (existing.LastMessage != null &&
                                        existing.LastMessage.Id == incoming.LastMessage.Id &&
                                        existing.LastMessage.Status > incoming.LastMessage.Status)
                                    {
                                        incoming.LastMessage.Status = existing.LastMessage.Status;
                                    }
                                    existing.LastMessage = incoming.LastMessage;
                                    existing.RefreshLastMessagePreview();
                                }
                            }

                            var convVm = App.Current.Services.GetService<ConversationViewModel>();
                            bool isCurrentChatActive = convVm?.CurrentChat != null && ConversationViewModel.ChatIdsMatch(convVm.CurrentChat.Id, existing.Id);
                            if (isCurrentChatActive)
                            {
                                existing.UnreadCount = 0;
                            }
                            else if (existing.LastMessage == null || incoming.LastMessageTime >= existing.LastMessageTime)
                            {
                                existing.UnreadCount = incoming.UnreadCount;
                            }
                            if (!string.IsNullOrEmpty(incoming.PictureUrl) &&
                                (string.IsNullOrEmpty(existing.PictureUrl) ||
                                 !incoming.PictureUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                                 existing.PictureUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) &&
                                existing.PictureUrl != incoming.PictureUrl)
                            {
                                existing.PictureUrl = incoming.PictureUrl;
                            }
                        }
                        else
                        {
                            Chats.Add(incoming);
                            IndexChat(incoming);
                        }
                    }

                    SortChatsInPlace();
                }

                _allMasterChats.Clear();
                _allMasterChats.AddRange(Chats);
            });

            if (_provider is IWhatsAppProvider waPicProvider && sortedIncoming.Count > 0)
            {
                _ = Task.Run(() => waPicProvider.RefreshMissingProfilePicturesAsync(sortedIncoming));
            }
        }
        catch (Exception)
        {
            _dispatcherService.Enqueue(() => LoadMockData());
        }
    }

    public void SortChatsInPlace()
    {
        var targetOrder = Chats.OrderByDescending(c => c.LastMessageTime).ToList();
        bool isAlreadySorted = true;
        for (int i = 0; i < targetOrder.Count; i++)
        {
            if (!ReferenceEquals(Chats[i], targetOrder[i]))
            {
                isAlreadySorted = false;
                break;
            }
        }
        if (isAlreadySorted) return;

        for (int i = 0; i < targetOrder.Count; i++)
        {
            var item = targetOrder[i];
            int currentIdx = Chats.IndexOf(item);
            if (currentIdx != i && currentIdx >= 0)
            {
                Chats.Move(currentIdx, i);
            }
        }
    }

    private string ResolveChatTitle(Chat chat)
    {
        if (chat.IsGroup) return chat.Title;

        if (!string.IsNullOrEmpty(chat.Id))
        {
            lock (_contactNamesById)
            {
                if (_contactNamesById.TryGetValue(chat.Id, out var name))
                {
                    return name;
                }
            }
        }

        var candidateDigits = new List<string>();
        if (!string.IsNullOrEmpty(chat.Title) && (chat.Title.StartsWith('+') || chat.Title.All(char.IsDigit)))
        {
            var tDigits = new string(chat.Title.Where(char.IsDigit).ToArray());
            if (tDigits.Length >= 7) candidateDigits.Add(tDigits);
        }

        string? idUserDigits = null;
        if (!string.IsNullOrEmpty(chat.Id) && !chat.Id.EndsWith("@lid", StringComparison.OrdinalIgnoreCase))
        {
            var userPart = chat.Id.Contains('@') ? chat.Id.Split('@')[0] : chat.Id;
            var cleanPart = userPart.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? userPart[3..] : userPart;
            var digitsOnly = cleanPart.Split(':')[0];
            var idDigits = new string(digitsOnly.Where(char.IsDigit).ToArray());
            if (idDigits.Length >= 7)
            {
                candidateDigits.Add(idDigits);
                idUserDigits = idDigits;
            }
        }

        lock (_contactNamesByPhoneDigits)
        {
            foreach (var num in candidateDigits)
            {
                if (_contactNamesByPhoneDigits.TryGetValue(num, out var name))
                {
                    return name;
                }
                if (num.Length > 10 && _contactNamesByPhoneDigits.TryGetValue(num.Substring(num.Length - 10), out var n10))
                {
                    return n10;
                }
            }
        }

        // If chat already has a valid saved contact name (not a phone number, not "WhatsApp Contact"), preserve it!
        if (!string.IsNullOrWhiteSpace(chat.Title) &&
            !chat.Title.StartsWith('+') &&
            chat.Title != "WhatsApp Contact" &&
            !chat.Title.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c == '-' || c == '(' || c == ')'))
        {
            return chat.Title;
        }

        // Unsaved 1-on-1 contact: format international phone number matching WhatsApp
        string? phone = null;
        var waProvider = _provider as IWhatsAppProvider;
        if (waProvider != null && !string.IsNullOrEmpty(chat.Id))
        {
            phone = waProvider.GetPhoneNumber(chat.Id);
        }
        if (string.IsNullOrEmpty(phone) && !string.IsNullOrEmpty(idUserDigits))
        {
            phone = $"+{idUserDigits}";
        }
        if (string.IsNullOrEmpty(phone) && !string.IsNullOrEmpty(chat.Title))
        {
            var titleDigits = new string(chat.Title.Where(char.IsDigit).ToArray());
            if (titleDigits.Length >= 7)
            {
                phone = chat.Title.StartsWith('+') ? chat.Title : $"+{titleDigits}";
            }
        }

        if (!string.IsNullOrEmpty(phone))
        {
            return ConversationViewModel.FormatPhoneNumber(phone);
        }

        return chat.Title;
    }

    public async Task RebuildContactsAsync()
    {
        var contactsMap = new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);

        // 1. Load from IContactRepository
        try
        {
            var repo = App.Current.Services.GetService<People.Core.Interfaces.Repositories.IContactRepository>();
            if (repo != null)
            {
                var dbContacts = await repo.GetAllAsync();
                foreach (var c in dbContacts)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name) || !string.IsNullOrWhiteSpace(c.PhoneNumber))
                    {
                        var key = !string.IsNullOrWhiteSpace(c.PhoneNumber) ? c.PhoneNumber : c.Id;
                        contactsMap[key] = c;
                    }
                }
            }
        }
        catch
        {
            // Ignore if DB not ready
        }

        // 2. Load from Windows ContactStore (Windows Contacts) with timeout to prevent cold-launch blocking
        try
        {
            var storeTask = Windows.ApplicationModel.Contacts.ContactManager.RequestStoreAsync(
                Windows.ApplicationModel.Contacts.ContactStoreAccessType.AllContactsReadOnly).AsTask();
            if (await Task.WhenAny(storeTask, Task.Delay(1500)) == storeTask)
            {
                var contactStore = await storeTask;
                if (contactStore != null)
                {
                    var winContacts = await contactStore.FindContactsAsync();
                    foreach (var wc in winContacts)
                    {
                        var name = wc.DisplayName;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = $"{wc.FirstName} {wc.LastName}".Trim();
                        }
                        var phone = wc.Phones?.FirstOrDefault()?.Number;
                        if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(phone))
                        {
                            var cleanPhone = phone != null ? new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray()) : null;
                            var contactId = !string.IsNullOrEmpty(cleanPhone)
                                ? $"{cleanPhone.TrimStart('+')}@s.whatsapp.net"
                                : wc.Id ?? Guid.NewGuid().ToString();

                            var c = new Contact
                            {
                                Id = contactId,
                                Name = !string.IsNullOrWhiteSpace(name) ? name : (phone ?? "Unknown"),
                                PhoneNumber = phone,
                                PlatformId = "WhatsApp"
                            };

                            var key = !string.IsNullOrWhiteSpace(cleanPhone) ? cleanPhone : c.Id;
                            if (!contactsMap.ContainsKey(key))
                            {
                                contactsMap[key] = c;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Windows Contacts store access may be restricted
        }

        // 2.5. Load synced contacts from WhatsApp Provider
        if (_provider is IWhatsAppProvider waProvider)
        {
            try
            {
                var waContacts = await waProvider.GetContactsAsync();
                foreach (var c in waContacts)
                {
                    if (!string.IsNullOrWhiteSpace(c.Name) && c.Name != "WhatsApp Contact")
                    {
                        var key = !string.IsNullOrWhiteSpace(c.PhoneNumber) ? c.PhoneNumber : c.Id;
                        contactsMap[key] = c;
                    }
                }
            }
            catch { }
        }

        // Note: We deliberately do NOT extract unsaved phone numbers from chats here,
        // so the new chat flyout only contains saved contacts from address book & WhatsApp.

        var byDigits = new Dictionary<string, string>(StringComparer.Ordinal);
        var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in contactsMap.Values)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
            {
                if (!string.IsNullOrWhiteSpace(c.Id))
                {
                    byId[c.Id] = c.Name;
                }
                if (!string.IsNullOrWhiteSpace(c.PhoneNumber))
                {
                    var digits = new string(c.PhoneNumber.Where(char.IsDigit).ToArray());
                    if (digits.Length >= 7)
                    {
                        byDigits[digits] = c.Name;
                        if (digits.Length > 10)
                        {
                            byDigits[digits.Substring(digits.Length - 10)] = c.Name;
                        }
                    }
                }
            }
        }

        lock (_contactNamesById)
        {
            _contactNamesById.Clear();
            foreach (var kvp in byId) _contactNamesById[kvp.Key] = kvp.Value;
        }
        lock (_contactNamesByPhoneDigits)
        {
            _contactNamesByPhoneDigits.Clear();
            foreach (var kvp in byDigits) _contactNamesByPhoneDigits[kvp.Key] = kvp.Value;
        }

        var sorted = contactsMap.Values
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id))
            .OrderBy(c => c.Name ?? string.Empty)
            .ToList();

        lock (_contactsLock)
        {
            _allContacts.Clear();
            _allContacts.AddRange(sorted);
        }
        _dispatcherService.Enqueue(() =>
        {
            foreach (var chat in Chats)
            {
                var resolved = ResolveChatTitle(chat);
                if (!string.IsNullOrEmpty(resolved) && chat.Title != resolved)
                {
                    chat.Title = resolved;
                }
            }
            if (FilteredContacts.Count > 0)
            {
                FilterContacts(string.Empty);
            }
        });
    }

    private readonly List<Chat> _allMasterChats = new();
    private string _currentChatFilter = string.Empty;

    public void FilterChats(string query)
    {
        _currentChatFilter = query?.Trim() ?? string.Empty;
        ApplyChatFilter();
    }

    private void ApplyChatFilter()
    {
        _dispatcherService.Enqueue(() =>
        {
            if (string.IsNullOrWhiteSpace(_currentChatFilter))
            {
                if (_allMasterChats.Count > 0 && (_allMasterChats.Count != Chats.Count || !Chats.SequenceEqual(_allMasterChats)))
                {
                    Chats.Clear();
                    foreach (var c in _allMasterChats)
                    {
                        Chats.Add(c);
                    }
                }
                return;
            }

            if (_allMasterChats.Count == 0 && Chats.Count > 0)
            {
                _allMasterChats.AddRange(Chats);
            }

            var query = _currentChatFilter;
            var matches = _allMasterChats.Where(c =>
                (!string.IsNullOrEmpty(c.Title) && c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.Id) && c.Id.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(c.LastMessagePreview) && c.LastMessagePreview.Contains(query, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            Chats.Clear();
            foreach (var m in matches)
            {
                Chats.Add(m);
            }
        });
    }

    public void FilterContacts(string query)
    {
        FilteredContacts.Clear();

        var trimmed = query?.Trim() ?? string.Empty;

        // If user typed numbers, offer starting direct chat with that number
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length >= 5)
        {
            var formattedNumber = trimmed.StartsWith("+") ? $"+{digits}" : $"+{digits}";
            FilteredContacts.Add(new Contact
            {
                Id = $"{digits}@s.whatsapp.net",
                Name = $"Message {formattedNumber}",
                PlatformId = "WhatsApp",
                PhoneNumber = formattedNumber
            });
        }

        List<Contact> contactsSnapshot;
        lock (_contactsLock)
        {
            contactsSnapshot = _allContacts.Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id)).ToList();
        }

        foreach (var c in contactsSnapshot)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Id)) continue;

            var name = c.Name ?? string.Empty;
            var phone = c.PhoneNumber ?? string.Empty;

            bool matches = string.IsNullOrEmpty(trimmed) ||
                name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(phone) && phone.Contains(trimmed, StringComparison.OrdinalIgnoreCase));

            if (matches)
            {
                if (!FilteredContacts.Any(existing => existing != null && existing.Id == c.Id))
                {
                    FilteredContacts.Add(c);
                }
            }
        }
    }

    public Chat GetOrCreateChatForContact(Contact contact)
    {
        var existing = Chats.FirstOrDefault(c => c.Id == contact.Id);
        if (existing != null)
        {
            return existing;
        }

        var title = contact.Name.StartsWith("Message +") ? contact.PhoneNumber ?? contact.Name : contact.Name;

        var newChat = new Chat
        {
            Id = contact.Id,
            PlatformId = "WhatsApp",
            Title = title,
            IsGroup = false,
            LastMessageTime = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        return newChat;
    }

    private void OnQrCodeReceived(object? sender, string qrString)
    {
        _dispatcherService.Enqueue(() =>
        {
            IsConnecting = false;
            HasError = false;
            _isRefreshingSession = false;
            _stalledRefreshSeconds = 0;
            QrCodeBytes = GenerateQrCodeBytes(qrString);
            if (!IsWelcomeVisible)
            {
                IsQrLinkVisible = true;
            }
            int ttl = _isFirstQrInSession ? FirstQrLifetimeSeconds : SubsequentQrLifetimeSeconds;
            _isFirstQrInSession = false;
            StartQrCountdown(ttl);
        });
    }

    private void StartQrCountdown(int ttlSeconds = 20)
    {
        _qrCountdownTimer?.Dispose();
        _qrSecondsRemaining = ttlSeconds;
        _stalledRefreshSeconds = 0;
        UpdateQrCountdownDisplay();

        _qrCountdownTimer = new System.Threading.Timer(_ =>
        {
            _dispatcherService.Enqueue(() =>
            {
                if (!IsQrLinkVisible || !HasQrCodeBytes)
                {
                    StopQrCountdown();
                    return;
                }

                if (_qrSecondsRemaining > 0)
                {
                    _qrSecondsRemaining--;
                    UpdateQrCountdownDisplay();
                }
                else
                {
                    _stalledRefreshSeconds++;
                    UpdateQrCountdownDisplay();

                    // If stuck at "Refreshing" for >= 4 seconds, WhatsApp pairing rotation has exhausted or stalled.
                    // Automatically request a fresh QR session to resume rotation seamlessly.
                    if (_stalledRefreshSeconds >= 4)
                    {
                        if (!_isRefreshingSession)
                        {
                            _isRefreshingSession = true;
                            _isFirstQrInSession = true;
                            _ = RefreshQrSessionAsync();
                        }
                        else if (_stalledRefreshSeconds >= 15)
                        {
                            _isRefreshingSession = false;
                            _stalledRefreshSeconds = 0;
                        }
                    }
                }
            });
        }, null, 1000, 1000);
    }

    private void UpdateQrCountdownDisplay()
    {
        if (_qrSecondsRemaining > 0)
        {
            QrRefreshCountdownText = $"Refreshes in {_qrSecondsRemaining}s";
        }
        else
        {
            QrRefreshCountdownText = "Refreshing";
        }
    }

    private void StopQrCountdown()
    {
        _qrCountdownTimer?.Dispose();
        _qrCountdownTimer = null;
        _stalledRefreshSeconds = 0;
        _isRefreshingSession = false;
        QrRefreshCountdownText = string.Empty;
    }

    private async Task RefreshQrSessionAsync()
    {
        if (_provider is IWhatsAppProvider waProvider)
        {
            try
            {
                await waProvider.DisconnectAsync();
                await waProvider.ConnectAsync();
                await waProvider.AuthenticateAsync();
            }
            catch (Exception ex)
            {
                _dispatcherService.Enqueue(() =>
                {
                    _isRefreshingSession = false;
                    HasError = true;
                    ErrorMessage = ex.Message;
                });
            }
        }
    }

    private byte[] GenerateQrCodeBytes(string text)
    {
        using var qrGenerator = new QRCoder.QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new QRCoder.PngByteQRCode(qrData);
        return qrCode.GetGraphic(5);
    }

    private void LoadMockData()
    {
        foreach (var chat in People.Providers.WhatsApp.WhatsAppTestData.GetTestingChats())
        {
            Chats.Add(chat);
        }
        _allMasterChats.Clear();
        _allMasterChats.AddRange(Chats);
        _ = RebuildContactsAsync();
    }
}
