using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using People.Converters;
using Windows.Storage.Streams;
using Windows.UI;

namespace People.Controls;

public enum WhiteBoardTool
{
    Pen,
    Pencil,
    Highlighter,
    Eraser
}

public class WhiteBoardStroke
{
    public WhiteBoardTool Tool { get; set; } = WhiteBoardTool.Pen;
    public Color Color { get; set; } = Colors.Black;
    public float Width { get; set; } = 4f;
    public List<Vector2> Points { get; } = new();

    public WhiteBoardStroke Clone()
    {
        var copy = new WhiteBoardStroke
        {
            Tool = this.Tool,
            Color = this.Color,
            Width = this.Width
        };
        copy.Points.AddRange(this.Points);
        return copy;
    }
}

public sealed partial class WhiteBoardControl : UserControl
{
    public event EventHandler? CancelClicked;
    public event EventHandler<string>? SendImageClicked;

    private readonly List<WhiteBoardStroke> _strokes = new();
    private readonly Stack<List<WhiteBoardStroke>> _undoStack = new();
    private readonly Stack<List<WhiteBoardStroke>> _redoStack = new();

    private WhiteBoardTool _currentTool = WhiteBoardTool.Pen;
    private Color _currentColor = Colors.Black;
    private float _currentWidth = 4f;

    private WhiteBoardStroke? _activeStroke;
    private bool _isDrawing;

    public WhiteBoardControl()
    {
        this.InitializeComponent();
        UpdateToolSelectionVisuals();
        UpdateUndoRedoState();
    }

    public void Reset()
    {
        _strokes.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        _activeStroke = null;
        _isDrawing = false;
        _currentTool = WhiteBoardTool.Pen;
        _currentColor = Colors.Black;
        _currentWidth = 4f;

        if (SizeSlider != null)
        {
            SizeSlider.Value = 4;
        }

        UpdateToolSelectionVisuals();
        UpdateUndoRedoState();
        DrawingCanvas?.Invalidate();
    }

    private void PushUndoState()
    {
        _undoStack.Push(_strokes.Select(s => s.Clone()).ToList());
        _redoStack.Clear();
        UpdateUndoRedoState();
    }

    private void UpdateUndoRedoState()
    {
        if (UndoButton != null) UndoButton.IsEnabled = _undoStack.Count > 0;
        if (RedoButton != null) RedoButton.IsEnabled = _redoStack.Count > 0;
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string toolName)
        {
            _currentTool = toolName switch
            {
                "Pencil" => WhiteBoardTool.Pencil,
                "Highlighter" => WhiteBoardTool.Highlighter,
                "Eraser" => WhiteBoardTool.Eraser,
                _ => WhiteBoardTool.Pen
            };
            UpdateToolSelectionVisuals();
        }
    }

    private void UpdateToolSelectionVisuals()
    {
        SetButtonActive(PenButton, _currentTool == WhiteBoardTool.Pen);
        SetButtonActive(PencilButton, _currentTool == WhiteBoardTool.Pencil);
        SetButtonActive(HighlighterButton, _currentTool == WhiteBoardTool.Highlighter);
        SetButtonActive(EraserButton, _currentTool == WhiteBoardTool.Eraser);
    }

    private void SetButtonActive(Button? btn, bool isActive)
    {
        if (btn == null) return;
        if (isActive)
        {
            btn.Background = ScopedResourceHelper.ResolveBrush(null, "AppAccentBrush") as Brush
                             ?? new SolidColorBrush(Color.FromArgb(50, 0, 120, 215));
            btn.BorderBrush = ScopedResourceHelper.ResolveBrush(null, "AppAccentBrush") as Brush
                              ?? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
        }
        else
        {
            btn.Background = new SolidColorBrush(Colors.Transparent);
            btn.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            try
            {
                var hexColor = hex.TrimStart('#');
                byte a = 255;
                int start = 0;
                if (hexColor.Length == 8)
                {
                    a = Convert.ToByte(hexColor[..2], 16);
                    start = 2;
                }
                byte r = Convert.ToByte(hexColor.Substring(start, 2), 16);
                byte g = Convert.ToByte(hexColor.Substring(start + 2, 2), 16);
                byte b = Convert.ToByte(hexColor.Substring(start + 4, 2), 16);
                _currentColor = Color.FromArgb(a, r, g, b);

                if (_currentTool == WhiteBoardTool.Eraser)
                {
                    _currentTool = WhiteBoardTool.Pen;
                    UpdateToolSelectionVisuals();
                }
            }
            catch { }
        }
    }

    private void SizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _currentWidth = (float)e.NewValue;
        if (SizeValueText != null)
        {
            SizeValueText.Text = $"{Math.Round(_currentWidth)}px";
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;

        _redoStack.Push(_strokes.Select(s => s.Clone()).ToList());
        var previousState = _undoStack.Pop();

        _strokes.Clear();
        _strokes.AddRange(previousState);
        UpdateUndoRedoState();
        DrawingCanvas?.Invalidate();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;

        _undoStack.Push(_strokes.Select(s => s.Clone()).ToList());
        var nextState = _redoStack.Pop();

        _strokes.Clear();
        _strokes.AddRange(nextState);
        UpdateUndoRedoState();
        DrawingCanvas?.Invalidate();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_strokes.Count == 0) return;

        PushUndoState();
        _strokes.Clear();
        DrawingCanvas?.Invalidate();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelClicked?.Invoke(this, EventArgs.Empty);
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_strokes.Count == 0)
        {
            CancelClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            SendButton.IsEnabled = false;

            int width = Math.Max(800, (int)DrawingCanvas.ActualWidth);
            int height = Math.Max(600, (int)DrawingCanvas.ActualHeight);

            var tempDir = Path.Combine(Path.GetTempPath(), "People", "Whiteboard");
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, $"whiteboard_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            CanvasDevice device = CanvasDevice.GetSharedDevice();
            using (var renderTarget = new CanvasRenderTarget(device, width, height, 96f))
            {
                using (var ds = renderTarget.CreateDrawingSession())
                {
                    ds.Clear(Colors.White);
                    RenderStrokesToSession(ds, _strokes);
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

    #region Pointer & Drawing Handlers

    private void DrawingCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(DrawingCanvas);
        var pos = new Vector2((float)pt.Position.X, (float)pt.Position.Y);

        if (_currentTool == WhiteBoardTool.Eraser)
        {
            PushUndoState();
            _isDrawing = true;
            DrawingCanvas.CapturePointer(e.Pointer);
            EraseAtPoint(pos);
            e.Handled = true;
            return;
        }

        PushUndoState();
        _isDrawing = true;
        DrawingCanvas.CapturePointer(e.Pointer);

        _activeStroke = new WhiteBoardStroke
        {
            Tool = _currentTool,
            Color = _currentColor,
            Width = _currentWidth
        };
        _activeStroke.Points.Add(pos);
        _strokes.Add(_activeStroke);

        DrawingCanvas.Invalidate();
        e.Handled = true;
    }

    private void DrawingCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing) return;

        var pt = e.GetCurrentPoint(DrawingCanvas);
        var pos = new Vector2((float)pt.Position.X, (float)pt.Position.Y);

        if (_currentTool == WhiteBoardTool.Eraser)
        {
            EraseAtPoint(pos);
            e.Handled = true;
            return;
        }

        if (_activeStroke != null)
        {
            if (_activeStroke.Points.Count == 0 || Vector2.Distance(_activeStroke.Points[^1], pos) >= 1.5f)
            {
                _activeStroke.Points.Add(pos);
                DrawingCanvas.Invalidate();
            }
        }
        e.Handled = true;
    }

    private void DrawingCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDrawing)
        {
            _isDrawing = false;
            DrawingCanvas.ReleasePointerCapture(e.Pointer);
            _activeStroke = null;
            DrawingCanvas.Invalidate();
            UpdateUndoRedoState();
        }
        e.Handled = true;
    }

    private void DrawingCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        DrawingCanvas_PointerReleased(sender, e);
    }

    private void DrawingCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        DrawingCanvas_PointerReleased(sender, e);
    }

    private void EraseAtPoint(Vector2 point)
    {
        float eraserRadius = Math.Max(16f, _currentWidth * 2.5f);
        bool modified = false;

        for (int i = _strokes.Count - 1; i >= 0; i--)
        {
            var stroke = _strokes[i];
            if (stroke.Points.Count == 0) continue;

            if (stroke.Points.Count == 1)
            {
                if (Vector2.Distance(stroke.Points[0], point) <= eraserRadius)
                {
                    _strokes.RemoveAt(i);
                    modified = true;
                }
                continue;
            }

            for (int j = 0; j < stroke.Points.Count - 1; j++)
            {
                if (DistanceToSegment(point, stroke.Points[j], stroke.Points[j + 1]) <= eraserRadius)
                {
                    _strokes.RemoveAt(i);
                    modified = true;
                    break;
                }
            }
        }

        if (modified)
        {
            DrawingCanvas.Invalidate();
        }
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float l2 = ab.LengthSquared();
        if (l2 < 0.0001f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f);
        Vector2 projection = a + t * ab;
        return Vector2.Distance(p, projection);
    }

    #endregion

    #region Rendering

    private void DrawingCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        RenderStrokesToSession(args.DrawingSession, _strokes);
    }

    private static void RenderStrokesToSession(CanvasDrawingSession ds, IEnumerable<WhiteBoardStroke> strokes)
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
            if (stroke.Points.Count == 0) continue;

            if (stroke.Tool == WhiteBoardTool.Highlighter)
            {
                float hlWidth = Math.Max(16f, stroke.Width * 3f);
                Color hlColor = Color.FromArgb(90, stroke.Color.R, stroke.Color.G, stroke.Color.B);

                RenderSmoothPath(ds, stroke.Points, hlColor, hlWidth, flatStyle);
            }
            else if (stroke.Tool == WhiteBoardTool.Pencil)
            {
                float pWidth = Math.Max(1.5f, stroke.Width * 0.75f);
                Color pColor = Color.FromArgb(170, stroke.Color.R, stroke.Color.G, stroke.Color.B);

                RenderSmoothPath(ds, stroke.Points, pColor, pWidth, roundStyle);
            }
            else
            {
                // Standard Pen
                RenderSmoothPath(ds, stroke.Points, stroke.Color, stroke.Width, roundStyle);
            }
        }
    }

    private static void RenderSmoothPath(
        CanvasDrawingSession ds,
        IReadOnlyList<Vector2> points,
        Color color,
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
            // Fallback to straight line segments if geometry construction encounters degenerate points
            for (int i = 0; i < points.Count - 1; i++)
            {
                ds.DrawLine(points[i], points[i + 1], color, strokeWidth, style);
            }
        }
    }

    #endregion
}
