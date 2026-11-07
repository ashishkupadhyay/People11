using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
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

        if (value is People.Core.Models.Message msg)
        {
            isOutgoing = msg.IsOutgoing;
        }
        else if (value is bool b)
        {
            isOutgoing = b;
        }

        if (isOutgoing)
        {
            return App.Current.Resources["OutgoingBubbleBrush"] as SolidColorBrush ?? s_defaultBlueBrush;
        }
        else
        {
            // Incoming message background
            return App.Current.Resources["IncomingBubbleBrush"] as SolidColorBrush ?? s_defaultGrayBrush;
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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource> _blurCache = new(StringComparer.Ordinal);

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string url && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                if (url.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                {
                    if (_blurCache.TryGetValue(url, out var cached))
                    {
                        return cached;
                    }

                    var commaIdx = url.IndexOf(',');
                    if (commaIdx >= 0)
                    {
                        var base64 = url.Substring(commaIdx + 1);
                        var bytes = System.Convert.FromBase64String(base64);

                        try
                        {
                            var decodeResult = Task.Run<(byte[]? smooth, int targetW, int targetH)>(async () =>
                            {
                                using var memStream = new System.IO.MemoryStream(bytes);
                                using var ras = memStream.AsRandomAccessStream();
                                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
                                var w = (int)decoder.PixelWidth;
                                var h = (int)decoder.PixelHeight;

                                if (w > 0 && h > 0)
                                {
                                    var pixelProvider = await decoder.GetPixelDataAsync(
                                        Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                                        Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                                        new Windows.Graphics.Imaging.BitmapTransform(),
                                        Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
                                        Windows.Graphics.Imaging.ColorManagementMode.ColorManageToSRgb);
                                    var px = pixelProvider.DetachPixelData();
                                    int targetW = Math.Clamp(w * 8, 256, 384);
                                    int targetH = Math.Clamp(h * 8, 256, 384);
                                    var smooth = BilinearScaleAndBlur(px, w, h, targetW, targetH, 16);
                                    return (smooth, targetW, targetH);
                                }
                                return (null, 0, 0);
                            }).GetAwaiter().GetResult();

                            if (decodeResult.smooth != null && decodeResult.targetW > 0 && decodeResult.targetH > 0)
                            {
                                var wb = new WriteableBitmap(decodeResult.targetW, decodeResult.targetH);
                                using (var pixelStream = wb.PixelBuffer.AsStream())
                                {
                                    pixelStream.Write(decodeResult.smooth, 0, decodeResult.smooth.Length);
                                }
                                wb.Invalidate();
                                _blurCache[url] = wb;
                                return wb;
                            }
                        }
                        catch
                        {
                            // Fallback to standard BitmapImage if decoding or WIC is unavailable
                        }

                        var ms = new System.IO.MemoryStream(bytes);
                        var bitmap = new BitmapImage();
                        ms.Position = 0;
                        bitmap.SetSource(ms.AsRandomAccessStream());
                        _blurCache[url] = bitmap;
                        return bitmap;
                    }
                }

                if (System.IO.File.Exists(url))
                {
                    return new BitmapImage(new Uri(url));
                }

                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    return new BitmapImage(uri);
                }
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static byte[] BilinearScaleAndBlur(byte[] src, int srcW, int srcH, int dstW, int dstH, int blurRadius)
    {
        var scaled = new byte[dstW * dstH * 4];
        float xRatio = (float)(srcW - 1) / Math.Max(1, dstW - 1);
        float yRatio = (float)(srcH - 1) / Math.Max(1, dstH - 1);

        for (int y = 0; y < dstH; y++)
        {
            float srcY = y * yRatio;
            int yLow = (int)srcY;
            int yHigh = Math.Min(srcH - 1, yLow + 1);
            float yWeight = srcY - yLow;

            int dstRowStart = y * dstW * 4;
            int srcRowLow = yLow * srcW * 4;
            int srcRowHigh = yHigh * srcW * 4;

            for (int x = 0; x < dstW; x++)
            {
                float srcX = x * xRatio;
                int xLow = (int)srcX;
                int xHigh = Math.Min(srcW - 1, xLow + 1);
                float xWeight = srcX - xLow;

                int idxTL = srcRowLow + (xLow * 4);
                int idxTR = srcRowLow + (xHigh * 4);
                int idxBL = srcRowHigh + (xLow * 4);
                int idxBR = srcRowHigh + (xHigh * 4);

                int dstIdx = dstRowStart + (x * 4);
                for (int c = 0; c < 4; c++)
                {
                    float top = src[idxTL + c] * (1f - xWeight) + src[idxTR + c] * xWeight;
                    float bottom = src[idxBL + c] * (1f - xWeight) + src[idxBR + c] * xWeight;
                    scaled[dstIdx + c] = (byte)(top * (1f - yWeight) + bottom * yWeight);
                }
            }
        }

        ApplyBoxBlur(scaled, dstW, dstH, blurRadius);
        return scaled;
    }

    private static void ApplyBoxBlur(byte[] pixels, int w, int h, int r)
    {
        if (w <= 1 || h <= 1 || r <= 0) return;
        byte[] temp = new byte[pixels.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlurHorizontal(pixels, temp, w, h, r);
            BoxBlurVertical(temp, pixels, w, h, r);
        }
    }

    private static void BoxBlurHorizontal(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int bSum = 0, gSum = 0, rSum = 0, count = 0;
                int xMin = Math.Max(0, x - r);
                int xMax = Math.Min(w - 1, x + r);
                for (int kx = xMin; kx <= xMax; kx++)
                {
                    int idx = rowStart + (kx * 4);
                    bSum += src[idx];
                    gSum += src[idx + 1];
                    rSum += src[idx + 2];
                    count++;
                }
                int dstIdx = rowStart + (x * 4);
                dst[dstIdx] = (byte)(bSum / count);
                dst[dstIdx + 1] = (byte)(gSum / count);
                dst[dstIdx + 2] = (byte)(rSum / count);
                dst[dstIdx + 3] = src[dstIdx + 3];
            }
        }
    }

    private static void BoxBlurVertical(byte[] src, byte[] dst, int w, int h, int r)
    {
        for (int x = 0; x < w; x++)
        {
            int colOffset = x * 4;
            for (int y = 0; y < h; y++)
            {
                int bSum = 0, gSum = 0, rSum = 0, count = 0;
                int yMin = Math.Max(0, y - r);
                int yMax = Math.Min(h - 1, y + r);
                for (int ky = yMin; ky <= yMax; ky++)
                {
                    int idx = (ky * w * 4) + colOffset;
                    bSum += src[idx];
                    gSum += src[idx + 1];
                    rSum += src[idx + 2];
                    count++;
                }
                int dstIdx = (y * w * 4) + colOffset;
                dst[dstIdx] = (byte)(bSum / count);
                dst[dstIdx + 1] = (byte)(gSum / count);
                dst[dstIdx + 2] = (byte)(rSum / count);
                dst[dstIdx + 3] = src[dstIdx + 3];
            }
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class BoolToPlayPauseGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isPlaying && isPlaying)
        {
            return "\uE769"; // Pause
        }
        return "\uE768"; // Play
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class OutgoingTextColorConverter : IValueConverter
{
    private static readonly SolidColorBrush s_white = new(Microsoft.UI.Colors.White);
    private static readonly SolidColorBrush s_black = new(Microsoft.UI.Colors.Black);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = false;
        if (value is People.Core.Models.Message msg)
        {
            isOutgoing = msg.IsOutgoing;
        }
        else if (value is bool b)
        {
            isOutgoing = b;
        }

        if (isOutgoing)
        {
            return App.Current.Resources["OutgoingBubbleTextBrush"] as SolidColorBrush ?? s_white;
        }
        return App.Current.Resources["IncomingBubbleTextBrush"] as SolidColorBrush ?? s_black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class OutgoingSubtextColorConverter : IValueConverter
{
    private static readonly SolidColorBrush s_translucentWhite = new(Microsoft.UI.ColorHelper.FromArgb(200, 255, 255, 255));
    private static readonly SolidColorBrush s_gray = new(Microsoft.UI.Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = false;
        if (value is People.Core.Models.Message msg)
        {
            isOutgoing = msg.IsOutgoing;
        }
        else if (value is bool b)
        {
            isOutgoing = b;
        }

        if (isOutgoing)
        {
            return App.Current.Resources["OutgoingBubbleSubtextBrush"] as SolidColorBrush ?? s_translucentWhite;
        }
        return App.Current.Resources["IncomingBubbleSubtextBrush"] as SolidColorBrush ?? s_gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class QuotedBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = value is People.Core.Models.Message msg ? msg.IsOutgoing : (value is bool b && b);
        return isOutgoing
            ? App.Current.Resources["QuotedOutgoingBackgroundBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            : App.Current.Resources["QuotedIncomingBackgroundBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class QuotedBorderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = value is People.Core.Models.Message msg ? msg.IsOutgoing : (value is bool b && b);
        return isOutgoing
            ? App.Current.Resources["QuotedOutgoingBorderBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray)
            : App.Current.Resources["QuotedIncomingBorderBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class QuotedSenderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = value is People.Core.Models.Message msg ? msg.IsOutgoing : (value is bool b && b);
        return isOutgoing
            ? App.Current.Resources["QuotedOutgoingSenderBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.White)
            : App.Current.Resources["QuotedIncomingSenderBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Black);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class QuotedTextBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = value is People.Core.Models.Message msg ? msg.IsOutgoing : (value is bool b && b);
        return isOutgoing
            ? App.Current.Resources["QuotedOutgoingTextBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.White)
            : App.Current.Resources["QuotedIncomingTextBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class BubbleCornerRadiusConverter : IValueConverter
{
    private static readonly CornerRadius s_outgoingRadius = new(8, 0, 8, 8);
    private static readonly CornerRadius s_incomingRadius = new(0, 8, 8, 8);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = value is bool b && b;
        return isOutgoing ? s_outgoingRadius : s_incomingRadius;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && !b)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class MessageStatusSendingVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (value is People.Core.Models.MessageStatus s && s == People.Core.Models.MessageStatus.Sending)
            ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class MessageStatusSentVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (value is People.Core.Models.MessageStatus s && s == People.Core.Models.MessageStatus.Sent)
            ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class MessageStatusDoubleCheckVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return (value is People.Core.Models.MessageStatus s && (s == People.Core.Models.MessageStatus.Delivered || s == People.Core.Models.MessageStatus.Read))
            ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class MessageStatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush s_readCyan = new(Microsoft.UI.ColorHelper.FromArgb(255, 83, 189, 235)); // #53BDEB

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is People.Core.Models.MessageStatus s && s == People.Core.Models.MessageStatus.Read)
        {
            return s_readCyan;
        }
        return App.Current.Resources["OutgoingBubbleSubtextBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.LightGray);
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
            if (local.Date > today.AddDays(-7))
            {
                return local.ToString("dddd");
            }
            return local.ToString("dd-MM-yy");
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

        if (Application.Current.Resources.TryGetValue("ChatListTimestampBrush", out var tsBrush) && tsBrush is Microsoft.UI.Xaml.Media.Brush tb)
        {
            return tb;
        }

        if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var secBrush) && secBrush is Microsoft.UI.Xaml.Media.Brush sb)
        {
            return sb;
        }

        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class StringNullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null || (value is string s && string.IsNullOrWhiteSpace(s)))
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
