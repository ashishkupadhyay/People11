using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using People.Core.Interfaces;

namespace People.Views;

public sealed partial class FlyoutWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    private const uint GW_OWNER = 4;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private readonly IntPtr _hWnd;
    private readonly AppWindow _appWindow;
    private readonly ISettingsService _settingsService;
    private string _selectedPlatform = "WhatsApp";
    private ChatListPage? _currentChatListPage;

    private bool _isTopTaskbar;
    private bool _isLeftTaskbar;
    private bool _isRightTaskbar;
    private bool _dismissedOnboardingInFlyout;

    public static FlyoutWindow? CurrentFlyoutWindow { get; private set; }
    public static ResourceDictionary? CurrentFlyoutResources { get; private set; }

    public bool IsFlyoutVisible { get; private set; }

    public FlyoutWindow()
    {
        CurrentFlyoutWindow = this;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
        _settingsService.SettingChanged += OnSettingChanged;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
        }

        ApplyFlyoutBackdrop();

        ContentFrame.Navigated += ContentFrame_Navigated;
        UpdateNavigationState();

        this.Activated += OnActivated;
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key == "FlyoutBackdrop")
        {
            DispatcherQueue.TryEnqueue(ApplyFlyoutBackdrop);
        }
        else if (key.StartsWith("BrandColor_") || key == "AppTheme" || key == $"{_selectedPlatform}Enabled")
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (key == "AppTheme")
                {
                    ApplyRequestedTheme();
                }
                ApplyFlyoutPlatformAccent(_selectedPlatform);
            });
        }
        else if (key == "HasCompletedOnboarding" || key == "WhatsAppTestingMode")
        {
            DispatcherQueue.TryEnqueue(UpdateNavigationState);
        }
    }

    private void ApplyFlyoutBackdrop()
    {
        var backdropStr = _settingsService.GetValue<string>("FlyoutBackdrop") ?? "AcrylicBase";
        var targetKind = backdropStr == "AcrylicThin"
            ? Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicKind.Thin
            : Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicKind.Base;

        if (this.SystemBackdrop is FlyoutAcrylicBackdrop existingBackdrop)
        {
            existingBackdrop.Kind = targetKind;
            existingBackdrop.RefreshSystemColors();
        }
        else
        {
            var newBackdrop = new FlyoutAcrylicBackdrop(targetKind);
            newBackdrop.SystemColorsRefreshed += OnFlyoutSystemColorsRefreshed;
            this.SystemBackdrop = newBackdrop;
        }

        ApplyRequestedTheme();
    }

    private void OnFlyoutSystemColorsRefreshed()
    {
        ApplyRequestedTheme();
        ApplyFlyoutPlatformAccent(_selectedPlatform);
    }

    public static bool IsHostedInFlyout(XamlRoot? xamlRoot)
    {
        return CurrentFlyoutWindow != null &&
               xamlRoot != null &&
               xamlRoot == CurrentFlyoutWindow.Content?.XamlRoot;
    }

    private static readonly string[] TaskbarSurfaceBrushKeys =
    [
        "AcrylicInAppFillColorDefaultBrush",
        "AcrylicBackgroundFillColorDefaultBrush",
        "SolidBackgroundFillColorBaseBrush",
        "SolidBackgroundFillColorSecondaryBrush",
        "SolidBackgroundFillColorTertiaryBrush",
        "FlyoutPresenterBackground",
        "ContentDialogBackground",
        "ComboBoxDropDownBackground",
        "LayerFillColorDefaultBrush",
        "LayerFillColorAltBrush",
        "ContentDialogTopOverlay",
        "CardStrokeColorDefaultBrush",
        "CardStrokeColorDefaultSolidBrush",
        "FlyoutBorderThemeBrush",
        "ContentDialogSeparatorBorderBrush"
    ];

    public static bool TryCreateTaskbarSurfaceBrush(
        out AcrylicBrush? surfaceBrush,
        out SolidColorBrush? borderBrush,
        double opacity = 0.84)
    {
        surfaceBrush = null;
        borderBrush = null;
        if (!FlyoutAcrylicBackdrop.TryGetTaskbarTintColors(out var tint, out var fallback, out var border, out var isLight))
        {
            return false;
        }

        surfaceBrush = new AcrylicBrush
        {
            TintColor = tint,
            FallbackColor = fallback,
            TintOpacity = opacity,
            TintLuminosityOpacity = isLight ? 0.86 : 0.88
        };
        borderBrush = new SolidColorBrush(border);
        return true;
    }

    public static void ApplySurfaceAccentToResources(ResourceDictionary targetDict)
    {
        if (!FlyoutAcrylicBackdrop.TryGetTaskbarTintColors(out var tint, out var fallback, out var border, out var isLight))
        {
            foreach (var key in TaskbarSurfaceBrushKeys)
            {
                targetDict.Remove(key);
            }
            return;
        }

        AcrylicBrush MakeAcrylic(double opacity) => new()
        {
            TintColor = tint,
            FallbackColor = fallback,
            TintOpacity = opacity,
            TintLuminosityOpacity = isLight ? 0.86 : 0.88
        };

        var acrylicDefault = MakeAcrylic(0.80);
        var acrylicSurface = MakeAcrylic(0.84);
        var acrylicPopup = MakeAcrylic(0.86);

        targetDict["AcrylicInAppFillColorDefaultBrush"] = acrylicDefault;
        targetDict["AcrylicBackgroundFillColorDefaultBrush"] = acrylicDefault;

        targetDict["SolidBackgroundFillColorBaseBrush"] = acrylicSurface;
        targetDict["SolidBackgroundFillColorSecondaryBrush"] = acrylicPopup;
        targetDict["SolidBackgroundFillColorTertiaryBrush"] = acrylicPopup;

        targetDict["FlyoutPresenterBackground"] = acrylicPopup;
        targetDict["ContentDialogBackground"] = acrylicSurface;
        targetDict["ComboBoxDropDownBackground"] = acrylicPopup;

        targetDict["LayerFillColorDefaultBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(isLight ? (byte)0x48 : (byte)0x5C, tint.R, tint.G, tint.B));
        targetDict["LayerFillColorAltBrush"] = new SolidColorBrush(Windows.UI.Color.FromArgb(isLight ? (byte)0x38 : (byte)0x48, fallback.R, fallback.G, fallback.B));
        targetDict["ContentDialogTopOverlay"] = new SolidColorBrush(Windows.UI.Color.FromArgb(isLight ? (byte)0x38 : (byte)0x48, isLight ? (byte)255 : (byte)0, isLight ? (byte)255 : (byte)0, isLight ? (byte)255 : (byte)0));

        var borderBrush = new SolidColorBrush(border);
        targetDict["CardStrokeColorDefaultBrush"] = borderBrush;
        targetDict["CardStrokeColorDefaultSolidBrush"] = borderBrush;
        targetDict["FlyoutBorderThemeBrush"] = borderBrush;
        targetDict["ContentDialogSeparatorBorderBrush"] = borderBrush;
    }

    public static void ApplyMenuFlyoutTheming(MenuFlyout menu, XamlRoot? xamlRoot)
    {
        if (!IsHostedInFlyout(xamlRoot) || !TryCreateTaskbarSurfaceBrush(out var bgBrush, out var borderBrush, 0.86))
        {
            menu.MenuFlyoutPresenterStyle = null;
            return;
        }

        var style = new Style(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(Control.BackgroundProperty, bgBrush!));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, borderBrush!));
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty,
            FlyoutAcrylicBackdrop.IsTaskbarLightTheme() ? ElementTheme.Light : ElementTheme.Dark));
        menu.MenuFlyoutPresenterStyle = style;
    }

    public static void ApplyFlyoutPresenterTheming(Flyout flyout, XamlRoot? xamlRoot, Style? baseStyle = null)
    {
        if (!IsHostedInFlyout(xamlRoot) || !TryCreateTaskbarSurfaceBrush(out var bgBrush, out var borderBrush, 0.86))
        {
            if (baseStyle != null)
            {
                flyout.FlyoutPresenterStyle = baseStyle;
            }
            return;
        }

        var style = new Style(typeof(FlyoutPresenter));
        if (baseStyle != null)
        {
            foreach (SetterBase sb in baseStyle.Setters)
            {
                if (sb is Setter s &&
                    s.Property != Control.BackgroundProperty &&
                    s.Property != Control.BorderBrushProperty &&
                    s.Property != FrameworkElement.RequestedThemeProperty)
                {
                    style.Setters.Add(new Setter(s.Property, s.Value));
                }
            }
        }
        style.Setters.Add(new Setter(Control.BackgroundProperty, bgBrush!));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, borderBrush!));
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty,
            FlyoutAcrylicBackdrop.IsTaskbarLightTheme() ? ElementTheme.Light : ElementTheme.Dark));
        flyout.FlyoutPresenterStyle = style;
    }

    private void PlatformMenuFlyout_Opening(object? sender, object e)
    {
        if (sender is MenuFlyout menu)
        {
            ApplyMenuFlyoutTheming(menu, Content?.XamlRoot);
        }
    }

    public void ApplyRequestedTheme(string? appThemeOverride = null)
    {
        if (RootGrid == null) return;

        if (FlyoutAcrylicBackdrop.IsTaskbarAccentColorEnabled() && !FlyoutAcrylicBackdrop.IsTaskbarLightTheme())
        {
            RootGrid.RequestedTheme = ElementTheme.Dark;
            return;
        }

        var theme = appThemeOverride ?? _settingsService.GetValue<string>("AppTheme") ?? "Default";
        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void ApplyFlyoutPlatformAccent(string platform)
    {
        if (ContentFrame.Content is OnboardingPage)
        {
            return;
        }

        var themeService = App.Current.Services.GetRequiredService<IThemeService>();
        themeService.ApplyAccentToResourceDictionary(RootGrid.Resources, platform);
        ApplySurfaceAccentToResources(RootGrid.Resources);
        CurrentFlyoutResources = RootGrid.Resources;

        if (ContentFrame.Content is ChatListPage chatList)
        {
            chatList.ApplyFlyoutTheming(RootGrid.Resources);
        }
        else if (ContentFrame.Content is ConversationPage convPage)
        {
            convPage.ApplyFlyoutTheming(RootGrid.Resources);
        }
    }

    private void PositionAndResizeWindow(POINT? anchorPos = null)
    {
        POINT cursor;
        if (anchorPos.HasValue && (anchorPos.Value.X != 0 || anchorPos.Value.Y != 0))
        {
            cursor = anchorPos.Value;
        }
        else
        {
            GetCursorPos(out cursor);
        }

        var displayArea = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var outer = displayArea.OuterBounds;

        uint dpi = GetDpiForWindow(_hWnd);
        double scale = dpi > 0 ? (dpi / 96.0) : 1.0;

        int screenMargin = (int)Math.Round(12 * scale);
        int taskbarGap = (int)Math.Round(6 * scale);
        double maxLogicalHeight = Math.Max(400, (workArea.Height / scale) - 32);
        double logicalHeight = Math.Min(720, maxLogicalHeight);
        double logicalWidth = 340;

        int physicalWidth = (int)Math.Round(logicalWidth * scale);
        int physicalHeight = (int)Math.Round(logicalHeight * scale);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));

        _isTopTaskbar = workArea.Y > outer.Y;
        _isLeftTaskbar = workArea.X > outer.X;
        _isRightTaskbar = (workArea.X + workArea.Width) < (outer.X + outer.Width);

        int x;
        int y;

        if (_isTopTaskbar)
        {
            // Taskbar is at the TOP of the monitor
            y = workArea.Y + taskbarGap;
            x = cursor.X - (physicalWidth / 2);
        }
        else if (_isLeftTaskbar)
        {
            // Taskbar is at the LEFT of the monitor
            x = workArea.X + taskbarGap;
            y = cursor.Y - (physicalHeight / 2);
        }
        else if (_isRightTaskbar)
        {
            // Taskbar is at the RIGHT of the monitor
            x = workArea.X + workArea.Width - physicalWidth - taskbarGap;
            y = cursor.Y - (physicalHeight / 2);
        }
        else
        {
            // Default: Taskbar is at the BOTTOM of the monitor
            y = workArea.Y + workArea.Height - physicalHeight - taskbarGap;
            x = cursor.X - (physicalWidth / 2);
        }

        // Clamp within workArea bounds
        int minX = workArea.X + (_isLeftTaskbar ? taskbarGap : screenMargin);
        int maxX = Math.Max(minX, workArea.X + workArea.Width - physicalWidth - (_isRightTaskbar ? taskbarGap : screenMargin));
        x = Math.Clamp(x, minX, maxX);

        int minY = workArea.Y + (_isTopTaskbar ? taskbarGap : screenMargin);
        int maxY = Math.Max(minY, workArea.Y + workArea.Height - physicalHeight - (!_isTopTaskbar && !_isLeftTaskbar && !_isRightTaskbar ? taskbarGap : screenMargin));
        y = Math.Clamp(y, minY, maxY);

        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    public void ShowFlyoutAnimated(POINT? anchorPos = null)
    {
        IsFlyoutVisible = true;
        if (this.SystemBackdrop is FlyoutAcrylicBackdrop flyoutBackdrop)
        {
            flyoutBackdrop.RefreshSystemColors();
        }
        ApplyRequestedTheme();
        UpdateNavigationState();
        PositionAndResizeWindow(anchorPos);
        ApplyFlyoutPlatformAccent(_selectedPlatform);

        double startX = 0;
        double startY = 0;

        if (_isTopTaskbar)
        {
            startY = -40;
        }
        else if (_isLeftTaskbar)
        {
            startX = -40;
        }
        else if (_isRightTaskbar)
        {
            startX = 40;
        }
        else
        {
            startY = 40;
        }

        SlideTransform.X = startX;
        SlideTransform.Y = startY;
        RootGrid.Opacity = 0;

        ShowWindow(_hWnd, SW_SHOW);
        ForceActivateForeground();

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animX = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animX, SlideTransform);
        Storyboard.SetTargetProperty(animX, "X");

        var animY = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animY, SlideTransform);
        Storyboard.SetTargetProperty(animY, "Y");

        var animOpacity = new DoubleAnimation
        {
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animOpacity, RootGrid);
        Storyboard.SetTargetProperty(animOpacity, "Opacity");

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Children.Add(animOpacity);
        sb.Begin();
    }

    public void HideFlyoutAnimated()
    {
        if (!IsFlyoutVisible) return;
        IsFlyoutVisible = false;

        double targetX = 0;
        double targetY = 0;

        if (_isTopTaskbar)
        {
            targetY = -30;
        }
        else if (_isLeftTaskbar)
        {
            targetX = -30;
        }
        else if (_isRightTaskbar)
        {
            targetX = 30;
        }
        else
        {
            targetY = 30;
        }

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var animX = new DoubleAnimation
        {
            To = targetX,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animX, SlideTransform);
        Storyboard.SetTargetProperty(animX, "X");

        var animY = new DoubleAnimation
        {
            To = targetY,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animY, SlideTransform);
        Storyboard.SetTargetProperty(animY, "Y");

        var animOpacity = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animOpacity, RootGrid);
        Storyboard.SetTargetProperty(animOpacity, "Opacity");

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Children.Add(animOpacity);

        sb.Completed += (s, e) =>
        {
            ShowWindow(_hWnd, SW_HIDE);
        };
        sb.Begin();
    }

    public void DestroyWindow()
    {
        _settingsService.SettingChanged -= OnSettingChanged;
        if (this.SystemBackdrop is FlyoutAcrylicBackdrop flyoutBackdrop)
        {
            flyoutBackdrop.SystemColorsRefreshed -= OnFlyoutSystemColorsRefreshed;
        }
        this.Close();
    }

    public void ForceActivateForeground()
    {
        keybd_event(0, 0, 0, UIntPtr.Zero);
        SetForegroundWindow(_hWnd);
        BringWindowToTop(_hWnd);
        SetActiveWindow(_hWnd);
        SetFocus(_hWnd);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (IsAppOrFlyoutFocused())
                {
                    return; // Ignore deactivation caused by internal popups/flyouts
                }
                HideFlyoutAnimated();
            });
        }
    }

    private bool IsAppOrFlyoutFocused()
    {
        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero) return false;
        if (fgHwnd == _hWnd) return true;

        if (App.Current.MainWindow != null)
        {
            var mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow);
            if (fgHwnd == mainHwnd)
            {
                return false; // Clicked on main window -> collapse flyout!
            }

            var owner = GetWindow(fgHwnd, GW_OWNER);
            if (owner == mainHwnd)
            {
                return false;
            }
            if (owner == _hWnd)
            {
                return true;
            }
        }

        return false;
    }

    public void UpdateNavigationState()
    {
        bool hasSignedInPlatform = MainWindow.HasActiveOrSignedInPlatform(App.Current.Services);
        if (!hasSignedInPlatform && !_dismissedOnboardingInFlyout)
        {
            FlyoutHeaderBar.Visibility = Visibility.Collapsed;
            if (ContentFrame.Content is not OnboardingPage)
            {
                Views.OnboardingPage.PreloadProfilePictures();
                ContentFrame.Navigate(typeof(OnboardingPage), "Flyout");
            }
        }
        else
        {
            FlyoutHeaderBar.Visibility = Visibility.Visible;
            if (ContentFrame.Content is OnboardingPage || ContentFrame.Content == null)
            {
                NavigateToPlatform(_selectedPlatform);
            }
            else
            {
                ApplyFlyoutPlatformAccent(_selectedPlatform);
            }
        }
    }

    public void NavigateToPlatformFromOnboarding(string platform = "WhatsApp")
    {
        _dismissedOnboardingInFlyout = true;
        FlyoutHeaderBar.Visibility = Visibility.Visible;
        NavigateToPlatform(platform);
    }

    private void NavigateToPlatform(string platform)
    {
        _selectedPlatform = platform;
        PlatformNameText.Text = platform;

        ApplyFlyoutPlatformAccent(platform);

        if (platform == "WhatsApp")
        {
            ContentFrame.Navigate(typeof(ChatListPage), "Flyout");
        }
        else
        {
            ContentFrame.Navigate(typeof(ComingSoonPage), platform);
        }
    }

    private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (ContentFrame.Content is OnboardingPage)
        {
            FlyoutHeaderBar.Visibility = Visibility.Collapsed;
            FlyoutBackButton.Visibility = Visibility.Collapsed;
            return;
        }

        FlyoutHeaderBar.Visibility = Visibility.Visible;

        if (_currentChatListPage != null)
        {
            _currentChatListPage.ConversationOpened -= OnConversationOpened;
            _currentChatListPage.ConversationClosed -= OnConversationClosed;
            _currentChatListPage = null;
        }

        if (ContentFrame.Content is ChatListPage chatList)
        {
            _currentChatListPage = chatList;
            chatList.ConversationOpened += OnConversationOpened;
            chatList.ConversationClosed += OnConversationClosed;
            FlyoutBackButton.Visibility = chatList.IsDetailVisibleInNarrowMode() ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            FlyoutBackButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnConversationOpened(object? sender, People.Core.Models.Chat chat)
    {
        FlyoutBackButton.Visibility = Visibility.Visible;
    }

    private void OnConversationClosed(object? sender, EventArgs e)
    {
        FlyoutBackButton.Visibility = Visibility.Collapsed;
    }

    private void FlyoutBackButton_Click(object sender, RoutedEventArgs e)
    {
        _currentChatListPage?.CloseDetail();
    }

    private void PlatformSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        var mainWindow = App.Current.MainWindow as MainWindow;
        if (mainWindow != null)
        {
            mainWindow.BringToFront();
            if (_currentChatListPage?.GetActiveConversation() is People.Core.Models.Chat activeChat)
            {
                mainWindow.OpenChat(activeChat);
            }
            else
            {
                mainWindow.OpenPlatform(_selectedPlatform);
            }
        }
        HideFlyoutAnimated();
    }

    private void PlatformMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string platform)
        {
            NavigateToPlatform(platform);
        }
    }

    private void OnEscapeInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_currentChatListPage != null && _currentChatListPage.IsDetailVisibleInNarrowMode())
        {
            _currentChatListPage.CloseDetail();
            return;
        }
        HideFlyoutAnimated();
    }
}
