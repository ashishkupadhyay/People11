using System;
using System.IO;
using System.Runtime.InteropServices;
using H.NotifyIcon;
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

    // Using FlyoutWindow.POINT

    private TaskbarIcon? _taskbarIcon;
    private FlyoutWindow? _flyoutWindow;

    public void Initialize()
    {
        App.Current.MainWindow?.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                try
                {
                    _taskbarIcon = new TaskbarIcon
                    {
                        ToolTipText = "People Messaging",
                        ContextFlyout = CreateContextMenu()
                    };

                    ApplyTaskbarIconAsset();

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
    }

    private Microsoft.UI.Xaml.Controls.MenuFlyout CreateContextMenu()
    {
        var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();

        var openAppItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Open Main App" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(openAppItem, "TrayOpenAppMenuBtn");
        openAppItem.Click += (s, e) => App.Current.MainWindow?.Activate();
        
        var settingsItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Settings" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(settingsItem, "TraySettingsMenuBtn");
        settingsItem.Click += (s, e) => {
            App.Current.MainWindow?.Activate();
            var navService = App.Current.Services.GetRequiredService<People.Core.Interfaces.INavigationService>();
            navService.NavigateTo("Settings");
        };

        var quitItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Quit" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(quitItem, "TrayQuitMenuBtn");
        quitItem.Click += (s, e) => {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _flyoutWindow?.DestroyWindow();
            _flyoutWindow = null;
            _taskbarIcon?.Dispose();
            Microsoft.UI.Xaml.Application.Current.Exit();
        };

        menu.Items.Add(openAppItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator());
        menu.Items.Add(quitItem);

        return menu;
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

    public void ShowFlyout()
    {
        GetCursorPos(out POINT clickPos);
        ShowFlyout(clickPos);
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
        GetCursorPos(out POINT clickPos);
        if (_flyoutWindow == null || !_flyoutWindow.IsFlyoutVisible)
        {
            ShowFlyout(clickPos);
        }
        else
        {
            HideFlyout();
        }
    }
}
