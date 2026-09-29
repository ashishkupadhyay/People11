namespace People.Core.Models;

public enum MessageStatus
{
    Sending,
    Sent,
    Delivered,
    Read,
    Failed
}

public class Message
{
    public string Id { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public bool IsOutgoing { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public MessageStatus Status { get; set; }
    public MessageContent Content { get; set; } = new();
    
    // For EF Core
    public Chat? Chat { get; set; }
}
