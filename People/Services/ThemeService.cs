using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using People.Core.Interfaces;

namespace People.Services;

public class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;
    private const string ThemeKey = "AppTheme";
    private string _currentTheme = "Default";

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Initialize()
    {
        _currentTheme = _settingsService.GetValue(ThemeKey, "Default") ?? "Default";
    }

    public void SetTheme(string theme)
    {
        _currentTheme = theme;
        _settingsService.SetValue(ThemeKey, theme);
        
        if (App.Current is App app && app.MainWindow != null)
        {
            if (app.MainWindow.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = _currentTheme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
        }
    }

    public void SetBackdrop(string backdrop)
    {
        if (App.Current is App app && app.MainWindow != null)
        {
            app.MainWindow.SystemBackdrop = backdrop switch
            {
                "Mica" => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
                "Mica Alt" => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
                "Desktop Acrylic" => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
                _ => null
            };
        }
    }

    public void ApplyToWindow(object windowObj)
    {
        if (windowObj is Window window)
        {
            if (window.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = _currentTheme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
            
            var backdrop = _settingsService.GetValue<string>("AppBackdrop") ?? "Mica";
            window.SystemBackdrop = backdrop switch
            {
                "Mica" => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
                "Mica Alt" => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
                "Desktop Acrylic" => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
                _ => null
            };
        }
    }

    public void ApplyBrandColorIfActive(string platformId)
    {
        bool isBrandColor = _settingsService.GetValue<bool>("BrandColor_" + platformId, false);
        if (isBrandColor)
        {
            Windows.UI.Color color = Microsoft.UI.Colors.Transparent;
            if (platformId == "WhatsApp") color = Microsoft.UI.ColorHelper.FromArgb(255, 37, 211, 102);
            else if (platformId == "Telegram") color = Microsoft.UI.ColorHelper.FromArgb(255, 36, 161, 222);
            else if (platformId == "Discord") color = Microsoft.UI.ColorHelper.FromArgb(255, 88, 101, 242);
            else if (platformId == "Signal") color = Microsoft.UI.ColorHelper.FromArgb(255, 58, 118, 240);
            
            if (color != Microsoft.UI.Colors.Transparent)
            {
                SetAppAccentColor(color);
                return;
            }
        }
        
        RestoreSystemAccentColor();
    }

    public void RestoreSystemAccentColor()
    {
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var systemAccent = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            SetAppAccentColor(systemAccent);
        }
        catch
        {
            // Fallback in case UISettings is unavailable in certain contexts
            SetAppAccentColor(Microsoft.UI.Colors.Blue);
        }
    }

    private void SetAppAccentColor(Windows.UI.Color color)
    {
        Application.Current.Resources["SystemAccentColor"] = color;
        Application.Current.Resources["SystemAccentColorLight1"] = color;
        Application.Current.Resources["SystemAccentColorLight2"] = color;
        Application.Current.Resources["SystemAccentColorLight3"] = color;
        Application.Current.Resources["SystemAccentColorDark1"] = color;
        Application.Current.Resources["SystemAccentColorDark2"] = color;
        Application.Current.Resources["SystemAccentColorDark3"] = color;
        
        // Update the fallback brushes used in our custom converter
        Application.Current.Resources["AccentFillColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);

        // Force UI refresh
        if (App.Current is App app && app.MainWindow?.Content is FrameworkElement rootElement)
        {
            var currentTheme = rootElement.RequestedTheme;
            rootElement.RequestedTheme = currentTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            rootElement.RequestedTheme = currentTheme;
        }
    }
}
