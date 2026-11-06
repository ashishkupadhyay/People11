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
                OnPropertyChanged(nameof(FormattedFileType));
                OnPropertyChanged(nameof(FormattedSizeAndType));
            }
        }
    }

    private long? _fileSize;
    public long? FileSize
    {
        get => _fileSize;
        set
        {
            if (_fileSize != value)
            {
                _fileSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedFileSize));
                OnPropertyChanged(nameof(FormattedSizeAndType));
            }
        }
    }

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
                OnPropertyChanged(nameof(FormattedFileType));
                OnPropertyChanged(nameof(FormattedSizeAndType));
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

    // Audio Playback state (transient, not serialized)
    private bool _isPlaying;
    [JsonIgnore]
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged();
            }
        }
    }

    private double _playbackProgress;
    [JsonIgnore]
    public double PlaybackProgress
    {
        get => _playbackProgress;
        set
        {
            if (Math.Abs(_playbackProgress - value) > 0.05)
            {
                _playbackProgress = value;
                OnPropertyChanged();
            }
        }
    }

    private string? _playbackTimeText;
    [JsonIgnore]
    public string? PlaybackTimeText
    {
        get => _playbackTimeText;
        set
        {
            if (_playbackTimeText != value)
            {
                _playbackTimeText = value;
                OnPropertyChanged();
            }
        }
    }

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
            // IMPORTANT: For video, check for extracted video thumbnail frame first, then fallback to ThumbnailUrl.
            if (Type == MessageContentType.Video)
            {
                if (!string.IsNullOrEmpty(LocalFilePath))
                {
                    var thumbPath = LocalFilePath + ".thumb.jpg";
                    if (System.IO.File.Exists(thumbPath))
                        return thumbPath;
                }
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

    public string FormattedFileType
    {
        get
        {
            if (IsAudio)
            {
                return DurationSeconds.HasValue ? "Voice message" : "Audio";
            }

            var target = !string.IsNullOrWhiteSpace(FileName) ? FileName : (!string.IsNullOrWhiteSpace(LocalFilePath) ? LocalFilePath : string.Empty);
            var ext = System.IO.Path.GetExtension(target).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(MimeType))
            {
                if (MimeType.Contains("pdf", StringComparison.OrdinalIgnoreCase)) ext = ".pdf";
                else if (MimeType.Contains("zip", StringComparison.OrdinalIgnoreCase)) ext = ".zip";
                else if (MimeType.Contains("word", StringComparison.OrdinalIgnoreCase)) ext = ".docx";
                else if (MimeType.Contains("excel", StringComparison.OrdinalIgnoreCase) || MimeType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase)) ext = ".xlsx";
            }

            return ext switch
            {
                ".zip" => "Compressed (zipped) Folder",
                ".rar" or ".7z" or ".tar" or ".gz" => "Compressed Archive",
                ".pdf" => "PDF Document",
                ".docx" or ".doc" => "Microsoft Word Document",
                ".xlsx" or ".xls" => "Microsoft Excel Worksheet",
                ".pptx" or ".ppt" => "Microsoft PowerPoint Presentation",
                ".txt" => "Text Document",
                ".csv" => "CSV File",
                ".json" => "JSON File",
                ".xml" => "XML Document",
                ".html" or ".htm" => "HTML Document",
                ".mp3" or ".m4a" or ".wav" or ".ogg" or ".opus" or ".flac" => "Audio File",
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "Video File",
                ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "Image File",
                ".exe" => "Application",
                ".msi" => "Windows Installer Package",
                ".apk" => "Android Package",
                _ when !string.IsNullOrEmpty(ext) => $"{ext.TrimStart('.').ToUpperInvariant()} File",
                _ => "Document"
            };
        }
    }

    public string FormattedSizeAndType
    {
        get
        {
            var size = FormattedFileSize;
            var type = FormattedFileType;
            if (!string.IsNullOrEmpty(size) && !string.IsNullOrEmpty(type))
                return $"{size} • {type}";
            if (!string.IsNullOrEmpty(size))
                return size;
            return type ?? string.Empty;
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
