using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using People.ViewModels;

namespace People.Views;

public sealed partial class MainShellPage : Page
{
    public static MainShellPage? Current { get; private set; }

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

    public MainShellPage()
    {
        Current = this;
        ViewModel = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        InitializeComponent();

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
        NavView.SizeChanged += NavView_SizeChanged;

        this.Loaded += MainShellPage_Loaded;
        this.ActualThemeChanged += (s, e) => UpdateSidebarIcons(ActualTheme);
    }

    private void MainShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (App.Current.MainWindow is MainWindow mw)
        {
            mw.SetTitleBar(AppTitleBar);
        }

        UpdateSidebarIcons(ActualTheme);

        if (ContentFrame.Content == null)
        {
            var navService = App.Current.Services.GetRequiredService<People.Core.Interfaces.INavigationService>();
            navService.NavigateTo("Chats", "WhatsApp");

            if (NavView.MenuItems.Count > 0)
            {
                NavView.SelectedItem = NavView.MenuItems[0];
            }
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (App.Current.MainWindow is MainWindow mw)
        {
            mw.SetTitleBar(AppTitleBar);
        }
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

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        HandleBackRequest();
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    public void HandleBackRequest()
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
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        ViewModel.IsBackEnabled = ContentFrame.CanGoBack;

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

    private void UpdateSidebarIcons(ElementTheme theme)
    {
        bool isDark = IsDarkTheme(theme);

        if (isDark)
        {
            _whatsAppDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Platform_Icons/WhatsApp-Logo-White.svg"));
            if (NavWhatsAppIcon != null) NavWhatsAppIcon.Source = _whatsAppDarkSource;

            _telegramDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Platform_Icons/Telegram-Logo-White.svg"));
            if (NavTelegramIcon != null) NavTelegramIcon.Source = _telegramDarkSource;

            _signalDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Platform_Icons/Signal-Logo-White.svg"));
            if (NavSignalIcon != null) NavSignalIcon.Source = _signalDarkSource;

            _discordDarkSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Platform_Icons/Discord-Logo-White.svg"));
            if (NavDiscordIcon != null) NavDiscordIcon.Source = _discordDarkSource;
        }
        else
        {
            _whatsAppLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Platform_Icons/WhatsApp-Logo-Black.svg"));
            if (NavWhatsAppIcon != null) NavWhatsAppIcon.Source = _whatsAppLightSource;

            _telegramLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Platform_Icons/Telegram-Logo-Black.svg"));
            if (NavTelegramIcon != null) NavTelegramIcon.Source = _telegramLightSource;

            _signalLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Platform_Icons/Signal-Logo-Black.svg"));
            if (NavSignalIcon != null) NavSignalIcon.Source = _signalLightSource;

            _discordLightSource ??= new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Platform_Icons/Discord-Logo-Black.svg"));
            if (NavDiscordIcon != null) NavDiscordIcon.Source = _discordLightSource;
        }
    }
}
