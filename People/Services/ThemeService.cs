using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using People.Core.Interfaces;

namespace People.Services;

public sealed record AccentPalette(
    Windows.UI.Color Accent,
    Windows.UI.Color Light1,
    Windows.UI.Color Light2,
    Windows.UI.Color Light3,
    Windows.UI.Color Dark1,
    Windows.UI.Color Dark2,
    Windows.UI.Color Dark3
);

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
            var palette = GetBrandPaletteIfEnabled(_activePlatform);

            if (palette != null)
            {
                SetAppAccentPalette(palette);
                return;
            }
        }

        SetSystemAccentColor();
    }

    private AccentPalette? GetBrandPaletteIfEnabled(string platform)
    {
        bool enabled = _settingsService.GetValue<bool>("BrandColor_" + platform, false);

        if (!enabled)
            return null;

        var baseColor = platform switch
        {
            "WhatsApp" => ColorHelper.FromArgb(255, 37, 211, 102),
            "Telegram" => ColorHelper.FromArgb(255, 36, 161, 222),
            "Discord" => ColorHelper.FromArgb(255, 88, 101, 242),
            "Signal" => ColorHelper.FromArgb(255, 58, 118, 240),
            "SMS" => ColorHelper.FromArgb(255, 0, 120, 215),
            _ => (Windows.UI.Color?)null
        };

        if (baseColor.HasValue)
        {
            return GeneratePalette(baseColor.Value);
        }

        return null;
    }

    private void SetSystemAccentColor()
    {
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var palette = new AccentPalette(
                uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent),
                uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight1),
                uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight2),
                uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight3),
                uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark1),
                uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark2),
                uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark3)
            );
            SetAppAccentPalette(palette);
        }
        catch
        {
            SetAppAccentPalette(GeneratePalette(Microsoft.UI.Colors.Blue));
        }
    }

    private void SetAppAccentPalette(AccentPalette palette)
    {
        SetAccentResource("Default", palette);
        SetAccentResource("Light", palette);
        SetAccentResource("Dark", palette);
        RefreshThemeResources();
    }

    private static void SetAccentResource(string theme, AccentPalette palette)
    {
        if (Application.Current.Resources.ThemeDictionaries[theme] is not ResourceDictionary dictionary) return;
        
        dictionary["SystemAccentColor"] = palette.Accent;
        dictionary["SystemAccentColorLight1"] = palette.Light1;
        dictionary["SystemAccentColorLight2"] = palette.Light2;
        dictionary["SystemAccentColorLight3"] = palette.Light3;
        dictionary["SystemAccentColorDark1"] = palette.Dark1;
        dictionary["SystemAccentColorDark2"] = palette.Dark2;
        dictionary["SystemAccentColorDark3"] = palette.Dark3;
    }

    private static AccentPalette GeneratePalette(Windows.UI.Color baseColor)
    {
        return new AccentPalette(
            baseColor,
            BlendWithWhite(baseColor, 0.15),
            BlendWithWhite(baseColor, 0.30),
            BlendWithWhite(baseColor, 0.45),
            BlendWithBlack(baseColor, 0.15),
            BlendWithBlack(baseColor, 0.30),
            BlendWithBlack(baseColor, 0.45)
        );
    }

    private static Windows.UI.Color BlendWithWhite(Windows.UI.Color color, double amount)
    {
        return Windows.UI.Color.FromArgb(
            color.A,
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));
    }

    private static Windows.UI.Color BlendWithBlack(Windows.UI.Color color, double amount)
    {
        return Windows.UI.Color.FromArgb(
            color.A,
            (byte)(color.R * (1.0 - amount)),
            (byte)(color.G * (1.0 - amount)),
            (byte)(color.B * (1.0 - amount)));
    }

    private void RefreshThemeResources()
    {
        if (App.Current.MainWindow?.Content is not FrameworkElement root) return;
        
        var originalTheme = root.RequestedTheme;
        do
        {
            root.RequestedTheme = root.RequestedTheme switch
            {
                ElementTheme.Light => ElementTheme.Dark,
                ElementTheme.Dark => ElementTheme.Default,
                ElementTheme.Default => ElementTheme.Light,
                _ => ElementTheme.Default
            };
        } while (root.RequestedTheme != originalTheme);
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
