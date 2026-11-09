using H.NotifyIcon;
using Microsoft.UI.Xaml.Media.Imaging;
using People.Core.Interfaces;
using People.Views;
using Microsoft.Extensions.DependencyInjection;

namespace People.Services;

public class TrayIconService : ITrayIconService
{
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
                    var isLightTaskbar = IsSystemUsesLightTheme();
                    var iconColor = isLightTaskbar 
                        ? Microsoft.UI.ColorHelper.FromArgb(255, 26, 26, 26) 
                        : Microsoft.UI.Colors.White;

                    _taskbarIcon = new TaskbarIcon
                    {
                        ToolTipText = "People Messaging",
                        ContextFlyout = CreateContextMenu(),
                        IconSource = new H.NotifyIcon.GeneratedIconSource
                        {
                            Text = "\xE716",
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(iconColor),
                            FontSize = 36,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                        }
                    };

                    _taskbarIcon.LeftClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleFlyout);
                    _taskbarIcon.ForceCreate();
                }
                catch
                {
                    // Ignore tray icon initialization failures
                }
            });
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
        // Badge support requires H.NotifyIcon extensions or custom icon drawing
        // For v1.0 we can ignore or set tooltip
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
        App.Current.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (_flyoutWindow == null)
            {
                _flyoutWindow = new FlyoutWindow();
                _flyoutWindow.Closed += (s, e) => _flyoutWindow = null;
            }

            // Position near the tray icon
            if (_taskbarIcon != null)
            {
                // In a real app we'd get the tray position. For WinUI 3, we can center or position bottom right.
                // H.NotifyIcon.WinUI has WindowExtensions.ShowNearSysTray if configured correctly.
            }

            _flyoutWindow.Activate();
            _flyoutWindow.ForceActivateForeground();
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
            _flyoutWindow?.Close();
            _flyoutWindow = null;
        });
    }

    private void ToggleFlyout()
    {
        if (_flyoutWindow == null)
        {
            ShowFlyout();
        }
        else
        {
            HideFlyout();
        }
    }
}
