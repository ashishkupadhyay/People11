using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using People.Core.Interfaces;
using People.Core.Interfaces.Repositories;
using People.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace People.ViewModels;

public partial class ConversationViewModel : ViewModelBase
{
    private readonly IMessagingProvider _provider;
    private readonly IDispatcherService _dispatcherService;
    private readonly ISettingsService? _settingsService;

    [ObservableProperty]
    public partial bool IsDoodleWallpaperVisible { get; set; }

    public void UpdateDoodleWallpaperVisibility()
    {
        var enabled = _settingsService?.GetValue<bool>("WhatsAppDoodleWallpaper", false) ?? false;
        var isWhatsApp = CurrentChat != null && CurrentChat.PlatformId == "WhatsApp";
        IsDoodleWallpaperVisible = isWhatsApp && enabled;
    }

    [ObservableProperty]
    public partial Chat? CurrentChat { get; set; }

    [ObservableProperty]
    public partial string MessageText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<Message> Messages { get; set; } = new();

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingOlder { get; set; }

    public bool IsNotLoadingOlder => !IsLoadingOlder;

    partial void OnIsLoadingOlderChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotLoadingOlder));
    }

    [ObservableProperty]
    public partial string CurrentChatDateString { get; set; } = "Today";

    [ObservableProperty]
    public partial string HeaderTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? HeaderSubtitle { get; set; }

    [ObservableProperty]
    public partial string? SenderPushName { get; set; }

    public bool HasMoreOlderMessages { get; set; } = true;

    public event EventHandler? InitialMessagesLoaded;

    public ConversationViewModel(IMessagingProvider provider, IDispatcherService dispatcherService)
    {
        _provider = provider;
        _dispatcherService = dispatcherService;
        _settingsService = App.Current.Services.GetService<ISettingsService>();
        if (_settingsService != null)
        {
            _settingsService.SettingChanged += (s, key) =>
            {
                if (key == "WhatsAppDoodleWallpaper")
                {
                    _dispatcherService.Enqueue(UpdateDoodleWallpaperVisibility);
                }
            };
        }
        _provider.MessageReceived += OnMessageReceived;
        _provider.ChatUpdated += OnChatUpdated;
        if (_provider is IWhatsAppProvider waProvider)
        {
            waProvider.MediaDownloadProgress += OnMediaDownloadProgress;
        }
    }

    private void OnMediaDownloadProgress(object? sender, (string MessageId, long BytesReceived, long TotalBytes) e)
    {
        _dispatcherService.Enqueue(() =>
        {
            var msg = Messages.FirstOrDefault(m => m.Id == e.MessageId);
            if (msg?.Content != null && msg.Content.IsDownloading)
            {
                double pct = e.TotalBytes > 0 ? (double)e.BytesReceived / e.TotalBytes * 100.0 : 0.0;
                msg.Content.DownloadProgress = Math.Min(100.0, Math.Max(0.0, pct));
            }
        });
    }

    public event EventHandler<Message>? OlderMessagesLoaded;
    private CancellationTokenSource? _loadChatCts;
    private int _consecutiveEmptyOlderLoads;

    public void CancelLoading()
    {
        _loadChatCts?.Cancel();
        _loadChatCts?.Dispose();
        _loadChatCts = null;
    }

    public async void LoadChat(Chat chat)
    {
        _loadChatCts?.Cancel();
        _loadChatCts?.Dispose();
        _loadChatCts = new CancellationTokenSource();
        var token = _loadChatCts.Token;

        CurrentChat = chat;
        UpdateDoodleWallpaperVisibility();
        int unreadCount = chat.UnreadCount;
        if (unreadCount > 0)
        {
            chat.UnreadCount = 0;
            var chatRepo = App.Current.Services.GetService<IChatRepository>();
            if (chatRepo != null)
            {
                _ = Task.Run(async () =>
                {
                    try { await chatRepo.SaveAsync(chat); } catch { }
                });
            }
        }

        if (_provider is IWhatsAppProvider waMarkProvider)
        {
            _ = waMarkProvider.MarkChatAsReadAsync(chat.Id);
        }

        _consecutiveEmptyOlderLoads = 0;
        HasMoreOlderMessages = true;
        IsLoading = Messages.Count == 0;
        UpdateChatDateString();
        UpdateSenderPushName();

        if (_provider is IWhatsAppProvider waSubProvider && string.IsNullOrEmpty(SenderPushName) && !chat.IsGroup)
        {
            waSubProvider.SubscribePresence(chat.Id);
        }

        if (string.IsNullOrEmpty(chat.PictureUrl) && _provider is IWhatsAppProvider waProvider)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var picUrl = await waProvider.GetProfilePictureUrlAsync(chat.Id);
                    if (!string.IsNullOrEmpty(picUrl))
                    {
                        _dispatcherService.Enqueue(() =>
                        {
                            if (token.IsCancellationRequested || CurrentChat?.Id != chat.Id) return;
                            chat.PictureUrl = picUrl;
                            OnPropertyChanged(nameof(CurrentChat));
                        });
                    }
                }
                catch { }
            }, token);
        }

        // Fast path 1: Instant synchronous in-memory messages from native bridge cache
        if (_provider is IWhatsAppProvider waFastProvider && Messages.Count == 0)
        {
            var memoryCached = waFastProvider.GetCachedMessages(chat.Id, 30);
            if (memoryCached.Count > 0)
            {
                Messages.Clear();
                foreach (var m in memoryCached)
                {
                    Messages.Add(m);
                }
                if (unreadCount > 0 && Messages.Count > 0)
                {
                    int unreadIndex = Math.Max(0, Messages.Count - unreadCount);
                    Messages[unreadIndex].ShowUnreadSeparator = true;
                    Messages[unreadIndex].UnreadSeparatorText = $"{unreadCount} unread message" + (unreadCount > 1 ? "s" : "");
                }
                RecalculateDateSeparators();
                UpdateChatDateString();
                UpdateSenderPushName();
                IsLoading = false;
                InitialMessagesLoaded?.Invoke(this, EventArgs.Empty);
            }
        }

        // Fast path 2: Load cached messages from SQLite if in-memory cache was empty
        if (Messages.Count == 0)
        {
            var messageRepo = App.Current.Services.GetService<IMessageRepository>();
            if (messageRepo != null)
            {
                try
                {
                    var cached = await messageRepo.GetByChatIdAsync(chat.Id, 0, 30);
                    if (token.IsCancellationRequested || CurrentChat?.Id != chat.Id) return;

                    if (cached.Count > 0)
                    {
                        Messages.Clear();
                        foreach (var m in cached.OrderBy(m => m.Timestamp))
                        {
                            Messages.Add(m);
                        }
                        if (unreadCount > 0 && Messages.Count > 0)
                        {
                            int unreadIndex = Math.Max(0, Messages.Count - unreadCount);
                            Messages[unreadIndex].ShowUnreadSeparator = true;
                            Messages[unreadIndex].UnreadSeparatorText = $"{unreadCount} unread message" + (unreadCount > 1 ? "s" : "");
                        }
                        RecalculateDateSeparators();
                        UpdateChatDateString();
                        UpdateSenderPushName();
                        IsLoading = false;
                        InitialMessagesLoaded?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch { }
            }
        }

        try
        {
            var msgs = await _provider.GetMessagesAsync(chat.Id, 0, 30, token);
            if (token.IsCancellationRequested || CurrentChat?.Id != chat.Id) return;

            _dispatcherService.Enqueue(() =>
            {
                if (token.IsCancellationRequested || CurrentChat?.Id != chat.Id) return;

                if (Messages.Count == 0)
                {
                    foreach (var msg in msgs)
                    {
                        Messages.Add(msg);
                    }
                }
                else
                {
                    // Merge incoming with existing without clearing
                    foreach (var msg in msgs)
                    {
                        var existing = Messages.FirstOrDefault(m => m.Id == msg.Id ||
                            (m.IsOutgoing && m.Status == MessageStatus.Sending && m.Content?.Text == msg.Content?.Text));
                        if (existing != null)
                        {
                            existing.Id = msg.Id;
                            if (msg.Status > existing.Status)
                            {
                                existing.Status = msg.Status;
                            }
                            if (msg.Content != null && (existing.Content == null || (existing.Content.Type == MessageContentType.Text && msg.Content.Type != MessageContentType.Text) || (string.IsNullOrEmpty(existing.Content.ThumbnailUrl) && !string.IsNullOrEmpty(msg.Content.ThumbnailUrl))))
                            {
                                existing.Content = msg.Content;
                            }
                        }
                        else
                        {
                            int index = 0;
                            while (index < Messages.Count && Messages[index].Timestamp < msg.Timestamp)
                            {
                                index++;
                            }
                            Messages.Insert(index, msg);
                        }
                    }
                }

                if (unreadCount > 0 && Messages.Count > 0 && !Messages.Any(m => m.ShowUnreadSeparator))
                {
                    int unreadIndex = Math.Max(0, Messages.Count - unreadCount);
                    Messages[unreadIndex].ShowUnreadSeparator = true;
                    Messages[unreadIndex].UnreadSeparatorText = $"{unreadCount} unread message" + (unreadCount > 1 ? "s" : "");
                }
                RecalculateDateSeparators();
                UpdateChatDateString();
                UpdateSenderPushName();
                IsLoading = false;
                InitialMessagesLoaded?.Invoke(this, EventArgs.Empty);

                // If chat only has a few messages (e.g. months-old chat with only 1-3 messages), auto-fetch older messages
                if (Messages.Count <= 3 && HasMoreOlderMessages && !IsLoadingOlder)
                {
                    _ = LoadOlderMessagesCommand.ExecuteAsync(null);
                }
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            _dispatcherService.Enqueue(() =>
            {
                if (token.IsCancellationRequested || CurrentChat?.Id != chat.Id) return;
                IsLoading = false;
            });
        }
    }

    public void ClearUnreadSeparators()
    {
        foreach (var msg in Messages)
        {
            if (msg.ShowUnreadSeparator)
            {
                msg.ShowUnreadSeparator = false;
                msg.UnreadSeparatorText = null;
            }
        }
    }

    [RelayCommand]
    public async Task LoadOlderMessagesAsync()
    {
        if (CurrentChat == null || IsLoadingOlder || !HasMoreOlderMessages) return;

        var targetChatId = CurrentChat.Id;
        IsLoadingOlder = true;
        var anchor = Messages.FirstOrDefault();

        try
        {
            var older = await _provider.GetOlderMessagesAsync(
                targetChatId, 
                25, 
                anchor?.Timestamp, 
                anchor?.Id, 
                anchor?.IsOutgoing ?? false);

            _dispatcherService.Enqueue(() =>
            {
                if (CurrentChat?.Id != targetChatId)
                {
                    IsLoadingOlder = false;
                    return;
                }

                if (older.Count == 0)
                {
                    _consecutiveEmptyOlderLoads++;
                    if (_consecutiveEmptyOlderLoads >= 3)
                    {
                        HasMoreOlderMessages = false;
                    }
                }
                else
                {
                    _consecutiveEmptyOlderLoads = 0;
                }

                bool addedAny = false;
                foreach (var msg in older)
                {
                    var existing = Messages.FirstOrDefault(m => m.Id == msg.Id);
                    if (existing != null)
                    {
                        if (msg.Content != null && (existing.Content == null || (existing.Content.Type == MessageContentType.Text && msg.Content.Type != MessageContentType.Text) || (string.IsNullOrEmpty(existing.Content.ThumbnailUrl) && !string.IsNullOrEmpty(msg.Content.ThumbnailUrl))))
                        {
                            existing.Content = msg.Content;
                        }
                    }
                    else
                    {
                        int index = 0;
                        while (index < Messages.Count && Messages[index].Timestamp < msg.Timestamp)
                        {
                            index++;
                        }
                        Messages.Insert(index, msg);
                        addedAny = true;
                    }
                }
                IsLoadingOlder = false;
                RecalculateDateSeparators();
                UpdateChatDateString();

                if (addedAny && anchor != null)
                {
                    OlderMessagesLoaded?.Invoke(this, anchor);
                }
            });
        }
        catch
        {
            _dispatcherService.Enqueue(() =>
            {
                IsLoadingOlder = false;
            });
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageText) || CurrentChat == null) return;

        var text = MessageText;
        MessageText = string.Empty;

        var content = new MessageContent { Text = text, Type = MessageContentType.Text };
        var pendingId = "pending_" + Guid.NewGuid().ToString("N");
        var pendingMsg = new Message
        {
            Id = pendingId,
            ChatId = CurrentChat.Id,
            PlatformId = CurrentChat.PlatformId,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow,
            Status = MessageStatus.Sending,
            SenderId = "me",
            IsOutgoing = true
        };

        // 1. Instantly display in conversation history with timer icon
        Messages.Add(pendingMsg);
        RecalculateDateSeparators();
        UpdateChatDateString();

        CurrentChat.LastMessage = pendingMsg;
        CurrentChat.LastMessageTime = pendingMsg.Timestamp;

        var chatRepo = App.Current.Services.GetService<IChatRepository>();
        if (chatRepo != null)
        {
            _ = Task.Run(async () =>
            {
                try { await chatRepo.SaveAsync(CurrentChat); } catch { }
            });
        }

        var chatListVm = App.Current.Services.GetService<ChatListViewModel>();
        if (chatListVm != null)
        {
            if (!chatListVm.Chats.Any(c => c.Id == CurrentChat.Id))
            {
                chatListVm.Chats.Insert(0, CurrentChat);
            }
            else
            {
                chatListVm.SortChatsInPlace();
            }
        }

        // 2. Dispatch to provider asynchronously
        Message sentMsg;
        try
        {
            sentMsg = await _provider.SendMessageAsync(CurrentChat.Id, content, pendingMsg.Id);
        }
        catch
        {
            sentMsg = pendingMsg;
        }

        // 3. Update the pending message in place
        _dispatcherService.Enqueue(() =>
        {
            var existing = Messages.FirstOrDefault(m => m.Id == pendingId);
            if (existing != null)
            {
                if (sentMsg.Id != pendingId)
                {
                    existing.Id = sentMsg.Id;
                }
                existing.Status = sentMsg.Status;
                existing.Timestamp = sentMsg.Timestamp;
            }
            else if (!Messages.Any(m => m.Id == sentMsg.Id))
            {
                Messages.Add(sentMsg);
            }
            RecalculateDateSeparators();
            UpdateChatDateString();
        });
    }

    private void OnChatUpdated(object? sender, ChatUpdatedEventArgs e)
    {
        if (CurrentChat == null) return;
        _dispatcherService.Enqueue(async () =>
        {
            UpdateHeaderDetails();
            await RefreshMessagesAsync();
        });
    }

    private async Task RefreshMessagesAsync()
    {
        if (CurrentChat == null) return;
        try
        {
            // Use a larger limit to catch newly-synced messages beyond the initial page
            var msgs = await _provider.GetMessagesAsync(CurrentChat.Id, 0, 100);
            _dispatcherService.Enqueue(() =>
            {
                bool anyAdded = false;
                foreach (var msg in msgs)
                {
                    var existingById = Messages.FirstOrDefault(m => m.Id == msg.Id);
                    if (existingById != null)
                    {
                        if (msg.Status > existingById.Status)
                        {
                            existingById.Status = msg.Status;
                        }
                        continue;
                    }

                    // Check if there is an outgoing pending message matching this sent message
                    if (msg.IsOutgoing)
                    {
                        var pendingMatch = Messages.FirstOrDefault(m => m.IsOutgoing &&
                            (m.Status == MessageStatus.Sending || m.Id.StartsWith("pending_")) &&
                            m.Content?.Text == msg.Content?.Text);
                        if (pendingMatch != null)
                        {
                            pendingMatch.Id = msg.Id;
                            pendingMatch.Status = msg.Status;
                            pendingMatch.Timestamp = msg.Timestamp;
                            continue;
                        }
                    }

                    int index = 0;
                    while (index < Messages.Count && Messages[index].Timestamp < msg.Timestamp)
                    {
                        index++;
                    }
                    Messages.Insert(index, msg);
                    anyAdded = true;
                }
                if (anyAdded)
                {
                    HasMoreOlderMessages = true;
                    RecalculateDateSeparators();
                    UpdateChatDateString();
                    UpdateSenderPushName();
                }
            });
        }
        catch
        {
            // Ignore
        }
    }

    public static bool ChatIdsMatch(string? id1, string? id2)
    {
        if (string.Equals(id1, id2, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrEmpty(id1) || string.IsNullOrEmpty(id2)) return false;

        var clean1 = id1.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? id1[3..] : id1;
        var clean2 = id2.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? id2[3..] : id2;

        if (string.Equals(clean1, clean2, StringComparison.OrdinalIgnoreCase)) return true;

        var parts1 = clean1.Split('@');
        var parts2 = clean2.Split('@');
        var user1 = parts1[0].Split(':')[0];
        var user2 = parts2[0].Split(':')[0];

        if (parts1.Length > 1 && parts2.Length > 1)
        {
            var server1 = parts1[1].ToLowerInvariant();
            var server2 = parts2[1].ToLowerInvariant();
            if (server1 == server2 && string.Equals(user1, user2, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        else if (string.Equals(user1, user2, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (CurrentChat != null && ChatIdsMatch(CurrentChat.Id, e.Message.ChatId))
        {
            _dispatcherService.Enqueue(() =>
            {
                var existing = Messages.FirstOrDefault(m => m.Id == e.Message.Id ||
                    (m.IsOutgoing && (m.Status == MessageStatus.Sending || m.Id.StartsWith("pending_")) &&
                     (m.Content?.Text == e.Message.Content?.Text || (m.Content?.Type != MessageContentType.Text && m.Content?.Type == e.Message.Content?.Type))));
                if (existing != null)
                {
                    existing.Id = e.Message.Id;
                    if (e.Message.Status > existing.Status)
                    {
                        existing.Status = e.Message.Status;
                    }
                    if (e.Message.Content != null && existing.Content != null)
                    {
                        if (!string.IsNullOrEmpty(e.Message.Content.LocalFilePath))
                            existing.Content.LocalFilePath = e.Message.Content.LocalFilePath;
                        if (!string.IsNullOrEmpty(e.Message.Content.ThumbnailUrl))
                            existing.Content.ThumbnailUrl = e.Message.Content.ThumbnailUrl;
                    }
                }
                else
                {
                    if (e.Message.Content != null && (!string.IsNullOrWhiteSpace(e.Message.Content.Text) || e.Message.Content.Type != MessageContentType.Text))
                    {
                        Messages.Add(e.Message);
                        RecalculateDateSeparators();
                        UpdateChatDateString();
                        UpdateSenderPushName();
                    }
                }
            });
        }
    }

    public void RecalculateDateSeparators()
    {
        DateTime? lastDate = null;
        for (int i = 0; i < Messages.Count; i++)
        {
            var msg = Messages[i];
            var msgDate = msg.Timestamp.ToLocalTime().Date;
            if (lastDate == null || msgDate != lastDate.Value)
            {
                msg.ShowDateSeparator = true;
                msg.DateSeparatorText = FormatChatDate(msg.Timestamp);
                lastDate = msgDate;
            }
            else
            {
                msg.ShowDateSeparator = false;
                msg.DateSeparatorText = null;
            }
        }
    }

    public void UpdateSenderPushName()
    {
        UpdateHeaderDetails();
    }

    public void UpdateHeaderDetails()
    {
        if (CurrentChat == null)
        {
            HeaderTitle = string.Empty;
            HeaderSubtitle = null;
            SenderPushName = null;
            return;
        }

        if (CurrentChat.IsGroup)
        {
            HeaderTitle = CurrentChat.Title ?? string.Empty;
            HeaderSubtitle = null;
            SenderPushName = null;
            return;
        }

        var waProvider = _provider as IWhatsAppProvider;
        var chatId = CurrentChat.Id ?? string.Empty;
        var cleanId = chatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? chatId[3..] : chatId;
        var userPart = cleanId.Split('@')[0].Split(':')[0];
        var idDigits = new string(userPart.Where(char.IsDigit).ToArray());

        // Resolve clean phone number
        string? phone = null;
        if (waProvider != null && !string.IsNullOrEmpty(chatId))
        {
            phone = waProvider.GetPhoneNumber(chatId);
        }
        if (string.IsNullOrEmpty(phone) && idDigits.Length >= 5)
        {
            phone = $"+{idDigits}";
        }

        string rawTitle = CurrentChat.Title?.Trim() ?? string.Empty;

        // Check whether this contact is saved in the user's address book
        string? savedName = null;
        if (waProvider != null && !string.IsNullOrEmpty(chatId))
        {
            savedName = waProvider.GetContactName(chatId);
            if (string.IsNullOrEmpty(savedName) && idDigits.Length >= 5)
            {
                savedName = waProvider.GetContactName(idDigits);
            }
        }

        bool isUnsaved;
        if (!string.IsNullOrWhiteSpace(savedName))
        {
            // Definitely a saved contact from address book
            isUnsaved = false;
        }
        else if (waProvider != null)
        {
            // Contact is NOT in address book -> unsaved contact
            isUnsaved = true;
        }
        else
        {
            // Fallback for mock/test provider
            if (rawTitle.StartsWith('+') || rawTitle.StartsWith('~') || IsPhoneNumber(rawTitle))
            {
                isUnsaved = true;
            }
            else
            {
                int letterCount = rawTitle.Count(char.IsLetter);
                int digitCount = rawTitle.Count(char.IsDigit);
                var titleDigits = new string(rawTitle.Where(char.IsDigit).ToArray());

                if ((letterCount == 0 && digitCount >= 5) || (!string.IsNullOrEmpty(idDigits) && titleDigits == idDigits))
                {
                    isUnsaved = true;
                }
                else
                {
                    isUnsaved = false;
                    savedName = rawTitle;
                }
            }
        }

        if (isUnsaved)
        {
            // Unsaved contact: Title is formatted phone number, Subtitle is user's set push name
            HeaderTitle = !string.IsNullOrEmpty(phone) ? FormatPhoneNumber(phone) : FormatPhoneNumber(rawTitle);

            string? pushName = null;
            if (waProvider != null && !string.IsNullOrEmpty(chatId))
            {
                pushName = waProvider.GetPushName(chatId);
                if (string.IsNullOrEmpty(pushName) && idDigits.Length >= 5)
                {
                    pushName = waProvider.GetPushName(idDigits);
                    if (string.IsNullOrEmpty(pushName) && idDigits.Length == 12 && idDigits.StartsWith("91"))
                    {
                        pushName = waProvider.GetPushName(idDigits[2..]);
                    }
                }
            }

            if (string.IsNullOrEmpty(pushName) && rawTitle.StartsWith('~'))
            {
                pushName = rawTitle.TrimStart('~').Trim();
            }

            // If pushName not found from bridge, but rawTitle is a non-phone name (e.g. "Sumalatha" or "Sangeetha S"),
            // rawTitle was set to the push name by the bridge
            if (string.IsNullOrEmpty(pushName) && !IsPhoneNumber(rawTitle) && !rawTitle.StartsWith('+'))
            {
                int letters = rawTitle.Count(char.IsLetter);
                if (letters > 0)
                {
                    pushName = rawTitle;
                }
            }

            if (string.IsNullOrEmpty(pushName) && CurrentChat.LastMessage != null && !CurrentChat.LastMessage.IsOutgoing)
            {
                var sn = CurrentChat.LastMessage.SenderName;
                if (!string.IsNullOrWhiteSpace(sn) && !IsPhoneNumber(sn))
                {
                    pushName = sn;
                }
            }

            if (string.IsNullOrEmpty(pushName))
            {
                var incomingMsg = Messages.LastOrDefault(m =>
                    !m.IsOutgoing &&
                    !string.IsNullOrWhiteSpace(m.SenderName) &&
                    !IsPhoneNumber(m.SenderName));
                if (incomingMsg != null)
                {
                    pushName = incomingMsg.SenderName;
                }
            }

            if (string.IsNullOrEmpty(pushName) && waProvider != null && !string.IsNullOrEmpty(chatId))
            {
                waProvider.SubscribePresence(chatId);
            }

            if (!string.IsNullOrWhiteSpace(pushName))
            {
                var cleaned = pushName.Trim().TrimStart('~').Trim();
                if (!string.IsNullOrEmpty(cleaned)
                    && cleaned.Any(char.IsLetter)
                    && !cleaned.StartsWith('+')
                    && !cleaned.Contains(".."))
                {
                    HeaderSubtitle = $"~{cleaned}";
                }
                else
                {
                    HeaderSubtitle = null;
                }
            }
            else
            {
                HeaderSubtitle = null;
            }
        }
        else
        {
            // Saved contact: Title is saved contact name, Subtitle is formatted phone number
            HeaderTitle = !string.IsNullOrWhiteSpace(savedName) ? savedName : rawTitle;
            HeaderSubtitle = !string.IsNullOrEmpty(phone) ? FormatPhoneNumber(phone) : null;
        }

        SenderPushName = HeaderSubtitle;
    }

    public static string FormatPhoneNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();

        if (trimmed.StartsWith("+") && (trimmed.Contains(' ') || trimmed.Contains('-')))
        {
            return trimmed;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return trimmed;

        if (digits.Length == 11 && digits.StartsWith("1"))
        {
            return $"+1 {digits.Substring(1, 3)} {digits.Substring(4, 3)} {digits.Substring(7)}";
        }
        if (digits.Length == 10)
        {
            return $"+{digits.Substring(0, 5)} {digits.Substring(5)}";
        }
        if (digits.Length == 12 && digits.StartsWith("91"))
        {
            return $"+91 {digits.Substring(2, 5)} {digits.Substring(7)}";
        }
        if (digits.Length == 12 && digits.StartsWith("44"))
        {
            return $"+44 {digits.Substring(2, 4)} {digits.Substring(6)}";
        }
        if (digits.Length > 10)
        {
            int ccLen = digits.Length > 12 ? 3 : (digits.Length > 11 ? 2 : 1);
            return $"+{digits.Substring(0, ccLen)} {digits.Substring(ccLen, 3)} {digits.Substring(ccLen + 3, 3)} {digits.Substring(ccLen + 6)}".Trim();
        }

        return trimmed.StartsWith("+") ? trimmed : $"+{digits}";
    }

    private static bool IsPhoneNumber(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var trimmed = s.Trim();
        if (trimmed.StartsWith('+')) return true;
        int digitCount = trimmed.Count(char.IsDigit);
        int letterCount = trimmed.Count(char.IsLetter);
        return letterCount == 0 && digitCount >= 5;
    }

    public void UpdateChatDateString()
    {
        if (Messages.Count == 0)
        {
            CurrentChatDateString = "Today";
            return;
        }

        var latestDate = Messages[^1].Timestamp;
        CurrentChatDateString = FormatChatDate(latestDate);
    }

    public static string FormatChatDate(DateTimeOffset date)
    {
        var localDate = date.ToLocalTime().Date;
        var today = DateTime.Today;
        if (localDate == today) return "Today";
        if (localDate == today.AddDays(-1)) return "Yesterday";
        if (localDate > today.AddDays(-7)) return localDate.ToString("dddd");

        int day = localDate.Day;
        string suffix = (day % 10 == 1 && day != 11) ? "st" :
                        (day % 10 == 2 && day != 12) ? "nd" :
                        (day % 10 == 3 && day != 13) ? "rd" : "th";

        return $"{day}{suffix} {localDate:MMMM yyyy}";
    }

    [ObservableProperty]
    public partial bool IsMediaViewerOpen { get; set; }

    [ObservableProperty]
    public partial Message? ActiveMediaItem { get; set; }

    [ObservableProperty]
    public partial string? ActiveMediaImageSource { get; set; }

    [ObservableProperty]
    public partial bool IsActiveMediaImage { get; set; }

    [ObservableProperty]
    public partial bool IsActiveMediaVideo { get; set; }

    [ObservableProperty]
    public partial bool IsMediaDownloading { get; set; }

    [ObservableProperty]
    public partial string ActiveMediaTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActiveMediaSubtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ActiveMediaCaption { get; set; }

    public event EventHandler<string?>? ActiveVideoSourceChanged;

    [RelayCommand]
    public async Task OpenMediaViewerAsync(Message? msg)
    {
        if (msg == null || msg.Content == null) return;
        if (!msg.Content.IsImage && !msg.Content.IsVideo) return;

        ActiveMediaItem = msg;
        IsActiveMediaImage = msg.Content.IsImage;
        IsActiveMediaVideo = msg.Content.IsVideo;
        ActiveMediaTitle = msg.Content.DisplayName;
        ActiveMediaSubtitle = $"{msg.SenderName ?? (msg.IsOutgoing ? "You" : "")} • {msg.FormattedTime}".Trim(' ', '•');
        ActiveMediaCaption = msg.Content.Caption;
        IsMediaViewerOpen = true;

        if (msg.Content.IsDownloaded)
        {
            if (IsActiveMediaImage)
            {
                ActiveMediaImageSource = msg.Content.LocalFilePath;
            }
            else if (IsActiveMediaVideo)
            {
                ActiveVideoSourceChanged?.Invoke(this, msg.Content.LocalFilePath);
            }
        }
        else
        {
            // Show thumbnail immediately while downloading
            if (IsActiveMediaImage && !string.IsNullOrEmpty(msg.Content.ThumbnailUrl))
            {
                ActiveMediaImageSource = msg.Content.ThumbnailUrl;
            }

            IsMediaDownloading = true;
            try
            {
                var localPath = await _provider.DownloadMediaAsync(msg.ChatId, msg.Id, msg.Content.FileName);
                if (!string.IsNullOrEmpty(localPath))
                {
                    msg.Content.LocalFilePath = localPath;
                    msg.Content.MediaUrl = localPath;

                    if (ActiveMediaItem?.Id == msg.Id)
                    {
                        if (IsActiveMediaImage)
                        {
                            ActiveMediaImageSource = localPath;
                        }
                        else if (IsActiveMediaVideo)
                        {
                            ActiveVideoSourceChanged?.Invoke(this, localPath);
                        }
                    }
                }
            }
            finally
            {
                IsMediaDownloading = false;
            }
        }
    }

    [RelayCommand]
    public void CloseMediaViewer()
    {
        IsMediaViewerOpen = false;
        ActiveMediaItem = null;
        ActiveMediaImageSource = null;
        IsActiveMediaImage = false;
        IsActiveMediaVideo = false;
        ActiveVideoSourceChanged?.Invoke(this, null);
    }

    [RelayCommand]
    public async Task OpenActiveMediaInDefaultAppAsync()
    {
        if (ActiveMediaItem != null)
        {
            await OpenMessageInDefaultAppAsync(ActiveMediaItem);
        }
    }

    [RelayCommand]
    public async Task SaveActiveMediaAsAsync()
    {
        if (ActiveMediaItem != null)
        {
            await SaveMediaAsAsync(ActiveMediaItem);
        }
    }

    [RelayCommand]
    public async Task SaveMediaAsAsync(Message? msg)
    {
        if (msg == null || msg.Content == null) return;

        try
        {
            string? localPath = msg.Content.LocalFilePath;
            if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
            {
                localPath = await _provider.DownloadMediaAsync(msg.ChatId, msg.Id, msg.Content.FileName);
                if (!string.IsNullOrEmpty(localPath))
                {
                    msg.Content.LocalFilePath = localPath;
                }
            }

            if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
            {
                return;
            }

            var ext = Path.GetExtension(localPath).ToLowerInvariant();
            var picker = new Windows.Storage.Pickers.FileSavePicker();

            var app = (App)App.Current;
            if (app.MainWindow != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(app.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            var suggestedName = !string.IsNullOrWhiteSpace(msg.Content.FileName)
                ? Path.GetFileNameWithoutExtension(msg.Content.FileName)
                : Path.GetFileNameWithoutExtension(localPath);
            picker.SuggestedFileName = suggestedName;

            var extFilter = string.IsNullOrEmpty(ext) ? ".bin" : ext;
            var desc = extFilter switch
            {
                ".jpg" or ".jpeg" => "JPEG Image",
                ".png" => "PNG Image",
                ".webp" => "WebP Image",
                ".mp4" => "MP4 Video",
                ".pdf" => "PDF Document",
                ".docx" => "Word Document",
                _ => $"{extFilter.TrimStart('.').ToUpperInvariant()} File"
            };

            picker.FileTypeChoices.Add(desc, new List<string> { extFilter });

            var destFile = await picker.PickSaveFileAsync();
            if (destFile != null)
            {
                using var inStream = File.OpenRead(localPath);
                using var outStream = await destFile.OpenStreamForWriteAsync();
                outStream.SetLength(0);
                await inStream.CopyToAsync(outStream);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveMediaAsAsync failed for {msg.Id}: {ex}");
        }
    }

    public async Task OpenMessageInDefaultAppAsync(Message? msg)
    {
        if (msg == null || msg.Content == null) return;

        string? filePath = msg.Content.LocalFilePath;
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
        {
            filePath = await _provider.DownloadMediaAsync(msg.ChatId, msg.Id, msg.Content.FileName);
            if (!string.IsNullOrEmpty(filePath))
            {
                msg.Content.LocalFilePath = filePath;
                msg.Content.MediaUrl = filePath;
            }
        }

        if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
        {
            await LaunchFileAsync(filePath);
        }
    }

    [RelayCommand]
    public async Task DownloadAndOpenDocumentAsync(Message? msg)
    {
        if (msg == null || msg.Content == null) return;

        if (msg.Content.IsDownloaded)
        {
            await LaunchFileAsync(msg.Content.LocalFilePath!);
            return;
        }

        if (msg.Content.IsDownloading)
        {
            return;
        }

        msg.Content.DownloadCts?.Cancel();
        msg.Content.DownloadCts?.Dispose();
        msg.Content.DownloadCts = new CancellationTokenSource();
        var ct = msg.Content.DownloadCts.Token;

        msg.Content.IsDownloading = true;
        msg.Content.DownloadProgress = 0.0;

        try
        {
            var downloadedPath = await _provider.DownloadMediaAsync(msg.ChatId, msg.Id, msg.Content.FileName, ct);
            if (!ct.IsCancellationRequested && !string.IsNullOrEmpty(downloadedPath) && File.Exists(downloadedPath))
            {
                _dispatcherService.Enqueue(async () =>
                {
                    msg.Content.LocalFilePath = downloadedPath;
                    msg.Content.DownloadProgress = 100.0;
                    msg.Content.IsDownloading = false;
                    await LaunchFileAsync(downloadedPath);
                });
            }
            else
            {
                _dispatcherService.Enqueue(() =>
                {
                    msg.Content.IsDownloading = false;
                    msg.Content.DownloadProgress = 0.0;
                });
            }
        }
        catch (OperationCanceledException)
        {
            _dispatcherService.Enqueue(() =>
            {
                msg.Content.IsDownloading = false;
                msg.Content.DownloadProgress = 0.0;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DownloadAndOpenDocumentAsync failed for {msg.Id}: {ex}");
            _dispatcherService.Enqueue(() =>
            {
                msg.Content.IsDownloading = false;
                msg.Content.DownloadProgress = 0.0;
            });
        }
    }

    [RelayCommand]
    public async Task DownloadMediaAsync(Message? msg)
    {
        if (msg?.Content == null || string.IsNullOrEmpty(msg.ChatId) || string.IsNullOrEmpty(msg.Id)) return;
        if (msg.Content.IsDownloaded || msg.Content.IsDownloading) return;

        msg.Content.DownloadCts?.Cancel();
        msg.Content.DownloadCts?.Dispose();
        msg.Content.DownloadCts = new CancellationTokenSource();
        var ct = msg.Content.DownloadCts.Token;

        msg.Content.IsDownloading = true;
        msg.Content.DownloadProgress = 0.0;

        try
        {
            var downloadedPath = await _provider.DownloadMediaAsync(msg.ChatId, msg.Id, msg.Content.FileName, ct);
            if (!ct.IsCancellationRequested && !string.IsNullOrEmpty(downloadedPath) && File.Exists(downloadedPath))
            {
                _dispatcherService.Enqueue(async () =>
                {
                    msg.Content.LocalFilePath = downloadedPath;
                    msg.Content.DownloadProgress = 100.0;
                    msg.Content.IsDownloading = false;

                    if (msg.Content.Type == MessageContentType.Video)
                    {
                        await EnsureVideoThumbnailAsync(msg.Content);
                    }

                    try
                    {
                        var repo = App.Current.Services.GetService<IMessageRepository>();
                        if (repo != null)
                        {
                            await repo.SaveAsync(msg);
                        }
                    }
                    catch { }
                });
            }
            else
            {
                _dispatcherService.Enqueue(() =>
                {
                    msg.Content.IsDownloading = false;
                    msg.Content.DownloadProgress = 0.0;
                });
            }
        }
        catch (OperationCanceledException)
        {
            _dispatcherService.Enqueue(() =>
            {
                msg.Content.IsDownloading = false;
                msg.Content.DownloadProgress = 0.0;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DownloadMediaAsync failed for {msg.Id}: {ex}");
            _dispatcherService.Enqueue(() =>
            {
                msg.Content.IsDownloading = false;
                msg.Content.DownloadProgress = 0.0;
            });
        }
    }

    [RelayCommand]
    public void CancelDownload(Message? msg)
    {
        if (msg?.Content == null) return;
        try
        {
            msg.Content.DownloadCts?.Cancel();
        }
        catch { }
        msg.Content.IsDownloading = false;
        msg.Content.DownloadProgress = 0.0;
    }

    [RelayCommand]
    public void CancelDocumentDownload(Message? msg) => CancelDownload(msg);

    private static async Task LaunchFileAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

            // In packaged Windows App SDK apps, sandboxed external viewers (Edge, Chrome) cannot read private app directories.
            // Copy to user Temp directory so all external readers have full permission.
            string launchPath = filePath;
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "People", "Media");
                Directory.CreateDirectory(tempDir);
                var tempPath = Path.Combine(tempDir, Path.GetFileName(filePath));
                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length != new FileInfo(filePath).Length)
                {
                    File.Copy(filePath, tempPath, true);
                }
                launchPath = tempPath;
            }
            catch { }

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(launchPath);
            var success = await Windows.System.Launcher.LaunchFileAsync(file);
            if (!success)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = launchPath,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private Windows.Media.Playback.MediaPlayer? _audioPlayer;

    public async Task PlayAudioAsync(Message? msg)
    {
        if (msg?.Content == null) return;
        if (!msg.Content.IsDownloaded)
        {
            await DownloadMediaAsync(msg);
        }

        if (msg.Content.IsDownloaded && !string.IsNullOrEmpty(msg.Content.LocalFilePath) && File.Exists(msg.Content.LocalFilePath))
        {
            try
            {
                _audioPlayer ??= new Windows.Media.Playback.MediaPlayer();
                _audioPlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(msg.Content.LocalFilePath));
                _audioPlayer.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlayAudioAsync failed: {ex.Message}");
                await LaunchFileAsync(msg.Content.LocalFilePath);
            }
        }
    }

    public static async Task EnsureVideoThumbnailAsync(MessageContent? content)
    {
        if (content == null || string.IsNullOrEmpty(content.LocalFilePath) || !File.Exists(content.LocalFilePath))
            return;

        var thumbPath = content.LocalFilePath + ".thumb.jpg";
        if (File.Exists(thumbPath) && new FileInfo(thumbPath).Length > 0)
        {
            content.ThumbnailUrl = thumbPath;
            return;
        }

        try
        {
            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(content.LocalFilePath);
            using var thumb = await storageFile.GetThumbnailAsync(
                Windows.Storage.FileProperties.ThumbnailMode.VideosView,
                320,
                Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

            if (thumb != null && thumb.Size > 0)
            {
                using var fileStream = File.Create(thumbPath);
                using var readStream = thumb.AsStreamForRead();
                await readStream.CopyToAsync(fileStream);
                content.ThumbnailUrl = thumbPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EnsureVideoThumbnailAsync failed: {ex.Message}");
        }
    }

    public async Task SendDocumentFileAsync(string filePath)
    {
        if (CurrentChat == null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        var fi = new FileInfo(filePath);
        var content = new MessageContent
        {
            Type = MessageContentType.Document,
            LocalFilePath = filePath,
            FileName = fi.Name,
            FileSize = fi.Length,
            Text = string.Empty
        };

        await SendCustomMessageAsync(content);
    }

    public async Task SendMediaFileAsync(string filePath)
    {
        if (CurrentChat == null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        var fi = new FileInfo(filePath);
        var ext = fi.Extension.ToLowerInvariant();
        bool isVideo = ext is ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm";
        var content = new MessageContent
        {
            Type = isVideo ? MessageContentType.Video : MessageContentType.Image,
            LocalFilePath = filePath,
            FileName = fi.Name,
            FileSize = fi.Length,
            ThumbnailUrl = isVideo ? null : filePath,
            Text = string.Empty
        };

        if (isVideo)
        {
            await EnsureVideoThumbnailAsync(content);
        }

        await SendCustomMessageAsync(content);
    }

    public async Task SendAudioFileAsync(string filePath)
    {
        if (CurrentChat == null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        var fi = new FileInfo(filePath);
        var content = new MessageContent
        {
            Type = MessageContentType.Audio,
            LocalFilePath = filePath,
            FileName = fi.Name,
            FileSize = fi.Length,
            Text = string.Empty
        };

        await SendCustomMessageAsync(content);
    }

    private async Task SendCustomMessageAsync(MessageContent content)
    {
        if (CurrentChat == null) return;
        var pendingId = "pending_" + Guid.NewGuid().ToString("N");
        var pendingMsg = new Message
        {
            Id = pendingId,
            ChatId = CurrentChat.Id,
            PlatformId = CurrentChat.PlatformId,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow,
            Status = MessageStatus.Sending,
            SenderId = "me",
            IsOutgoing = true
        };

        Messages.Add(pendingMsg);
        RecalculateDateSeparators();
        UpdateChatDateString();
        CurrentChat.LastMessage = pendingMsg;
        CurrentChat.LastMessageTime = pendingMsg.Timestamp;

        try
        {
            var repo = App.Current.Services.GetService<IMessageRepository>();
            if (repo != null) await repo.SaveAsync(pendingMsg);
            var chatRepo = App.Current.Services.GetService<IChatRepository>();
            if (chatRepo != null) await chatRepo.SaveAsync(CurrentChat);
        }
        catch { }

        try
        {
            var sentMsg = await _provider.SendMessageAsync(CurrentChat.Id, content, pendingMsg.Id);
            _dispatcherService.Enqueue(() =>
            {
                var existing = Messages.FirstOrDefault(m => m.Id == pendingId);
                if (existing != null)
                {
                    existing.Id = sentMsg.Id;
                    existing.Status = sentMsg.Status;
                }
            });
        }
        catch { }
    }
}
