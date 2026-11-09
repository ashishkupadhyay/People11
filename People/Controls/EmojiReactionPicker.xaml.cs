using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace People.Controls;

public sealed partial class EmojiReactionPicker : UserControl
{
    public event EventHandler<string>? EmojiSelected;
    public event EventHandler? BackRequested;

    private string _activeCategory = "Smileys";
    private DispatcherTimer? _searchDebounceTimer;

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

    public void Reset()
    {
        _searchDebounceTimer?.Stop();
        SearchBox.Text = string.Empty;
        CategoryScrollViewer.Visibility = Visibility.Visible;
        _activeCategory = "Smileys";
        HighlightCategoryTab(_activeCategory);
        LoadCategoryEmojis(_activeCategory);

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            SearchBox.Focus(FocusState.Programmatic);
        });
    }

    private void BuildCategoryTabs()
    {
        CategoryTabsPanel.Children.Clear();
        foreach (var cat in EmojiData.Categories)
        {
            var icon = EmojiData.CategoryIcons.TryGetValue(cat, out var ic) ? ic : cat[..1];

            var containerGrid = new Grid
            {
                Width = 32,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var textBlock = new TextBlock
            {
                Text = icon,
                FontSize = 16,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3)
            };

            var indicator = new Rectangle
            {
                Name = "ActiveIndicator",
                Height = 2.5,
                Width = 16,
                RadiusX = 1.25,
                RadiusY = 1.25,
                Fill = GetAccentBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = cat == _activeCategory ? Visibility.Visible : Visibility.Collapsed
            };

            containerGrid.Children.Add(textBlock);
            containerGrid.Children.Add(indicator);

            var btn = new Button
            {
                Content = containerGrid,
                Tag = cat,
                Padding = new Thickness(0),
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(4),
                Background = cat == _activeCategory ? GetSubtleSecondaryBrush() : GetTransparentBrush(),
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
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
                btn.Background = isSelected ? GetSubtleSecondaryBrush() : GetTransparentBrush();

                if (btn.Content is Grid grid)
                {
                    foreach (var element in grid.Children)
                    {
                        if (element is Rectangle rect && rect.Name == "ActiveIndicator")
                        {
                            rect.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
                        }
                    }
                }
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

    private static Brush GetAccentBrush()
    {
        if (Application.Current.Resources.TryGetValue("AppAccentBrush", out var b) && b is Brush brush)
        {
            return brush;
        }
        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var ab) && ab is Brush aBrush)
        {
            return aBrush;
        }
        return new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
    }
}
