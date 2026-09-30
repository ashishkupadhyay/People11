using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using People.ViewModels;

namespace People.Views;

public sealed partial class ChatListPage : Page
{
    public ChatListViewModel ViewModel { get; }

    private bool _isFlyout = false;

    public ChatListPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ChatListViewModel>();
        InitializeComponent();
        
        DetailFrame.Navigated += DetailFrame_Navigated;
        
        this.SizeChanged += ChatListPage_SizeChanged;
    }

    private void ChatListPage_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        UpdateAdaptiveLayout(e.NewSize.Width);
    }

    private void DetailFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        // Only allow hits if we are looking at a conversation.
        DetailFrame.IsHitTestVisible = DetailFrame.Content is ConversationPage;
        
        // If we navigated back to the blank page, clear the backstack so we don't build it up
        if (DetailFrame.Content is not ConversationPage && DetailFrame.CanGoBack)
        {
            DetailFrame.BackStack.Clear();
        }

        UpdateAdaptiveLayout(this.ActualWidth);
        UpdateAccentColor();
    }
    
    private void UpdateAccentColor()
    {
        if (_isFlyout) return;

        var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();
        if (DetailFrame.Content is ConversationPage convPage && convPage.ViewModel.CurrentChat != null)
        {
            string platformId = convPage.ViewModel.CurrentChat.PlatformId;
            if (string.IsNullOrEmpty(platformId))
            {
                if (convPage.ViewModel.CurrentChat.Id?.StartsWith("wa_") == true) platformId = "WhatsApp";
                else if (convPage.ViewModel.CurrentChat.Id?.StartsWith("tg_") == true) platformId = "Telegram";
                else platformId = "WhatsApp";
            }
            themeService.SetActivePlatform(platformId);
        }
    }
    
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        if (DetailFrame.Content == null)
        {
            DetailFrame.Navigate(typeof(Page), null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
        }

        if (e.Parameter is string param)
        {
            if (param == "Flyout")
            {
                _isFlyout = true;
                return;
            }
            
            // If the parameter is a platform ID (like "WhatsApp"), set it immediately.
            if (!string.IsNullOrEmpty(param) && !_isFlyout)
            {
                var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();
                themeService.SetActivePlatform(param);
                return;
            }
        }
        
        UpdateAccentColor();
    }

    private void UpdateAdaptiveLayout(double width)
    {
        var mainVm = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        bool hasConversation = DetailFrame.Content is ConversationPage;

        if (width < 640)
        {
            // Narrow State
            // We no longer collapse grids here so transitions can play out.
            // DetailFrame is on top, and if empty/transparent, ListGrid is seen.
            mainVm.IsBackEnabled = hasConversation;
            PlaceholderGrid.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        else
        {
            // Wide State handled by VisualStateManager
            var navService = App.Current.Services.GetRequiredService<People.Core.Interfaces.INavigationService>();
            mainVm.IsBackEnabled = navService.CanGoBack;
            PlaceholderGrid.Visibility = hasConversation ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    private void ChatList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is People.Core.Models.Chat chat)
        {
            if (DetailFrame.Content is ConversationPage convPage && convPage.ViewModel.CurrentChat?.Id == chat.Id)
            {
                return;
            }
            DetailFrame.Navigate(typeof(ConversationPage), chat);
        }
    }

    private void NewChatList_ItemClick(object sender, ItemClickEventArgs e)
    {
        // For now just close flyout and navigate to dummy chat or first chat
        if (sender is ListView listView && listView.XamlRoot != null)
        {
            // Close flyout
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(listView) is Microsoft.UI.Xaml.FrameworkElement parent)
            {
                // Simplistic flyout close placeholder
            }
        }
        
        // Navigate to the first chat for demo purposes
        if (ViewModel.Chats.Count > 0)
        {
            DetailFrame.Navigate(typeof(ConversationPage), ViewModel.Chats[0]);
        }
    }

    public bool IsDetailVisibleInNarrowMode()
    {
        return this.ActualWidth < 640 && DetailFrame.Content is ConversationPage;
    }

    public void CloseDetail()
    {
        if (DetailFrame.CanGoBack)
        {
            DetailFrame.GoBack();
        }
        else
        {
            DetailFrame.Navigate(typeof(Page), null, new Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionInfo() { Effect = Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromLeft });
        }
    }
}
