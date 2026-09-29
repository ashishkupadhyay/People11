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
        }

        NavView.SizeChanged += NavView_SizeChanged;
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

        // Layout switching:
        // Wide: NavView in Row 1 (hamburger is below title bar).
        // Medium/Narrow: NavView in Row 0 (hamburger is in title bar).
        if (width >= 1007)
        {
            Grid.SetRow(NavView, 1);
            Grid.SetRowSpan(NavView, 1);
            AppTitleBar.Margin = new Thickness(0, 0, 0, 0);
            ContentFrame.Margin = new Thickness(0, 0, 0, 0);
        }
        else
        {
            Grid.SetRow(NavView, 0);
            Grid.SetRowSpan(NavView, 2);
            AppTitleBar.Margin = new Thickness(48, 0, 0, 0); // Leave space for Hamburger
            ContentFrame.Margin = new Thickness(0, 48, 0, 0);
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

    private void CustomBackButton_Click(object sender, RoutedEventArgs e)
    {
        HandleBackRequest();
    }

    private void CustomBackButton_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(BackAnimatedIcon, "PointerOver");
    }

    private void CustomBackButton_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(BackAnimatedIcon, "Normal");
    }

    private void CustomBackButton_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(BackAnimatedIcon, "Pressed");
    }

    private void CustomBackButton_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        Microsoft.UI.Xaml.Controls.AnimatedIcon.SetState(BackAnimatedIcon, "PointerOver");
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
