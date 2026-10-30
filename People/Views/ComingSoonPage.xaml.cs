using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

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
        this.ActualThemeChanged += (s, e) => {
            if (TitleTextBlock.Text.Contains(" Support is Coming Soon"))
            {
                string appName = TitleTextBlock.Text.Replace(" Support is Coming Soon", "");
                UpdateAnimation(appName);
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        var themeService = App.Current.Services.GetRequiredService<People.Core.Interfaces.IThemeService>();

        if (e.Parameter is string appName)
        {
            themeService.SetActivePlatform(appName);

            if (AppStyles.TryGetValue(appName, out var style))
            {
                TitleTextBlock.Text = $"{appName} Support is Coming Soon";
                
                // Set background gradient
                ColorStop1.Color = GetColorFromHex(style.Hex1);
                ColorStop2.Color = GetColorFromHex(style.Hex2);
                
                UpdateAnimation(appName);
            }
        }
        else
        {
            themeService.ClearActivePlatform();
            TitleTextBlock.Text = "Coming Soon";
            
            // Clear background gradient
            ColorStop1.Color = Microsoft.UI.Colors.Transparent;
            ColorStop2.Color = Microsoft.UI.Colors.Transparent;
            
        }
    }
    
    private void UpdateAnimation(string appName)
    {
        if (AppLottiePlayer == null) return;
        
        bool isDark = App.Current.RequestedTheme == Microsoft.UI.Xaml.ApplicationTheme.Dark;
        if (this.ActualTheme != Microsoft.UI.Xaml.ElementTheme.Default)
        {
            isDark = this.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
        }

        string themeSuffix = "white";
        string prefix = appName.ToLower(); 
        
        switch(prefix)
        {
            case "whatsapp": 
                AppLottiePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Whatsapp_animated_reveal_white() : new AnimatedVisuals.Whatsapp_animated_reveal_black();
                break;
            case "telegram": 
                AppLottiePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Telegram_animated_white() : new AnimatedVisuals.Telegram_animated_black();
                break;
            case "signal": 
                AppLottiePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Signal_animated_reveal_white() : new AnimatedVisuals.Signal_animated_reveal_black();
                break;
            case "discord": 
                AppLottiePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Discord_animated_reveal_filled_white() : new AnimatedVisuals.Discord_animated_reveal_filled_black();
                break;
            default: 
                AppLottiePlayer.Source = themeSuffix == "white" ? new AnimatedVisuals.Chat_animated_white() : new AnimatedVisuals.Chat_animated_black();
                break;
        }
        
        _ = AppLottiePlayer.PlayAsync(0, 1, false);
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
