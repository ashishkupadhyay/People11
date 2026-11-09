using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace People.Controls;

public sealed partial class WaveformProgressBar : UserControl
{
    private readonly Canvas _unplayedCanvas;
    private readonly Canvas _playedCanvas;
    private readonly Grid _playedContainer;
    private readonly RectangleGeometry _playedClip;
    private readonly Ellipse _scrubThumb;
    private bool _isDragging;
    private double _previousWidth = -1;

    // Deterministic waveform profile for voice messages
    private static readonly double[] WaveformPattern = new double[]
    {
        0.25, 0.45, 0.70, 0.35, 0.20, 0.50, 0.85, 0.95, 0.60, 0.30,
        0.40, 0.75, 0.55, 0.20, 0.35, 0.65, 0.90, 0.80, 0.45, 0.25,
        0.50, 0.70, 0.85, 0.60, 0.40, 0.75, 0.95, 0.50, 0.30, 0.45,
        0.65, 0.80, 0.40, 0.20, 0.35, 0.60, 0.85, 0.70, 0.30, 0.50,
        0.75, 0.90, 0.55, 0.35, 0.20, 0.40, 0.65, 0.80, 0.50, 0.30
    };

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(WaveformProgressBar),
            new PropertyMetadata(0.0, OnValueChangedStatic));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum),
            typeof(double),
            typeof(WaveformProgressBar),
            new PropertyMetadata(100.0, OnMaximumChangedStatic));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public event EventHandler<double>? ValueChanged;
    public event EventHandler<double>? SeekRequested;

    public WaveformProgressBar()
    {
        Height = 24;
        MinHeight = 20;
        MinWidth = 80;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = true;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ManipulationMode = ManipulationModes.None;

        var rootGrid = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // 1. Unplayed background bars
        _unplayedCanvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
        rootGrid.Children.Add(_unplayedCanvas);

        // 2. Played overlay bars (clipped by rectangle)
        _playedClip = new RectangleGeometry();
        _playedContainer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Clip = _playedClip,
            IsHitTestVisible = false
        };

        _playedCanvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
        _playedContainer.Children.Add(_playedCanvas);
        rootGrid.Children.Add(_playedContainer);

        // 3. Subtle scrub thumb indicator
        _scrubThumb = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Application.Current.Resources.TryGetValue("AppAccentBrush", out var ab) && ab is Brush b ? b : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 168, 132)),
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 255, 255, 255)),
            StrokeThickness = 1.5,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Opacity = 0.9
        };
        rootGrid.Children.Add(_scrubThumb);

        Content = rootGrid;

        SizeChanged += OnSizeChanged;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        PointerCanceled += OnPointerCanceled;
    }

    private static void OnValueChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformProgressBar control)
        {
            control.UpdatePlaybackClip();
            control.ValueChanged?.Invoke(control, (double)e.NewValue);
        }
    }

    private static void OnMaximumChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformProgressBar control)
        {
            control.UpdatePlaybackClip();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - _previousWidth) > 1.0)
        {
            _previousWidth = e.NewSize.Width;
            RebuildBars();
            UpdatePlaybackClip();
        }
    }

    private void RebuildBars()
    {
        _unplayedCanvas.Children.Clear();
        _playedCanvas.Children.Clear();

        double width = ActualWidth;
        double height = ActualHeight > 0 ? ActualHeight : 24;

        if (width <= 0) return;

        double barWidth = 2.5;
        double barGap = 2.0;
        double pitch = barWidth + barGap;
        int barCount = (int)Math.Max(1, Math.Floor((width + barGap) / pitch));

        Brush unplayedBrush = Application.Current.Resources.TryGetValue("TextFillColorTertiaryBrush", out var ub) && ub is Brush uBr ? uBr : new SolidColorBrush(Windows.UI.Color.FromArgb(120, 150, 150, 150));
        Brush playedBrush = Application.Current.Resources.TryGetValue("AppAccentBrush", out var pb) && pb is Brush pBr ? pBr : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 168, 132));

        for (int i = 0; i < barCount; i++)
        {
            double patternVal = WaveformPattern[i % WaveformPattern.Length];
            double barHeight = Math.Max(4, patternVal * (height - 4));
            double x = i * pitch;
            double y = (height - barHeight) / 2.0;

            // Unplayed bar
            var unplayedBar = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 1.25,
                RadiusY = 1.25,
                Fill = unplayedBrush
            };
            Canvas.SetLeft(unplayedBar, x);
            Canvas.SetTop(unplayedBar, y);
            _unplayedCanvas.Children.Add(unplayedBar);

            // Played bar
            var playedBar = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 1.25,
                RadiusY = 1.25,
                Fill = playedBrush
            };
            Canvas.SetLeft(playedBar, x);
            Canvas.SetTop(playedBar, y);
            _playedCanvas.Children.Add(playedBar);
        }
    }

    private void UpdatePlaybackClip()
    {
        double width = ActualWidth;
        double height = ActualHeight > 0 ? ActualHeight : 24;
        if (width <= 0) return;

        double max = Maximum > 0 ? Maximum : 100.0;
        double fraction = Math.Clamp(Value / max, 0.0, 1.0);
        double clipWidth = fraction * width;

        _playedClip.Rect = new Rect(0, 0, clipWidth, height);

        // Position scrub thumb
        Canvas.SetLeft(_scrubThumb, Math.Max(0, clipWidth - 4));
        _scrubThumb.Margin = new Thickness(Math.Max(0, clipWidth - 4), 0, 0, 0);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled) return;
        var prop = e.GetCurrentPoint(this).Properties;
        if (prop.IsLeftButtonPressed)
        {
            _isDragging = CapturePointer(e.Pointer);
            SeekFromPointer(e, isFinal: false);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging && IsEnabled)
        {
            SeekFromPointer(e, isFinal: false);
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleasePointerCapture(e.Pointer);
            SeekFromPointer(e, isFinal: true);
            e.Handled = true;
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }

    private void SeekFromPointer(PointerRoutedEventArgs e, bool isFinal)
    {
        double width = ActualWidth;
        if (width <= 0) return;

        var pt = e.GetCurrentPoint(this).Position;
        double fraction = Math.Clamp(pt.X / width, 0.0, 1.0);
        double max = Maximum > 0 ? Maximum : 100.0;
        double newVal = fraction * max;
        Value = newVal;
        UpdatePlaybackClip();

        SeekRequested?.Invoke(this, newVal);
    }
}
