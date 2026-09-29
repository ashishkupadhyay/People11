using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using People.ViewModels;

namespace People.Views;

public sealed partial class ChatListPage : Page
{
    public ChatListViewModel ViewModel { get; }

    public ChatListPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ChatListViewModel>();
        InitializeComponent();
        
        // Initialize with a blank page so there is something to go back to.
        DetailFrame.Navigate(typeof(Page), null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
        
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
