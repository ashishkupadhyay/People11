using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace People.Core.Models;

public enum MessageStatus
{
    Sending,
    Sent,
    Delivered,
    Read,
    Failed
}

public class Message : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string? SenderName { get; set; }
    public bool IsOutgoing { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    private MessageStatus _status;
    public MessageStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }
    public MessageContent Content { get; set; } = new();

    // Reply / Quote information
    public string? QuotedText { get; set; }
    public string? QuotedSender { get; set; }

    // Reactions
    public List<string> Reactions { get; set; } = new();

    public string FormattedTime => Timestamp != default ? Timestamp.ToLocalTime().ToString("t") : string.Empty;
    
    // For EF Core
    public Chat? Chat { get; set; }

    [NotMapped]
    public bool IsSystemNotice => Content?.Type == MessageContentType.SystemNotice;

    private bool _showDateSeparator;
    [NotMapped]
    public bool ShowDateSeparator
    {
        get => _showDateSeparator;
        set
        {
            if (_showDateSeparator != value)
            {
                _showDateSeparator = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAnySeparator));
            }
        }
    }

    private string? _dateSeparatorText;
    [NotMapped]
    public string? DateSeparatorText
    {
        get => _dateSeparatorText;
        set
        {
            if (_dateSeparatorText != value)
            {
                _dateSeparatorText = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _showUnreadSeparator;
    [NotMapped]
    public bool ShowUnreadSeparator
    {
        get => _showUnreadSeparator;
        set
        {
            if (_showUnreadSeparator != value)
            {
                _showUnreadSeparator = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAnySeparator));
            }
        }
    }

    private string? _unreadSeparatorText;
    [NotMapped]
    public string? UnreadSeparatorText
    {
        get => _unreadSeparatorText;
        set
        {
            if (_unreadSeparatorText != value)
            {
                _unreadSeparatorText = value;
                OnPropertyChanged();
            }
        }
    }

    [NotMapped]
    public bool HasAnySeparator => _showDateSeparator || _showUnreadSeparator;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
