using System;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using People.Core.Interfaces;
using People.Core.Models;
using People.ViewModels;

namespace People.Views;

public sealed partial class ConversationPage : Page
{
    public ConversationViewModel ViewModel { get; }
    private readonly DispatcherTimer _pillHideTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private bool _isPillVisible;

    private static readonly SvgImageSource _lightDoodleSource = new(new Uri("ms-appx:///Assets/Background/whatsapp_doodle_light.svg"));
    private static readonly SvgImageSource _darkDoodleSource = new(new Uri("ms-appx:///Assets/Background/whatsapp_doodle_dark.svg"));

    public ConversationPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ConversationViewModel>();
        InitializeComponent();

        Loaded += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                MessageInputBox?.Focus(FocusState.Programmatic);
            });
        };

        ActualThemeChanged += (s, e) =>
        {
            if (DoodleCanvas != null)
            {
                UpdateDoodleTiles(DoodleCanvas.ActualWidth, DoodleCanvas.ActualHeight);
            }
        };

        _pillHideTimer.Tick += (s, e) =>
        {
            _pillHideTimer.Stop();
            HideFloatingDatePill();
        };

        ViewModel.Messages.CollectionChanged += (s, e) =>
        {
            // Only auto-scroll to the bottom for single live messages, NOT during initial/older batch load
            if (ViewModel.Messages.Count > 0 && e.Action == NotifyCollectionChangedAction.Add)
            {
                if (!ViewModel.IsLoading && !ViewModel.IsLoadingOlder)
                {
                    if (e.NewStartingIndex >= ViewModel.Messages.Count - (e.NewItems?.Count ?? 1))
                    {
                        MessagesListView.ScrollIntoView(ViewModel.Messages[^1]);
                    }
                }
            }
        };

        ViewModel.InitialMessagesLoaded += (s, e) =>
        {
            if (ViewModel.Messages.Count > 0)
            {
                MessagesListView.UpdateLayout();
                var unreadTarget = ViewModel.Messages.FirstOrDefault(m => m.ShowUnreadSeparator);
                if (unreadTarget != null)
                {
                    MessagesListView.ScrollIntoView(unreadTarget, ScrollIntoViewAlignment.Leading);
                }
                else
                {
                    MessagesListView.ScrollIntoView(ViewModel.Messages[^1]);
                }
            }
        };

        ViewModel.OlderMessagesLoaded += (s, anchor) =>
        {
            if (anchor != null)
            {
                MessagesListView.UpdateLayout();
                MessagesListView.ScrollIntoView(anchor, ScrollIntoViewAlignment.Leading);
            }
        };

        ViewModel.ActiveVideoSourceChanged += (s, path) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    MediaViewerPlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
                }
                else
                {
                    MediaViewerPlayer.MediaPlayer?.Pause();
                    MediaViewerPlayer.Source = null;
                }
            });
        };

        KeyDown += (s, e) =>
        {
            if (ViewModel.IsMediaViewerOpen && e.Key == Windows.System.VirtualKey.Escape)
            {
                ViewModel.CloseMediaViewer();
                e.Handled = true;
            }
        };

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ConversationViewModel.IsMediaViewerOpen) && ViewModel.IsMediaViewerOpen)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    try { LightboxEntranceStoryboard?.Begin(); } catch { }
                });
            }
        };
    }

    private ScrollViewer? _messagesScrollViewer;

    private void MessagesListView_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _messagesScrollViewer = FindVisualChild<ScrollViewer>(MessagesListView);
        if (_messagesScrollViewer != null)
        {
            _messagesScrollViewer.ViewChanged += MessagesScrollViewer_ViewChanged;
        }
        MessagesListView.PointerWheelChanged += MessagesListView_PointerWheelChanged;
    }

    private void MessagesListView_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ViewModel == null || ViewModel.IsLoadingOlder || !ViewModel.HasMoreOlderMessages || ViewModel.Messages.Count == 0) return;

        var pointerPoint = e.GetCurrentPoint(MessagesListView);
        int delta = pointerPoint.Properties.MouseWheelDelta;
        if (delta > 0) // Scrolling up
        {
            if (_messagesScrollViewer == null || _messagesScrollViewer.ScrollableHeight <= 50 || _messagesScrollViewer.VerticalOffset <= 20)
            {
                _ = ViewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
            }
        }
    }

    private void MessagesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_messagesScrollViewer == null || ViewModel == null) return;

        // When user scrolls near top (within 150px) or on a sparse list, dynamically load older messages
        if ((_messagesScrollViewer.ScrollableHeight <= 50 || _messagesScrollViewer.VerticalOffset <= 150) && !ViewModel.IsLoadingOlder && ViewModel.HasMoreOlderMessages && ViewModel.Messages.Count > 0)
        {
            _ = ViewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
        }

        UpdateFloatingDatePill();
        ShowFloatingDatePill();
        _pillHideTimer.Stop();
        _pillHideTimer.Start();
    }

    private void ShowFloatingDatePill()
    {
        if (_isPillVisible) return;
        _isPillVisible = true;
        AnimatePill(0, 1.0);
    }

    private void HideFloatingDatePill()
    {
        if (!_isPillVisible) return;
        _isPillVisible = false;
        AnimatePill(-50, 0.0);
    }

    private void AnimatePill(double targetY, double targetOpacity)
    {
        if (FloatingDatePill == null || PillTransform == null) return;
        var animY = new DoubleAnimation
        {
            To = targetY,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var animOp = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(200)
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(animY, PillTransform);
        Storyboard.SetTargetProperty(animY, "Y");
        Storyboard.SetTarget(animOp, FloatingDatePill);
        Storyboard.SetTargetProperty(animOp, "Opacity");
        sb.Children.Add(animY);
        sb.Children.Add(animOp);
        sb.Begin();
    }

    private void MessageInputBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
            bool enterSendsMessage = settingsService.GetValue<bool>("EnterSendsMessage", true);

            if (enterSendsMessage)
            {
                var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                bool isShiftDown = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

                if (!isShiftDown)
                {
                    e.Handled = true;
                    if (ViewModel.SendMessageCommand.CanExecute(null))
                    {
                        ViewModel.SendMessageCommand.Execute(null);
                    }
                }
            }
        }
    }

    private void UpdateFloatingDatePill()
    {
        if (_messagesScrollViewer == null || ViewModel == null || ViewModel.Messages.Count == 0) return;

        if (_messagesScrollViewer.VerticalOffset <= 10)
        {
            var first = ViewModel.Messages.FirstOrDefault();
            if (first != null)
            {
                ViewModel.CurrentChatDateString = ConversationViewModel.FormatChatDate(first.Timestamp);
                return;
            }
        }

        if (_messagesScrollViewer.VerticalOffset >= _messagesScrollViewer.ScrollableHeight - 10)
        {
            var last = ViewModel.Messages.LastOrDefault();
            if (last != null)
            {
                ViewModel.CurrentChatDateString = ConversationViewModel.FormatChatDate(last.Timestamp);
                return;
            }
        }

        double width = MessagesListView.ActualWidth;
        if (width <= 0) return;

        var transform = MessagesListView.TransformToVisual(null);
        if (transform == null) return;

        for (double y = 70; y <= 160; y += 30)
        {
            var hostPoint = transform.TransformPoint(new Windows.Foundation.Point(width / 2, y));
            var hitElements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(hostPoint, MessagesListView);
            bool found = false;
            foreach (var elem in hitElements)
            {
                var container = elem as ListViewItem ?? FindParent<ListViewItem>(elem);
                if (container != null)
                {
                    if (MessagesListView.ItemFromContainer(container) is Message msg)
                    {
                        ViewModel.CurrentChatDateString = ConversationViewModel.FormatChatDate(msg.Timestamp);
                        found = true;
                        break;
                    }
                }
            }
            if (found) break;
        }
    }

    private static T? FindVisualChild<T>(Microsoft.UI.Xaml.DependencyObject parent) where T : Microsoft.UI.Xaml.DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }
            var nested = FindVisualChild<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }
        return null;
    }

    private static T? FindParent<T>(Microsoft.UI.Xaml.DependencyObject child) where T : Microsoft.UI.Xaml.DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T parent) return parent;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void DoodleCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDoodleTiles(e.NewSize.Width, e.NewSize.Height);
    }

    private void UpdateDoodleTiles(double width, double height)
    {
        if (width <= 0 || height <= 0 || DoodleCanvas == null) return;

        DoodleCanvas.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, width, height)
        };

        const double tileWidth = 300.0;
        const double tileHeight = 534.22;

        int cols = (int)Math.Ceiling(width / tileWidth);
        int rows = (int)Math.Ceiling(height / tileHeight);
        int totalNeeded = cols * rows;

        var isLight = ActualTheme == ElementTheme.Light;
        var currentSource = isLight ? _lightDoodleSource : _darkDoodleSource;

        while (DoodleCanvas.Children.Count < totalNeeded)
        {
            var img = new Image
            {
                Width = tileWidth,
                Height = tileHeight,
                Stretch = Stretch.Uniform,
                Source = currentSource,
                IsHitTestVisible = false
            };
            DoodleCanvas.Children.Add(img);
        }

        int idx = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var img = (Image)DoodleCanvas.Children[idx++];
                img.Visibility = Visibility.Visible;
                img.Source = currentSource;
                Canvas.SetLeft(img, c * tileWidth);
                Canvas.SetTop(img, r * tileHeight);
            }
        }

        for (int i = idx; i < DoodleCanvas.Children.Count; i++)
        {
            DoodleCanvas.Children[i].Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is Chat chat)
        {
            ViewModel.LoadChat(chat);
        }
        base.OnNavigatedTo(e);

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            MessageInputBox?.Focus(FocusState.Programmatic);
        });
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.CancelLoading();
        ViewModel.ClearUnreadSeparators();
        base.OnNavigatedFrom(e);
    }

    private void MediaBubble_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
        {
            _ = ViewModel.OpenMediaViewerAsync(msg);
        }
    }

    private void OpenMediaInApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
        {
            _ = ViewModel.OpenMediaViewerAsync(msg);
        }
    }

    private void OpenMediaDefaultApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
        {
            _ = ViewModel.OpenMessageInDefaultAppAsync(msg);
        }
    }

    private void DocumentCard_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
        {
            _ = ViewModel.DownloadAndOpenDocumentAsync(msg);
        }
    }

    private void OpenDocumentDefaultApp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
        {
            _ = ViewModel.DownloadAndOpenDocumentAsync(msg);
        }
    }

    private void SaveMediaAs_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
        {
            _ = ViewModel.SaveMediaAsAsync(msg);
        }
    }

    private void DownloadMedia_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
        {
            _ = ViewModel.DownloadMediaAsync(msg);
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
        {
            ViewModel.CancelDownload(msg);
        }
    }

    private void PlayAudio_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
        {
            _ = ViewModel.PlayAudioAsync(msg);
        }
    }

    private void AudioSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider slider && slider.DataContext is Message msg && msg.Content != null)
        {
            if (slider.FocusState != FocusState.Unfocused)
            {
                ViewModel.SeekAudio(msg.Content, e.NewValue);
            }
        }
    }

    private async void AttachDocument_Click(object sender, RoutedEventArgs e)
    {
        AttachFlyout?.Hide();
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ViewModel.SendDocumentFileAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AttachDocument_Click failed: {ex.Message}");
        }
    }

    private async void AttachMedia_Click(object sender, RoutedEventArgs e)
    {
        AttachFlyout?.Hide();
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".mov");
            picker.FileTypeFilter.Add(".mkv");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ViewModel.SendMediaFileAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AttachMedia_Click failed: {ex.Message}");
        }
    }

    private async void AttachCamera_Click(object sender, RoutedEventArgs e)
    {
        AttachFlyout?.Hide();
        try
        {
            var captureUI = new Windows.Media.Capture.CameraCaptureUI();
            captureUI.PhotoSettings.Format = Windows.Media.Capture.CameraCaptureUIPhotoFormat.Jpeg;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(captureUI, hwnd);

            var file = await captureUI.CaptureFileAsync(Windows.Media.Capture.CameraCaptureUIMode.PhotoOrVideo);
            if (file != null)
            {
                await ViewModel.SendMediaFileAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AttachCamera_Click failed: {ex.Message}");
        }
    }

    private async void AttachAudio_Click(object sender, RoutedEventArgs e)
    {
        AttachFlyout?.Hide();
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".m4a");
            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".ogg");
            picker.FileTypeFilter.Add(".opus");
            picker.FileTypeFilter.Add(".flac");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ViewModel.SendAudioFileAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AttachAudio_Click failed: {ex.Message}");
        }
    }
}
