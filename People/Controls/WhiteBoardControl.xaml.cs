using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Streams;
using Windows.UI.Core;

namespace People.Controls;

public sealed partial class WhiteBoardControl : UserControl
{
    public event EventHandler? CancelClicked;
    public event EventHandler<string>? SendImageClicked;

    public WhiteBoardControl()
    {
        this.InitializeComponent();
        this.Loaded += WhiteBoardControl_Loaded;
    }

    private void WhiteBoardControl_Loaded(object sender, RoutedEventArgs e)
    {
        ConfigureInkPresenter();
    }

    private void ConfigureInkPresenter()
    {
        if (MyInkCanvas?.InkPresenter != null)
        {
            MyInkCanvas.InkPresenter.InputDeviceTypes =
                CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Mouse | CoreInputDeviceTypes.Touch;
        }
    }

    public void Reset()
    {
        if (MyInkCanvas?.InkPresenter?.StrokeContainer != null)
        {
            MyInkCanvas.InkPresenter.StrokeContainer.Clear();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelClicked?.Invoke(this, EventArgs.Empty);
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var strokeContainer = MyInkCanvas?.InkPresenter?.StrokeContainer;
        var strokes = strokeContainer?.GetStrokes();

        if (strokes == null || strokes.Count == 0)
        {
            CancelClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            SendButton.IsEnabled = false;

            int width = Math.Max(800, (int)MyInkCanvas!.ActualWidth);
            int height = Math.Max(600, (int)MyInkCanvas.ActualHeight);

            var tempDir = Path.Combine(Path.GetTempPath(), "People", "Whiteboard");
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, $"whiteboard_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            CanvasDevice device = CanvasDevice.GetSharedDevice();
            using (var renderTarget = new CanvasRenderTarget(device, width, height, 96f))
            {
                using (var ds = renderTarget.CreateDrawingSession())
                {
                    ds.Clear(Colors.White);
                    RenderNativeStrokes(ds, strokes);
                }

                using (var fileStream = File.Create(filePath))
                using (var outStream = fileStream.AsRandomAccessStream())
                {
                    await renderTarget.SaveAsync(outStream, CanvasBitmapFileFormat.Png);
                }
            }

            SendImageClicked?.Invoke(this, filePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to export whiteboard image: {ex.Message}");
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    private static void RenderNativeStrokes(CanvasDrawingSession ds, IEnumerable<Windows.UI.Input.Inking.InkStroke> strokes)
    {
        using var roundStyle = new CanvasStrokeStyle
        {
            StartCap = CanvasCapStyle.Round,
            EndCap = CanvasCapStyle.Round,
            LineJoin = CanvasLineJoin.Round
        };

        using var flatStyle = new CanvasStrokeStyle
        {
            StartCap = CanvasCapStyle.Square,
            EndCap = CanvasCapStyle.Square,
            LineJoin = CanvasLineJoin.Miter
        };

        foreach (var stroke in strokes)
        {
            var points = stroke.GetInkPoints();
            if (points == null || points.Count == 0) continue;

            var pts = points.Select(p => new Vector2((float)p.Position.X, (float)p.Position.Y)).ToList();
            var da = stroke.DrawingAttributes;
            var winColor = da.Color;
            float strokeWidth = (float)Math.Max(1.0, da.Size.Width);
            bool isHighlighter = da.DrawAsHighlighter;

            if (isHighlighter)
            {
                var hlColor = Windows.UI.Color.FromArgb(90, winColor.R, winColor.G, winColor.B);
                RenderSmoothPath(ds, pts, hlColor, strokeWidth * 2.5f, flatStyle);
            }
            else
            {
                RenderSmoothPath(ds, pts, winColor, strokeWidth, roundStyle);
            }
        }
    }

    private static void RenderSmoothPath(
        CanvasDrawingSession ds,
        IReadOnlyList<Vector2> points,
        Windows.UI.Color color,
        float strokeWidth,
        CanvasStrokeStyle style)
    {
        if (points.Count == 1)
        {
            ds.FillCircle(points[0], Math.Max(1f, strokeWidth * 0.5f), color);
            return;
        }

        if (points.Count == 2)
        {
            ds.DrawLine(points[0], points[1], color, strokeWidth, style);
            return;
        }

        try
        {
            using var builder = new CanvasPathBuilder(ds.Device);
            builder.BeginFigure(points[0]);

            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector2 midPoint = (points[i] + points[i + 1]) * 0.5f;
                builder.AddQuadraticBezier(points[i], midPoint);
            }

            builder.AddLine(points[^1]);
            builder.EndFigure(CanvasFigureLoop.Open);

            using var geometry = CanvasGeometry.CreatePath(builder);
            ds.DrawGeometry(geometry, color, strokeWidth, style);
        }
        catch
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                ds.DrawLine(points[i], points[i + 1], color, strokeWidth, style);
            }
        }
    }
}
