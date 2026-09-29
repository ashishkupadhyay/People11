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
        string normalizedPlatformId = platformId;
        if (string.IsNullOrEmpty(normalizedPlatformId))
        {
            normalizedPlatformId = "WhatsApp"; // Default fallback
        }
        else if (string.Equals(platformId, "whatsapp", StringComparison.OrdinalIgnoreCase)) normalizedPlatformId = "WhatsApp";
        else if (string.Equals(platformId, "telegram", StringComparison.OrdinalIgnoreCase)) normalizedPlatformId = "Telegram";

        bool isBrandColor = _settingsService.GetValue<bool>("BrandColor_" + normalizedPlatformId, false);
        if (isBrandColor)
        {
            Windows.UI.Color color = Microsoft.UI.Colors.Transparent;
            if (normalizedPlatformId == "WhatsApp") color = Microsoft.UI.ColorHelper.FromArgb(255, 37, 211, 102);
            else if (normalizedPlatformId == "Telegram") color = Microsoft.UI.ColorHelper.FromArgb(255, 36, 161, 222);
            else if (string.Equals(platformId, "discord", StringComparison.OrdinalIgnoreCase)) color = Microsoft.UI.ColorHelper.FromArgb(255, 88, 101, 242);
            else if (string.Equals(platformId, "signal", StringComparison.OrdinalIgnoreCase)) color = Microsoft.UI.ColorHelper.FromArgb(255, 58, 118, 240);
            
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
        var appResources = Application.Current.Resources;
        
        UpdateThemeDictionary("Light", color);
        UpdateThemeDictionary("Dark", color);

        appResources["SystemAccentColor"] = color;
        appResources["SystemAccentColorLight1"] = color;
        appResources["SystemAccentColorLight2"] = color;
        appResources["SystemAccentColorLight3"] = color;
        appResources["SystemAccentColorDark1"] = color;
        appResources["SystemAccentColorDark2"] = color;
        appResources["SystemAccentColorDark3"] = color;
        
        // Update the fallback brushes used in our custom converter
        appResources["AccentFillColorDefaultBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);

        // Force UI refresh
        if (App.Current is App app && app.MainWindow?.Content is FrameworkElement rootElement)
        {
            var originalTheme = rootElement.RequestedTheme;
            
            // If it's Default, we toggle to Dark or Light and back to Default
            if (originalTheme == ElementTheme.Default)
            {
                rootElement.RequestedTheme = ElementTheme.Dark;
                rootElement.RequestedTheme = ElementTheme.Light;
                rootElement.RequestedTheme = ElementTheme.Default;
            }
            else
            {
                rootElement.RequestedTheme = originalTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                rootElement.RequestedTheme = originalTheme;
            }
        }
    }

    private void UpdateThemeDictionary(string themeName, Windows.UI.Color color)
    {
        var appResources = Application.Current.Resources;
        
        if (!appResources.ThemeDictionaries.TryGetValue(themeName, out var themeObj) || !(themeObj is Microsoft.UI.Xaml.ColorPaletteResources palette))
        {
            palette = new Microsoft.UI.Xaml.ColorPaletteResources();
            if (themeObj is ResourceDictionary existingDict)
            {
                foreach (var item in existingDict)
                {
                    palette[item.Key] = item.Value;
                }
            }
            appResources.ThemeDictionaries[themeName] = palette;
        }
        
        palette.Accent = color;
    }
}
