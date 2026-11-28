using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using People.ViewModels;

namespace People;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }
    public bool HasNavigatedContent => RootFrame?.Content != null;

    private bool _isExplicitExit;

    public void CloseExplicitly()
    {
        _isExplicitExit = true;
        Close();
    }

    public MainWindow()
    {
        ViewModel = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();
        themeService.Initialize();
        themeService.ApplyToWindow(this);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        try
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "TrayIcons", "TrayIcon_Color.ico");
            if (System.IO.File.Exists(icoPath))
            {
                appWindow.SetIcon(icoPath);
            }
        }
        catch { }

        // Keep app running in background when closed via title bar close button
        appWindow.Closing += (sender, args) =>
        {
            if (!_isExplicitExit)
            {
                args.Cancel = true;
                sender.Hide();
            }
        };

        if (this.Content is FrameworkElement rootElement)
        {
            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
                UpdateTitleBarTheme(titleBar, rootElement.ActualTheme);
            }

            rootElement.ActualThemeChanged += (s, args) =>
            {
                if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
                {
                    UpdateTitleBarTheme(appWindow.TitleBar, rootElement.ActualTheme);
                }
            };
        }

        var settingsService = App.Current.Services.GetRequiredService<People.Core.Interfaces.ISettingsService>();
        uint dpi = GetDpiForWindow(hwnd);
        float scalingFactor = dpi / 96f;

        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(340 * scalingFactor);
            presenter.PreferredMinimumHeight = (int)(420 * scalingFactor);
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

        RootGrid.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((s, e) =>
        {
            var trayService = App.Current.Services.GetService<People.Core.Interfaces.ITrayIconService>();
            trayService?.HideFlyout();
        }), true);

        Activated += (s, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                var trayService = App.Current.Services.GetService<People.Core.Interfaces.ITrayIconService>();
                trayService?.HideFlyout();
            }
        };

        RootFrame.Navigated += RootFrame_Navigated;

        // Determine initial navigation: Onboarding or Main Shell
        // If user clicked Get Started but didn't activate or sign in to any platform, reopen Onboarding
        bool hasCompletedOnboarding = settingsService.GetValue<bool>("HasCompletedOnboarding", false);
        bool hasActivePlatform = HasActiveOrSignedInPlatform(App.Current.Services);
        if (!hasCompletedOnboarding || !hasActivePlatform)
        {
            Views.OnboardingPage.PreloadProfilePictures();
            RootFrame.Navigate(typeof(Views.OnboardingPage));
        }
        else
        {
            RootFrame.Navigate(typeof(Views.MainShellPage));
        }
    }

    private void RootFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // When content is first navigated into the frame, make RootGrid transparent to reveal Mica/Acrylic backdrop
        if (RootGrid.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush scb || scb.Color != Microsoft.UI.Colors.Transparent)
        {
            var settingsService = App.Current.Services.GetRequiredService<People.Core.Interfaces.ISettingsService>();
            var backdrop = settingsService.GetValue<string>("AppBackdrop") ?? "Mica";
            if (backdrop != "None")
            {
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
    }

    public void ShowOnboarding()
    {
        Views.OnboardingPage.PreloadProfilePictures();
        RootFrame.Navigate(typeof(Views.OnboardingPage), null, new Microsoft.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());
    }

    public void BringToFront()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        if (appWindow != null)
        {
            appWindow.Show();
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }
        }
        this.Activate();
        ShowWindow(hwnd, 9); // SW_RESTORE
        SetForegroundWindow(hwnd);
    }

    public static bool HasActiveOrSignedInPlatform(IServiceProvider services)
    {
        try
        {
            var providerManager = services.GetService<People.Providers.ProviderManager>();
            if (providerManager != null)
            {
                foreach (var provider in providerManager.GetAllProviders())
                {
                    if (provider is People.Core.Interfaces.IWhatsAppProvider wa && wa.IsAuthenticated)
                    {
                        return true;
                    }
                    if (provider.ConnectionState == People.Core.Models.ConnectionState.Connected)
                    {
                        return true;
                    }
                }
            }

            var dbPath = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "people.db");
            if (!System.IO.File.Exists(dbPath))
            {
                return false;
            }

            var dbFactory = services.GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<People.Data.PeopleDbContext>>();
            if (dbFactory != null)
            {
                using var db = dbFactory.CreateDbContext();
                if (System.Linq.Queryable.Any(db.Chats))
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    public void OpenPlatform(string platform)
    {
        if (RootFrame.Content is not Views.MainShellPage)
        {
            RootFrame.Navigate(typeof(Views.MainShellPage));
        }

        Views.MainShellPage.Current?.OpenPlatform(platform);
    }

    public void OpenChat(People.Core.Models.Chat chat)
    {
        if (RootFrame.Content is not Views.MainShellPage)
        {
            RootFrame.Navigate(typeof(Views.MainShellPage));
        }

        Views.MainShellPage.Current?.OpenChat(chat);
    }

    private static void UpdateTitleBarTheme(Microsoft.UI.Windowing.AppWindowTitleBar titleBar, ElementTheme theme)
    {
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.PreferredTheme = theme switch
        {
            ElementTheme.Light => Microsoft.UI.Windowing.TitleBarTheme.Light,
            ElementTheme.Dark => Microsoft.UI.Windowing.TitleBarTheme.Dark,
            _ => Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
