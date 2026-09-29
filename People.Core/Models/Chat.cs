namespace People.Core.Models;

public class Chat
{
    public string Id { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsGroup { get; set; }
    public string? PictureUrl { get; set; }
    public int UnreadCount { get; set; }
    public DateTimeOffset LastMessageTime { get; set; }
    public Message? LastMessage { get; set; }
    
    // For EF Core
    public ICollection<Contact> Participants { get; set; } = new List<Contact>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
