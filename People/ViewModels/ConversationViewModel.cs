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
        if (CurrentChat == null || CurrentChat.IsGroup)
        {
            SenderPushName = null;
            return;
        }

        bool isPhoneNumber = CurrentChat.Title.StartsWith('+') || CurrentChat.Title.All(c => char.IsDigit(c) || c == ' ' || c == '-');
        if (!isPhoneNumber)
        {
            SenderPushName = null;
            return;
        }

        // Only pick an incoming sender name that is a real name (NOT another phone number)
        var incomingMsg = Messages.LastOrDefault(m =>
            !m.IsOutgoing &&
            !string.IsNullOrWhiteSpace(m.SenderName) &&
            !m.SenderName.StartsWith('+') &&
            !m.SenderName.TrimStart('~').All(c => char.IsDigit(c) || c == ' ' || c == '-'));

        if (incomingMsg != null && !string.IsNullOrWhiteSpace(incomingMsg.SenderName))
        {
            var name = incomingMsg.SenderName.Trim().TrimStart('~').Trim();
            if (!string.IsNullOrEmpty(name))
            {
                SenderPushName = $"~{name}";
                return;
            }
        }

        SenderPushName = null;
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
