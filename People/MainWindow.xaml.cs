using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using People.ViewModels;
using System;

namespace People;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }

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
        };

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = appWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonPressedBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
            
            if (this.Content is FrameworkElement rootElement)
            {
                UpdateTitleBarColors(titleBar, rootElement.ActualTheme);
                UpdateSidebarIcons(rootElement.ActualTheme);
                rootElement.ActualThemeChanged += (s, args) => 
                {
                    UpdateTitleBarColors(titleBar, rootElement.ActualTheme);
                    UpdateSidebarIcons(rootElement.ActualTheme);
                };
            }
        }

        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            uint dpi = GetDpiForWindow(hwnd);
            float scalingFactor = dpi / 96f;
            presenter.PreferredMinimumWidth = (int)(480 * scalingFactor);
            presenter.PreferredMinimumHeight = (int)(480 * scalingFactor);
        }

        NavView.SizeChanged += NavView_SizeChanged;
    }

    private void UpdateTitleBarColors(Microsoft.UI.Windowing.AppWindowTitleBar titleBar, ElementTheme theme)
    {
        bool isDark = theme == ElementTheme.Dark || (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        var foregroundColor = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonForegroundColor = foregroundColor;
        titleBar.ButtonHoverForegroundColor = foregroundColor;
        titleBar.ButtonPressedForegroundColor = foregroundColor;
        titleBar.ButtonInactiveForegroundColor = isDark ? Microsoft.UI.ColorHelper.FromArgb(255, 150, 150, 150) : Microsoft.UI.ColorHelper.FromArgb(255, 100, 100, 100);
    }

    private void UpdateSidebarIcons(ElementTheme theme)
    {
        bool isDark = theme == ElementTheme.Dark || (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        string suffix = isDark ? "White" : "Black";
        
        if (NavWhatsAppIcon != null) NavWhatsAppIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri($"ms-appx:///Assets/Icons/WhatsApp-Logo-{suffix}.svg"));
        if (NavTelegramIcon != null) NavTelegramIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri($"ms-appx:///Assets/Icons/Telegram-Logo-{suffix}.svg"));
        if (NavSignalIcon != null) NavSignalIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri($"ms-appx:///Assets/Icons/Signal-Logo-{suffix}.svg"));
        if (NavDiscordIcon != null) NavDiscordIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri($"ms-appx:///Assets/Icons/Discord-Logo-{suffix}.svg"));
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


