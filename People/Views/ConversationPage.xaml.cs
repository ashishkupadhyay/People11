using System;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
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

    public ConversationPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ConversationViewModel>();
        InitializeComponent();

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
            if (_messagesScrollViewer == null || _messagesScrollViewer.VerticalOffset <= 5)
            {
                _ = ViewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
            }
        }
    }

    private void MessagesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_messagesScrollViewer == null || ViewModel == null) return;

        // When user scrolls near top (within 150px) and not currently loading, dynamically load older messages
        if (_messagesScrollViewer.VerticalOffset <= 150 && !ViewModel.IsLoadingOlder && ViewModel.HasMoreOlderMessages && ViewModel.Messages.Count > 0)
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
        AnimatePill(-40, 0.0);
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

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is Chat chat)
        {
            ViewModel.LoadChat(chat);
        }
        base.OnNavigatedTo(e);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.ClearUnreadSeparators();
        base.OnNavigatedFrom(e);
    }
}
