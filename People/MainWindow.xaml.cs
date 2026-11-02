using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using People.ViewModels;
using System;
using System.Threading.Tasks;

namespace People;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }

    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _telegramDarkSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _telegramLightSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _signalDarkSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _signalLightSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _discordDarkSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _discordLightSource;

    private void UpdateSidebarIcons(ElementTheme theme)
    {
        bool isDark = IsDarkTheme(theme);
        _telegramDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Telegram-Logo-White.svg")) { RasterizePixelWidth = 36, RasterizePixelHeight = 36 };
        _telegramLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Telegram-Logo-Black.svg")) { RasterizePixelWidth = 36, RasterizePixelHeight = 36 };
        if (NavTelegramIcon != null) NavTelegramIcon.Source = isDark ? _telegramDarkSource : _telegramLightSource;

        _signalDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Signal-Logo-White.svg")) { RasterizePixelWidth = 36, RasterizePixelHeight = 36 };
        _signalLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Signal-Logo-Black.svg")) { RasterizePixelWidth = 36, RasterizePixelHeight = 36 };
        if (NavSignalIcon != null) NavSignalIcon.Source = isDark ? _signalDarkSource : _signalLightSource;

        _discordDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Discord-Logo-White.svg")) { RasterizePixelWidth = 36, RasterizePixelHeight = 36 };
        _discordLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Discord-Logo-Black.svg")) { RasterizePixelWidth = 36, RasterizePixelHeight = 36 };
        if (NavDiscordIcon != null) NavDiscordIcon.Source = isDark ? _discordDarkSource : _discordLightSource;
    }

    public Microsoft.UI.Xaml.Visibility ConvertBoolToVisibility(bool isVisible)
    {
        return isVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int crColor);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetClassLong")]
    private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetClassLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return new IntPtr(SetClassLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    private const int GCLP_HBRBACKGROUND = -10;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    public MainWindow()
    {
        ViewModel = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        InitializeComponent();
        
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        
        var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();
        themeService.Initialize();
        themeService.ApplyToWindow(this);

        var navServiceCore = App.Current.Services.GetRequiredService<People.Core.Interfaces.INavigationService>();
        if (navServiceCore is People.Services.NavigationService navService)
        {
            navService.Initialize(ContentFrame);
            navService.Configure("Chats", typeof(Views.ChatListPage));
            navService.Configure("Conversation", typeof(Views.ConversationPage));
            navService.Configure("Settings", typeof(Views.SettingsPage));
            navService.Configure("ComingSoon", typeof(Views.ComingSoonPage));
        }

        ContentFrame.Navigated += ContentFrame_Navigated;
        
        ContentFrame.Loaded += (s, e) => {
            if (NavView.MenuItems.Count == 0)
                return;

            var initialItem = NavView.MenuItems[0] as NavigationViewItem;
            NavView.SelectedItem = initialItem;

            var platformId = initialItem?.Tag?.ToString();

            if (!string.IsNullOrWhiteSpace(platformId))
                navServiceCore.NavigateTo("Chats", platformId);
            else
                navServiceCore.NavigateTo("Chats");

            DismissSplashAsync();
        };

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // 1. Force DWM to use immersive dark mode frame and dark caption
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        int captionColor = 0x00202020; // COLORREF: 0x00BBGGRR -> #202020
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

        // 2. Set native Win32 window background brush to #202020
        // This guarantees that before DirectX presents the first frame during the DWM scale-in animation,
        // DWM composites the HWND with the #202020 dark background (zero black screen flash).
        IntPtr darkBrush = CreateSolidBrush(0x00202020);
        SetClassLongPtr(hwnd, GCLP_HBRBACKGROUND, darkBrush);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        
        if (this.Content is FrameworkElement rootElement)
        {
            UpdateSidebarIcons(rootElement.ActualTheme);

            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
                UpdateTitleBarColors(titleBar, rootElement.ActualTheme);
            }

            rootElement.ActualThemeChanged += (s, args) => 
            {
                UpdateSidebarIcons(rootElement.ActualTheme);
                if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
                {
                    UpdateTitleBarColors(appWindow.TitleBar, rootElement.ActualTheme);
                }
            };
        }

        var settingsService = App.Current.Services.GetRequiredService<People.Core.Interfaces.ISettingsService>();
        uint dpi = GetDpiForWindow(hwnd);
        float scalingFactor = dpi / 96f;

        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(360 * scalingFactor);
            presenter.PreferredMinimumHeight = (int)(480 * scalingFactor);
        }

        int savedWidth = settingsService.GetValue<int>("WindowWidth", 0);
        int savedHeight = settingsService.GetValue<int>("WindowHeight", 0);

        if (savedWidth > 0 && savedHeight > 0)
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(savedWidth, savedHeight));
        }
        else
        {
            int defaultWidth = (int)(1120 * scalingFactor);
            int defaultHeight = (int)(680 * scalingFactor);
            appWindow.Resize(new Windows.Graphics.SizeInt32(defaultWidth, defaultHeight));
        }

        int savedX = settingsService.GetValue<int>("WindowPositionX", int.MinValue);
        int savedY = settingsService.GetValue<int>("WindowPositionY", int.MinValue);

        if (savedX != int.MinValue && savedY != int.MinValue)
        {
            appWindow.Move(new Windows.Graphics.PointInt32(savedX, savedY));
        }
        else
        {
            // First launch: center on display work area
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                int centerX = displayArea.WorkArea.X + Math.Max(0, (displayArea.WorkArea.Width - appWindow.Size.Width) / 2);
                int centerY = displayArea.WorkArea.Y + Math.Max(0, (displayArea.WorkArea.Height - appWindow.Size.Height) / 2);
                appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
            }
        }

        appWindow.Changed += (s, args) =>
        {
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p && p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Restored)
            {
                if (args.DidSizeChange)
                {
                    settingsService.SetValue("WindowWidth", appWindow.Size.Width);
                    settingsService.SetValue("WindowHeight", appWindow.Size.Height);
                }
                if (args.DidPositionChange)
                {
                    settingsService.SetValue("WindowPositionX", appWindow.Position.X);
                    settingsService.SetValue("WindowPositionY", appWindow.Position.Y);
                }
            }
        };

        NavView.SizeChanged += NavView_SizeChanged;
    }

    private async void DismissSplashAsync()
    {
        // Allow a brief moment for the initial page to measure, layout, and render
        await Task.Delay(350);

        if (SplashOverlay == null || SplashOverlay.Visibility == Visibility.Collapsed)
            return;

        var fadeAnimation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        };

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeAnimation, SplashOverlay);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeAnimation, "Opacity");
        storyboard.Children.Add(fadeAnimation);

        storyboard.Completed += (s, e) =>
        {
            SplashOverlay.Visibility = Visibility.Collapsed;
        };

        storyboard.Begin();
    }

    private static bool IsDarkTheme(ElementTheme theme)
    {
        if (theme == ElementTheme.Dark) return true;
        if (theme == ElementTheme.Light) return false;
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var bg = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return bg.R < 128;
        }
        catch
        {
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
    }

    private void UpdateTitleBarColors(Microsoft.UI.Windowing.AppWindowTitleBar titleBar, ElementTheme theme)
    {
        bool isDark = IsDarkTheme(theme);
        var foregroundColor = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonForegroundColor = foregroundColor;
        titleBar.ButtonHoverForegroundColor = foregroundColor;
        titleBar.ButtonPressedForegroundColor = foregroundColor;
        titleBar.ButtonInactiveForegroundColor = isDark ? Microsoft.UI.ColorHelper.FromArgb(255, 150, 150, 150) : Microsoft.UI.ColorHelper.FromArgb(255, 100, 100, 100);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void NavView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;

        if (width < 1007)
        {
            NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
        }
        else
        {
            NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var navService = App.Current.Services.GetRequiredService<People.Core.Interfaces.INavigationService>();
        
        if (args.IsSettingsInvoked)
        {
            navService.NavigateTo("Settings");
        }
        else
        {
            var tag = args.InvokedItemContainer?.Tag?.ToString();
            if (tag != null)
            {
                if (tag == "WhatsApp") 
                {
                    navService.NavigateTo("Chats", tag);
                }
                else
                {
                    navService.NavigateTo("ComingSoon", tag);
                }
            }
        }
    }

    private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        HandleBackRequest();
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        HandleBackRequest();
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void HandleBackRequest()
    {
        if (ContentFrame.Content is Views.ChatListPage chatList && chatList.IsDetailVisibleInNarrowMode())
        {
            chatList.CloseDetail();
            return;
        }

        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        // Handle UI updates based on display mode if necessary
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        ViewModel.IsBackEnabled = ContentFrame.CanGoBack;

        var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();
        if (ContentFrame.Content is Views.SettingsPage)
        {
            themeService.ClearActivePlatform();
        }
        else if (ContentFrame.Content is Views.ChatListPage chatList)
        {
            chatList.UpdateAccentColor();
        }
        else if (ContentFrame.Content is Views.ConversationPage convPage)
        {
            themeService.SetActivePlatform(convPage.ViewModel.CurrentChat?.PlatformId);
        }
        else
        {
            themeService.ClearActivePlatform();
        }
    }
}
