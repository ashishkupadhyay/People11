using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using People.Core.Models;

namespace People.Controls;

public sealed partial class ReactionPillControl : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(Message),
            typeof(ReactionPillControl),
            new PropertyMetadata(null, OnMessagePropertyChanged));

    public Message? Message
    {
        get => (Message?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public event RoutedEventHandler? Click;

    private Message? _subscribedMessage;
    private int _previousReactionCount;
    private string _previousDistinctReactionSignature = string.Empty;
    private bool _hasRenderedOnce;

    public ReactionPillControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        PopOutStoryboard.Completed += (s, e) =>
        {
            if (_previousReactionCount == 0)
            {
                Visibility = Visibility.Collapsed;
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateUI(isInitial: !_hasRenderedOnce);
        _hasRenderedOnce = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeFromMessage();
    }

    private static void OnMessagePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ReactionPillControl control)
        {
            control.UnsubscribeFromMessage();
            if (e.NewValue is Message msg)
            {
                control.SubscribeToMessage(msg);
            }
            control.UpdateUI(isInitial: !control._hasRenderedOnce);
        }
    }

    private void SubscribeToMessage(Message msg)
    {
        _subscribedMessage = msg;
        _subscribedMessage.PropertyChanged += Message_PropertyChanged;
    }

    private void UnsubscribeFromMessage()
    {
        if (_subscribedMessage != null)
        {
            _subscribedMessage.PropertyChanged -= Message_PropertyChanged;
            _subscribedMessage = null;
        }
    }

    private void Message_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Message.Reactions) ||
            e.PropertyName == nameof(Message.DistinctReactionEmojis) ||
            e.PropertyName == nameof(Message.TotalReactionCount) ||
            e.PropertyName == nameof(Message.HasReactions))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateUI(isInitial: false);
            });
        }
    }

    private void UpdateUI(bool isInitial)
    {
        var msg = Message;
        if (msg == null || !msg.HasReactions)
        {
            if (IsLoaded && !isInitial && _previousReactionCount > 0)
            {
                // Play bouncy pop-out animation before hiding
                PopOutStoryboard.Stop();
                PopOutStoryboard.Begin();
                _previousReactionCount = 0;
                _previousDistinctReactionSignature = string.Empty;
            }
            else
            {
                Visibility = Visibility.Collapsed;
                _previousReactionCount = 0;
                _previousDistinctReactionSignature = string.Empty;
            }
            return;
        }

        var distinctEmojis = msg.DistinctReactionEmojis;
        var count = msg.TotalReactionCount;
        var signature = string.Join(",", distinctEmojis) + ":" + count;

        EmojisTextBlock.Text = string.Join(" ", distinctEmojis);
        TotalCountTextBlock.Text = count > 1 ? count.ToString() : string.Empty;
        TotalCountTextBlock.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;

        Visibility = Visibility.Visible;

        if (isInitial || !IsLoaded)
        {
            // Initial render or pre-load without animation
            PillScale.ScaleX = 1.0;
            PillScale.ScaleY = 1.0;
            PillBorder.Opacity = 1.0;
        }
        else
        {
            PopOutStoryboard.Stop();
            PillBorder.Opacity = 1.0;

            if (_previousReactionCount == 0 && count > 0)
            {
                // New reaction added: Play bouncy pop-in animation
                PopInStoryboard.Stop();
                PopInStoryboard.Begin();
            }
            else if (count < _previousReactionCount)
            {
                // Reaction removed (while other reactions remain): Play popout-in bouncy animation
                PopOutInStoryboard.Stop();
                PopOutInStoryboard.Begin();
            }
            else if (signature != _previousDistinctReactionSignature)
            {
                // Reaction changed or additional reaction added: Play playful pulse bounce
                BouncePulseStoryboard.Stop();
                BouncePulseStoryboard.Begin();
            }
        }

        _previousReactionCount = count;
        _previousDistinctReactionSignature = signature;
    }

    private void PillButton_Click(object sender, RoutedEventArgs e)
    {
        Click?.Invoke(this, e);
    }
}
