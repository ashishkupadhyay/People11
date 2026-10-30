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
        this.ActualThemeChanged += (s, e) => UpdateEmptyStateAnimation(_platformId);
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
    
    private string? _platformId;
    
    public void UpdateAccentColor()
    {
        if (_isFlyout) return;

        var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();
        string? activePlatform = _platformId;

        if (DetailFrame.Content is ConversationPage convPage && convPage.ViewModel.CurrentChat != null)
        {
            var chat = convPage.ViewModel.CurrentChat;
            activePlatform = !string.IsNullOrWhiteSpace(chat.PlatformId)
                ? chat.PlatformId
                : chat.Id?.StartsWith("wa_") == true
                    ? "WhatsApp"
                    : chat.Id?.StartsWith("tg_") == true
                        ? "Telegram"
                        : _platformId;
        }

        themeService.SetActivePlatform(activePlatform);
        
        UpdateEmptyStateAnimation(activePlatform);
    }
    
    private void UpdateEmptyStateAnimation(string? platformId)
    {
        if (EmptyStatePlayer == null) return;
        
        // Use App.Current.RequestedTheme as baseline, or the element theme if overridden
        bool isDark = App.Current.RequestedTheme == Microsoft.UI.Xaml.ApplicationTheme.Dark;
        if (this.ActualTheme != Microsoft.UI.Xaml.ElementTheme.Default)
        {
            isDark = this.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
        }

        string themeSuffix = isDark ? "white" : "black";
        string prefix = (platformId ?? "sms").ToLower(); 

        switch(prefix)
        {
            case "whatsapp": 
                EmptyStatePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Whatsapp_animated_reveal_white() : new AnimatedVisuals.Whatsapp_animated_reveal_black();
                break;
            case "sms": 
                EmptyStatePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Chat_animated_white() : new AnimatedVisuals.Chat_animated_black();
                break;
            case "telegram": 
                EmptyStatePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Telegram_animated_white() : new AnimatedVisuals.Telegram_animated_black();
                break;
            case "signal": 
                EmptyStatePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Signal_animated_reveal_white() : new AnimatedVisuals.Signal_animated_reveal_black();
                break;
            case "discord": 
                EmptyStatePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Discord_animated_reveal_filled_white() : new AnimatedVisuals.Discord_animated_reveal_filled_black();
                break;
            default: 
                EmptyStatePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Chat_animated_white() : new AnimatedVisuals.Chat_animated_black();
                break;
        }
        
        _ = EmptyStatePlayer.PlayAsync(0, 1, false);
    }
    
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string param)
        {
            if (param == "Flyout")
            {
                _isFlyout = true;
                return;
            }
            
            _platformId = param;
        }
        
        if (DetailFrame.Content == null)
        {
            DetailFrame.Navigate(typeof(Page), null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
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

    private void SearchContactsBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.FilterContacts(sender.Text);
    }

    private void NewChatList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is People.Core.Models.Contact contact)
        {
            NewChatFlyout?.Hide();
            var chat = ViewModel.GetOrCreateChatForContact(contact);
            DetailFrame.Navigate(typeof(ConversationPage), chat);
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

