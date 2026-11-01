using System.Collections.Specialized;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using People.Core.Models;
using People.ViewModels;

namespace People.Views;

public sealed partial class ConversationPage : Page
{
    public ConversationViewModel ViewModel { get; }

    public ConversationPage()
    {
        ViewModel = App.Current.Services.GetRequiredService<ConversationViewModel>();
        InitializeComponent();

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
                MessagesListView.ScrollIntoView(ViewModel.Messages[^1]);
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
}
