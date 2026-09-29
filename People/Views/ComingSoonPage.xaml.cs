using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;

namespace People.Views;

public sealed partial class ComingSoonPage : Page
{
    private static readonly Dictionary<string, (string Hex1, string Hex2, string Glyph)> AppStyles = new()
    {
        { "SMS", ("#0078D7", "#00A1F1", "\uE8BD") },
        { "Telegram", ("#0088cc", "#33AADD", "\uE8F1") },
        { "Signal", ("#3A76F0", "#1851B4", "\uE119") },
        { "Discord", ("#5865F2", "#7289DA", "\uE90A") }
    };

    public ComingSoonPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string appName && AppStyles.TryGetValue(appName, out var style))
        {
            TitleTextBlock.Text = $"{appName} Support is Coming Soon";
            AppIcon.Glyph = style.Glyph;
            
            ColorStop1.Color = GetColorFromHex(style.Hex1);
            ColorStop2.Color = GetColorFromHex(style.Hex2);
        }
        else
        {
            TitleTextBlock.Text = "Coming Soon";
        }
        
        base.OnNavigatedTo(e);
    }

    private Windows.UI.Color GetColorFromHex(string hex)
    {
        hex = hex.Replace("#", string.Empty);
        byte a = 255;
        byte r = 0, g = 0, b = 0;

        if (hex.Length == 8)
        {
            a = System.Convert.ToByte(hex.Substring(0, 2), 16);
            r = System.Convert.ToByte(hex.Substring(2, 2), 16);
            g = System.Convert.ToByte(hex.Substring(4, 2), 16);
            b = System.Convert.ToByte(hex.Substring(6, 2), 16);
        }
        else if (hex.Length == 6)
        {
            r = System.Convert.ToByte(hex.Substring(0, 2), 16);
            g = System.Convert.ToByte(hex.Substring(2, 2), 16);
            b = System.Convert.ToByte(hex.Substring(4, 2), 16);
        }
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }
}
