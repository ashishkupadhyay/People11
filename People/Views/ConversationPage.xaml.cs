using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

    private MenuFlyout? _textContextMenu;
    private MenuFlyout? _mediaContextMenu;
    private MenuFlyout? _documentContextMenu;
    private MenuFlyout? _revokedContextMenu;
    private Message? _contextMenuTargetMessage;

    private static readonly SvgImageSource _lightDoodleSource = new(new Uri("ms-appx:///Assets/Background/whatsapp_doodle_light.svg"));
    private static readonly SvgImageSource _darkDoodleSource = new(new Uri("ms-appx:///Assets/Background/whatsapp_doodle_dark.svg"));

    public ConversationPage()
    {
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
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
            // Auto-scroll to bottom only for new bottom messages when already near bottom or when sending outgoing
            if (ViewModel.Messages.Count > 0 && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                if (!ViewModel.IsLoading && !ViewModel.IsLoadingOlder &&
                    e.NewStartingIndex >= ViewModel.Messages.Count - (e.NewItems?.Count ?? 1))
                {
                    bool addedIsOutgoing = e.NewItems?.OfType<Message>().Any(m => m.IsOutgoing) == true;
                    bool isNearBottom = _messagesScrollViewer == null ||
                        (_messagesScrollViewer.ScrollableHeight - _messagesScrollViewer.VerticalOffset) <= 160;
                    if (isNearBottom || addedIsOutgoing)
                    {
                        MessagesListView.ScrollIntoView(ViewModel.Messages[^1]);
                    }
                }
            }
        };

        ViewModel.InitialMessagesLoaded += (s, e) =>
        {
            _initialLoadCts?.Cancel();
            _initialLoadCts?.Dispose();
            _initialLoadCts = new CancellationTokenSource();
            var token = _initialLoadCts.Token;

            _initialLoadComplete = false;
            _isRestoringScrollAnchor = false;
            ScrollToTarget();

            // Perform post-layout adjustment once virtualization containers begin measuring
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (token.IsCancellationRequested) return;
                ScrollToTarget();
            });

            // Allow the layout pass and container virtualization to settle at the true scroll extent
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(80, token);
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        if (token.IsCancellationRequested) return;
                        ScrollToTarget();
                    });

                    await Task.Delay(180, token);
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        if (token.IsCancellationRequested) return;
                        ScrollToTarget();
                        _lastScrollVerticalOffset = _messagesScrollViewer?.VerticalOffset ?? 0;
                        _initialLoadComplete = true;
                    });
                }
                catch (OperationCanceledException) { }
            }, token);
        };

        ViewModel.OlderMessagesLoaded += (s, anchor) =>
        {
            if (anchor != null)
            {
                _isRestoringScrollAnchor = true;
                MessagesListView.ScrollIntoView(anchor, ScrollIntoViewAlignment.Leading);
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
                {
                    try
                    {
                        await Task.Delay(120);
                        _lastScrollVerticalOffset = _messagesScrollViewer?.VerticalOffset ?? 0;
                    }
                    finally
                    {
                        _isRestoringScrollAnchor = false;
                    }
                });
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
            if (WhiteBoardOverlay.Visibility == Visibility.Visible && e.Key == Windows.System.VirtualKey.Escape)
            {
                WhiteBoardOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }

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
    private bool _isRestoringScrollAnchor;
    private double _lastScrollVerticalOffset;
    private CancellationTokenSource? _initialLoadCts;

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
        if (ViewModel == null || !_initialLoadComplete || _isRestoringScrollAnchor || ViewModel.IsLoadingOlder || !ViewModel.HasMoreOlderMessages || ViewModel.Messages.Count == 0) return;

        var pointerPoint = e.GetCurrentPoint(MessagesListView);
        int delta = pointerPoint.Properties.MouseWheelDelta;
        if (delta > 0) // Scrolling up
        {
            if (_messagesScrollViewer == null || _messagesScrollViewer.VerticalOffset <= 60)
            {
                _ = ViewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
            }
        }
    }

    private void MessagesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_messagesScrollViewer == null || ViewModel == null) return;

        double currentOffset = _messagesScrollViewer.VerticalOffset;
        bool scrolledUp = currentOffset < _lastScrollVerticalOffset;
        _lastScrollVerticalOffset = currentOffset;

        // Trigger older history load only on an active upward scroll near the top, never on anchor restore/layout collapse
        if (_initialLoadComplete && !_isRestoringScrollAnchor && scrolledUp &&
            _messagesScrollViewer.ScrollableHeight > 100 && currentOffset <= 60 &&
            !ViewModel.IsLoadingOlder && ViewModel.HasMoreOlderMessages && ViewModel.Messages.Count > 0)
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
        if (GetMessageFromSender(sender) is Message msg)
        {
            _ = ViewModel.OpenMediaViewerAsync(msg);
        }
    }

    private void OpenMediaDefaultApp_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromSender(sender) is Message msg)
        {
            _ = ViewModel.OpenMessageInDefaultAppAsync(msg);
        }
    }

    private void DocumentCard_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (GetMessageFromSender(sender) is Message msg)
        {
            _ = ViewModel.DownloadAndOpenDocumentAsync(msg);
        }
    }

    private void OpenDocumentDefaultApp_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromSender(sender) is Message msg)
        {
            _ = ViewModel.DownloadAndOpenDocumentAsync(msg);
        }
    }

    private void SaveMediaAs_Click(object sender, RoutedEventArgs e)
    {
        if (GetMessageFromSender(sender) is Message msg)
        {
            _ = ViewModel.SaveMediaAsAsync(msg);
        }
    }

    private Message? GetMessageFromSender(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is Message msg)
            return msg;
        return _contextMenuTargetMessage;
    }

    private void MessageBubble_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.Handled) return;

        if (sender is FrameworkElement fe && fe.DataContext is Message msg)
        {
            _contextMenuTargetMessage = msg;
            MenuFlyout flyout;
            if (msg.IsRevoked)
            {
                flyout = GetOrCreateRevokedContextMenu();
            }
            else if (msg.Content?.IsImage == true || msg.Content?.IsVideo == true)
            {
                flyout = GetOrCreateMediaContextMenu();
            }
            else if (msg.Content?.IsDocument == true)
            {
                flyout = GetOrCreateDocumentContextMenu();
            }
            else
            {
                flyout = GetOrCreateTextContextMenu();
            }

            args.Handled = true;
            if (args.TryGetPosition(fe, out var pt))
            {
                flyout.ShowAt(fe, new FlyoutShowOptions { Position = pt });
            }
            else
            {
                flyout.ShowAt(fe);
            }
        }
    }

    private MenuFlyout GetOrCreateRevokedContextMenu()
    {
        if (_revokedContextMenu == null)
        {
            _revokedContextMenu = new MenuFlyout();

            var deleteItem = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteItem.Click += MessageDelete_Click;
            _revokedContextMenu.Items.Add(deleteItem);

            var infoItem = new MenuFlyoutItem { Text = "Info", Icon = new FontIcon { Glyph = "\uE946" } };
            infoItem.Click += MessageInfo_Click;
            _revokedContextMenu.Items.Add(infoItem);
        }
        return _revokedContextMenu;
    }

    private MenuFlyout GetOrCreateTextContextMenu()
    {
        if (_textContextMenu == null)
        {
            _textContextMenu = new MenuFlyout();

            var replyItem = new MenuFlyoutItem { Text = "Reply", Icon = new FontIcon { Glyph = "\uE97A" } };
            replyItem.Click += MessageReply_Click;
            _textContextMenu.Items.Add(replyItem);

            var forwardItem = new MenuFlyoutItem { Text = "Forward", Icon = new FontIcon { Glyph = "\uEE35" } };
            forwardItem.Click += MessageForward_Click;
            _textContextMenu.Items.Add(forwardItem);

            var copyItem = new MenuFlyoutItem { Text = "Copy", Icon = new FontIcon { Glyph = "\uE8C8" } };
            copyItem.Click += MessageCopy_Click;
            _textContextMenu.Items.Add(copyItem);

            var deleteItem = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteItem.Click += MessageDelete_Click;
            _textContextMenu.Items.Add(deleteItem);

            var infoItem = new MenuFlyoutItem { Text = "Info", Icon = new FontIcon { Glyph = "\uE946" } };
            infoItem.Click += MessageInfo_Click;
            _textContextMenu.Items.Add(infoItem);
        }
        return _textContextMenu;
    }

    private MenuFlyout GetOrCreateMediaContextMenu()
    {
        if (_mediaContextMenu == null)
        {
            _mediaContextMenu = new MenuFlyout();

            var replyItem = new MenuFlyoutItem { Text = "Reply", Icon = new FontIcon { Glyph = "\uE97A" } };
            replyItem.Click += MessageReply_Click;
            _mediaContextMenu.Items.Add(replyItem);

            var openItem = new MenuFlyoutItem { Text = "Open", Icon = new FontIcon { Glyph = "\uE93A" } };
            openItem.Click += OpenMediaInApp_Click;
            _mediaContextMenu.Items.Add(openItem);

            var openDefaultItem = new MenuFlyoutItem { Text = "Open with default app", Icon = new FontIcon { Glyph = "\uE7AC" } };
            openDefaultItem.Click += OpenMediaDefaultApp_Click;
            _mediaContextMenu.Items.Add(openDefaultItem);

            var saveAsItem = new MenuFlyoutItem { Text = "Save as", Icon = new FontIcon { Glyph = "\uE74E" } };
            saveAsItem.Click += SaveMediaAs_Click;
            _mediaContextMenu.Items.Add(saveAsItem);

            var forwardItem = new MenuFlyoutItem { Text = "Forward", Icon = new FontIcon { Glyph = "\uEE35" } };
            forwardItem.Click += MessageForward_Click;
            _mediaContextMenu.Items.Add(forwardItem);

            var shareItem = new MenuFlyoutItem { Text = "Share", Icon = new FontIcon { Glyph = "\uE72D" } };
            shareItem.Click += MessageShare_Click;
            _mediaContextMenu.Items.Add(shareItem);

            var copyItem = new MenuFlyoutItem { Text = "Copy", Icon = new FontIcon { Glyph = "\uE8C8" } };
            copyItem.Click += MessageCopy_Click;
            _mediaContextMenu.Items.Add(copyItem);

            var deleteItem = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteItem.Click += MessageDelete_Click;
            _mediaContextMenu.Items.Add(deleteItem);

            var infoItem = new MenuFlyoutItem { Text = "Info", Icon = new FontIcon { Glyph = "\uE946" } };
            infoItem.Click += MessageInfo_Click;
            _mediaContextMenu.Items.Add(infoItem);
        }
        return _mediaContextMenu;
    }

    private MenuFlyout GetOrCreateDocumentContextMenu()
    {
        if (_documentContextMenu == null)
        {
            _documentContextMenu = new MenuFlyout();

            var replyItem = new MenuFlyoutItem { Text = "Reply", Icon = new FontIcon { Glyph = "\uE97A" } };
            replyItem.Click += MessageReply_Click;
            _documentContextMenu.Items.Add(replyItem);

            var openDefaultItem = new MenuFlyoutItem { Text = "Open with default app", Icon = new FontIcon { Glyph = "\uE7AC" } };
            openDefaultItem.Click += OpenDocumentDefaultApp_Click;
            _documentContextMenu.Items.Add(openDefaultItem);

            var saveAsItem = new MenuFlyoutItem { Text = "Save as", Icon = new FontIcon { Glyph = "\uE74E" } };
            saveAsItem.Click += SaveMediaAs_Click;
            _documentContextMenu.Items.Add(saveAsItem);

            var forwardItem = new MenuFlyoutItem { Text = "Forward", Icon = new FontIcon { Glyph = "\uEE35" } };
            forwardItem.Click += MessageForward_Click;
            _documentContextMenu.Items.Add(forwardItem);

            var shareItem = new MenuFlyoutItem { Text = "Share", Icon = new FontIcon { Glyph = "\uE72D" } };
            shareItem.Click += MessageShare_Click;
            _documentContextMenu.Items.Add(shareItem);

            var copyItem = new MenuFlyoutItem { Text = "Copy", Icon = new FontIcon { Glyph = "\uE8C8" } };
            copyItem.Click += MessageCopy_Click;
            _documentContextMenu.Items.Add(copyItem);

            var deleteItem = new MenuFlyoutItem { Text = "Delete", Icon = new FontIcon { Glyph = "\uE74D" } };
            deleteItem.Click += MessageDelete_Click;
            _documentContextMenu.Items.Add(deleteItem);

            var infoItem = new MenuFlyoutItem { Text = "Info", Icon = new FontIcon { Glyph = "\uE946" } };
            infoItem.Click += MessageInfo_Click;
            _documentContextMenu.Items.Add(infoItem);
        }
        return _documentContextMenu;
    }

    private void MessageReply_Click(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg == null) return;

        ViewModel.SetReplyingTo(msg);
        MessageInputBox?.Focus(FocusState.Programmatic);
    }

    private void MessageCopy_Click(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg == null) return;

        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;

            string textToCopy = msg.Content?.DisplayCaptionOrText ?? msg.Content?.Text ?? string.Empty;

            string? filePath = msg.Content?.LocalFilePath ?? msg.Content?.EffectiveImagePath;
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    var fileTask = Windows.Storage.StorageFile.GetFileFromPathAsync(filePath).AsTask();
                    fileTask.Wait(500);
                    if (fileTask.IsCompletedSuccessfully && fileTask.Result != null)
                    {
                        package.SetStorageItems(new[] { fileTask.Result });
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(textToCopy) && msg.Content != null)
            {
                if (msg.Content.IsImage) textToCopy = "Photo";
                else if (msg.Content.IsVideo) textToCopy = "Video";
                else if (msg.Content.IsAudio) textToCopy = "Audio";
                else if (msg.Content.IsDocument) textToCopy = msg.Content.DisplayName ?? "Document";
            }

            package.SetText(textToCopy);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MessageCopy_Click error: {ex.Message}");
        }
    }

    private void ApplyDialogTransparency(ContentDialog dialog)
    {
        dialog.CornerRadius = new CornerRadius(12);
        dialog.BorderThickness = new Thickness(1);
        dialog.RequestedTheme = this.ActualTheme;

        // Apply active platform accent (or system accent fallback) to dialog's scoped resources
        var themeService = App.Current.Services.GetService<People.Core.Interfaces.IThemeService>();
        var platformId = ViewModel?.CurrentChat?.PlatformId ?? "WhatsApp";
        themeService?.ApplyAccentToResourceDictionary(dialog.Resources, platformId);

        bool isDark = this.ActualTheme == ElementTheme.Dark || (this.ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        if (Application.Current.Resources.TryGetValue("AcrylicInAppFillColorDefaultBrush", out var acrylicObj) && acrylicObj is Brush acrylicBrush)
        {
            dialog.Background = acrylicBrush;
        }
        else
        {
            dialog.Background = new AcrylicBrush
            {
                TintColor = isDark ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32) : Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243),
                TintOpacity = 0.75,
                FallbackColor = isDark ? Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32) : Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243)
            };
        }

        if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var borderObj) && borderObj is Brush borderBrush)
        {
            dialog.BorderBrush = borderBrush;
        }
    }

    private async void MessageDelete_Click(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg == null) return;

        if (this.XamlRoot == null) return;

        // WhatsApp Delete Eligibility:
        // "Delete for everyone" is allowed for non-revoked outgoing messages within 60 hours (~2.5 days) that are not sending/failed.
        bool canDeleteForEveryone = !msg.IsRevoked &&
                                    msg.IsOutgoing &&
                                    msg.Status != MessageStatus.Sending &&
                                    msg.Status != MessageStatus.Failed &&
                                    (DateTimeOffset.UtcNow - msg.Timestamp).TotalHours <= 60.0;

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Delete message?",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        ApplyDialogTransparency(dialog);

        if (canDeleteForEveryone)
        {
            dialog.Content = "Are you sure you want to delete this message? You can delete it for everyone or just for yourself.";
            dialog.PrimaryButtonText = "Delete for everyone";
            dialog.SecondaryButtonText = "Delete for me";

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteMessageAsync(msg, forEveryone: true);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await ViewModel.DeleteMessageAsync(msg, forEveryone: false);
            }
        }
        else
        {
            dialog.Content = "Are you sure you want to delete this message from your chat history?";
            dialog.PrimaryButtonText = "Delete for me";

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteMessageAsync(msg, forEveryone: false);
            }
        }
    }

    private async void MessageInfo_Click(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg == null) return;

        if (this.XamlRoot == null) return;

        var panel = new StackPanel { Spacing = 10, MinWidth = 280 };

        void AddInfoRow(string label, string value)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
                FontSize = 13
            };
            Grid.SetColumn(lbl, 0);

            var val = new TextBlock
            {
                Text = value,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(val, 1);

            row.Children.Add(lbl);
            row.Children.Add(val);
            panel.Children.Add(row);
        }

        string typeStr = msg.Content?.Type.ToString() ?? "Text";
        AddInfoRow("Type", typeStr);

        string senderStr = msg.IsOutgoing ? "You" : (!string.IsNullOrWhiteSpace(msg.SenderName) ? msg.SenderName : "Contact");
        AddInfoRow("From", senderStr);

        if (msg.Timestamp != DateTimeOffset.MinValue && msg.Timestamp != DateTimeOffset.UnixEpoch)
        {
            AddInfoRow("Date", msg.Timestamp.ToLocalTime().ToString("f"));
        }

        if (msg.IsOutgoing)
        {
            AddInfoRow("Status", msg.Status.ToString());
        }

        if (msg.IsEdited)
        {
            AddInfoRow("Edited", "Yes");
        }

        if (msg.IsRevoked)
        {
            AddInfoRow("Status", "Deleted");
        }

        if (msg.Content?.IsMedia == true || msg.Content?.IsDocument == true)
        {
            if (!string.IsNullOrEmpty(msg.Content.DisplayName))
                AddInfoRow("File", msg.Content.DisplayName);
            if (!string.IsNullOrEmpty(msg.Content.FormattedSizeAndType))
                AddInfoRow("Size / Type", msg.Content.FormattedSizeAndType);
            if (!string.IsNullOrEmpty(msg.Content.FormattedDuration))
                AddInfoRow("Duration", msg.Content.FormattedDuration);
        }

        if (msg.Reactions != null && msg.Reactions.Count > 0)
        {
            var reactionsSummary = string.Join("  ", msg.Reactions.Select(r => $"{r.Emoji} {(r.IsFromMe ? "You" : (!string.IsNullOrWhiteSpace(r.SenderName) ? r.SenderName : "User"))}"));
            AddInfoRow("Reactions", reactionsSummary);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Message info",
            Content = panel,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        ApplyDialogTransparency(dialog);

        await dialog.ShowAsync();
    }

    private async void MessageForward_Click(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg == null) return;

        if (this.XamlRoot == null) return;

        var chatListVm = App.Current.Services.GetService<ChatListViewModel>();
        var targetChats = new List<Chat>();
        var seenChatIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Gather all active conversations
        if (chatListVm?.Chats != null)
        {
            foreach (var chat in chatListVm.Chats)
            {
                if (chat != null && !string.IsNullOrEmpty(chat.Id) && seenChatIds.Add(chat.Id))
                {
                    targetChats.Add(chat);
                }
            }
        }

        // 2. Also gather contacts without an active conversation
        if (chatListVm?.FilteredContacts != null)
        {
            foreach (var contact in chatListVm.FilteredContacts)
            {
                if (contact != null && !string.IsNullOrEmpty(contact.Id) && !seenChatIds.Contains(contact.Id))
                {
                    var chat = chatListVm.GetOrCreateChatForContact(contact);
                    if (chat != null && seenChatIds.Add(chat.Id))
                    {
                        targetChats.Add(chat);
                    }
                }
            }
        }

        if (targetChats.Count == 0)
        {
            var noChatsDialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Forward message",
                Content = "No conversations or contacts available to forward this message to.",
                CloseButtonText = "Close"
            };
            ApplyDialogTransparency(noChatsDialog);
            await noChatsDialog.ShowAsync();
            return;
        }

        var filteredChats = new System.Collections.ObjectModel.ObservableCollection<Chat>(targetChats);

        // Dialog content layout
        var contentStack = new StackPanel { Spacing = 12, Width = 360 };

        // Search Box for contacts / chats
        var searchBox = new AutoSuggestBox
        {
            PlaceholderText = "Search contacts...",
            QueryIcon = new SymbolIcon(Symbol.Find),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // ListView for target selection
        var listView = new ListView
        {
            ItemsSource = filteredChats,
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 320,
            MinHeight = 180,
            Width = 360,
            IsItemClickEnabled = true
        };

        // Plain and simple contact list: avatar and title only
        try
        {
            var template = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
              xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
    <Grid Padding=""4,6"" ColumnSpacing=""12"">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width=""Auto"" />
            <ColumnDefinition Width=""*"" />
        </Grid.ColumnDefinitions>
        <PersonPicture Grid.Column=""0"" Width=""38"" Height=""38"" VerticalAlignment=""Center""
                       DisplayName=""{Binding Title}"" />
        <TextBlock Grid.Column=""1"" Text=""{Binding Title}"" FontWeight=""Medium"" FontSize=""14"" VerticalAlignment=""Center"" TextTrimming=""CharacterEllipsis"" />
    </Grid>
</DataTemplate>");
            listView.ItemTemplate = template;
        }
        catch { }

        listView.ContainerContentChanging += (lv, args) =>
        {
            if (args.Item is Chat c && args.ItemContainer?.ContentTemplateRoot is Grid grid)
            {
                if (grid.Children.Count > 0 && grid.Children[0] is PersonPicture pp)
                {
                    pp.DisplayName = c.Title;
                    if (!string.IsNullOrEmpty(c.PictureUrl))
                    {
                        try
                        {
                            pp.ProfilePicture = new BitmapImage(new Uri(c.PictureUrl));
                        }
                        catch { }
                    }
                    else
                    {
                        pp.ProfilePicture = null;
                    }
                }
            }
        };

        var emptyText = new TextBlock
        {
            Text = "No contacts found",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 40, 0, 40)
        };

        var listContainer = new Grid();
        listContainer.Children.Add(listView);
        listContainer.Children.Add(emptyText);

        contentStack.Children.Add(searchBox);
        contentStack.Children.Add(listContainer);

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Forward message",
            Content = contentStack,
            PrimaryButtonText = "Forward",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        ApplyDialogTransparency(dialog);

        if (filteredChats.Count > 0)
        {
            listView.SelectedIndex = 0;
            dialog.IsPrimaryButtonEnabled = true;
        }
        else
        {
            dialog.IsPrimaryButtonEnabled = false;
        }

        // Real-time search filtering
        searchBox.TextChanged += (s, args) =>
        {
            var query = searchBox.Text.Trim();
            filteredChats.Clear();
            IEnumerable<Chat> matches = targetChats;
            if (!string.IsNullOrEmpty(query))
            {
                matches = targetChats.Where(c =>
                    c.Title != null && c.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var m in matches)
            {
                filteredChats.Add(m);
            }

            if (filteredChats.Count > 0)
            {
                listView.SelectedIndex = 0;
                emptyText.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = true;
            }
            else
            {
                emptyText.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = false;
            }
        };

        // Selection handling
        listView.SelectionChanged += (s, args) =>
        {
            dialog.IsPrimaryButtonEnabled = listView.SelectedItem is Chat;
        };

        // Click to select
        listView.ItemClick += (s, args) =>
        {
            if (args.ClickedItem is Chat selected)
            {
                listView.SelectedItem = selected;
                dialog.IsPrimaryButtonEnabled = true;
            }
        };

        // Double-tap shortcut to forward immediately
        listView.DoubleTapped += (s, args) =>
        {
            if (listView.SelectedItem is Chat target)
            {
                dialog.Hide();
                _ = ViewModel.ForwardMessageAsync(msg, target);
            }
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && listView.SelectedItem is Chat targetChat)
        {
            await ViewModel.ForwardMessageAsync(msg, targetChat);
        }
    }

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid _dtm_iid = new Guid(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    private static Windows.ApplicationModel.DataTransfer.DataTransferManager? _dataTransferManager;
    private static IntPtr _lastDataTransferManagerHwnd = IntPtr.Zero;
    private static Message? _currentSharingMessage;

    private void EnsureDataTransferManager(IntPtr hWnd)
    {
        if (_dataTransferManager == null || _lastDataTransferManagerHwnd != hWnd)
        {
            var interop = Windows.ApplicationModel.DataTransfer.DataTransferManager.As<IDataTransferManagerInterop>();
            IntPtr result = interop.GetForWindow(hWnd, _dtm_iid);
            _dataTransferManager = WinRT.MarshalInterface<Windows.ApplicationModel.DataTransfer.DataTransferManager>.FromAbi(result);
            _lastDataTransferManagerHwnd = hWnd;
            _dataTransferManager.DataRequested += DataTransferManager_DataRequested;
        }
    }

    private async void DataTransferManager_DataRequested(Windows.ApplicationModel.DataTransfer.DataTransferManager sender, Windows.ApplicationModel.DataTransfer.DataRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        try
        {
            var msg = _currentSharingMessage;
            if (msg == null)
            {
                args.Request.FailWithDisplayText("No content to share.");
                return;
            }

            string? filePath = msg.Content?.LocalFilePath ?? msg.Content?.EffectiveImagePath;
            string? text = msg.Content?.DisplayCaptionOrText;
            bool hasFile = !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
            bool hasText = !string.IsNullOrWhiteSpace(text);

            if (!hasFile && !hasText)
            {
                args.Request.FailWithDisplayText("This media file is not downloaded yet. Please download it first to share.");
                return;
            }

            string title = !string.IsNullOrWhiteSpace(msg.Content?.DisplayName)
                ? msg.Content.DisplayName
                : (msg.Content?.IsImage == true ? "Photo" : (msg.Content?.IsVideo == true ? "Video" : (msg.Content?.IsDocument == true ? "Document" : "Shared message")));

            args.Request.Data.Properties.Title = title;
            args.Request.Data.Properties.Description = "Shared from People";

            if (hasFile)
            {
                try
                {
                    // Copy to user Temp directory so all external share targets have unrestricted read access across sandbox boundaries
                    string shareFilePath = filePath!;
                    try
                    {
                        var tempDir = Path.Combine(Path.GetTempPath(), "People", "Share");
                        Directory.CreateDirectory(tempDir);
                        var tempPath = Path.Combine(tempDir, Path.GetFileName(filePath!));
                        if (!File.Exists(tempPath) || new FileInfo(tempPath).Length != new FileInfo(filePath!).Length)
                        {
                            File.Copy(filePath!, tempPath, true);
                        }
                        shareFilePath = tempPath;
                    }
                    catch { }

                    var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(shareFilePath);
                    args.Request.Data.SetStorageItems(new[] { storageFile });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load StorageFile for share: {ex.Message}");
                }
            }

            if (hasText && !string.IsNullOrWhiteSpace(text))
            {
                args.Request.Data.SetText(text);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DataRequested error: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void MessageShare_Click(object sender, RoutedEventArgs e)
    {
        var msg = GetMessageFromSender(sender);
        if (msg == null) return;

        // If the message contains media or document that is not downloaded, download it first!
        if (msg.Content != null && (msg.Content.IsImage || msg.Content.IsVideo || msg.Content.IsAudio || msg.Content.IsDocument))
        {
            var downloadedPath = await ViewModel.EnsureMediaDownloadedAsync(msg);
            if (string.IsNullOrEmpty(downloadedPath) || !File.Exists(downloadedPath))
            {
                // Download was not completed or failed; avoid showing empty share sheet
                return;
            }
        }

        IntPtr hWnd = IntPtr.Zero;
        if (App.Current.MainWindow != null)
        {
            hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow);
        }
        if (hWnd == IntPtr.Zero)
        {
            hWnd = GetActiveWindow();
        }
        if (hWnd == IntPtr.Zero) return;

        _currentSharingMessage = msg;
        EnsureDataTransferManager(hWnd);

        try
        {
            var interop = Windows.ApplicationModel.DataTransfer.DataTransferManager.As<IDataTransferManagerInterop>();
            interop.ShowShareUIForWindow(hWnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowShareUIForWindow failed: {ex.Message}");
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

    private void AttachWhiteBoard_Click(object sender, RoutedEventArgs e)
    {
        AttachFlyout?.Hide();
        WhiteBoard.Reset();
        WhiteBoardOverlay.Visibility = Visibility.Visible;
    }

    private void WhiteBoard_CancelClicked(object sender, EventArgs e)
    {
        WhiteBoardOverlay.Visibility = Visibility.Collapsed;
    }

    private async void WhiteBoard_SendImageClicked(object sender, string imagePath)
    {
        WhiteBoardOverlay.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            await ViewModel.SendMediaFileAsync(imagePath);
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
        bool isFlyout = (dict != null && ReferenceEquals(dict, FlyoutWindow.CurrentFlyoutResources))
            || (this.XamlRoot != null &&
                FlyoutWindow.CurrentFlyoutWindow != null &&
                this.XamlRoot == FlyoutWindow.CurrentFlyoutWindow.Content?.XamlRoot);
        if (isFlyout)
        {
            var transparentBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            this.Background = transparentBrush;
            if (ConversationHeaderBorder != null)
            {
                ConversationHeaderBorder.Background = transparentBrush;
            }
        }

        var targetDict = dict ?? FlyoutWindow.CurrentFlyoutResources;
        if (targetDict == null) return;

        foreach (var key in Resources.Keys)
        {
            if (Resources[key] is Converters.IScopedResourceConverter conv)
            {
                conv.ScopedResources = targetDict;
            }
        }

        if (targetDict.TryGetValue("TextControlBorderBrushFocused", out var focusBrush) && focusBrush is Microsoft.UI.Xaml.Media.Brush b)
        {
            Resources["TextControlBorderBrushFocused"] = b;
            Resources["TextControlBorderThemeThicknessFocused"] = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 2);
            if (MessageInputBox != null)
            {
                MessageInputBox.Resources["TextControlBorderBrushFocused"] = b;
                MessageInputBox.Resources["TextControlBorderThemeThicknessFocused"] = new Microsoft.UI.Xaml.Thickness(0, 0, 0, 2);
            }
        }
    }
}
