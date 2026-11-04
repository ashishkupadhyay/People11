namespace People.Core.Models;

public enum MessageContentType
{
    Text,
    Image,
    Video,
    Audio,
    Document,
    Location,
    SystemNotice
}

public class MessageContent
{
    public MessageContentType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? MediaUrl { get; set; }
    public string? MimeType { get; set; }
    public byte[]? ThumbnailData { get; set; }

    // Media properties
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? LocalFilePath { get; set; }
    public string? Caption { get; set; }
    public int? DurationSeconds { get; set; }

    // Helpers
    public bool IsImage => Type == MessageContentType.Image;
    public bool IsVideo => Type == MessageContentType.Video;
    public bool IsAudio => Type == MessageContentType.Audio;
    public bool IsDocument => Type == MessageContentType.Document;
    public bool IsMedia => IsImage || IsVideo || IsAudio || IsDocument;

    public bool IsDownloaded => !string.IsNullOrEmpty(LocalFilePath) && System.IO.File.Exists(LocalFilePath);

    public string DisplayName => !string.IsNullOrWhiteSpace(FileName)
        ? FileName
        : (!string.IsNullOrWhiteSpace(Caption) ? Caption : Type.ToString());

    public string? DisplayCaptionOrText
    {
        get
        {
            if (IsMedia)
            {
                return !string.IsNullOrWhiteSpace(Caption) ? Caption : null;
            }
            return !string.IsNullOrWhiteSpace(Text) ? Text : null;
        }
    }

    public bool HasTextOrCaption => !string.IsNullOrWhiteSpace(DisplayCaptionOrText);

    public string? EffectiveImagePath
    {
        get
        {
            if (!string.IsNullOrEmpty(LocalFilePath) && System.IO.File.Exists(LocalFilePath))
                return LocalFilePath;
            if (!string.IsNullOrEmpty(ThumbnailUrl))
                return ThumbnailUrl;
            if (!string.IsNullOrEmpty(MediaUrl) && System.IO.File.Exists(MediaUrl))
                return MediaUrl;
            return null;
        }
    }

    public string FormattedFileSize
    {
        get
        {
            if (!FileSize.HasValue || FileSize.Value <= 0) return string.Empty;
            double bytes = FileSize.Value;
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024 * 1024 * 1024):0.0} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024 * 1024):0.0} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024:0} KB";
            return $"{bytes} B";
        }
    }

    public string FormattedDuration
    {
        get
        {
            if (!DurationSeconds.HasValue || DurationSeconds.Value <= 0) return string.Empty;
            int totalSec = DurationSeconds.Value;
            int mins = totalSec / 60;
            int secs = totalSec % 60;
            return $"{mins}:{secs:D2}";
        }
    }
}
