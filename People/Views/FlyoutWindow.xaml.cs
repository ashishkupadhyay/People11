using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using People.Core.Interfaces;

namespace People.Views;

public sealed partial class FlyoutWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    private const uint GW_OWNER = 4;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private readonly IntPtr _hWnd;
    private readonly AppWindow _appWindow;
    private readonly ISettingsService _settingsService;
    private string _selectedPlatform = "WhatsApp";
    private ChatListPage? _currentChatListPage;

    private bool _isTopTaskbar;
    private bool _isLeftTaskbar;
    private bool _isRightTaskbar;

    public bool IsFlyoutVisible { get; private set; }

    public FlyoutWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
        _settingsService.SettingChanged += OnSettingChanged;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
        }

        ContentFrame.Navigated += ContentFrame_Navigated;
        NavigateToPlatform("WhatsApp");

        this.Activated += OnActivated;
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key.StartsWith("BrandColor_") || key == "AppTheme" || key == $"{_selectedPlatform}Enabled")
        {
            DispatcherQueue.TryEnqueue(() => ApplyFlyoutPlatformAccent(_selectedPlatform));
        }
    }

    private void ApplyFlyoutPlatformAccent(string platform)
    {
        var themeService = App.Current.Services.GetRequiredService<People.Services.ThemeService>();
        themeService.ApplyAccentToResourceDictionary(RootGrid.Resources, platform == "SMS" ? null : platform);
    }

    private void PositionAndResizeWindow()
    {
        GetCursorPos(out POINT cursor);
        var displayArea = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var outer = displayArea.OuterBounds;

        uint dpi = GetDpiForWindow(_hWnd);
        double scale = dpi > 0 ? (dpi / 96.0) : 1.0;

        int margin = (int)Math.Round(12 * scale);
        double maxLogicalHeight = Math.Max(400, (workArea.Height / scale) - 32);
        double logicalHeight = Math.Min(660, maxLogicalHeight);
        double logicalWidth = 360;

        int physicalWidth = (int)Math.Round(logicalWidth * scale);
        int physicalHeight = (int)Math.Round(logicalHeight * scale);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));

        _isTopTaskbar = workArea.Y > outer.Y;
        _isLeftTaskbar = workArea.X > outer.X;
        _isRightTaskbar = (workArea.X + workArea.Width) < (outer.X + outer.Width);

        int x;
        int y;

        if (_isTopTaskbar)
        {
            // Taskbar is at the TOP of the monitor
            y = workArea.Y + margin;
            x = cursor.X - (physicalWidth / 2);
        }
        else if (_isLeftTaskbar)
        {
            // Taskbar is at the LEFT of the monitor
            x = workArea.X + margin;
            y = cursor.Y - (physicalHeight / 2);
        }
        else if (_isRightTaskbar)
        {
            // Taskbar is at the RIGHT of the monitor
            x = workArea.X + workArea.Width - physicalWidth - margin;
            y = cursor.Y - (physicalHeight / 2);
        }
        else
        {
            // Default: Taskbar is at the BOTTOM of the monitor
            y = workArea.Y + workArea.Height - physicalHeight - margin;
            x = cursor.X - (physicalWidth / 2);
        }

        // Clamp within workArea bounds
        int minX = workArea.X + margin;
        int maxX = Math.Max(minX, workArea.X + workArea.Width - physicalWidth - margin);
        x = Math.Clamp(x, minX, maxX);

        int minY = workArea.Y + margin;
        int maxY = Math.Max(minY, workArea.Y + workArea.Height - physicalHeight - margin);
        y = Math.Clamp(y, minY, maxY);

        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    public void ShowFlyoutAnimated()
    {
        IsFlyoutVisible = true;
        PositionAndResizeWindow();
        ApplyFlyoutPlatformAccent(_selectedPlatform);

        double startX = 0;
        double startY = 0;

        if (_isTopTaskbar)
        {
            startY = -40;
        }
        else if (_isLeftTaskbar)
        {
            startX = -40;
        }
        else if (_isRightTaskbar)
        {
            startX = 40;
        }
        else
        {
            startY = 40;
        }

        SlideTransform.X = startX;
        SlideTransform.Y = startY;
        RootGrid.Opacity = 0;

        ShowWindow(_hWnd, SW_SHOW);
        ForceActivateForeground();

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animX = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animX, SlideTransform);
        Storyboard.SetTargetProperty(animX, "X");

        var animY = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animY, SlideTransform);
        Storyboard.SetTargetProperty(animY, "Y");

        var animOpacity = new DoubleAnimation
        {
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animOpacity, RootGrid);
        Storyboard.SetTargetProperty(animOpacity, "Opacity");

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Children.Add(animOpacity);
        sb.Begin();
    }

    public void HideFlyoutAnimated()
    {
        if (!IsFlyoutVisible) return;
        IsFlyoutVisible = false;

        double targetX = 0;
        double targetY = 0;

        if (_isTopTaskbar)
        {
            targetY = -30;
        }
        else if (_isLeftTaskbar)
        {
            targetX = -30;
        }
        else if (_isRightTaskbar)
        {
            targetX = 30;
        }
        else
        {
            targetY = 30;
        }

        var sb = new Storyboard();
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var animX = new DoubleAnimation
        {
            To = targetX,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animX, SlideTransform);
        Storyboard.SetTargetProperty(animX, "X");

        var animY = new DoubleAnimation
        {
            To = targetY,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animY, SlideTransform);
        Storyboard.SetTargetProperty(animY, "Y");

        var animOpacity = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(animOpacity, RootGrid);
        Storyboard.SetTargetProperty(animOpacity, "Opacity");

        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Children.Add(animOpacity);

        sb.Completed += (s, e) =>
        {
            ShowWindow(_hWnd, SW_HIDE);
        };
        sb.Begin();
    }

    public void DestroyWindow()
    {
        _settingsService.SettingChanged -= OnSettingChanged;
        this.Close();
    }

    public void ForceActivateForeground()
    {
        keybd_event(0, 0, 0, UIntPtr.Zero);
        SetForegroundWindow(_hWnd);
        BringWindowToTop(_hWnd);
        SetActiveWindow(_hWnd);
        SetFocus(_hWnd);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (IsAppOrFlyoutFocused())
                {
                    return; // Ignore deactivation caused by internal popups/flyouts
                }
                HideFlyoutAnimated();
            });
        }
    }

    private bool IsAppOrFlyoutFocused()
    {
        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero) return false;
        if (fgHwnd == _hWnd) return true;

        if (App.Current.MainWindow != null)
        {
            var mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow);
            if (fgHwnd == mainHwnd)
            {
                return false; // Clicked on main window -> collapse flyout!
            }

            var owner = GetWindow(fgHwnd, GW_OWNER);
            if (owner == mainHwnd)
            {
                return false;
            }
            if (owner == _hWnd)
            {
                return true;
            }
        }

        return false;
    }

    private void NavigateToPlatform(string platform)
    {
        _selectedPlatform = platform;
        PlatformNameText.Text = platform;

        ApplyFlyoutPlatformAccent(platform);

        if (platform == "WhatsApp")
        {
            ContentFrame.Navigate(typeof(ChatListPage), "Flyout");
        }
        else
        {
            ContentFrame.Navigate(typeof(ComingSoonPage), platform);
        }
    }

    private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_currentChatListPage != null)
        {
            _currentChatListPage.ConversationOpened -= OnConversationOpened;
            _currentChatListPage.ConversationClosed -= OnConversationClosed;
            _currentChatListPage = null;
        }

        if (ContentFrame.Content is ChatListPage chatList)
        {
            _currentChatListPage = chatList;
            chatList.ConversationOpened += OnConversationOpened;
            chatList.ConversationClosed += OnConversationClosed;
            FlyoutBackButton.Visibility = chatList.IsDetailVisibleInNarrowMode() ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            FlyoutBackButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnConversationOpened(object? sender, People.Core.Models.Chat chat)
    {
        FlyoutBackButton.Visibility = Visibility.Visible;
    }

    private void OnConversationClosed(object? sender, EventArgs e)
    {
        FlyoutBackButton.Visibility = Visibility.Collapsed;
    }

    private void FlyoutBackButton_Click(object sender, RoutedEventArgs e)
    {
        _currentChatListPage?.CloseDetail();
    }

    private void PlatformNameButton_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = App.Current.MainWindow as MainWindow;
        if (mainWindow != null)
        {
            mainWindow.BringToFront();
            if (_currentChatListPage?.GetActiveConversation() is People.Core.Models.Chat activeChat)
            {
                mainWindow.OpenChat(activeChat);
            }
            else
            {
                mainWindow.OpenPlatform(_selectedPlatform);
            }
        }
        HideFlyoutAnimated();
    }

    private void PlatformMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string platform)
        {
            NavigateToPlatform(platform);
        }
    }

    private void OnEscapeInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_currentChatListPage != null && _currentChatListPage.IsDetailVisibleInNarrowMode())
        {
            _currentChatListPage.CloseDetail();
            return;
        }
        HideFlyoutAnimated();
    }
}
