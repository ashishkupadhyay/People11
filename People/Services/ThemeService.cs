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
    private readonly IDispatcherService _dispatcherService;
    private const string ThemeKey = "AppTheme";
    
    private string _currentTheme = "Default";
    private string? _activePlatform;

    public ThemeService(ISettingsService settingsService, IDispatcherService dispatcherService)
    {
        _settingsService = settingsService;
        _dispatcherService = dispatcherService;
        _settingsService.SettingChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (_activePlatform == null) return;

        if (key == $"BrandColor_{_activePlatform}" || (_activePlatform == "WhatsApp" && key == "WhatsAppEnabled"))
        {
            _dispatcherService.Enqueue(() => ApplyCurrentAccent());
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
            // Only set transparent if the frame already has navigated content;
            // otherwise keep themed background so initial window scale-in is smooth and never black.
            if (window is MainWindow mw && mw.HasNavigatedContent)
            {
                root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
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
            
            if (backdrop == "Mica" && window.SystemBackdrop is Microsoft.UI.Xaml.Media.MicaBackdrop { Kind: Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base })
            {
                // Already assigned via XAML, avoid resetting the composition controller
            }
            else
            {
                window.SystemBackdrop = backdrop switch
                {
                    "Mica" => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base },
                    "Mica Alt" => new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt },
                    "Desktop Acrylic" => new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
                    _ => null
                };
            }

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
            bool useBrandColor = _settingsService.GetValue<bool>("BrandColor_" + _activePlatform, _activePlatform == "WhatsApp");

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

    private void SetAppAccentResources(AccentPalette palette)
    {
        ApplyAccentToResourceDictionary(Microsoft.UI.Xaml.Application.Current.Resources, _activePlatform);
    }

    public void ApplyAccentToResourceDictionary(object resourceDictionary, string? platformId)
    {
        if (resourceDictionary is not Microsoft.UI.Xaml.ResourceDictionary targetDict)
            return;
        var normPlatform = NormalizePlatform(platformId);
        AccentPalette? palette = null;
        if (!string.IsNullOrEmpty(normPlatform))
        {
            palette = GetBrandPaletteIfEnabled(normPlatform);
        }
        palette ??= _systemAccentPalette ?? ReadSystemAccentPalette();

        bool isDark = _currentTheme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => (App.Current.MainWindow?.Content is FrameworkElement root && root.ActualTheme != ElementTheme.Default)
                ? root.ActualTheme == ElementTheme.Dark
                : App.Current.RequestedTheme == ApplicationTheme.Dark
        };

        bool isWhatsAppBrand = normPlatform == "WhatsApp" && _settingsService.GetValue<bool>("BrandColor_WhatsApp", true);

        Windows.UI.Color effectiveAccent;
        Windows.UI.Color effectiveHover;
        Windows.UI.Color effectivePressed;
        Windows.UI.Color effectiveForeground;
        Windows.UI.Color effectiveSubtext;

        Windows.UI.Color btnBg;
        Windows.UI.Color btnHover;
        Windows.UI.Color btnPressed;
        Windows.UI.Color btnFg;

        Windows.UI.Color outgoingBubbleBg;
        Windows.UI.Color outgoingBubbleText;
        Windows.UI.Color outgoingBubbleSubtext;

        Windows.UI.Color incomingBg;
        Windows.UI.Color incomingText;
        Windows.UI.Color incomingSubtext;

        Windows.UI.Color quotedOutBg;
        Windows.UI.Color quotedOutBorder;
        Windows.UI.Color quotedOutSender;
        Windows.UI.Color quotedOutText;

        Windows.UI.Color quotedInBg;
        Windows.UI.Color quotedInBorder;
        Windows.UI.Color quotedInSender;
        Windows.UI.Color quotedInText;

        if (isWhatsAppBrand)
        {
            // Official WhatsApp Web true brand colors
            var waTeal = ColorHelper.FromArgb(255, 0, 168, 132); // #00A884
            var waGreen = ColorHelper.FromArgb(255, 37, 211, 102); // #25D366

            effectiveAccent = waTeal;
            effectiveHover = isDark ? waGreen : ColorHelper.FromArgb(255, 6, 207, 156);
            effectivePressed = isDark ? ColorHelper.FromArgb(255, 0, 128, 100) : ColorHelper.FromArgb(255, 0, 140, 110);
            effectiveForeground = Microsoft.UI.Colors.White;
            effectiveSubtext = isDark ? ColorHelper.FromArgb(255, 134, 150, 160) : ColorHelper.FromArgb(255, 102, 119, 129);

            if (isDark)
            {
                // WhatsApp Web Dark Mode
                outgoingBubbleBg = ColorHelper.FromArgb(255, 0, 92, 75); // #005C4B
                outgoingBubbleText = ColorHelper.FromArgb(255, 233, 237, 239); // #E9EDEF
                outgoingBubbleSubtext = ColorHelper.FromArgb(255, 134, 150, 160); // #8696A0

                // Dark mode incoming message bubble: true grey #242626
                incomingBg = ColorHelper.FromArgb(255, 36, 38, 38); // #242626
                incomingText = ColorHelper.FromArgb(255, 233, 237, 239); // #E9EDEF
                incomingSubtext = ColorHelper.FromArgb(255, 134, 150, 160); // #8696A0

                // WhatsApp brand mode buttons (New Chat '+', Send button) use WhatsApp accent color
                btnBg = waTeal;
                btnHover = waGreen;
                btnPressed = ColorHelper.FromArgb(255, 0, 128, 100);
                btnFg = Microsoft.UI.Colors.White;

                quotedOutBg = ColorHelper.FromArgb(45, 0, 0, 0); // #025144 tint
                quotedOutBorder = waGreen;
                quotedOutSender = waGreen;
                quotedOutText = ColorHelper.FromArgb(255, 209, 215, 219);

                quotedInBg = ColorHelper.FromArgb(20, 255, 255, 255);
                quotedInBorder = waTeal;
                quotedInSender = waTeal;
                quotedInText = ColorHelper.FromArgb(255, 134, 150, 160);
            }
            else
            {
                // WhatsApp Web Light Mode
                outgoingBubbleBg = ColorHelper.FromArgb(255, 217, 253, 211); // #D9FDD3
                outgoingBubbleText = ColorHelper.FromArgb(255, 17, 27, 33); // #111B21
                outgoingBubbleSubtext = ColorHelper.FromArgb(255, 102, 119, 129); // #667781

                // Light mode incoming message bubble: pure white #FFFFFF
                incomingBg = ColorHelper.FromArgb(255, 255, 255, 255); // #FFFFFF
                incomingText = ColorHelper.FromArgb(255, 17, 27, 33); // #111B21
                incomingSubtext = ColorHelper.FromArgb(255, 102, 119, 129); // #667781

                btnBg = waTeal;
                btnHover = ColorHelper.FromArgb(255, 6, 207, 156);
                btnPressed = ColorHelper.FromArgb(255, 0, 140, 110);
                btnFg = Microsoft.UI.Colors.White;

                quotedOutBg = ColorHelper.FromArgb(20, 0, 0, 0); // #CEF2C8 tint
                quotedOutBorder = waTeal;
                quotedOutSender = waTeal;
                quotedOutText = ColorHelper.FromArgb(255, 84, 101, 111);

                quotedInBg = ColorHelper.FromArgb(240, 245, 245, 245);
                quotedInBorder = waTeal;
                quotedInSender = waTeal;
                quotedInText = ColorHelper.FromArgb(255, 84, 101, 111);
            }
        }
        else
        {
            // Windows Phone Link & System Accent mode
            effectiveAccent = isDark ? palette.Light2 : (GetRelativeLuminance(palette.Dark1) > 0.35 ? palette.Dark2 : palette.Dark1);
            effectiveHover = isDark ? palette.Light1 : palette.Accent;
            effectivePressed = isDark ? palette.Light3 : palette.Dark2;
            effectiveForeground = isDark ? ColorHelper.FromArgb(255, 28, 28, 28) : Microsoft.UI.Colors.White;
            effectiveSubtext = isDark ? ColorHelper.FromArgb(170, 0, 0, 0) : ColorHelper.FromArgb(210, 255, 255, 255);

            btnBg = effectiveAccent;
            btnHover = effectiveHover;
            btnPressed = effectivePressed;
            btnFg = effectiveForeground;

            outgoingBubbleBg = effectiveAccent;
            outgoingBubbleText = effectiveForeground;
            outgoingBubbleSubtext = effectiveSubtext;

            incomingBg = isDark ? ColorHelper.FromArgb(255, 45, 45, 45) : ColorHelper.FromArgb(255, 242, 242, 242); // #F2F2F2
            incomingText = isDark ? Microsoft.UI.Colors.White : ColorHelper.FromArgb(255, 28, 28, 28);
            incomingSubtext = isDark ? ColorHelper.FromArgb(255, 160, 160, 160) : ColorHelper.FromArgb(255, 112, 112, 112);

            quotedOutBg = isDark ? ColorHelper.FromArgb(25, 0, 0, 0) : ColorHelper.FromArgb(35, 255, 255, 255);
            quotedOutBorder = isDark ? palette.Dark2 : palette.Light3;
            quotedOutSender = isDark ? palette.Dark3 : palette.Light3;
            quotedOutText = isDark ? ColorHelper.FromArgb(255, 56, 56, 56) : ColorHelper.FromArgb(255, 240, 240, 240);

            quotedInBg = isDark ? ColorHelper.FromArgb(20, 255, 255, 255) : ColorHelper.FromArgb(14, 0, 0, 0);
            quotedInBorder = isDark ? palette.Light2 : palette.Dark1;
            quotedInSender = isDark ? palette.Light2 : palette.Dark1;
            quotedInText = isDark ? ColorHelper.FromArgb(255, 204, 204, 204) : ColorHelper.FromArgb(255, 102, 102, 102);
        }

        targetDict["AppAccentColor"] = effectiveAccent;
        
        bool isAppResources = ReferenceEquals(targetDict, Microsoft.UI.Xaml.Application.Current.Resources);

        // Mutate existing brush instances in-place for app-level resources, or assign freshly scoped brushes for local dictionaries
        void SetOrMutateBrush(string key, Windows.UI.Color color)
        {
            if (isAppResources)
            {
                if (targetDict.TryGetValue(key, out var res) && res is Microsoft.UI.Xaml.Media.SolidColorBrush sb)
                {
                    sb.Color = color;
                }
                else
                {
                    targetDict[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
                }
            }
            else
            {
                // Scoped dictionary (e.g. FlyoutWindow RootGrid.Resources):
                // NEVER mutate fallback brushes in Application.Current.Resources!
                // Always create and assign a local brush instance directly to targetDict.
                targetDict[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
            }
        }

        SetOrMutateBrush("AppAccentBrush", effectiveAccent);
        SetOrMutateBrush("AppAccentHoverBrush", effectiveHover);
        SetOrMutateBrush("AppAccentPressedBrush", effectivePressed);
        SetOrMutateBrush("AppAccentForegroundBrush", effectiveForeground);

        SetOrMutateBrush("AppAccentButtonBrush", btnBg);
        SetOrMutateBrush("AppAccentButtonHoverBrush", btnHover);
        SetOrMutateBrush("AppAccentButtonPressedBrush", btnPressed);
        SetOrMutateBrush("AppAccentButtonForegroundBrush", btnFg);

        var chatListTsColor = isDark ? ColorHelper.FromArgb(255, 134, 150, 160) : ColorHelper.FromArgb(255, 102, 119, 129);
        SetOrMutateBrush("ChatListTimestampBrush", chatListTsColor);

        SetOrMutateBrush("IncomingBubbleBrush", incomingBg);
        SetOrMutateBrush("IncomingBubbleTextBrush", incomingText);
        SetOrMutateBrush("IncomingBubbleSubtextBrush", incomingSubtext);

        SetOrMutateBrush("OutgoingBubbleBrush", outgoingBubbleBg);
        SetOrMutateBrush("OutgoingBubbleTextBrush", outgoingBubbleText);
        SetOrMutateBrush("OutgoingBubbleSubtextBrush", outgoingBubbleSubtext);

        SetOrMutateBrush("QuotedIncomingBackgroundBrush", quotedInBg);
        SetOrMutateBrush("QuotedIncomingBorderBrush", quotedInBorder);
        SetOrMutateBrush("QuotedIncomingSenderBrush", quotedInSender);
        SetOrMutateBrush("QuotedIncomingTextBrush", quotedInText);

        SetOrMutateBrush("QuotedOutgoingBackgroundBrush", quotedOutBg);
        SetOrMutateBrush("QuotedOutgoingBorderBrush", quotedOutBorder);
        SetOrMutateBrush("QuotedOutgoingSenderBrush", quotedOutSender);
        SetOrMutateBrush("QuotedOutgoingTextBrush", quotedOutText);

        SetOrMutateBrush("TextControlBorderBrushFocused", effectiveAccent);
        SetOrMutateBrush("TextControlFocusBorderBrush", effectiveAccent);
        SetOrMutateBrush("TextControlSelectionHighlightColor", effectiveAccent);

        if (isAppResources && targetDict.TryGetValue("AppAccentElevationBorderFocusedBrush", out var borderRes) && borderRes is Microsoft.UI.Xaml.Media.LinearGradientBrush lgb && lgb.GradientStops.Count > 0)
        {
            lgb.GradientStops[0].Color = effectiveAccent;
        }
        else
        {
            var newLgb = new Microsoft.UI.Xaml.Media.LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1)
            };
            newLgb.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = effectiveAccent, Offset = 0.0 });
            newLgb.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = isDark ? ColorHelper.FromArgb(255, 30, 30, 30) : ColorHelper.FromArgb(255, 230, 230, 230), Offset = 1.0 });
            targetDict["AppAccentElevationBorderFocusedBrush"] = newLgb;
        }

        string[] brushKeysToUpdate = {
            "AccentFillColorDefaultBrush",
            "AccentFillColorSecondaryBrush",
            "AccentFillColorTertiaryBrush",
            "AccentTextFillColorPrimaryBrush",
            "AccentTextFillColorSecondaryBrush",
            "AccentTextFillColorTertiaryBrush",
            "SystemControlHighlightAccentBrush",
            "ListViewItemSelectionIndicatorBrush",
            "ListViewItemSelectionIndicatorPointerOverBrush",
            "ListViewItemSelectionIndicatorPressedBrush"
        };

        if (isAppResources)
        {
            // Only update navigation view selection indicator for global application scope
            SetOrMutateBrush("NavigationViewSelectionIndicatorForeground", effectiveAccent);
        }

        foreach (var key in brushKeysToUpdate)
        {
            if (key.Contains("Secondary") || key.Contains("PointerOver"))
            {
                SetOrMutateBrush(key, effectiveHover);
            }
            else if (key.Contains("Tertiary") || key.Contains("Pressed"))
            {
                SetOrMutateBrush(key, effectivePressed);
            }
            else
            {
                SetOrMutateBrush(key, effectiveAccent);
            }
        }
    }

    private static double GetRelativeLuminance(Windows.UI.Color c)
    {
        double r = c.R / 255.0;
        double g = c.G / 255.0;
        double b = c.B / 255.0;

        r = (r <= 0.03928) ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = (g <= 0.03928) ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = (b <= 0.03928) ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private bool IsBrandAccentEligible(string platform)
    {
        return platform switch
        {
            "WhatsApp" => _settingsService.GetValue<bool>("WhatsAppEnabled", true),
            "Telegram" => _settingsService.GetValue<bool>("TelegramEnabled", true),
            "Discord" => _settingsService.GetValue<bool>("DiscordEnabled", true),
            "Signal" => _settingsService.GetValue<bool>("SignalEnabled", true),
            "SMS" => _settingsService.GetValue<bool>("SMSEnabled", true),
            _ => false
        };
    }

    private AccentPalette? GetBrandPaletteIfEnabled(string platform)
    {
        if (!IsBrandAccentEligible(platform))
            return null;

        bool useBrandColor = _settingsService.GetValue<bool>("BrandColor_" + platform, platform == "WhatsApp");

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









