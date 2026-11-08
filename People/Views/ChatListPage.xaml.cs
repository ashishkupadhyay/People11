using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using People.ViewModels;

namespace People.Views;

public sealed partial class ChatListPage : Page
{
    public ChatListViewModel ViewModel { get; }

    private bool _isFlyout = false;

    private static readonly Microsoft.UI.Xaml.Media.Imaging.SvgImageSource _lightDoodleSource = new(new Uri("ms-appx:///Assets/Background/whatsapp_doodle_light.svg"));
    private static readonly Microsoft.UI.Xaml.Media.Imaging.SvgImageSource _darkDoodleSource = new(new Uri("ms-appx:///Assets/Background/whatsapp_doodle_dark.svg"));

    public ChatListPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ChatListViewModel>();
        InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        
        DetailFrame.Navigated += DetailFrame_Navigated;
        this.SizeChanged += ChatListPage_SizeChanged;
        this.Loaded += (s, e) => UpdateEmptyStateAnimation(_platformId);
        this.ActualThemeChanged += (s, e) =>
        {
            UpdateEmptyStateAnimation(_platformId);
            if (OnboardingDoodleCanvas != null)
            {
                UpdateOnboardingDoodleTiles(OnboardingDoodleCanvas.ActualWidth, OnboardingDoodleCanvas.ActualHeight);
            }
        };

        ViewModel.RequestSlideToQr += (s, e) => PlaySlideToQrAnimation();
        ViewModel.RequestDrillInToSync += (s, e) => PlayDrillInToSyncAnimation();
        ViewModel.RequestDrillInToMain += (s, e) => PlayDrillInToMainAnimation();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ChatListViewModel.IsWelcomeVisible) ||
                e.PropertyName == nameof(ChatListViewModel.IsQrLinkVisible) ||
                e.PropertyName == nameof(ChatListViewModel.IsInitialSyncing))
            {
                if (ViewModel.IsWelcomeVisible || ViewModel.IsQrLinkVisible || ViewModel.IsInitialSyncing)
                {
                    MainContentGrid.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                }
            }
        };

        NewChatFlyout.Opened += (s, e) =>
        {
            SearchContactsBox.Text = string.Empty;
            ViewModel.FilterContacts(string.Empty);
            _ = ViewModel.RebuildContactsAsync();
        };
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
    private string? _lastEmptyStateKey;
    
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
        string key = $"{prefix}_{themeSuffix}";

        if (_lastEmptyStateKey != key || EmptyStatePlayer.Source == null)
        {
            _lastEmptyStateKey = key;
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
        }

        // Explicitly play once with looped: false so it never loops when re-navigating to the page
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

        if (ViewModel.IsWelcomeVisible || ViewModel.IsQrLinkVisible || ViewModel.IsInitialSyncing)
        {
            MainContentGrid.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        else
        {
            MainContentGrid.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            MainContentGrid.Opacity = 1.0;
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
            DetailFrame.Navigate(
                typeof(ConversationPage),
                chat,
                new Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionInfo { Effect = Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight });
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
            DetailFrame.Navigate(
                typeof(ConversationPage),
                chat,
                new Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionInfo { Effect = Microsoft.UI.Xaml.Media.Animation.SlideNavigationTransitionEffect.FromRight });
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

    private void OnboardingDoodleCanvas_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        UpdateOnboardingDoodleTiles(e.NewSize.Width, e.NewSize.Height);
    }

    private void UpdateOnboardingDoodleTiles(double width, double height)
    {
        if (width <= 0 || height <= 0 || OnboardingDoodleCanvas == null) return;

        OnboardingDoodleCanvas.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, width, height)
        };

        const double tileWidth = 300.0;
        const double tileHeight = 534.22;

        int cols = (int)Math.Ceiling(width / tileWidth);
        int rows = (int)Math.Ceiling(height / tileHeight);
        int totalNeeded = cols * rows;

        bool isDark = App.Current.RequestedTheme == Microsoft.UI.Xaml.ApplicationTheme.Dark;
        if (this.ActualTheme != Microsoft.UI.Xaml.ElementTheme.Default)
        {
            isDark = this.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
        }
        var currentSource = isDark ? _darkDoodleSource : _lightDoodleSource;

        while (OnboardingDoodleCanvas.Children.Count < totalNeeded)
        {
            var img = new Microsoft.UI.Xaml.Controls.Image
            {
                Width = tileWidth,
                Height = tileHeight,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                Source = currentSource,
                IsHitTestVisible = false
            };
            OnboardingDoodleCanvas.Children.Add(img);
        }

        int idx = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var img = (Microsoft.UI.Xaml.Controls.Image)OnboardingDoodleCanvas.Children[idx++];
                img.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                img.Source = currentSource;
                Microsoft.UI.Xaml.Controls.Canvas.SetLeft(img, c * tileWidth);
                Microsoft.UI.Xaml.Controls.Canvas.SetTop(img, r * tileHeight);
            }
        }

        for (int i = idx; i < OnboardingDoodleCanvas.Children.Count; i++)
        {
            OnboardingDoodleCanvas.Children[i].Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    private void PlaySlideToQrAnimation()
    {
        QrLinkOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        double startX = this.ActualWidth > 0 ? this.ActualWidth : 800;
        QrLinkTransform.TranslateX = startX;
        QrLinkOverlay.Opacity = 0;

        var animSlideOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = -250,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animSlideOut, WelcomeTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animSlideOut, "TranslateX");

        var animFadeOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(250)
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animFadeOut, WelcomeOverlay);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animFadeOut, "Opacity");

        var animSlideIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animSlideIn, QrLinkTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animSlideIn, "TranslateX");

        var animFadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(300)
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animFadeIn, QrLinkOverlay);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animFadeIn, "Opacity");

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(animSlideOut);
        sb.Children.Add(animFadeOut);
        sb.Children.Add(animSlideIn);
        sb.Children.Add(animFadeIn);
        sb.Completed += (s, e) =>
        {
            WelcomeOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        };
        sb.Begin();
    }

    private void PlayDrillInToSyncAnimation()
    {
        SyncingOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        SyncingTransform.ScaleX = 0.90;
        SyncingTransform.ScaleY = 0.90;
        SyncingTransform.CenterX = this.ActualWidth / 2;
        SyncingTransform.CenterY = this.ActualHeight / 2;
        SyncingOverlay.Opacity = 0;

        QrLinkTransform.CenterX = this.ActualWidth / 2;
        QrLinkTransform.CenterY = this.ActualHeight / 2;

        var animQrScaleX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.08, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        var animQrScaleY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.08, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        var animQrFade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(250) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animQrScaleX, QrLinkTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animQrScaleX, "ScaleX");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animQrScaleY, QrLinkTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animQrScaleY, "ScaleY");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animQrFade, QrLinkOverlay);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animQrFade, "Opacity");

        var animSyncScaleX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(350), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        var animSyncScaleY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(350), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        var animSyncFade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(250) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animSyncScaleX, SyncingTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animSyncScaleX, "ScaleX");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animSyncScaleY, SyncingTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animSyncScaleY, "ScaleY");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animSyncFade, SyncingOverlay);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animSyncFade, "Opacity");

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(animQrScaleX);
        sb.Children.Add(animQrScaleY);
        sb.Children.Add(animQrFade);
        sb.Children.Add(animSyncScaleX);
        sb.Children.Add(animSyncScaleY);
        sb.Children.Add(animSyncFade);
        sb.Completed += (s, e) =>
        {
            QrLinkOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            OnboardingDoodleHost.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        };
        sb.Begin();
    }

    private void PlayDrillInToMainAnimation()
    {
        MainContentGrid.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        MainLayoutTransform.ScaleX = 0.92;
        MainLayoutTransform.ScaleY = 0.92;
        MainLayoutTransform.CenterX = this.ActualWidth / 2;
        MainLayoutTransform.CenterY = this.ActualHeight / 2;
        MainContentGrid.Opacity = 0;

        SyncingTransform.CenterX = this.ActualWidth / 2;
        SyncingTransform.CenterY = this.ActualHeight / 2;

        var animSyncScaleX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.08, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        var animSyncScaleY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.08, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        var animSyncFade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 0.0, Duration = TimeSpan.FromMilliseconds(250) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animSyncScaleX, SyncingTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animSyncScaleX, "ScaleX");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animSyncScaleY, SyncingTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animSyncScaleY, "ScaleY");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animSyncFade, SyncingOverlay);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animSyncFade, "Opacity");

        var animMainScaleX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(350), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        var animMainScaleY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(350), EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut } };
        var animMainFade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1.0, Duration = TimeSpan.FromMilliseconds(250) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animMainScaleX, MainLayoutTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animMainScaleX, "ScaleX");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animMainScaleY, MainLayoutTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animMainScaleY, "ScaleY");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animMainFade, MainContentGrid);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animMainFade, "Opacity");

        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(animSyncScaleX);
        sb.Children.Add(animSyncScaleY);
        sb.Children.Add(animSyncFade);
        sb.Children.Add(animMainScaleX);
        sb.Children.Add(animMainScaleY);
        sb.Children.Add(animMainFade);
        sb.Completed += (s, e) =>
        {
            SyncingOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        };
        sb.Begin();
    }
}

