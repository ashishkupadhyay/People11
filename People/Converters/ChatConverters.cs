using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace People.Converters;

public interface IScopedResourceConverter
{
    ResourceDictionary? ScopedResources { get; set; }
}

public static class ScopedResourceHelper
{
    public static object? ResolveBrush(ResourceDictionary? scopedResources, string key)
    {
        if (scopedResources != null && scopedResources.TryGetValue(key, out var res))
        {
            return res;
        }
        if (Application.Current.Resources.TryGetValue(key, out var appRes))
        {
            return appRes;
        }
        return null;
    }
}

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

public class OutgoingBackgroundConverter : IValueConverter, IScopedResourceConverter
{
    public ResourceDictionary? ScopedResources { get; set; }

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
            return ScopedResourceHelper.ResolveBrush(ScopedResources, "OutgoingBubbleBrush") as SolidColorBrush ?? s_defaultBlueBrush;
        }
        else
        {
            // Incoming message background
            return ScopedResourceHelper.ResolveBrush(ScopedResources, "IncomingBubbleBrush") as SolidColorBrush ?? s_defaultGrayBrush;
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

public class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
            return Visibility.Collapsed;
        
        return Visibility.Visible;
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
                string? base64 = null;
                if (url.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIdx = url.IndexOf(',');
                    if (commaIdx >= 0)
                    {
                        base64 = url.Substring(commaIdx + 1).Trim();
                    }
                }
                else if (url.Length > 50 && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("ms-appx", StringComparison.OrdinalIgnoreCase) && !url.Contains(':') && !System.IO.File.Exists(url))
                {
                    base64 = url.Trim();
                }

                if (!string.IsNullOrEmpty(base64))
                {
                    if (_blurCache.TryGetValue(url, out var cached))
                    {
                        return cached;
                    }

                    byte[]? bytes = null;
                    try
                    {
                        bytes = System.Convert.FromBase64String(base64);
                    }
                    catch { }

                    if (bytes != null && bytes.Length > 0)
                    {
                        var blurred = DecodeAndBlurThumbnail(bytes);
                        if (blurred != null)
                        {
                            _blurCache[url] = blurred;
                            return blurred;
                        }
                    }
                }

                if (url.Contains("winui_gallery_ink_canvas", StringComparison.OrdinalIgnoreCase))
                {
                    bool isDark = false;
                    try
                    {
                        if (App.Current is App app && app.MainWindow?.Content is FrameworkElement rootFe)
                        {
                            isDark = rootFe.ActualTheme == ElementTheme.Dark;
                        }
                        else if (Application.Current.RequestedTheme == ApplicationTheme.Dark)
                        {
                            isDark = true;
                        }
                    }
                    catch { }

                    string targetFileName = isDark ? "winui_gallery_ink_canvas_dark.png" : "winui_gallery_ink_canvas_light.png";

                    // 1. Try local disk path in AppContext.BaseDirectory
                    string diskPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Conversation_Test_Images", targetFileName);
                    if (!System.IO.File.Exists(diskPath))
                    {
                        var srcDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Conversation_Test_Images", targetFileName));
                        if (System.IO.File.Exists(srcDir))
                            diskPath = srcDir;
                    }

                    if (System.IO.File.Exists(diskPath))
                    {
                        return new BitmapImage(new Uri(diskPath));
                    }

                    // 2. Try ms-appx
                    try
                    {
                        return new BitmapImage(new Uri($"ms-appx:///Assets/Conversation_Test_Images/{targetFileName}"));
                    }
                    catch { }

                    // 3. Fallback to light theme image if dark is unavailable
                    string lightDiskPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Conversation_Test_Images", "winui_gallery_ink_canvas_light.png");
                    if (System.IO.File.Exists(lightDiskPath))
                    {
                        return new BitmapImage(new Uri(lightDiskPath));
                    }
                    return new BitmapImage(new Uri("ms-appx:///Assets/Conversation_Test_Images/winui_gallery_ink_canvas_light.png"));
                }

                if (System.IO.File.Exists(url))
                {
                    var bmp = new BitmapImage();
                    if (string.Equals(parameter?.ToString(), "Avatar", StringComparison.OrdinalIgnoreCase))
                    {
                        bmp.DecodePixelWidth = 128;
                    }
                    bmp.UriSource = new Uri(url);
                    return bmp;
                }

                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var bmp = new BitmapImage();
                    if (string.Equals(parameter?.ToString(), "Avatar", StringComparison.OrdinalIgnoreCase))
                    {
                        bmp.DecodePixelWidth = 128;
                    }
                    bmp.UriSource = uri;
                    return bmp;
                }
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static bool TryReadImageDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            // PNG IHDR chunk (big-endian width at 16, height at 20)
            width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            return width > 0 && height > 0;
        }
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            // JPEG SOF0/SOF2 marker scan
            int i = 2;
            while (i + 9 < bytes.Length)
            {
                if (bytes[i] != 0xFF)
                {
                    i++;
                    continue;
                }
                byte marker = bytes[i + 1];
                if (marker == 0xC0 || marker == 0xC2 || marker == 0xC1)
                {
                    height = (bytes[i + 5] << 8) | bytes[i + 6];
                    width = (bytes[i + 7] << 8) | bytes[i + 8];
                    return width > 0 && height > 0;
                }
                if (marker == 0xD9 || marker == 0xDA) break;
                if (marker == 0x00 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8))
                {
                    i += 2;
                    continue;
                }
                int segLen = (bytes[i + 2] << 8) | bytes[i + 3];
                if (segLen < 2) break;
                i += 2 + segLen;
            }
        }
        return false;
    }

    private static ImageSource? DecodeAndBlurThumbnail(byte[] bytes)
    {
        try
        {
            TryReadImageDimensions(bytes, out int srcW, out int srcH);
            if (srcW <= 0 || srcH <= 0)
            {
                srcW = 40;
                srcH = 30;
            }

            // Keep 4x upscaled minimum 160x120 while preserving aspect ratio for pre-allocated WriteableBitmap
            int targetW = Math.Clamp(srcW * 4, 160, 320);
            int targetH = Math.Clamp(srcH * 4, 120, 320);

            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var wb = new WriteableBitmap(targetW, targetH);

            // Decode, scale, and blur completely off the UI thread so scrolling/navigation never stalls
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    using var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    using (var stream = ras.AsStreamForWrite())
                    {
                        await stream.WriteAsync(bytes, 0, bytes.Length);
                        await stream.FlushAsync();
                    }
                    ras.Seek(0);

                    var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
                    if (decoder.PixelWidth <= 0 || decoder.PixelHeight <= 0) return;

                    var transform = new Windows.Graphics.Imaging.BitmapTransform
                    {
                        ScaledWidth = (uint)targetW,
                        ScaledHeight = (uint)targetH,
                        InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Linear
                    };

                    var pixelProvider = await decoder.GetPixelDataAsync(
                        Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                        Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                        transform,
                        Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
                        Windows.Graphics.Imaging.ColorManagementMode.ColorManageToSRgb);

                    byte[] px = pixelProvider.DetachPixelData();
                    if (px == null || px.Length == 0) return;

                    ApplyFastStackBlur(px, targetW, targetH, 10);

                    var dq = dispatcher ?? wb.DispatcherQueue;
                    dq?.TryEnqueue(() =>
                    {
                        try
                        {
                            using var pixelStream = wb.PixelBuffer.AsStream();
                            int len = Math.Min(px.Length, (int)pixelStream.Length);
                            pixelStream.Write(px, 0, len);
                            wb.Invalidate();
                        }
                        catch { }
                    });
                }
                catch { }
            });

            return wb;
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyFastStackBlur(byte[] pixels, int w, int h, int radius)
    {
        if (radius < 1 || w <= 0 || h <= 0) return;
        byte[] temp = new byte[pixels.Length];
        
        // 3 passes of horizontal + vertical box blur = Gaussian approximation
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlurHorizontal(pixels, temp, w, h, radius);
            BoxBlurVertical(temp, pixels, w, h, radius);
        }
    }

    private static void BoxBlurHorizontal(byte[] src, byte[] dst, int w, int h, int r)
    {
        float iarr = 1f / (r + r + 1);
        for (int y = 0; y < h; y++)
        {
            int ti = y * w * 4;
            int li = ti;
            int ri = ti + r * 4;
            int fv_b = src[ti], fv_g = src[ti + 1], fv_r = src[ti + 2], fv_a = src[ti + 3];
            int lv_b = src[ti + (w - 1) * 4], lv_g = src[ti + (w - 1) * 4 + 1], lv_r = src[ti + (w - 1) * 4 + 2], lv_a = src[ti + (w - 1) * 4 + 3];

            int val_b = (r + 1) * fv_b;
            int val_g = (r + 1) * fv_g;
            int val_r = (r + 1) * fv_r;
            int val_a = (r + 1) * fv_a;

            for (int j = 0; j < r; j++)
            {
                int idx = ti + Math.Min(j, w - 1) * 4;
                val_b += src[idx];
                val_g += src[idx + 1];
                val_r += src[idx + 2];
                val_a += src[idx + 3];
            }

            for (int j = 0; j <= r; j++)
            {
                int r_idx = ti + Math.Min(ri / 4 - y * w, w - 1) * 4;
                val_b += src[r_idx] - fv_b;
                val_g += src[r_idx + 1] - fv_g;
                val_r += src[r_idx + 2] - fv_r;
                val_a += src[r_idx + 3] - fv_a;

                dst[ti] = (byte)(val_b * iarr + 0.5f);
                dst[ti + 1] = (byte)(val_g * iarr + 0.5f);
                dst[ti + 2] = (byte)(val_r * iarr + 0.5f);
                dst[ti + 3] = (byte)(val_a * iarr + 0.5f);

                ri += 4;
                ti += 4;
            }

            for (int j = r + 1; j < w - r; j++)
            {
                int r_idx = ti + r * 4;
                int l_idx = ti - (r + 1) * 4;

                val_b += src[r_idx] - src[l_idx];
                val_g += src[r_idx + 1] - src[l_idx + 1];
                val_r += src[r_idx + 2] - src[l_idx + 2];
                val_a += src[r_idx + 3] - src[l_idx + 3];

                dst[ti] = (byte)(val_b * iarr + 0.5f);
                dst[ti + 1] = (byte)(val_g * iarr + 0.5f);
                dst[ti + 2] = (byte)(val_r * iarr + 0.5f);
                dst[ti + 3] = (byte)(val_a * iarr + 0.5f);

                ti += 4;
            }

            for (int j = w - r; j < w; j++)
            {
                int l_idx = ti - (r + 1) * 4;

                val_b += lv_b - src[l_idx];
                val_g += lv_g - src[l_idx + 1];
                val_r += lv_r - src[l_idx + 2];
                val_a += lv_a - src[l_idx + 3];

                dst[ti] = (byte)(val_b * iarr + 0.5f);
                dst[ti + 1] = (byte)(val_g * iarr + 0.5f);
                dst[ti + 2] = (byte)(val_r * iarr + 0.5f);
                dst[ti + 3] = (byte)(val_a * iarr + 0.5f);

                ti += 4;
            }
        }
    }

    private static void BoxBlurVertical(byte[] src, byte[] dst, int w, int h, int r)
    {
        float iarr = 1f / (r + r + 1);
        int stride = w * 4;
        for (int x = 0; x < w; x++)
        {
            int ti = x * 4;
            int li = ti;
            int ri = ti + r * stride;

            int fv_b = src[ti], fv_g = src[ti + 1], fv_r = src[ti + 2], fv_a = src[ti + 3];
            int lv_b = src[ti + (h - 1) * stride], lv_g = src[ti + (h - 1) * stride + 1], lv_r = src[ti + (h - 1) * stride + 2], lv_a = src[ti + (h - 1) * stride + 3];

            int val_b = (r + 1) * fv_b;
            int val_g = (r + 1) * fv_g;
            int val_r = (r + 1) * fv_r;
            int val_a = (r + 1) * fv_a;

            for (int j = 0; j < r; j++)
            {
                int idx = ti + Math.Min(j, h - 1) * stride;
                val_b += src[idx];
                val_g += src[idx + 1];
                val_r += src[idx + 2];
                val_a += src[idx + 3];
            }

            for (int j = 0; j <= r; j++)
            {
                int r_idx = ti + Math.Min(j + r, h - 1) * stride;
                val_b += src[r_idx] - fv_b;
                val_g += src[r_idx + 1] - fv_g;
                val_r += src[r_idx + 2] - fv_r;
                val_a += src[r_idx + 3] - fv_a;

                dst[ti] = (byte)(val_b * iarr + 0.5f);
                dst[ti + 1] = (byte)(val_g * iarr + 0.5f);
                dst[ti + 2] = (byte)(val_r * iarr + 0.5f);
                dst[ti + 3] = (byte)(val_a * iarr + 0.5f);

                ti += stride;
            }

            for (int j = r + 1; j < h - r; j++)
            {
                int r_idx = ti + r * stride;
                int l_idx = ti - (r + 1) * stride;

                val_b += src[r_idx] - src[l_idx];
                val_g += src[r_idx + 1] - src[l_idx + 1];
                val_r += src[r_idx + 2] - src[l_idx + 2];
                val_a += src[r_idx + 3] - src[l_idx + 3];

                dst[ti] = (byte)(val_b * iarr + 0.5f);
                dst[ti + 1] = (byte)(val_g * iarr + 0.5f);
                dst[ti + 2] = (byte)(val_r * iarr + 0.5f);
                dst[ti + 3] = (byte)(val_a * iarr + 0.5f);

                ti += stride;
            }

            for (int j = h - r; j < h; j++)
            {
                int l_idx = ti - (r + 1) * stride;

                val_b += lv_b - src[l_idx];
                val_g += lv_g - src[l_idx + 1];
                val_r += lv_r - src[l_idx + 2];
                val_a += lv_a - src[l_idx + 3];

                dst[ti] = (byte)(val_b * iarr + 0.5f);
                dst[ti + 1] = (byte)(val_g * iarr + 0.5f);
                dst[ti + 2] = (byte)(val_r * iarr + 0.5f);
                dst[ti + 3] = (byte)(val_a * iarr + 0.5f);

                ti += stride;
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

public class OutgoingTextColorConverter : IValueConverter, IScopedResourceConverter
{
    public ResourceDictionary? ScopedResources { get; set; }

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
            return ScopedResourceHelper.ResolveBrush(ScopedResources, "OutgoingBubbleTextBrush") as SolidColorBrush ?? s_white;
        }
        return ScopedResourceHelper.ResolveBrush(ScopedResources, "IncomingBubbleTextBrush") as SolidColorBrush ?? s_black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class OutgoingSubtextColorConverter : IValueConverter, IScopedResourceConverter
{
    public ResourceDictionary? ScopedResources { get; set; }

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
            return ScopedResourceHelper.ResolveBrush(ScopedResources, "OutgoingBubbleSubtextBrush") as SolidColorBrush ?? s_translucentWhite;
        }
        return ScopedResourceHelper.ResolveBrush(ScopedResources, "IncomingBubbleSubtextBrush") as SolidColorBrush ?? s_gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class QuotedBackgroundConverter : IValueConverter, IScopedResourceConverter
{
    public ResourceDictionary? ScopedResources { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = value is People.Core.Models.Message msg ? msg.IsOutgoing : (value is bool b && b);
        return isOutgoing
            ? ScopedResourceHelper.ResolveBrush(ScopedResources, "QuotedOutgoingBackgroundBrush") as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            : ScopedResourceHelper.ResolveBrush(ScopedResources, "QuotedIncomingBackgroundBrush") as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class QuotedBorderBrushConverter : IValueConverter, IScopedResourceConverter
{
    public ResourceDictionary? ScopedResources { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = value is People.Core.Models.Message msg ? msg.IsOutgoing : (value is bool b && b);
        return isOutgoing
            ? ScopedResourceHelper.ResolveBrush(ScopedResources, "QuotedOutgoingBorderBrush") as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray)
            : ScopedResourceHelper.ResolveBrush(ScopedResources, "QuotedIncomingBorderBrush") as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class QuotedSenderBrushConverter : IValueConverter, IScopedResourceConverter
{
    public ResourceDictionary? ScopedResources { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = value is People.Core.Models.Message msg ? msg.IsOutgoing : (value is bool b && b);
        return isOutgoing
            ? ScopedResourceHelper.ResolveBrush(ScopedResources, "QuotedOutgoingSenderBrush") as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.White)
            : ScopedResourceHelper.ResolveBrush(ScopedResources, "QuotedIncomingSenderBrush") as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Black);
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class QuotedTextBrushConverter : IValueConverter, IScopedResourceConverter
{
    public ResourceDictionary? ScopedResources { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isOutgoing = value is People.Core.Models.Message msg ? msg.IsOutgoing : (value is bool b && b);
        return isOutgoing
            ? ScopedResourceHelper.ResolveBrush(ScopedResources, "QuotedOutgoingTextBrush") as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.White)
            : ScopedResourceHelper.ResolveBrush(ScopedResources, "QuotedIncomingTextBrush") as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
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

public class MessageStatusBrushConverter : IValueConverter, IScopedResourceConverter
{
    public ResourceDictionary? ScopedResources { get; set; }

    private static readonly SolidColorBrush s_readCyan = new(Microsoft.UI.ColorHelper.FromArgb(255, 83, 189, 235)); // #53BDEB
    private static readonly SolidColorBrush s_failedRed = new(Microsoft.UI.ColorHelper.FromArgb(255, 229, 72, 77));
    private static readonly SolidColorBrush s_neutralFallback = new(Microsoft.UI.ColorHelper.FromArgb(255, 138, 145, 153));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is People.Core.Models.MessageStatus s)
        {
            if (s == People.Core.Models.MessageStatus.Read)
            {
                return s_readCyan;
            }
            if (s == People.Core.Models.MessageStatus.Failed)
            {
                return ScopedResourceHelper.ResolveBrush(ScopedResources, "SystemFillColorCriticalBrush") as Microsoft.UI.Xaml.Media.Brush ?? s_failedRed;
            }
        }
        if (string.Equals(parameter?.ToString(), "Neutral", StringComparison.OrdinalIgnoreCase))
        {
            return ScopedResourceHelper.ResolveBrush(ScopedResources, "TextFillColorSecondaryBrush") as Microsoft.UI.Xaml.Media.Brush ?? s_neutralFallback;
        }
        return ScopedResourceHelper.ResolveBrush(ScopedResources, "OutgoingBubbleSubtextBrush") as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Microsoft.UI.Colors.LightGray);
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

public class UnreadToTimestampBrushConverter : IValueConverter, IScopedResourceConverter
{
    public ResourceDictionary? ScopedResources { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count && count > 0)
        {
            if (ScopedResourceHelper.ResolveBrush(ScopedResources, "AppAccentBrush") is Microsoft.UI.Xaml.Media.Brush b)
            {
                return b;
            }
        }

        if (ScopedResourceHelper.ResolveBrush(ScopedResources, "ChatListTimestampBrush") is Microsoft.UI.Xaml.Media.Brush tb)
        {
            return tb;
        }

        if (ScopedResourceHelper.ResolveBrush(ScopedResources, "TextFillColorSecondaryBrush") is Microsoft.UI.Xaml.Media.Brush sb)
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
