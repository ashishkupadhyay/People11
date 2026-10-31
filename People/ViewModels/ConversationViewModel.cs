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

    public ObservableCollection<Message> Messages { get; } = new();

    [ObservableProperty]
    public partial string MessageText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingOlder { get; set; }

    public ConversationViewModel(IMessagingProvider provider, IDispatcherService dispatcherService)
    {
        _provider = provider;
        _dispatcherService = dispatcherService;
        _provider.MessageReceived += OnMessageReceived;
        _provider.ChatUpdated += OnChatUpdated;
    }

    public async void LoadChat(Chat chat)
    {
        CurrentChat = chat;
        Messages.Clear();
        IsLoading = true;

        try
        {
            var msgs = await _provider.GetMessagesAsync(chat.Id);
            _dispatcherService.Enqueue(() =>
            {
                Messages.Clear();
                foreach (var msg in msgs)
                {
                    Messages.Add(msg);
                }
                IsLoading = false;

                // If chat has few or no messages, automatically fetch older messages from phone
                if (Messages.Count < 10)
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

    [RelayCommand]
    public async Task LoadOlderMessagesAsync()
    {
        if (CurrentChat == null || IsLoadingOlder) return;

        IsLoadingOlder = true;

        try
        {
            var older = await _provider.GetOlderMessagesAsync(CurrentChat.Id, 25);
            _dispatcherService.Enqueue(() =>
            {
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
                    }
                }
                IsLoadingOlder = false;
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
                    }
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
                }
            });
        }
    }
}
