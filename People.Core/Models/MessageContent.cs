namespace People.Core.Models;

public enum MessageContentType
{
    Text,
    Image,
    Video,
    Audio,
    Document,
    Location
}

public class MessageContent
{
    public MessageContentType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? MediaUrl { get; set; }
    public string? MimeType { get; set; }
    public byte[]? ThumbnailData { get; set; }
}
