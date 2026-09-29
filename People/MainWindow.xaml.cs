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
