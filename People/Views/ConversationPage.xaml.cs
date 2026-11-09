using System;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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

    private Flyout? _quickReactionFlyout;
    private Flyout? _fullEmojiPickerFlyout;
    private Flyout? _reactionDetailsFlyout;
    private People.Controls.QuickReactionPicker? _quickReactionPicker;
    private People.Controls.EmojiReactionPicker? _fullEmojiPicker;
    private People.Controls.ReactionDetailsControl? _reactionDetailsControl;
    private Message? _activeReactionMessage;
    private FrameworkElement? _activeReactionButton;

    private static readonly SvgImageSource _lightDoodleSource = new(new Uri("ms-appx:///Assets/Background/whatsapp_doodle_light.svg"));
    private static readonly SvgImageSource _darkDoodleSource = new(new Uri("ms-appx:///Assets/Background/whatsapp_doodle_dark.svg"));

    public ConversationPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ConversationViewModel>();
        InitializeComponent();

        Loaded += (s, e) =>
        {
            UpdateBubbleMaxWidth(ActualWidth);
            if (this.XamlRoot != null && FlyoutWindow.CurrentFlyoutWindow != null && this.XamlRoot == FlyoutWindow.CurrentFlyoutWindow.Content?.XamlRoot)
            {
                ApplyFlyoutTheming(FlyoutWindow.CurrentFlyoutResources);
            }
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                MessageInputBox?.Focus(FocusState.Programmatic);
            });
        };

        SizeChanged += (s, e) =>
        {
            UpdateBubbleMaxWidth(e.NewSize.Width);
        };

        ActualThemeChanged += (s, e) =>
        {
            if (DoodleCanvas != null)
            {
                UpdateDoodleTiles(DoodleCanvas.ActualWidth, DoodleCanvas.ActualHeight);
            }
            Bindings.Update();
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
            _initialLoadComplete = false;
            ScrollToTarget();

            // Perform post-layout adjustment once virtualization containers begin measuring
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                ScrollToTarget();
            });

            // Allow the layout pass and container virtualization to settle at the true scroll extent
            _ = Task.Run(async () =>
            {
                await Task.Delay(80);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    ScrollToTarget();
                });

                await Task.Delay(180);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    ScrollToTarget();
                    _initialLoadComplete = true;
                });
            });
        };

        ViewModel.OlderMessagesLoaded += (s, anchor) =>
        {
            if (anchor != null)
            {
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
    private bool _initialLoadComplete;

    private void ScrollToTarget(bool forceBottom = false)
    {
        if (ViewModel == null || ViewModel.Messages.Count == 0) return;

        if (_messagesScrollViewer == null)
        {
            _messagesScrollViewer = FindVisualChild<ScrollViewer>(MessagesListView);
        }

        var unreadTarget = forceBottom ? null : ViewModel.Messages.FirstOrDefault(m => m.ShowUnreadSeparator);
        if (unreadTarget != null)
        {
            MessagesListView.ScrollIntoView(unreadTarget, ScrollIntoViewAlignment.Leading);
        }
        else
        {
            if (_messagesScrollViewer != null && _messagesScrollViewer.ScrollableHeight > 0)
            {
                _messagesScrollViewer.ChangeView(null, _messagesScrollViewer.ScrollableHeight, null, disableAnimation: true);
            }
            MessagesListView.ScrollIntoView(ViewModel.Messages[^1], ScrollIntoViewAlignment.Default);
        }
    }

    private void MessagesListView_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _messagesScrollViewer = FindVisualChild<ScrollViewer>(MessagesListView);
        if (_messagesScrollViewer != null)
        {
            _messagesScrollViewer.ViewChanged += MessagesScrollViewer_ViewChanged;
        }
        MessagesListView.PointerWheelChanged += MessagesListView_PointerWheelChanged;
        UpdateBubbleMaxWidth(MessagesListView.ActualWidth > 0 ? MessagesListView.ActualWidth : ActualWidth);
        ScrollToTarget();
    }

    private void UpdateBubbleMaxWidth(double availableWidth)
    {
        if (availableWidth <= 0) return;
        // ListView padding (16*2=32) + Reaction trigger button (28) + column spacing (6) + bubble tail margins (14) = 80
        double computedBubbleWidth = availableWidth - 80;
        double bubbleMax = Math.Clamp(computedBubbleWidth, 180, 480);
        double mediaMax = Math.Clamp(computedBubbleWidth, 160, 320);

        People.Core.Models.Message.SharedMaxBubbleWidth = bubbleMax;
        People.Core.Models.Message.SharedMaxMediaWidth = mediaMax;
    }

    private void MessagesListView_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ViewModel == null || !_initialLoadComplete || ViewModel.IsLoadingOlder || !ViewModel.HasMoreOlderMessages || ViewModel.Messages.Count == 0) return;

        var pointerPoint = e.GetCurrentPoint(MessagesListView);
        int delta = pointerPoint.Properties.MouseWheelDelta;
        if (delta > 0) // Scrolling up
        {
            if (_messagesScrollViewer == null || _messagesScrollViewer.VerticalOffset <= 20)
            {
                _ = ViewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
            }
        }
    }

    private void MessagesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_messagesScrollViewer == null || ViewModel == null) return;

        // When user scrolls near top (within 60px) on an established chat, dynamically load older messages
        if (_initialLoadComplete && _messagesScrollViewer.ScrollableHeight > 100 && _messagesScrollViewer.VerticalOffset <= 60 && !ViewModel.IsLoadingOlder && ViewModel.HasMoreOlderMessages && ViewModel.Messages.Count > 0)
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
        _initialLoadComplete = false;
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

    private void AudioWaveform_SeekRequested(object sender, double newValue)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Message msg && msg.Content != null)
        {
            ViewModel.SeekAudio(msg.Content, newValue);
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

    private void MessageBubbleRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetReactionButtonOpacity(sender as DependencyObject, 1.0);
    }

    private void MessageBubbleRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetReactionButtonOpacity(sender as DependencyObject, 0.0);
    }

    private static bool SetReactionButtonOpacity(DependencyObject? root, double opacity)
    {
        if (root == null) return false;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button btn && btn.Name is "OutgoingReactionButton" or "IncomingReactionButton")
            {
                if (btn.Visibility == Visibility.Visible)
                {
                    btn.Opacity = opacity;
                    return true;
                }
            }
            else if (SetReactionButtonOpacity(child, opacity))
            {
                return true;
            }
        }
        return false;
    }

    private void EmojiPicker_EmojiSelected(object sender, string emoji)
    {
        if (sender is FrameworkElement fe)
        {
            DependencyObject? current = fe;
            Message? targetMsg = fe.DataContext as Message;

            while (current != null)
            {
                if (targetMsg == null && current is FrameworkElement parentFe && parentFe.DataContext is Message m)
                {
                    targetMsg = m;
                }
                if (current is Microsoft.UI.Xaml.Controls.FlyoutPresenter fp && fp.Parent is Microsoft.UI.Xaml.Controls.Primitives.Popup popup)
                {
                    popup.IsOpen = false;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }

            if (targetMsg != null)
            {
                _ = ViewModel.ReactToMessageAsync(targetMsg, emoji);
            }
        }
    }

    private void ReactionEmoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string emoji)
        {
            EmojiPicker_EmojiSelected(btn, emoji);
        }
    }

    private void ReactionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            Message? targetMsg = fe.DataContext as Message;
            if (targetMsg == null)
            {
                DependencyObject? current = fe;
                while (current != null)
                {
                    if (current is FrameworkElement elem && elem.DataContext is Message msg)
                    {
                        targetMsg = msg;
                        break;
                    }
                    current = VisualTreeHelper.GetParent(current);
                }
            }

            if (targetMsg != null)
            {
                _activeReactionMessage = targetMsg;
                _activeReactionButton = fe;
                GetOrCreateQuickReactionFlyout().ShowAt(fe);
            }
        }
    }

    private void ReactionPill_Click(object sender, RoutedEventArgs e)
    {
        Message? targetMsg = null;
        if (sender is People.Controls.ReactionPillControl pill)
        {
            targetMsg = pill.Message;
        }
        else if (sender is FrameworkElement fe)
        {
            targetMsg = fe.DataContext as Message;
            if (targetMsg == null)
            {
                DependencyObject? current = fe;
                while (current != null)
                {
                    if (current is FrameworkElement elem && elem.DataContext is Message msg)
                    {
                        targetMsg = msg;
                        break;
                    }
                    current = VisualTreeHelper.GetParent(current);
                }
            }
        }

        if (targetMsg != null && targetMsg.HasReactions && sender is FrameworkElement anchor)
        {
            var detailsFlyout = GetOrCreateReactionDetailsFlyout();
            _reactionDetailsControl?.SetMessage(targetMsg);
            detailsFlyout.ShowAt(anchor);
        }
    }

    private Flyout GetOrCreateQuickReactionFlyout()
    {
        if (_quickReactionFlyout == null)
        {
            _quickReactionPicker = new People.Controls.QuickReactionPicker();
            _quickReactionPicker.EmojiSelected += (s, emoji) =>
            {
                _quickReactionFlyout?.Hide();
                if (_activeReactionMessage != null)
                {
                    _ = ViewModel.ReactToMessageAsync(_activeReactionMessage, emoji);
                }
            };
            _quickReactionPicker.ExpandRequested += (s, e) =>
            {
                _quickReactionFlyout?.Hide();
                var fullFlyout = GetOrCreateFullEmojiPickerFlyout();
                _fullEmojiPicker?.Reset();
                if (_activeReactionButton != null)
                {
                    fullFlyout.ShowAt(_activeReactionButton);
                }
            };

            var quickFlyoutStyle = new Style(typeof(FlyoutPresenter));
            quickFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            quickFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(22)));
            quickFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
            quickFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(0)));

            _quickReactionFlyout = new Flyout
            {
                Content = _quickReactionPicker,
                Placement = FlyoutPlacementMode.Top,
                FlyoutPresenterStyle = quickFlyoutStyle
            };
        }
        return _quickReactionFlyout;
    }

    private Flyout GetOrCreateFullEmojiPickerFlyout()
    {
        if (_fullEmojiPickerFlyout == null)
        {
            _fullEmojiPicker = new People.Controls.EmojiReactionPicker();
            _fullEmojiPicker.EmojiSelected += (s, emoji) =>
            {
                _fullEmojiPickerFlyout?.Hide();
                if (_activeReactionMessage != null)
                {
                    _ = ViewModel.ReactToMessageAsync(_activeReactionMessage, emoji);
                }
            };
            _fullEmojiPicker.BackRequested += (s, e) =>
            {
                _fullEmojiPickerFlyout?.Hide();
                if (_activeReactionButton != null)
                {
                    GetOrCreateQuickReactionFlyout().ShowAt(_activeReactionButton);
                }
            };

            var fullFlyoutStyle = new Style(typeof(FlyoutPresenter));
            fullFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            fullFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
            fullFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
            fullFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(0)));

            _fullEmojiPickerFlyout = new Flyout
            {
                Content = _fullEmojiPicker,
                Placement = FlyoutPlacementMode.Top,
                FlyoutPresenterStyle = fullFlyoutStyle
            };
        }
        return _fullEmojiPickerFlyout;
    }

    private Flyout GetOrCreateReactionDetailsFlyout()
    {
        if (_reactionDetailsFlyout == null)
        {
            _reactionDetailsControl = new People.Controls.ReactionDetailsControl();
            _reactionDetailsControl.ReactionClicked += (s, reaction) =>
            {
                if (reaction.IsFromMe && _reactionDetailsControl.CurrentMessage is Message msg)
                {
                    _reactionDetailsFlyout?.Hide();
                    _ = ViewModel.ReactToMessageAsync(msg, reaction.Emoji);
                }
            };

            var detailsFlyoutStyle = new Style(typeof(FlyoutPresenter));
            detailsFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
            detailsFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
            detailsFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
            detailsFlyoutStyle.Setters.Add(new Setter(FlyoutPresenter.BorderThicknessProperty, new Thickness(0)));

            _reactionDetailsFlyout = new Flyout
            {
                Content = _reactionDetailsControl,
                Placement = FlyoutPlacementMode.Top,
                FlyoutPresenterStyle = detailsFlyoutStyle
            };
        }
        return _reactionDetailsFlyout;
    }

    public void ApplyFlyoutTheming(Microsoft.UI.Xaml.ResourceDictionary? dict = null)
    {
        var targetDict = dict ?? FlyoutWindow.CurrentFlyoutResources;
        if (targetDict == null) return;

        foreach (var key in Resources.Keys)
        {
            if (Resources[key] is Converters.IScopedResourceConverter conv)
            {
                conv.ScopedResources = targetDict;
            }
        }

        if (!Resources.MergedDictionaries.Contains(targetDict))
        {
            Resources.MergedDictionaries.Add(targetDict);
        }

        if (targetDict.TryGetValue("TextControlBorderBrushFocused", out var focusBrush) && focusBrush is Microsoft.UI.Xaml.Media.Brush b)
        {
            Resources["TextControlBorderBrushFocused"] = b;
            if (MessageInputBox != null)
            {
                MessageInputBox.Resources["TextControlBorderBrushFocused"] = b;
            }
        }
    }
}
