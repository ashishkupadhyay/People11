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
    public bool HasNavigatedContent => ContentFrame?.Content != null;

    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _whatsAppDarkSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _whatsAppLightSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _telegramDarkSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _telegramLightSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _signalDarkSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _signalLightSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _discordDarkSource;
    private Microsoft.UI.Xaml.Media.Imaging.SvgImageSource? _discordLightSource;

    private void UpdateSidebarIcons(ElementTheme theme)
    {
        bool isDark = IsDarkTheme(theme);

        if (isDark)
        {
            _whatsAppDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/WhatsApp-Logo-White.svg"));
            if (NavWhatsAppIcon != null) NavWhatsAppIcon.Source = _whatsAppDarkSource;

            _telegramDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Telegram-Logo-White.svg"));
            if (NavTelegramIcon != null) NavTelegramIcon.Source = _telegramDarkSource;

            _signalDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Signal-Logo-White.svg"));
            if (NavSignalIcon != null) NavSignalIcon.Source = _signalDarkSource;

            _discordDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Discord-Logo-White.svg"));
            if (NavDiscordIcon != null) NavDiscordIcon.Source = _discordDarkSource;
        }
        else
        {
            _whatsAppLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/WhatsApp-Logo-Black.svg"));
            if (NavWhatsAppIcon != null) NavWhatsAppIcon.Source = _whatsAppLightSource;

            _telegramLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Telegram-Logo-Black.svg"));
            if (NavTelegramIcon != null) NavTelegramIcon.Source = _telegramLightSource;

            _signalLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Signal-Logo-Black.svg"));
            if (NavSignalIcon != null) NavSignalIcon.Source = _signalLightSource;

            _discordLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/Discord-Logo-Black.svg"));
            if (NavDiscordIcon != null) NavDiscordIcon.Source = _discordLightSource;
        }
    }

    public Microsoft.UI.Xaml.Visibility ConvertBoolToVisibility(bool isVisible)
    {
        return isVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

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
        
        if (NavView.MenuItems.Count > 0)
        {
            var initialItem = NavView.MenuItems[0] as NavigationViewItem;
            NavView.SelectedItem = initialItem;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
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
        int targetWidth = savedWidth > 0 ? savedWidth : (int)(1120 * scalingFactor);
        int targetHeight = savedHeight > 0 ? savedHeight : (int)(680 * scalingFactor);

        int savedX = settingsService.GetValue<int>("WindowPositionX", int.MinValue);
        int savedY = settingsService.GetValue<int>("WindowPositionY", int.MinValue);
        int targetX = savedX;
        int targetY = savedY;

        if (targetX == int.MinValue || targetY == int.MinValue)
        {
            // First launch: center on display work area
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                targetX = displayArea.WorkArea.X + Math.Max(0, (displayArea.WorkArea.Width - targetWidth) / 2);
                targetY = displayArea.WorkArea.Y + Math.Max(0, (displayArea.WorkArea.Height - targetHeight) / 2);
            }
        }

        if (targetX != int.MinValue && targetY != int.MinValue)
        {
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(targetX, targetY, targetWidth, targetHeight));
        }
        else
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(targetWidth, targetHeight));
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

        Activated += (s, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                var trayService = App.Current.Services.GetService<People.Core.Interfaces.ITrayIconService>();
                trayService?.HideFlyout();
            }
        };
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
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = foregroundColor;
        titleBar.ButtonHoverForegroundColor = foregroundColor;
        titleBar.ButtonPressedForegroundColor = foregroundColor;
        titleBar.ButtonHoverBackgroundColor = isDark 
            ? Microsoft.UI.ColorHelper.FromArgb(25, 255, 255, 255) 
            : Microsoft.UI.ColorHelper.FromArgb(25, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = isDark 
            ? Microsoft.UI.ColorHelper.FromArgb(40, 255, 255, 255) 
            : Microsoft.UI.ColorHelper.FromArgb(40, 0, 0, 0);
        titleBar.ButtonInactiveForegroundColor = isDark 
            ? Microsoft.UI.ColorHelper.FromArgb(255, 150, 150, 150) 
            : Microsoft.UI.ColorHelper.FromArgb(255, 100, 100, 100);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public void BringToFront()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        if (appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
        this.Activate();
        SetForegroundWindow(hwnd);
    }

    public void OpenPlatform(string platform)
    {
        var navService = App.Current.Services.GetRequiredService<People.Core.Interfaces.INavigationService>();
        if (platform == "WhatsApp")
        {
            navService.NavigateTo("Chats", platform);
            if (NavView.MenuItems.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }
        }
        else
        {
            navService.NavigateTo("ComingSoon", platform);
            foreach (var item in NavView.MenuItems)
            {
                if (item is NavigationViewItem nvi && (string)nvi.Tag == platform)
                {
                    NavView.SelectedItem = nvi;
                    break;
                }
            }
        }
    }

    public void OpenChat(People.Core.Models.Chat chat)
    {
        OpenPlatform(chat.PlatformId ?? "WhatsApp");
        if (ContentFrame.Content is Views.ChatListPage chatListPage)
        {
            chatListPage.OpenChatInDetail(chat);
        }
    }

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

        // When content is first navigated into the frame, make RootGrid transparent to reveal Mica backdrop
        if (RootGrid.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush scb || scb.Color != Microsoft.UI.Colors.Transparent)
        {
            var settingsService = App.Current.Services.GetRequiredService<People.Core.Interfaces.ISettingsService>();
            var backdrop = settingsService.GetValue<string>("AppBackdrop") ?? "Mica";
            if (backdrop != "None")
            {
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();
        if (ContentFrame.Content is Views.SettingsPage)
        {
            themeService.ClearActivePlatform();
        }
        else if (ContentFrame.Content is Views.ConversationPage convPage)
        {
            themeService.SetActivePlatform(convPage.ViewModel.CurrentChat?.PlatformId);
        }
        else if (ContentFrame.Content is not Views.ChatListPage)
        {
            themeService.ClearActivePlatform();
        }
    }
}
