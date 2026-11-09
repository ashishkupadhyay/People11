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

    public Message? CurrentMessage => _message;

    public ReactionDetailsControl()
    {
        InitializeComponent();
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
        EmojiTabsPanel.Children.Clear();
        if (_message == null || _message.Reactions.Count == 0) return;

        // "All" tab
        AddTabButton("All", $"All {_message.TotalReactionCount}", "All");

        // Per emoji tabs
        var grouped = _message.Reactions.GroupBy(r => r.Emoji);
        foreach (var group in grouped)
        {
            AddTabButton(group.Key, $"{group.Key} {group.Count()}", group.Key);
        }
    }

    private void AddTabButton(string key, string label, string tag)
    {
        var isSelected = key == _activeFilter;
        var btn = new Button
        {
            Content = new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI")
            },
            Tag = tag,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4),
            Background = isSelected ? GetSubtleSecondaryBrush() : GetTransparentBrush(),
            BorderThickness = new Thickness(0)
        };
        btn.Click += TabButton_Click;
        EmojiTabsPanel.Children.Add(btn);
    }

    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeFilter = tag;
            BuildTabs();
            RefreshList();
        }
    }

    private void RefreshList()
    {
        if (_message == null)
        {
            ReactorsItemsControl.ItemsSource = null;
            return;
        }

        if (_activeFilter == "All")
        {
            ReactorsItemsControl.ItemsSource = _message.Reactions.ToList();
        }
        else
        {
            ReactorsItemsControl.ItemsSource = _message.Reactions.Where(r => r.Emoji == _activeFilter).ToList();
        }
    }

    private void ReactorItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is MessageReaction reaction)
        {
            ReactionClicked?.Invoke(this, reaction);
        }
    }

    private static Brush GetSubtleSecondaryBrush()
    {
        return Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var b) && b is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private static Brush GetTransparentBrush()
    {
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
