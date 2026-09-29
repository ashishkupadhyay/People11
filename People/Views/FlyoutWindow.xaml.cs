using Microsoft.UI.Xaml;

using Microsoft.UI.Windowing;
using Microsoft.UI;

namespace People.Views;

public sealed partial class FlyoutWindow : Window
{
    public FlyoutWindow()
    {
        InitializeComponent();
        
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        
        appWindow.Resize(new Windows.Graphics.SizeInt32(380, 520));

        // Position at bottom right (above taskbar)
        var displayArea = DisplayArea.Primary;
        var workArea = displayArea.WorkArea;
        
        // Offset by 24 pixels from the edges
        int x = workArea.Width - 380 - 24;
        int y = workArea.Height - 520 - 24;
        
        appWindow.Move(new Windows.Graphics.PointInt32(x, y));

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(true, false);
        }

        ContentFrame.Navigate(typeof(ChatListPage));
        
        this.Activated += OnActivated;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            this.Close();
        }
    }

    private void OnEscapeInvoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        this.Close();
    }

    private void WhatsAppBtn_Click(object sender, RoutedEventArgs e)
    {
        // For now, just bring the main app window to the foreground
        App.Current.MainWindow?.Activate();
        this.Close();
    }
}
