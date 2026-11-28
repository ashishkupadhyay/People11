using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace People.Controls;

public sealed partial class EmojiReactionPicker : UserControl
{
    public event EventHandler<string>? EmojiSelected;
    public event EventHandler? BackRequested;

    private string _activeCategory = "Smileys";
    private DispatcherTimer? _searchDebounceTimer;
    private bool _isUpdatingSelection;

    // Static cached lookup by category - zero allocation, O(1) instantaneous category switching
    private static readonly Dictionary<string, List<EmojiItem>> CategoryCache =
        EmojiData.Categories.ToDictionary(
            cat => cat,
            cat => EmojiData.AllEmojis.Where(e => e.Category == cat).ToList()
        );

    public EmojiReactionPicker()
    {
        InitializeComponent();
        BuildCategoryTabs();

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

        Loaded += (s, e) => Reset();
    }

    public void ApplySurfaceBrushes(Brush? background, Brush? border)
    {
        if (ContainerBorder == null) return;
        if (background != null) ContainerBorder.Background = background;
        else ContainerBorder.ClearValue(Border.BackgroundProperty);
        if (border != null) ContainerBorder.BorderBrush = border;
        else ContainerBorder.ClearValue(Border.BorderBrushProperty);
    }

    public void Reset()
    {
        _searchDebounceTimer?.Stop();
        SearchBox.Text = string.Empty;
        CategoryScrollViewer.Visibility = Visibility.Visible;
        _activeCategory = "Smileys";
        SelectCategoryItem(_activeCategory);
        LoadCategoryEmojis(_activeCategory);

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            SearchBox.Focus(FocusState.Programmatic);
        });
    }

    private void BuildCategoryTabs()
    {
        _isUpdatingSelection = true;
        try
        {
            CategorySelectorBar.Items.Clear();
            var emojiFont = new FontFamily("Segoe UI Emoji");

            foreach (var cat in EmojiData.Categories)
            {
                var icon = EmojiData.CategoryIcons.TryGetValue(cat, out var ic) ? ic : cat[..1];
                var item = new SelectorBarItem
                {
                    Icon = new FontIcon
                    {
                        Glyph = icon,
                        FontFamily = emojiFont,
                        FontSize = 15
                    },
                    Tag = cat,
                    MinWidth = 34,
                    Padding = new Thickness(6, 4, 6, 4)
                };
                ToolTipService.SetToolTip(item, cat);
                AutomationProperties.SetName(item, cat);
                CategorySelectorBar.Items.Add(item);
            }

            SelectCategoryItem(_activeCategory);
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void SelectCategoryItem(string category)
    {
        _isUpdatingSelection = true;
        try
        {
            foreach (var item in CategorySelectorBar.Items)
            {
                if (item.Tag is string cat && cat == category)
                {
                    CategorySelectorBar.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void CategorySelectorBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_isUpdatingSelection) return;
        if (sender.SelectedItem is SelectorBarItem item && item.Tag is string cat)
        {
            _activeCategory = cat;
            LoadCategoryEmojis(cat);
            EmojiScrollViewer.ChangeView(0, 0, 1.0f, true);
        }
    }

    private void LoadCategoryEmojis(string category)
    {
        if (CategoryCache.TryGetValue(category, out var items))
        {
            EmojiRepeater.ItemsSource = items;
            EmptySearchText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        var query = sender.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(query))
        {
            _searchDebounceTimer?.Stop();
            CategoryScrollViewer.Visibility = Visibility.Visible;
            LoadCategoryEmojis(_activeCategory);
            return;
        }

        _searchDebounceTimer?.Stop();
        _searchDebounceTimer?.Start();
    }

    private void SearchDebounceTimer_Tick(object? sender, object e)
    {
        _searchDebounceTimer?.Stop();

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

        EmojiRepeater.ItemsSource = matches;
        EmptySearchText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmojiScrollViewer.ChangeView(0, 0, 1.0f, true);
    }

    private void EmojiButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var emoji = btn.Tag as string ?? (btn.DataContext as EmojiItem)?.Emoji;
            if (!string.IsNullOrEmpty(emoji))
            {
                EmojiSelected?.Invoke(this, emoji);
            }
        }
    }
}
