using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace People.Converters;

public class OutgoingAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isOutgoing && isOutgoing)
            return HorizontalAlignment.Right;
        
        return HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class OutgoingBackgroundConverter : IValueConverter
{
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
            
            bool isBrandColor = settings?.GetValue<bool>("BrandColor_" + normalizedPlatformId, false) ?? false;

            if (isBrandColor)
            {
                if (normalizedPlatformId == "WhatsApp") return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 37, 211, 102));
                if (normalizedPlatformId == "Telegram") return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 36, 161, 222));
                if (string.Equals(platformId, "discord", StringComparison.OrdinalIgnoreCase)) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 88, 101, 242));
                if (string.Equals(platformId, "matrix", StringComparison.OrdinalIgnoreCase)) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0)); // Or Matrix green
                if (string.Equals(platformId, "signal", StringComparison.OrdinalIgnoreCase)) return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 58, 118, 240));
            }

            return App.Current.Resources["AccentFillColorDefaultBrush"] as SolidColorBrush ?? new SolidColorBrush(Microsoft.UI.Colors.Blue);
        }
        else
        {
            // Incoming message background - use ControlFillColorDefaultBrush so it's visible in light mode
            return App.Current.Resources["ControlFillColorDefaultBrush"] as SolidColorBrush ?? new SolidColorBrush(Microsoft.UI.Colors.LightGray);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b && b)
            return Visibility.Visible;
        
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
