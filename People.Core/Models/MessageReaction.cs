using System;

namespace People.Core.Models;

public class MessageReaction
{
    public string Emoji { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string? SenderName { get; set; }
    public bool IsFromMe { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string DisplayName => IsFromMe ? "You" : (!string.IsNullOrWhiteSpace(SenderName) ? SenderName : (!string.IsNullOrWhiteSpace(SenderId) ? SenderId : "Unknown"));
}
