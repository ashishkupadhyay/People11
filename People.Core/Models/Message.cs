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
    private static double _sharedMaxBubbleWidth = 480;
    public static double SharedMaxBubbleWidth
    {
        get => _sharedMaxBubbleWidth;
        set
        {
            if (Math.Abs(_sharedMaxBubbleWidth - value) > 1)
            {
                _sharedMaxBubbleWidth = value;
                SharedMaxWidthChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    private static double _sharedMaxMediaWidth = 320;
    public static double SharedMaxMediaWidth
    {
        get => _sharedMaxMediaWidth;
        set
        {
            if (Math.Abs(_sharedMaxMediaWidth - value) > 1)
            {
                _sharedMaxMediaWidth = value;
                SharedMaxWidthChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    public static event EventHandler? SharedMaxWidthChanged;

    public double MaxBubbleWidth => _sharedMaxBubbleWidth;
    public double MaxMediaWidth => Math.Min(_sharedMaxMediaWidth, _sharedMaxBubbleWidth);

    public Message()
    {
        SharedMaxWidthChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(MaxBubbleWidth));
            OnPropertyChanged(nameof(MaxMediaWidth));
        };
    }
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
    private MessageContent _content = new();
    public MessageContent Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                _content = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSystemNotice));
            }
        }
    }

    // Reply / Quote information
    public string? QuotedText { get; set; }
    public string? QuotedSender { get; set; }

    // Reactions
    private List<MessageReaction> _reactions = new();
    public List<MessageReaction> Reactions
    {
        get => _reactions;
        set
        {
            if (_reactions != value)
            {
                _reactions = value ?? new List<MessageReaction>();
                OnPropertyChanged();
                NotifyReactionPropertiesChanged();
            }
        }
    }

    [NotMapped]
    public IReadOnlyList<string> DistinctReactionEmojis =>
        _reactions.Select(r => r.Emoji).Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();

    [NotMapped]
    public int TotalReactionCount => _reactions.Count;

    [NotMapped]
    public bool HasReactions => _reactions.Count > 0;

    [NotMapped]
    public bool HasMultipleReactions => _reactions.Count > 1;

    [NotMapped]
    public bool HasUserReacted => _reactions.Any(r => r.IsFromMe);

    [NotMapped]
    public MessageReaction? UserReaction => _reactions.FirstOrDefault(r => r.IsFromMe);

    public void NotifyReactionPropertiesChanged()
    {
        OnPropertyChanged(nameof(DistinctReactionEmojis));
        OnPropertyChanged(nameof(TotalReactionCount));
        OnPropertyChanged(nameof(HasReactions));
        OnPropertyChanged(nameof(HasMultipleReactions));
        OnPropertyChanged(nameof(HasUserReacted));
        OnPropertyChanged(nameof(UserReaction));
    }

    public void SetUserReaction(string senderId, string? senderName, bool isFromMe, string emoji)
    {
        var updated = new List<MessageReaction>(_reactions);
        updated.RemoveAll(r => (isFromMe && r.IsFromMe) || (!string.IsNullOrEmpty(senderId) && r.SenderId == senderId));
        if (!string.IsNullOrWhiteSpace(emoji))
        {
            updated.Add(new MessageReaction
            {
                Emoji = emoji,
                SenderId = senderId,
                SenderName = senderName,
                IsFromMe = isFromMe,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        Reactions = updated;
    }

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
