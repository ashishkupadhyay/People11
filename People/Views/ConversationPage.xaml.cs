using System.Collections.Specialized;
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

        // When user scrolls near top (within 120px) and not currently loading, dynamically load older messages
        if (_messagesScrollViewer.VerticalOffset <= 120 && !ViewModel.IsLoadingOlder && ViewModel.HasMoreOlderMessages && ViewModel.Messages.Count > 0)
        {
            _ = ViewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
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

    private async void MessagesRefreshContainer_RefreshRequested(RefreshContainer sender, RefreshRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        if (ViewModel != null && !ViewModel.IsLoadingOlder && ViewModel.HasMoreOlderMessages)
        {
            var anchor = ViewModel.Messages.FirstOrDefault();
            await ViewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
            if (anchor != null)
            {
                MessagesListView.UpdateLayout();
                MessagesListView.ScrollIntoView(anchor, ScrollIntoViewAlignment.Leading);
            }
        }
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
