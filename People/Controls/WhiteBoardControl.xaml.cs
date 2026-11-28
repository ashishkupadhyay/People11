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
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace People.Controls;

public sealed partial class WhiteBoardControl : UserControl
{
    public event EventHandler? CancelClicked;
    public event EventHandler<(string ImagePath, string? Caption)>? SendImageClicked;

    private readonly Stack<List<Windows.UI.Input.Inking.InkStroke>> _undoSnapshots = new();
    private readonly Stack<List<Windows.UI.Input.Inking.InkStroke>> _redoSnapshots = new();
    private List<Windows.UI.Input.Inking.InkStroke> _currentSnapshot = new();
    private bool _isPresenterHooked;
    private bool _isApplyingHistory;

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
        var presenter = MyInkCanvas?.InkPresenter;
        if (presenter == null) return;

        presenter.InputDeviceTypes =
            CoreInputDeviceTypes.Pen | CoreInputDeviceTypes.Mouse | CoreInputDeviceTypes.Touch;

        if (!_isPresenterHooked)
        {
            presenter.StrokesCollected += (_, _) =>
            {
                if (!_isApplyingHistory) RecordStrokeSnapshot();
            };
            presenter.StrokesErased += (_, _) =>
            {
                if (!_isApplyingHistory) RecordStrokeSnapshot();
            };
            if (MyInkToolbar != null)
            {
                MyInkToolbar.EraseAllClicked += MyInkToolbar_EraseAllClicked;
            }
            this.PreviewKeyDown += WhiteBoardControl_PreviewKeyDown;
            _isPresenterHooked = true;
            UpdateUndoRedoButtons();
        }
    }

    private void MyInkToolbar_EraseAllClicked(InkToolbar sender, object args)
    {
        if (_currentSnapshot.Count == 0) return;
        DispatcherQueue?.TryEnqueue(RecordStrokeSnapshot);
    }

    private void RecordStrokeSnapshot()
    {
        var container = MyInkCanvas?.InkPresenter?.StrokeContainer;
        if (container == null) return;

        var liveStrokes = container.GetStrokes();
        if (liveStrokes.Count == 0 && _currentSnapshot.Count == 0)
        {
            return;
        }

        _undoSnapshots.Push(_currentSnapshot);
        _redoSnapshots.Clear();
        _currentSnapshot = liveStrokes.Select(s => s.Clone()).ToList();
        UpdateUndoRedoButtons();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoSnapshots.Count == 0) return;

        _redoSnapshots.Push(_currentSnapshot);
        _currentSnapshot = _undoSnapshots.Pop();
        RestoreSnapshot(_currentSnapshot);
        UpdateUndoRedoButtons();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoSnapshots.Count == 0) return;

        _undoSnapshots.Push(_currentSnapshot);
        _currentSnapshot = _redoSnapshots.Pop();
        RestoreSnapshot(_currentSnapshot);
        UpdateUndoRedoButtons();
    }

    private void RestoreSnapshot(List<Windows.UI.Input.Inking.InkStroke> snapshot)
    {
        var container = MyInkCanvas?.InkPresenter?.StrokeContainer;
        if (container == null) return;

        _isApplyingHistory = true;
        try
        {
            container.Clear();
            if (snapshot.Count > 0)
            {
                container.AddStrokes(snapshot.Select(s => s.Clone()));
            }
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }

    private void UpdateUndoRedoButtons()
    {
        if (UndoButton != null)
        {
            UndoButton.IsEnabled = _undoSnapshots.Count > 0;
        }
        if (RedoButton != null)
        {
            RedoButton.IsEnabled = _redoSnapshots.Count > 0;
        }
    }

    private void WhiteBoardControl_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (CaptionTextBox != null && CaptionTextBox.FocusState != FocusState.Unfocused)
        {
            return;
        }

        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        bool isCtrlDown = (ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        if (!isCtrlDown) return;

        if (e.Key == VirtualKey.Z)
        {
            Undo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Y)
        {
            Redo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void CaptionTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter &&
            MyInkCanvas?.InkPresenter?.StrokeContainer?.GetStrokes().Count > 0)
        {
            e.Handled = true;
            Send_Click(this, new RoutedEventArgs());
        }
    }

    public void Reset()
    {
        _isApplyingHistory = true;
        try
        {
            MyInkCanvas?.InkPresenter?.StrokeContainer?.Clear();
            _undoSnapshots.Clear();
            _redoSnapshots.Clear();
            _currentSnapshot.Clear();
            if (CaptionTextBox != null)
            {
                CaptionTextBox.Text = string.Empty;
            }
            UpdateUndoRedoButtons();
        }
        finally
        {
            _isApplyingHistory = false;
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

            float actualWidth = (float)(MyInkCanvas?.ActualWidth ?? 800);
            float actualHeight = (float)(MyInkCanvas?.ActualHeight ?? 600);
            if (actualWidth <= 0) actualWidth = 800f;
            if (actualHeight <= 0) actualHeight = 600f;

            // Proportional supersampling for small canvas/flyout screens without letterbox margins
            float minDim = Math.Min(actualWidth, actualHeight);
            float scale = minDim < 600f ? Math.Min(2.0f, 600f / minDim) : 1.0f;
            int width = (int)Math.Ceiling(actualWidth * scale);
            int height = (int)Math.Ceiling(actualHeight * scale);

            var tempDir = Path.Combine(Path.GetTempPath(), "People", "Whiteboard");
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, $"whiteboard_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            CanvasDevice device = CanvasDevice.GetSharedDevice();
            using (var renderTarget = new CanvasRenderTarget(device, width, height, 96f))
            {
                using (var ds = renderTarget.CreateDrawingSession())
                {
                    ds.Clear(Colors.White);
                    if (scale != 1.0f)
                    {
                        ds.Transform = Matrix3x2.CreateScale(scale);
                    }
                    RenderNativeStrokes(ds, strokes);
                }

                using (var fileStream = File.Create(filePath))
                using (var outStream = fileStream.AsRandomAccessStream())
                {
                    await renderTarget.SaveAsync(outStream, CanvasBitmapFileFormat.Png);
                }
            }

            string? caption = string.IsNullOrWhiteSpace(CaptionTextBox?.Text)
                ? null
                : CaptionTextBox.Text.Trim();

            SendImageClicked?.Invoke(this, (filePath, caption));
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
