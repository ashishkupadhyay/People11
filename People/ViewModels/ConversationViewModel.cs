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

    public ConversationViewModel(IMessagingProvider provider, IDispatcherService dispatcherService)
    {
        _provider = provider;
        _dispatcherService = dispatcherService;
        _provider.MessageReceived += OnMessageReceived;
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
