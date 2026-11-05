using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;

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

public class MessageContent : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private MessageContentType _type;
    public MessageContentType Type
    {
        get => _type;
        set
        {
            if (_type != value)
            {
                _type = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsImage));
                OnPropertyChanged(nameof(IsVideo));
                OnPropertyChanged(nameof(IsAudio));
                OnPropertyChanged(nameof(IsDocument));
                OnPropertyChanged(nameof(IsMedia));
                OnPropertyChanged(nameof(DisplayCaptionOrText));
                OnPropertyChanged(nameof(HasTextOrCaption));
                OnPropertyChanged(nameof(EffectiveImagePath));
            }
        }
    }

    private string _text = string.Empty;
    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayCaptionOrText));
                OnPropertyChanged(nameof(HasTextOrCaption));
            }
        }
    }

    public string? MediaUrl { get; set; }
    public string? MimeType { get; set; }
    public byte[]? ThumbnailData { get; set; }

    // Media properties
    private string? _fileName;
    public string? FileName
    {
        get => _fileName;
        set
        {
            if (_fileName != value)
            {
                _fileName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public long? FileSize { get; set; }

    private string? _thumbnailUrl;
    public string? ThumbnailUrl
    {
        get => _thumbnailUrl;
        set
        {
            if (_thumbnailUrl != value)
            {
                _thumbnailUrl = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveImagePath));
            }
        }
    }

    private string? _localFilePath;
    public string? LocalFilePath
    {
        get => _localFilePath;
        set
        {
            if (_localFilePath != value)
            {
                _localFilePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDownloaded));
                OnPropertyChanged(nameof(ShowDownloadIcon));
                OnPropertyChanged(nameof(EffectiveImagePath));
            }
        }
    }

    private string? _caption;
    public string? Caption
    {
        get => _caption;
        set
        {
            if (_caption != value)
            {
                _caption = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(DisplayCaptionOrText));
                OnPropertyChanged(nameof(HasTextOrCaption));
            }
        }
    }

    public int? DurationSeconds { get; set; }

    // Download state properties (transient, not serialized)
    private bool _isDownloading;
    [JsonIgnore]
    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (_isDownloading != value)
            {
                _isDownloading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowDownloadIcon));
            }
        }
    }

    private double _downloadProgress;
    [JsonIgnore]
    public double DownloadProgress
    {
        get => _downloadProgress;
        set
        {
            if (Math.Abs(_downloadProgress - value) > 0.001)
            {
                _downloadProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    [JsonIgnore]
    public bool IsProgressIndeterminate => DownloadProgress <= 0.0;

    [JsonIgnore]
    public bool ShowDownloadIcon => !IsDownloading && !IsDownloaded;

    [JsonIgnore]
    public CancellationTokenSource? DownloadCts { get; set; }

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
            // IMPORTANT: For video, NEVER return LocalFilePath because LocalFilePath is an MP4 video file, NOT an image.
            if (Type == MessageContentType.Video)
            {
                if (!string.IsNullOrEmpty(ThumbnailUrl))
                    return ThumbnailUrl;
                return null;
            }
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
