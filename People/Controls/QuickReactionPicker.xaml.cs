using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace People.Controls;

public sealed partial class QuickReactionPicker : UserControl
{
    public event EventHandler<string>? EmojiSelected;
    public event EventHandler? ExpandRequested;

    public QuickReactionPicker()
    {
        InitializeComponent();
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
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }
}
