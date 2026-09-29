using CommunityToolkit.Mvvm.ComponentModel;
using People.Core.Models;
using System.Collections.ObjectModel;

namespace People.ViewModels;

public partial class ConversationViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial Chat? CurrentChat { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<Message> Messages { get; set; } = new();

    [ObservableProperty]
    public partial string MessageText { get; set; } = string.Empty;

    public void LoadChat(Chat chat)
    {
        CurrentChat = chat;
        Messages.Clear();
        LoadMockMessages();
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
