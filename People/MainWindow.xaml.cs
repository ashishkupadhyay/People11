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
            NavView.SelectedItem = NavView.MenuItems[0];
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
                    navService.NavigateTo("Chats");
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
    }

}
