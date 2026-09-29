using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using People.Core.Models;
using System.Collections.ObjectModel;

namespace People.ViewModels;

public partial class ConversationViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial Chat? CurrentChat { get; set; }

    public ObservableCollection<Message> Messages { get; } = new();

    [ObservableProperty]
    public partial string MessageText { get; set; } = string.Empty;

    public void LoadChat(Chat chat)
    {
        CurrentChat = chat;
        Messages.Clear();
        LoadMockMessages();
    }

    [RelayCommand]
    private void SendMessage()
    {
        if (string.IsNullOrWhiteSpace(MessageText)) return;

        Messages.Add(new Message 
        { 
            Id = System.Guid.NewGuid().ToString(), 
            IsOutgoing = true, 
            Content = new MessageContent { Text = MessageText }, 
            Timestamp = System.DateTimeOffset.Now 
        });

        MessageText = string.Empty;
    }

    private void LoadMockMessages()
    {
        Messages.Add(new Message 
        { 
            Id = "1", 
            IsOutgoing = false, 
            Content = new MessageContent { Text = "Hello!" }, 
            Timestamp = DateTimeOffset.Now.AddMinutes(-10) 
        });
        Messages.Add(new Message 
        { 
            Id = "2", 
            IsOutgoing = true, 
            Content = new MessageContent { Text = "Hey! What's up?" }, 
            Timestamp = DateTimeOffset.Now.AddMinutes(-5) 
        });
    }
}
