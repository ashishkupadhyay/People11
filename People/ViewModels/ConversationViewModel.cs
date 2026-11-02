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
        _provider.MessageReceived += OnMessageReceived;
        _provider.ChatUpdated += OnChatUpdated;
    }

    public event EventHandler<Message>? OlderMessagesLoaded;

    public async void LoadChat(Chat chat)
    {
        CurrentChat = chat;
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
                        });
                    }
                }
                catch { }
            });
        }

        try
        {
            var msgs = await _provider.GetMessagesAsync(chat.Id, 0, 30);
            _dispatcherService.Enqueue(() =>
            {
                Messages.Clear();
                foreach (var msg in msgs)
                {
                    Messages.Add(msg);
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

                if (msgs.Count <= 3 && HasMoreOlderMessages)
                {
                    _ = LoadOlderMessagesAsync();
                }
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
        var sentMsg = await _provider.SendMessageAsync(CurrentChat.Id, content);

        CurrentChat.LastMessage = sentMsg;
        CurrentChat.LastMessageTime = sentMsg.Timestamp;

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
            _dispatcherService.Enqueue(() =>
            {
                if (!chatListVm.Chats.Any(c => c.Id == CurrentChat.Id))
                {
                    chatListVm.Chats.Insert(0, CurrentChat);
                }
                else
                {
                    chatListVm.SortChatsInPlace();
                }
            });
        }

        _dispatcherService.Enqueue(() =>
        {
            if (!Messages.Any(m => m.Id == sentMsg.Id))
            {
                Messages.Add(sentMsg);
                RecalculateDateSeparators();
                UpdateChatDateString();
            }
        });
    }

    private void OnChatUpdated(object? sender, ChatUpdatedEventArgs e)
    {
        if (CurrentChat == null) return;
        _dispatcherService.Enqueue(async () =>
        {
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
                    if (!Messages.Any(m => m.Id == msg.Id))
                    {
                        int index = 0;
                        while (index < Messages.Count && Messages[index].Timestamp < msg.Timestamp)
                        {
                            index++;
                        }
                        Messages.Insert(index, msg);
                        anyAdded = true;
                    }
                }
                if (anyAdded)
                {
                    RecalculateDateSeparators();
                    UpdateChatDateString();
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
        if (CurrentChat != null && e.Message.ChatId == CurrentChat.Id)
        {
            _dispatcherService.Enqueue(() =>
            {
                if (!Messages.Any(m => m.Id == e.Message.Id))
                {
                    Messages.Add(e.Message);
                    RecalculateDateSeparators();
                    UpdateChatDateString();
                    UpdateSenderPushName();
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

        string rawTitle = CurrentChat.Title?.Trim() ?? string.Empty;
        bool isTitlePhoneNumber = IsPhoneNumber(rawTitle);

        if (isTitlePhoneNumber)
        {
            // Unsaved contact: Title is phone number, Subtitle is user's provided push name
            HeaderTitle = FormatPhoneNumber(rawTitle);

            string? pushName = null;
            if (_provider is IWhatsAppProvider waProvider && !string.IsNullOrEmpty(CurrentChat.Id))
            {
                pushName = waProvider.GetPushName(CurrentChat.Id);
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

            if (!string.IsNullOrWhiteSpace(pushName))
            {
                var cleaned = pushName.Trim().TrimStart('~').Trim();
                HeaderSubtitle = !string.IsNullOrEmpty(cleaned) ? $"~{cleaned}" : null;
            }
            else
            {
                HeaderSubtitle = null;
            }
        }
        else
        {
            // Saved contact: Title is saved contact name, Subtitle is phone number
            HeaderTitle = rawTitle;

            string? phone = null;
            if (_provider is IWhatsAppProvider waProvider && !string.IsNullOrEmpty(CurrentChat.Id))
            {
                phone = waProvider.GetPhoneNumber(CurrentChat.Id);
            }

            if (string.IsNullOrEmpty(phone) && !string.IsNullOrEmpty(CurrentChat.Id))
            {
                var cleanId = CurrentChat.Id.StartsWith("wa_", StringComparison.OrdinalIgnoreCase)
                    ? CurrentChat.Id[3..]
                    : CurrentChat.Id;
                if (cleanId.Contains('@'))
                {
                    var userPart = cleanId.Split('@')[0];
                    var digits = new string(userPart.Where(char.IsDigit).ToArray());
                    if (digits.Length >= 5)
                    {
                        phone = $"+{digits}";
                    }
                }
            }

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
        if (trimmed.StartsWith('+'))
        {
            return trimmed.Skip(1).All(c => char.IsDigit(c) || c == ' ' || c == '-' || c == '(' || c == ')');
        }
        int digitCount = trimmed.Count(char.IsDigit);
        return digitCount >= 7 && trimmed.All(c => char.IsDigit(c) || c == ' ' || c == '-' || c == '(' || c == ')');
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

        int day = localDate.Day;
        string suffix = (day % 10 == 1 && day != 11) ? "st" :
                        (day % 10 == 2 && day != 12) ? "nd" :
                        (day % 10 == 3 && day != 13) ? "rd" : "th";

        return $"{day}{suffix} {localDate:MMMM yyyy}";
    }
}
