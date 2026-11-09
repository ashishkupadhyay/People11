using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace People.Controls;

public sealed partial class SimpleWrapPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0;
        double y = 0;
        double maxRowHeight = 0;
        double maxWidth = 0;

        foreach (UIElement child in Children)
        {
            child.Measure(availableSize);
            var sz = child.DesiredSize;

            if (x + sz.Width > availableSize.Width && x > 0)
            {
                y += maxRowHeight;
                x = 0;
                maxRowHeight = 0;
            }

            x += sz.Width;
            if (sz.Height > maxRowHeight) maxRowHeight = sz.Height;
            if (x > maxWidth) maxWidth = x;
        }

        y += maxRowHeight;
        double width = double.IsInfinity(availableSize.Width) ? maxWidth : availableSize.Width;
        return new Size(width, y);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        double y = 0;
        double maxRowHeight = 0;

        foreach (UIElement child in Children)
        {
            var sz = child.DesiredSize;

            if (x + sz.Width > finalSize.Width && x > 0)
            {
                y += maxRowHeight;
                x = 0;
                maxRowHeight = 0;
            }

            child.Arrange(new Rect(x, y, sz.Width, sz.Height));
            x += sz.Width;
            if (sz.Height > maxRowHeight) maxRowHeight = sz.Height;
        }

        return finalSize;
    }
}
