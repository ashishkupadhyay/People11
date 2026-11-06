using CommunityToolkit.Mvvm.ComponentModel;

namespace People.Core.Models;

public partial class Chat : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _platformId = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isGroup;

    [ObservableProperty]
    private string? _pictureUrl;

    [ObservableProperty]
    private int _unreadCount;

    [ObservableProperty]
    private DateTimeOffset _lastMessageTime;

    [ObservableProperty]
    private Message? _lastMessage;

    public string LastMessagePreview
    {
        get
        {
            var msg = LastMessage;
            if (msg == null)
            {
                return string.Empty;
            }

            string text;
            if (msg.Content == null)
            {
                text = string.Empty;
            }
            else
            {
                switch (msg.Content.Type)
                {
                    case MessageContentType.Image:
                        text = string.IsNullOrWhiteSpace(msg.Content.Caption)
                            ? "📷 Photo"
                            : $"📷 {msg.Content.Caption.Trim()}";
                        break;
                    case MessageContentType.Video:
                        text = string.IsNullOrWhiteSpace(msg.Content.Caption)
                            ? "🎥 Video"
                            : $"🎥 {msg.Content.Caption.Trim()}";
                        break;
                    case MessageContentType.Document:
                        text = string.IsNullOrWhiteSpace(msg.Content.FileName)
                            ? "📄 Document"
                            : $"📄 {msg.Content.FileName.Trim()}";
                        break;
                    case MessageContentType.Audio:
                        text = "🎵 Audio";
                        break;
                    default:
                        text = msg.Content.Text?.Trim() ?? string.Empty;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            if (IsGroup)
            {
                if (msg.IsOutgoing)
                {
                    return $"You: {text}";
                }

                if (!string.IsNullOrWhiteSpace(msg.SenderName))
                {
                    return $"{msg.SenderName.Trim()}: {text}";
                }

                if (!string.IsNullOrWhiteSpace(msg.SenderId))
                {
                    var user = msg.SenderId.Contains('@') ? msg.SenderId.Split('@')[0] : msg.SenderId;
                    var phone = user.StartsWith('+') ? user : $"+{user}";
                    return $"{phone}: {text}";
                }
            }
            else if (msg.IsOutgoing)
            {
                return $"You: {text}";
            }

            return text;
        }
    }

    partial void OnLastMessageChanged(Message? value)
    {
        OnPropertyChanged(nameof(LastMessagePreview));
    }

    public string Initials => IsGroup ? string.Empty : ExtractInitials(Title);

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(Initials));
    }

    partial void OnIsGroupChanged(bool value)
    {
        OnPropertyChanged(nameof(Initials));
    }

    public static string ExtractInitials(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var trimmed = title.Trim();
        // If it's an unsaved number (starts with + or contains only digits/separators), return empty so person glyph displays
        if (trimmed.StartsWith('+') || trimmed.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c == '-' || c == '(' || c == ')'))
        {
            return string.Empty;
        }

        // Strip bracketed text like (CSIR-4PI (NIDSA)) or [Work] or {tag}
        var cleaned = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\([^\)]*\)", " ");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\[[^\]]*\]", " ");

        // Remove punctuation but keep letters and whitespace
        var words = cleaned.Split(new[] { ' ', '\t', ',', '.', '-', '_', '/', '\\', '\'', '"', ':', ';' }, StringSplitOptions.RemoveEmptyEntries)
                           .Where(w => w.Any(char.IsLetter))
                           .ToList();

        if (words.Count == 0)
            return string.Empty;

        if (words.Count == 1)
        {
            var firstLetter = words[0].First(char.IsLetter);
            return char.ToUpperInvariant(firstLetter).ToString();
        }

        // 2 or more words: take first letter of first word and first letter of second word
        var first = char.ToUpperInvariant(words[0].First(char.IsLetter));
        var second = char.ToUpperInvariant(words[1].First(char.IsLetter));
        return $"{first}{second}";
    }
    
    // For EF Core
    public ICollection<Contact> Participants { get; set; } = new List<Contact>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
