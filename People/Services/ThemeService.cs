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
        _settingsService.SettingChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (_activePlatform == null) return;

        if (key == $"BrandColor_{_activePlatform}" || (_activePlatform == "WhatsApp" && key == "WhatsAppEnabled"))
        {
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue != null)
            {
                dispatcherQueue.TryEnqueue(() => ApplyCurrentAccent());
            }
            else
            {
                ApplyCurrentAccent();
            }
        }
    }

    private AccentPalette? _systemAccentPalette;

    public void Initialize()
    {
        _currentTheme = _settingsService.GetValue(ThemeKey, "Default") ?? "Default";
        _activePlatform = null;

        try
        {
            _systemAccentPalette = ReadSystemAccentPalette();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeService] Initial system accent read failed: {ex.Message}");
        }
    }

    private static AccentPalette ReadSystemAccentPalette()
    {
        var uiSettings = new Windows.UI.ViewManagement.UISettings();

        return new AccentPalette(
            uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent),
            uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight1),
            uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight2),
            uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight3),
            uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark1),
            uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark2),
            uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark3)
        );
    }

        private string? _currentBackdrop;

    private static void ApplyRootBackground(Window window, string backdrop)
    {
        if (window?.Content is not Microsoft.UI.Xaml.Controls.Panel root)
            return;

        if (backdrop == "None")
        {
            root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                root.ActualTheme == ElementTheme.Dark
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 249, 249, 249));
        }
        else
        {
            root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
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

        if (App.Current.MainWindow != null && _currentBackdrop != null)
        {
            ApplyRootBackground(App.Current.MainWindow, _currentBackdrop);
        }

        ApplyCurrentAccent();
    }

    public void SetBackdrop(string backdrop)
    {
        if (App.Current is App app && app.MainWindow != null)
        {
            if (_currentBackdrop == backdrop) return;
            _currentBackdrop = backdrop;

            app.MainWindow.SystemBackdrop = backdrop switch
            {
                "Mica" => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
                "Mica Alt" => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
                "Desktop Acrylic" => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
                _ => null
            };

            ApplyRootBackground(app.MainWindow, backdrop);
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

            _currentBackdrop = backdrop;

            ApplyRootBackground(window, backdrop);
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
        System.Diagnostics.Debug.WriteLine($"[ThemeService] ApplyCurrentAccent invoked. ActivePlatform: {_activePlatform}, CurrentTheme: {_currentTheme}");

        // 1. Keep system accent independent
        System.Diagnostics.Debug.WriteLine("[ThemeService] Applying Windows system accent fallback to system resources");
        SetSystemAccentColor();

        // 2. Resolve active app accent
        AccentPalette? appPalette = null;
        if (!string.IsNullOrEmpty(_activePlatform))
        {
            appPalette = GetBrandPaletteIfEnabled(_activePlatform);
            bool useBrandColor = _settingsService.GetValue<bool>("BrandColor_" + _activePlatform, false);

            System.Diagnostics.Debug.WriteLine($"[ThemeService] Brand color enabled setting for {_activePlatform}: {useBrandColor}");
            System.Diagnostics.Debug.WriteLine($"[ThemeService] Brand palette available: {appPalette != null}");
        }

        // 3. Update app-specific brushes
        appPalette ??= _systemAccentPalette;
        if (appPalette != null)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeService] Setting AppAccentResources (Accent: {appPalette.Accent})");
            SetAppAccentResources(appPalette);
        }
    }

    private static void SetAppAccentResources(AccentPalette palette)
    {
        var appResources = Microsoft.UI.Xaml.Application.Current.Resources;
        
        var accentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(palette.Accent);
        var hoverBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(palette.Light1);
        var pressedBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(palette.Dark1);

        // Output the Color itself so it can be used inside SolidColorBrush definitions in XAML if needed
        appResources["AppAccentColor"] = palette.Accent;
        
        // Dynamically replace the color of the existing brushes to force UI re-evaluation
        if (appResources["AppAccentBrush"] is Microsoft.UI.Xaml.Media.SolidColorBrush ab) ab.Color = palette.Accent;
        if (appResources["AppAccentHoverBrush"] is Microsoft.UI.Xaml.Media.SolidColorBrush hb) hb.Color = palette.Light1;
        if (appResources["AppAccentPressedBrush"] is Microsoft.UI.Xaml.Media.SolidColorBrush pb) pb.Color = palette.Dark1;

        if (appResources["AppAccentElevationBorderFocusedBrush"] is Microsoft.UI.Xaml.Media.LinearGradientBrush lgb && lgb.GradientStops.Count > 0)
        {
            lgb.GradientStops[0].Color = palette.Accent;
        }

        // Central Mapping: Update the Color property of existing brushes IN PLACE.
        // This is strictly required because controls like NavigationView and AutoSuggestBox
        // are already loaded and will not re-evaluate their ThemeResource bindings if we
        // simply replace the dictionary object. By changing the Color property of the existing
        // SolidColorBrush, the UI instantly receives a dependency property change notification.
        
        string[] brushKeysToUpdate = {
            "AccentFillColorDefaultBrush",
            "AccentFillColorSecondaryBrush",
            "AccentFillColorTertiaryBrush",
            "AccentTextFillColorPrimaryBrush",
            "AccentTextFillColorSecondaryBrush",
            "AccentTextFillColorTertiaryBrush",
            "SystemControlHighlightAccentBrush",
            "NavigationViewSelectionIndicatorForeground",
            "ListViewItemSelectionIndicatorBrush",
            "ListViewItemSelectionIndicatorPointerOverBrush",
            "ListViewItemSelectionIndicatorPressedBrush"
        };

        foreach (var key in brushKeysToUpdate)
        {
            if (appResources.TryGetValue(key, out var resource) && resource is Microsoft.UI.Xaml.Media.SolidColorBrush solidBrush)
            {
                // Select hover/pressed variants based on the key name
                if (key.Contains("Secondary") || key.Contains("PointerOver"))
                {
                    solidBrush.Color = palette.Light1;
                }
                else if (key.Contains("Tertiary") || key.Contains("Pressed"))
                {
                    solidBrush.Color = palette.Dark1;
                }
                else
                {
                    solidBrush.Color = palette.Accent;
                }
            }
        }
    }

    private bool IsBrandAccentEligible(string platform)
    {
        return platform switch
        {
            "WhatsApp" => _settingsService.GetValue<bool>("WhatsAppEnabled", true),
            "Telegram" => false,
            "Discord" => false,
            "Signal" => false,
            "SMS" => false,
            _ => false
        };
    }

    private AccentPalette? GetBrandPaletteIfEnabled(string platform)
    {
        if (!IsBrandAccentEligible(platform))
            return null;

        bool useBrandColor = _settingsService.GetValue<bool>("BrandColor_" + platform, false);

        if (!useBrandColor)
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

        return baseColor.HasValue
            ? GeneratePalette(baseColor.Value)
            : null;
    }

    private void SetSystemAccentColor()
    {
        try
        {
            _systemAccentPalette = ReadSystemAccentPalette();
            SetAppAccentPalette(_systemAccentPalette);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeService] System accent read failed: {ex.Message}");

            if (_systemAccentPalette is not null)
            {
                SetAppAccentPalette(_systemAccentPalette);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ThemeService] No cached system accent available. Keeping the framework's default accent.");
            }
        }
    }

    private void SetAppAccentPalette(AccentPalette palette)
    {
        SetAccentResource("Default", palette);
        SetAccentResource("Light", palette);
        SetAccentResource("Dark", palette);
    }

    private static void SetAccentResource(string theme, AccentPalette palette)
    {
        if (!Microsoft.UI.Xaml.Application.Current.Resources.ThemeDictionaries.TryGetValue(theme, out var resource) ||
            resource is not Microsoft.UI.Xaml.ResourceDictionary dict)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeService] WARNING: Theme dictionary {theme} not found!");
            return;
        }

        var cpr = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<Microsoft.UI.Xaml.ColorPaletteResources>(dict.MergedDictionaries));

        if (cpr == null)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeService] WARNING: ColorPaletteResources not found in {theme}!");
            return;
        }

        cpr.Accent = palette.Accent;

        System.Diagnostics.Debug.WriteLine($"[ThemeService] Set {theme} ColorPaletteResources.Accent to {palette.Accent}");
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









