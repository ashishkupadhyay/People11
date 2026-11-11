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

        if (this.Content is FrameworkElement rootElement)
        {
            if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
                UpdateTitleBarColors(titleBar, rootElement.ActualTheme);
            }

            rootElement.ActualThemeChanged += (s, args) =>
            {
                if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
                {
                    UpdateTitleBarColors(appWindow.TitleBar, rootElement.ActualTheme);
                }
            };
        }

        var settingsService = App.Current.Services.GetRequiredService<People.Core.Interfaces.ISettingsService>();
        uint dpi = GetDpiForWindow(hwnd);
        float scalingFactor = dpi / 96f;

        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(340 * scalingFactor);
            presenter.PreferredMinimumHeight = (int)(480 * scalingFactor);
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
        bool hasCompletedOnboarding = settingsService.GetValue<bool>("HasCompletedOnboarding", false);
        if (!hasCompletedOnboarding)
        {
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
        RootFrame.Navigate(typeof(Views.OnboardingPage), null, new Microsoft.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());
    }

    public void BringToFront()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        if (appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
        this.Activate();
        SetForegroundWindow(hwnd);
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

    private void UpdateTitleBarColors(Microsoft.UI.Windowing.AppWindowTitleBar titleBar, ElementTheme theme)
    {
        bool isDark = IsDarkTheme(theme);
        var foregroundColor = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = foregroundColor;
        titleBar.ButtonHoverForegroundColor = foregroundColor;
        titleBar.ButtonPressedForegroundColor = foregroundColor;
        titleBar.ButtonHoverBackgroundColor = isDark
            ? Microsoft.UI.ColorHelper.FromArgb(25, 255, 255, 255)
            : Microsoft.UI.ColorHelper.FromArgb(25, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = isDark
            ? Microsoft.UI.ColorHelper.FromArgb(40, 255, 255, 255)
            : Microsoft.UI.ColorHelper.FromArgb(40, 0, 0, 0);
        titleBar.ButtonInactiveForegroundColor = isDark
            ? Microsoft.UI.ColorHelper.FromArgb(255, 150, 150, 150)
            : Microsoft.UI.ColorHelper.FromArgb(255, 100, 100, 100);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
