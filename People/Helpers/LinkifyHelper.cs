using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.System;

namespace People.Helpers;

public static class LinkifyHelper
{
    private static readonly Regex UrlRegex = new(
        @"(https?:\/\/[^\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(LinkifyHelper),
            new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject obj) => (string?)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string? value) => obj.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBlock rtb) return;

        rtb.Blocks.Clear();
        var text = e.NewValue as string;
        if (string.IsNullOrEmpty(text)) return;

        var paragraph = new Paragraph();
        var matches = UrlRegex.Matches(text);
        int lastIndex = 0;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex, match.Index - lastIndex) });
            }

            var urlStr = match.Value;
            var hyperlink = new Hyperlink();
            hyperlink.Inlines.Add(new Run { Text = urlStr });
            hyperlink.Click += async (s, args) =>
            {
                if (Uri.TryCreate(urlStr, UriKind.Absolute, out var uri))
                {
                    try
                    {
                        await Launcher.LaunchUriAsync(uri);
                    }
                    catch
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = urlStr,
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                }
            };
            paragraph.Inlines.Add(hyperlink);

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex) });
        }

        rtb.Blocks.Add(paragraph);
    }
}

