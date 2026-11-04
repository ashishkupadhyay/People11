using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using People.Core.Interfaces;
using People.Core.Interfaces.Repositories;
using People.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
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
    }

    public event EventHandler<Message>? OlderMessagesLoaded;

    public async void LoadChat(Chat chat)
    {
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

        Messages.Clear();
        IsLoading = true;
        HasMoreOlderMessages = true;
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
                            chat.PictureUrl = picUrl;
                            OnPropertyChanged(nameof(CurrentChat));
                        });
                    }
                }
                catch { }
            });
        }

        // Fast path: Immediately load cached messages from SQLite so UI displays instantly
        var messageRepo = App.Current.Services.GetService<IMessageRepository>();
        if (messageRepo != null)
        {
            try
            {
                var cached = await messageRepo.GetByChatIdAsync(chat.Id, 0, 30);
                if (cached.Count > 0 && CurrentChat?.Id == chat.Id)
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

        try
        {
            var msgs = await _provider.GetMessagesAsync(chat.Id, 0, 30);
            if (CurrentChat?.Id != chat.Id) return;

            _dispatcherService.Enqueue(() =>
            {
                if (CurrentChat?.Id != chat.Id) return;

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
                            existing.Status = msg.Status;
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
            });
        }
        catch
        {
            _dispatcherService.Enqueue(() =>
            {
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

        IsLoadingOlder = true;
        var anchor = Messages.FirstOrDefault();

        try
        {
            var older = await _provider.GetOlderMessagesAsync(CurrentChat.Id, 25, anchor?.Timestamp);
            _dispatcherService.Enqueue(() =>
            {
                if (older.Count == 0)
                {
                    HasMoreOlderMessages = false;
                }

                bool addedAny = false;
                foreach (var msg in older)
                {
                    if (!Messages.Any(m => m.Id == msg.Id))
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
                        if (existingById.Status != msg.Status)
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

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (CurrentChat != null && (e.Message.ChatId == CurrentChat.Id || CurrentChat.Id.Contains(e.Message.ChatId) || e.Message.ChatId.Contains(CurrentChat.Id)))
        {
            _dispatcherService.Enqueue(() =>
            {
                var existing = Messages.FirstOrDefault(m => m.Id == e.Message.Id ||
                    (m.IsOutgoing && (m.Status == MessageStatus.Sending || m.Id.StartsWith("pending_")) && m.Content?.Text == e.Message.Content?.Text));
                if (existing != null)
                {
                    existing.Id = e.Message.Id;
                    existing.Status = e.Message.Status;
                }
                else
                {
                    if (e.Message.Content != null && !string.IsNullOrWhiteSpace(e.Message.Content.Text))
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
}
