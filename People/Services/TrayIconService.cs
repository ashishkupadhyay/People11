using System;
using System.IO;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using People.Core.Interfaces;
using People.Views;
using POINT = People.Views.FlyoutWindow.POINT;
using Microsoft.Extensions.DependencyInjection;

namespace People.Services;

public class TrayIconService : ITrayIconService
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    private TaskbarIcon? _taskbarIcon;
    private FlyoutWindow? _flyoutWindow;
    private MenuFlyout? _contextMenu;

    public void Initialize()
    {
        App.Current.MainWindow?.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                try
                {
                    _contextMenu = CreateContextMenu();

                    _taskbarIcon = new TaskbarIcon
                    {
                        ToolTipText = "People Messaging",
                        ContextMenuMode = ContextMenuMode.SecondWindow,
                        ContextFlyout = _contextMenu
                    };

                    ApplyTaskbarIconAsset();
                    ApplyTaskbarMenuTheme();

                    _taskbarIcon.LeftClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleFlyout);
                    _taskbarIcon.ForceCreate();

                    // Pre-warm the flyout window in the background so opening is instant without black flash
                    _flyoutWindow = new FlyoutWindow();
                    _flyoutWindow.Closed += (s, e) => _flyoutWindow = null;

                    Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                }
                catch
                {
                    // Ignore tray icon initialization failures
                }
            });
    }

    private static string GetIconFilePath(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "Assets", "Icons", fileName);
        if (File.Exists(path)) return path;

        try
        {
            var packagePath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
            path = Path.Combine(packagePath, "Assets", "Icons", fileName);
            if (File.Exists(path)) return path;
        }
        catch { }

        return path;
    }

    private void ApplyTaskbarIconAsset()
    {
        if (_taskbarIcon == null) return;
        var isLightTaskbar = IsSystemUsesLightTheme();
        var iconFileName = isLightTaskbar ? "TrayIcon_Dark.ico" : "TrayIcon_White.ico";
        var iconPath = GetIconFilePath(iconFileName);

        if (File.Exists(iconPath))
        {
            try
            {
                _taskbarIcon.Icon = new System.Drawing.Icon(iconPath);
                return;
            }
            catch { }
        }

        // Fallback to BitmapImage / PNG
        var pngFileName = isLightTaskbar ? "TrayIcon_Dark.png" : "TrayIcon_White.png";
        try
        {
            _taskbarIcon.IconSource = new BitmapImage(new Uri($"ms-appx:///Assets/Icons/{pngFileName}"));
        }
        catch { }
    }

    private void ApplyTaskbarMenuTheme()
    {
        bool isLight = IsSystemUsesLightTheme();

        // 1. Update native Win32 popup menu theme (UxTheme: 2 = ForceDark, 3 = ForceLight)
        try
        {
            SetPreferredAppMode(isLight ? 3 : 2);
        }
        catch { }

        // 2. Update WinUI context menu flyout theme to match the taskbar
        if (_contextMenu != null)
        {
            var targetTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;
            var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
            presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 210.0));
            presenterStyle.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, targetTheme));
            _contextMenu.MenuFlyoutPresenterStyle = presenterStyle;

            foreach (var item in _contextMenu.Items)
            {
                if (item is FrameworkElement fe)
                {
                    fe.RequestedTheme = targetTheme;
                }
            }
        }
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTrayIconTheme();
        });
    }

    private void UpdateTrayIconTheme()
    {
        ApplyTaskbarIconAsset();
        ApplyTaskbarMenuTheme();
    }

    private MenuFlyout CreateContextMenu()
    {
        var menu = new MenuFlyout();

        var openAppItem = new MenuFlyoutItem { Text = "Open Main App", MinWidth = 190.0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(openAppItem, "TrayOpenAppMenuBtn");
        Action openAction = () =>
        {
            App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (App.Current.MainWindow is MainWindow mw)
                {
                    mw.BringToFront();
                }
            });
        };
        openAppItem.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(openAction);
        openAppItem.Click += (s, e) => openAction();

        var settingsItem = new MenuFlyoutItem { Text = "Settings", MinWidth = 190.0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(settingsItem, "TraySettingsMenuBtn");
        Action settingsAction = () =>
        {
            App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (App.Current.MainWindow is MainWindow mw)
                {
                    mw.BringToFront();
                    var navService = App.Current.Services.GetRequiredService<People.Core.Interfaces.INavigationService>();
                    navService.NavigateTo("Settings");
                }
            });
        };
        settingsItem.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(settingsAction);
        settingsItem.Click += (s, e) => settingsAction();

        var quitItem = new MenuFlyoutItem { Text = "Quit", MinWidth = 190.0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(quitItem, "TrayQuitMenuBtn");
        Action quitAction = () =>
        {
            QuitApplication();
        };
        quitItem.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(quitAction);
        quitItem.Click += (s, e) => quitAction();

        menu.Items.Add(openAppItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(quitItem);

        var isLight = IsSystemUsesLightTheme();
        var targetTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;
        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 210.0));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, targetTheme));
        menu.MenuFlyoutPresenterStyle = presenterStyle;

        foreach (var item in menu.Items)
        {
            if (item is FrameworkElement fe)
            {
                fe.RequestedTheme = targetTheme;
            }
        }

        return menu;
    }

    public void QuitApplication()
    {
        App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _flyoutWindow?.DestroyWindow();
                _flyoutWindow = null;
                _taskbarIcon?.Dispose();
                _taskbarIcon = null;

                if (App.Current.MainWindow is MainWindow mw)
                {
                    mw.CloseExplicitly();
                }
                else
                {
                    Microsoft.UI.Xaml.Application.Current.Exit();
                }
            }
            catch
            {
                Environment.Exit(0);
            }
        });
    }

    public void SetBadgeCount(int count)
    {
        if (_taskbarIcon != null)
        {
            _taskbarIcon.ToolTipText = count > 0 ? $"People ({count} unread)" : "People Messaging";
        }
    }

    public void ShowNotification(string title, string message)
    {
        if (_taskbarIcon != null)
        {
            _taskbarIcon.ShowNotification(title, message);
        }
    }

    private POINT GetTrayAnchorPoint()
    {
        if (_taskbarIcon?.TrayIcon != null)
        {
            try
            {
                var nid = new NOTIFYICONIDENTIFIER
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                    hWnd = _taskbarIcon.TrayIcon.WindowHandle,
                    guidItem = _taskbarIcon.TrayIcon.Id
                };
                int hr = Shell_NotifyIconGetRect(ref nid, out RECT rect);
                if (hr == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top)
                {
                    return new POINT
                    {
                        X = (rect.Left + rect.Right) / 2,
                        Y = (rect.Top + rect.Bottom) / 2
                    };
                }
            }
            catch { }
        }

        GetCursorPos(out POINT clickPos);
        return clickPos;
    }

    public void ShowFlyout()
    {
        ShowFlyout(GetTrayAnchorPoint());
    }

    public void ShowFlyout(POINT? anchorPos)
    {
        App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (_flyoutWindow == null)
            {
                _flyoutWindow = new FlyoutWindow();
                _flyoutWindow.Closed += (s, e) => _flyoutWindow = null;
            }

            _flyoutWindow.ShowFlyoutAnimated(anchorPos);
        });
    }

    private static bool IsSystemUsesLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("SystemUsesLightTheme");
            if (val is int intVal) return intVal == 1;
        }
        catch { }
        return false;
    }

    public void HideFlyout()
    {
        App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            _flyoutWindow?.HideFlyoutAnimated();
        });
    }

    private void ToggleFlyout()
    {
        var anchor = GetTrayAnchorPoint();
        if (_flyoutWindow == null || !_flyoutWindow.IsFlyoutVisible)
        {
            ShowFlyout(anchor);
        }
        else
        {
            HideFlyout();
        }
    }
}
