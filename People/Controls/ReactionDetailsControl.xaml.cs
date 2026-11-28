using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using People.Core.Models;

namespace People.Controls;

public sealed partial class ReactionDetailsControl : UserControl
{
    public event EventHandler<MessageReaction>? ReactionClicked;

    private Message? _message;
    private string _activeFilter = "All";
    private bool _isUpdatingTabs;

    public Message? CurrentMessage => _message;

    public ReactionDetailsControl()
    {
        InitializeComponent();
    }

    public void ApplySurfaceBrushes(Brush? background, Brush? border)
    {
        if (ContainerBorder == null) return;
        if (background != null) ContainerBorder.Background = background;
        else ContainerBorder.ClearValue(Border.BackgroundProperty);
        if (border != null) ContainerBorder.BorderBrush = border;
        else ContainerBorder.ClearValue(Border.BorderBrushProperty);
    }

    public void SetMessage(Message message)
    {
        _message = message;
        _activeFilter = "All";
        TotalCountText.Text = message.TotalReactionCount.ToString();
        BuildTabs();
        RefreshList();
    }

    private void BuildTabs()
    {
        _isUpdatingTabs = true;
        try
        {
            EmojiFilterSelectorBar.Items.Clear();
            if (_message == null || _message.Reactions.Count == 0) return;

            var itemFont = new FontFamily("Segoe UI Emoji, Segoe UI");
            var allItem = new SelectorBarItem
            {
                Text = $"All {_message.TotalReactionCount}",
                Tag = "All",
                FontFamily = itemFont,
                Padding = new Thickness(10, 4, 10, 4),
                MinWidth = 0
            };
            EmojiFilterSelectorBar.Items.Add(allItem);

            foreach (var group in _message.Reactions.GroupBy(r => r.Emoji))
            {
                EmojiFilterSelectorBar.Items.Add(new SelectorBarItem
                {
                    Text = $"{group.Key} {group.Count()}",
                    Tag = group.Key,
                    FontFamily = itemFont,
                    Padding = new Thickness(10, 4, 10, 4),
                    MinWidth = 0
                });
            }

            EmojiFilterSelectorBar.SelectedItem = allItem;
        }
        finally
        {
            _isUpdatingTabs = false;
        }
    }

    private void EmojiFilterSelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_isUpdatingTabs) return;
        if (sender.SelectedItem is SelectorBarItem item && item.Tag is string tag)
        {
            _activeFilter = tag;
            RefreshList();
        }
    }

    private void RefreshList()
    {
        if (_message == null)
        {
            ReactorsListView.ItemsSource = null;
            return;
        }

        ReactorsListView.ItemsSource = _activeFilter == "All"
            ? _message.Reactions.ToList()
            : _message.Reactions.Where(r => r.Emoji == _activeFilter).ToList();
    }

    private void ReactorsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MessageReaction reaction)
        {
            ReactionClicked?.Invoke(this, reaction);
        }
    }
}
