using System;
using System.Collections.Generic;

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
    public string? SenderName { get; set; }
    public bool IsOutgoing { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public MessageStatus Status { get; set; }
    public MessageContent Content { get; set; } = new();

    // Reply / Quote information
    public string? QuotedText { get; set; }
    public string? QuotedSender { get; set; }

    // Reactions
    public List<string> Reactions { get; set; } = new();

    public string FormattedTime => Timestamp != default ? Timestamp.ToLocalTime().ToString("t") : string.Empty;
    
    // For EF Core
    public Chat? Chat { get; set; }
}
