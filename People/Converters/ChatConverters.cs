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
            bool isBrandColor = settings?.GetValue<bool>("BrandColor_" + platformId, false) ?? false;

            if (isBrandColor)
            {
                if (platformId == "WhatsApp") return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 37, 211, 102));
                if (platformId == "Telegram") return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 36, 161, 222));
                if (platformId == "Discord") return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 88, 101, 242));
                if (platformId == "Matrix") return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 0, 0)); // Or Matrix green
                if (platformId == "Signal") return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 58, 118, 240));
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
