using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using People.Core.Interfaces;
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
        Messages.Clear();
        IsLoading = true;
        HasMoreOlderMessages = true;
        UpdateChatDateString();

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
                RecalculateDateSeparators();
                UpdateChatDateString();
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
