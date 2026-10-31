using CommunityToolkit.Mvvm.ComponentModel;

namespace People.Core.Models;

public partial class Chat : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _platformId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isGroup;

    [ObservableProperty]
    private string? _pictureUrl;

    [ObservableProperty]
    private int _unreadCount;

    [ObservableProperty]
    private DateTimeOffset _lastMessageTime;

    [ObservableProperty]
    private Message? _lastMessage;
    
    // For EF Core
    public ICollection<Contact> Participants { get; set; } = new List<Contact>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
