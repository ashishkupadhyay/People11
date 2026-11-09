using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    private readonly IntPtr _hWnd;
    private readonly AppWindow _appWindow;
    private string _selectedPlatform = "WhatsApp";
    private ChatListPage? _currentChatListPage;

    public FlyoutWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(true, false);
        }

        PositionAndResizeWindow();

        ContentFrame.Navigated += ContentFrame_Navigated;
        NavigateToPlatform("WhatsApp");

        this.Activated += OnActivated;
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
        double logicalHeight = Math.Min(600, maxLogicalHeight);
        double logicalWidth = 380;

        int physicalWidth = (int)Math.Round(logicalWidth * scale);
        int physicalHeight = (int)Math.Round(logicalHeight * scale);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));

        bool isTopTaskbar = workArea.Y > outer.Y;
        bool isLeftTaskbar = workArea.X > outer.X;
        bool isRightTaskbar = (workArea.X + workArea.Width) < (outer.X + outer.Width);

        int x;
        int y;

        if (isTopTaskbar)
        {
            // Taskbar is at the TOP of the monitor
            y = workArea.Y + margin;
            x = cursor.X - (physicalWidth / 2);
        }
        else if (isLeftTaskbar)
        {
            // Taskbar is at the LEFT of the monitor
            x = workArea.X + margin;
            y = cursor.Y - (physicalHeight / 2);
        }
        else if (isRightTaskbar)
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
                this.Close();
            });
        }
    }

    private bool IsAppOrFlyoutFocused()
    {
        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero) return false;
        if (fgHwnd == _hWnd) return true;

        GetWindowThreadProcessId(fgHwnd, out uint fgPid);
        if (fgPid == (uint)Environment.ProcessId)
        {
            return true;
        }

        return false;
    }

    private void NavigateToPlatform(string platform)
    {
        _selectedPlatform = platform;
        PlatformNameText.Text = platform;

        var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();
        themeService.SetActivePlatform(platform == "SMS" ? null : platform);

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
        this.Close();
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
        this.Close();
    }
}
