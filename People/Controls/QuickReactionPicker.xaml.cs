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

    public void ApplySurfaceBrushes(Microsoft.UI.Xaml.Media.Brush? background, Microsoft.UI.Xaml.Media.Brush? border)
    {
        if (ContainerBorder == null) return;
        if (background != null) ContainerBorder.Background = background;
        else ContainerBorder.ClearValue(Border.BackgroundProperty);
        if (border != null) ContainerBorder.BorderBrush = border;
        else ContainerBorder.ClearValue(Border.BorderBrushProperty);
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
