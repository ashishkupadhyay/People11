using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using People.Core.Models;

namespace People.Views.Dialogs;

public sealed partial class ForwardMessageDialog : ContentDialog
{
    private readonly IReadOnlyList<Chat> _allChats;

    public ObservableCollection<Chat> FilteredChats { get; }
    public double DialogWidth { get; }
    public Chat? SelectedChat => ChatsListView.SelectedItem as Chat;
    public bool ConfirmedByDoubleTap { get; private set; }

    public ForwardMessageDialog(IReadOnlyList<Chat> targetChats, double dialogWidth)
    {
        _allChats = targetChats;
        FilteredChats = new ObservableCollection<Chat>(targetChats);
        DialogWidth = dialogWidth;
        InitializeComponent();

        if (FilteredChats.Count > 0)
        {
            ChatsListView.SelectedIndex = 0;
            IsPrimaryButtonEnabled = true;
        }
        else
        {
            EmptyText.Visibility = Visibility.Visible;
            IsPrimaryButtonEnabled = false;
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        var query = sender.Text?.Trim() ?? string.Empty;
        FilteredChats.Clear();

        IEnumerable<Chat> matches = string.IsNullOrEmpty(query)
            ? _allChats
            : _allChats.Where(c => c.Title != null && c.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var match in matches)
        {
            FilteredChats.Add(match);
        }

        if (FilteredChats.Count > 0)
        {
            ChatsListView.SelectedIndex = 0;
            EmptyText.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = true;
        }
        else
        {
            EmptyText.Visibility = Visibility.Visible;
            IsPrimaryButtonEnabled = false;
        }
    }

    private void ChatsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = ChatsListView.SelectedItem is Chat;
    }

    private void ChatsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Chat selected)
        {
            ChatsListView.SelectedItem = selected;
            IsPrimaryButtonEnabled = true;
        }
    }

    private void ChatsListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ChatsListView.SelectedItem is Chat)
        {
            ConfirmedByDoubleTap = true;
            Hide();
        }
    }
}
