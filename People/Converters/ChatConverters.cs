using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace People.Converters;

public class OutgoingAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isOutgoing && isOutgoing)
            return HorizontalAlignment.Right;
        
        return HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class OutgoingBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush s_whatsAppBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 37, 211, 102));
    private static readonly SolidColorBrush s_telegramBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 36, 161, 222));
    private static readonly SolidColorBrush s_discordBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 88, 101, 242));
    private static readonly SolidColorBrush s_matrixBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0));
    private static readonly SolidColorBrush s_signalBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 58, 118, 240));
    private static readonly SolidColorBrush s_smsBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 212));
    private static readonly SolidColorBrush s_defaultBlueBrush = new(Microsoft.UI.Colors.Blue);
    private static readonly SolidColorBrush s_defaultGrayBrush = new(Microsoft.UI.Colors.LightGray);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = false;
        string platformId = "WhatsApp";

        if (value is People.Core.Models.Message msg)
        {
            isOutgoing = msg.IsOutgoing;
            platformId = msg.PlatformId;
        }
        else if (value is bool b)
        {
            isOutgoing = b;
        }

        if (isOutgoing)
        {
            var settings = (Microsoft.UI.Xaml.Application.Current as App)?.Services.GetService(typeof(People.Core.Interfaces.ISettingsService)) as People.Core.Interfaces.ISettingsService;
            
            // Normalize platformId for setting key (handles old database entries or missing data)
            if (string.IsNullOrEmpty(platformId) && value is People.Core.Models.Message m)
            {
                if (m.ChatId?.StartsWith("wa_") == true) platformId = "whatsapp";
                else if (m.ChatId?.StartsWith("tg_") == true) platformId = "telegram";
                else platformId = "whatsapp";
            }
            
            string normalizedPlatformId = platformId;
            if (string.Equals(platformId, "whatsapp", StringComparison.OrdinalIgnoreCase)) normalizedPlatformId = "WhatsApp";
            else if (string.Equals(platformId, "telegram", StringComparison.OrdinalIgnoreCase)) normalizedPlatformId = "Telegram";
            else if (string.Equals(platformId, "discord", StringComparison.OrdinalIgnoreCase)) normalizedPlatformId = "Discord";
            else if (string.Equals(platformId, "signal", StringComparison.OrdinalIgnoreCase)) normalizedPlatformId = "Signal";
            else if (string.Equals(platformId, "sms", StringComparison.OrdinalIgnoreCase)) normalizedPlatformId = "SMS";
            else if (string.Equals(platformId, "matrix", StringComparison.OrdinalIgnoreCase)) normalizedPlatformId = "Matrix";
            
            bool isBrandColor = settings?.GetValue<bool>("BrandColor_" + normalizedPlatformId, false) ?? false;

            if (isBrandColor)
            {
                if (normalizedPlatformId == "WhatsApp") return s_whatsAppBrush;
                if (normalizedPlatformId == "Telegram") return s_telegramBrush;
                if (normalizedPlatformId == "Discord") return s_discordBrush;
                if (normalizedPlatformId == "Matrix") return s_matrixBrush;
                if (normalizedPlatformId == "Signal") return s_signalBrush;
                if (normalizedPlatformId == "SMS") return s_smsBrush;
            }

            return App.Current.Resources["AccentFillColorDefaultBrush"] as SolidColorBrush ?? s_defaultBlueBrush;
        }
        else
        {
            // Incoming message background
            return App.Current.Resources["ControlFillColorDefaultBrush"] as SolidColorBrush ?? s_defaultGrayBrush;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
            return Visibility.Visible;
        
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}


public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string str && !string.IsNullOrWhiteSpace(str))
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string url && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                return new BitmapImage(new Uri(url));
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class OutgoingTextColorConverter : IValueConverter
{
    private static readonly SolidColorBrush s_white = new(Microsoft.UI.Colors.White);
    private static readonly SolidColorBrush s_black = new(Microsoft.UI.Colors.Black);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isOutgoing && isOutgoing)
        {
            return s_white;
        }
        return App.Current.Resources["TextFillColorPrimaryBrush"] as Brush ?? s_black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class OutgoingSubtextColorConverter : IValueConverter
{
    private static readonly SolidColorBrush s_translucentWhite = new(Microsoft.UI.ColorHelper.FromArgb(200, 255, 255, 255));
    private static readonly SolidColorBrush s_gray = new(Microsoft.UI.Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isOutgoing && isOutgoing)
        {
            return s_translucentWhite;
        }
        return App.Current.Resources["TextFillColorSecondaryBrush"] as Brush ?? s_gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class CollectionToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is System.Collections.ICollection coll)
        {
            return coll.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class GroupSenderVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is People.Core.Models.Message msg)
        {
            if (msg.IsOutgoing || string.IsNullOrWhiteSpace(msg.SenderName))
                return Visibility.Collapsed;

            // Only show sender name in group chats
            bool isGroup = !string.IsNullOrEmpty(msg.ChatId) && (msg.ChatId.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) || msg.ChatId.Contains("group", StringComparison.OrdinalIgnoreCase));
            return isGroup ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class PersonPictureDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is People.Core.Models.Chat chat)
        {
            if (chat.IsGroup)
            {
                return string.Empty;
            }

            var title = chat.Title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                var trimmed = title.Trim();
                // If it's an unsaved number, return empty string so PersonPicture displays the native person glyph rather than digit initials
                if (trimmed.StartsWith('+') || trimmed.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c == '-' || c == '(' || c == ')'))
                {
                    return string.Empty;
                }
                return title;
            }
            return string.Empty;
        }

        if (value is string titleStr && !string.IsNullOrWhiteSpace(titleStr))
        {
            var trimmed = titleStr.Trim();
            if (trimmed.StartsWith('+') || trimmed.All(c => char.IsDigit(c) || char.IsWhiteSpace(c) || c == '-' || c == '(' || c == ')'))
            {
                return string.Empty;
            }
            return titleStr;
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class ChatTimestampConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTimeOffset dto)
        {
            var local = dto.ToLocalTime();
            var today = DateTime.Today;
            if (local.Date == today)
            {
                return local.ToString("HH:mm");
            }
            if (local.Date == today.AddDays(-1))
            {
                return "Yesterday";
            }
            return local.ToString("dd/MM/yy");
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class ChatLastMessagePreviewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is People.Core.Models.Chat chat)
        {
            var msg = chat.LastMessage;
            if (msg == null || string.IsNullOrWhiteSpace(msg.Content?.Text))
            {
                return string.Empty;
            }

            var text = msg.Content.Text.Trim();

            if (chat.IsGroup)
            {
                if (msg.IsOutgoing)
                {
                    return $"You: {text}";
                }

                if (!string.IsNullOrWhiteSpace(msg.SenderName))
                {
                    var sender = msg.SenderName.Trim();
                    return $"{sender}: {text}";
                }

                if (!string.IsNullOrWhiteSpace(msg.SenderId))
                {
                    var user = msg.SenderId.Contains('@') ? msg.SenderId.Split('@')[0] : msg.SenderId;
                    var phone = user.StartsWith('+') ? user : $"+{user}";
                    return $"{phone}: {text}";
                }
            }

            return text;
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count && count > 0)
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class UnreadToTimestampBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count && count > 0)
        {
            if (Application.Current.Resources.TryGetValue("AppAccentBrush", out var accentBrush) && accentBrush is Microsoft.UI.Xaml.Media.Brush b)
            {
                return b;
            }
        }

        if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var secBrush) && secBrush is Microsoft.UI.Xaml.Media.Brush sb)
        {
            return sb;
        }

        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
