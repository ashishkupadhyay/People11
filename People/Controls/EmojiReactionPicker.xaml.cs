using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace People.Controls;

public sealed partial class EmojiReactionPicker : UserControl
{
    public event EventHandler<string>? EmojiSelected;

    private string _activeCategory = "Smileys";

    public EmojiReactionPicker()
    {
        InitializeComponent();
        BuildCategoryTabs();
        LoadCategoryEmojis(_activeCategory);

        Loaded += (s, e) => Reset();
    }

    public void Reset()
    {
        QuickBarBorder.Visibility = Visibility.Visible;
        ExpandedPickerBorder.Visibility = Visibility.Collapsed;
        SearchBox.Text = string.Empty;
        _activeCategory = "Smileys";
        HighlightCategoryTab(_activeCategory);
        LoadCategoryEmojis(_activeCategory);
    }

    private void BuildCategoryTabs()
    {
        CategoryTabsPanel.Children.Clear();
        foreach (var cat in EmojiData.Categories)
        {
            var icon = EmojiData.CategoryIcons.TryGetValue(cat, out var ic) ? ic : cat[..1];
            var btn = new Button
            {
                Content = new TextBlock { Text = icon, FontSize = 16 },
                Tag = cat,
                Padding = new Thickness(0),
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = cat == _activeCategory
                    ? (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var b) && b is Microsoft.UI.Xaml.Media.Brush br ? br : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent))
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            ToolTipService.SetToolTip(btn, cat);
            btn.Click += CategoryTab_Click;
            CategoryTabsPanel.Children.Add(btn);
        }
    }

    private void HighlightCategoryTab(string category)
    {
        foreach (var child in CategoryTabsPanel.Children)
        {
            if (child is Button btn && btn.Tag is string cat)
            {
                var isSelected = cat == category;
                btn.Background = isSelected
                    ? (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var b) && b is Microsoft.UI.Xaml.Media.Brush br ? br : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent))
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
    }

    private void CategoryTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cat)
        {
            _activeCategory = cat;
            HighlightCategoryTab(cat);
            LoadCategoryEmojis(cat);
            EmojiScrollViewer.ChangeView(0, 0, 1.0f, true);
        }
    }

    private void LoadCategoryEmojis(string category)
    {
        var filtered = EmojiData.AllEmojis.Where(x => x.Category == category).ToList();
        EmojiItemsControl.ItemsSource = filtered;
        EmptySearchText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QuickEmoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string emoji)
        {
            EmojiSelected?.Invoke(this, emoji);
        }
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        QuickBarBorder.Visibility = Visibility.Collapsed;
        ExpandedPickerBorder.Visibility = Visibility.Visible;
        SearchBox.Text = string.Empty;
        HighlightCategoryTab(_activeCategory);
        LoadCategoryEmojis(_activeCategory);

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            SearchBox.Focus(FocusState.Programmatic);
        });
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        Reset();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(query))
        {
            CategoryScrollViewer.Visibility = Visibility.Visible;
            LoadCategoryEmojis(_activeCategory);
            return;
        }

        CategoryScrollViewer.Visibility = Visibility.Collapsed;

        var matches = EmojiData.AllEmojis
            .Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.Emoji.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        EmojiItemsControl.ItemsSource = matches;
        EmptySearchText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EmojiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string emoji)
        {
            EmojiSelected?.Invoke(this, emoji);
            Reset();
        }
    }
}
