using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using People.Core.Interfaces;

namespace People.Services;

public class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;
    private const string ThemeKey = "AppTheme";
    
    private string _currentTheme = "Default";
    private string? _activePlatform;

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Initialize()
    {
        _currentTheme = _settingsService.GetValue(ThemeKey, "Default") ?? "Default";
        _activePlatform = null;
    }

    public void SetTheme(string theme)
    {
        _currentTheme = theme;
        _settingsService.SetValue(ThemeKey, theme);
        
        if (App.Current.MainWindow?.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        ApplyCurrentAccent();
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

    public void SetActivePlatform(string? platformId)
    {
        _activePlatform = NormalizePlatform(platformId);
        ApplyCurrentAccent();
    }

    public void ClearActivePlatform()
    {
        _activePlatform = null;
        ApplyCurrentAccent();
    }

    public void ApplyBrandColorIfActive(string platformId)
    {
        SetActivePlatform(platformId);
    }

    public void RestoreSystemAccentColor()
    {
        _activePlatform = null;
        ApplyCurrentAccent();
    }

    private void ApplyCurrentAccent()
    {
        if (!string.IsNullOrEmpty(_activePlatform))
        {
            var color = GetBrandColorIfEnabled(_activePlatform);

            if (color.HasValue)
            {
                SetAppAccentColor(color.Value);
                return;
            }
        }

        SetSystemAccentColor();
    }

    private Windows.UI.Color? GetBrandColorIfEnabled(string platform)
    {
        bool enabled = _settingsService.GetValue<bool>("BrandColor_" + platform, false);

        if (!enabled)
            return null;

        return platform switch
        {
            "WhatsApp" => ColorHelper.FromArgb(255, 37, 211, 102),
            "Telegram" => ColorHelper.FromArgb(255, 36, 161, 222),
            "Discord" => ColorHelper.FromArgb(255, 88, 101, 242),
            "Signal" => ColorHelper.FromArgb(255, 58, 118, 240),
            "SMS" => ColorHelper.FromArgb(255, 0, 120, 215),
            _ => null
        };
    }

    private void SetSystemAccentColor()
    {
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var color = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            SetAppAccentColor(color);
        }
        catch
        {
            SetAppAccentColor(Microsoft.UI.Colors.Blue);
        }
    }

    private void SetAppAccentColor(Windows.UI.Color color)
    {
        var resources = Application.Current.Resources;

        if (resources.ThemeDictionaries.TryGetValue("Light", out var lightObj))
        {
            if (lightObj is Microsoft.UI.Xaml.ColorPaletteResources light)
                light.Accent = color;
            else if (lightObj is ResourceDictionary lightDict)
                EnsureColorPaletteResources("Light", lightDict, color);
        }
        else
        {
            EnsureColorPaletteResources("Light", new ResourceDictionary(), color);
        }

        if (resources.ThemeDictionaries.TryGetValue("Dark", out var darkObj))
        {
            if (darkObj is Microsoft.UI.Xaml.ColorPaletteResources dark)
                dark.Accent = color;
            else if (darkObj is ResourceDictionary darkDict)
                EnsureColorPaletteResources("Dark", darkDict, color);
        }
        else
        {
            EnsureColorPaletteResources("Dark", new ResourceDictionary(), color);
        }
    }

    private void EnsureColorPaletteResources(string theme, ResourceDictionary existingDict, Windows.UI.Color color)
    {
        var palette = new Microsoft.UI.Xaml.ColorPaletteResources();
        foreach (var item in existingDict)
        {
            palette[item.Key] = item.Value;
        }
        palette.Accent = color;
        Application.Current.Resources.ThemeDictionaries[theme] = palette;
    }

    private static string? NormalizePlatform(string? platformId)
    {
        if (string.IsNullOrWhiteSpace(platformId))
            return null;

        return platformId.ToLowerInvariant() switch
        {
            "whatsapp" => "WhatsApp",
            "telegram" => "Telegram",
            "discord" => "Discord",
            "signal" => "Signal",
            "sms" => "SMS",
            _ => platformId
        };
    }
}
