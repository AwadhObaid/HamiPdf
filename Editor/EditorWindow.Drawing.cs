using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HamiPdf.Editor;

public partial class EditorWindow
{
    private enum DrawMode { None, Highlight, Pen }
    private DrawMode drawMode;
    private readonly List<StrokePoint> draftPoints = [];
    private FrameworkElement? draftVisual;

    private void Highlight_Click(object sender, RoutedEventArgs e) => SetDrawMode(DrawMode.Highlight);
    private void Pen_Click(object sender, RoutedEventArgs e) => SetDrawMode(DrawMode.Pen);
    private void Note_Click(object sender, RoutedEventArgs e) => Add(new OverlayItem
    {
        Kind = OverlayKind.Note, Text = "اكتب ملاحظتك هنا", Width = 220, Height = 120,
        FontSize = 18, Color = "#12344B"
    });

    private void SetDrawMode(DrawMode mode)
    {
        if (busy || session == null || !CommitProperties()) return;
        bool same = drawMode == mode;
        CancelDrawing();
        if (same) return;
        drawMode = mode;
        OverlayCanvas.Cursor = Cursors.Cross;
        OverlayCanvas.ForceCursor = true;
        var active = new SolidColorBrush(Color.FromRgb(221,243,240));
        if (mode == DrawMode.Pen) PenButton.Background = active;
        else HighlightButton.Background = active;
        EditorStatus.Text = mode == DrawMode.Pen
            ? "اسحب بالقلم فوق الصفحة، ثم ارفع المؤشر لإنهاء الرسم. Esc للإلغاء."
            : "اسحب مستطيلًا فوق المنطقة المطلوبة للتظليل. Esc للإلغاء.";
    }

    private StrokePoint Pointer(MouseEventArgs e)
    {
        var p = e.GetPosition(OverlayCanvas);
        return StrokeGeometry.Clamp(new StrokePoint(p.X,p.Y), CurrentPage.DisplayWidth, CurrentPage.DisplayHeight);
    }

    private void Draw_Start(object sender, MouseButtonEventArgs e)
    {
        if (busy || session == null || drawMode == DrawMode.None) return;
        e.Handled = true;
        if (!CommitProperties()) return;
        Select(null);
        draftPoints.Clear(); draftPoints.Add(Pointer(e));
        if (drawMode == DrawMode.Highlight)
            draftVisual = new Border { Background = Brushes.Gold, Opacity = .3, IsHitTestVisible = false };
        else
            draftVisual = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(21,101,192)), StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
            };
        OverlayCanvas.Children.Add(draftVisual);
        if (!OverlayCanvas.CaptureMouse()) CancelDrawing();
    }

    private void Draw_Move(object sender, MouseEventArgs e)
    {
        if (draftVisual == null || !OverlayCanvas.IsMouseCaptured) return;
        e.Handled = true;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelDrawing(); return; }
        var p = Pointer(e);
        if (drawMode == DrawMode.Highlight)
        {
            var start = draftPoints[0];
            Canvas.SetLeft(draftVisual, Math.Min(start.X,p.X));
            Canvas.SetTop(draftVisual, Math.Min(start.Y,p.Y));
            draftVisual.Width = Math.Abs(start.X-p.X); draftVisual.Height = Math.Abs(start.Y-p.Y);
        }
        else if (draftVisual is Polyline line)
        {
            var last = draftPoints[^1];
            if (Math.Abs(last.X-p.X)+Math.Abs(last.Y-p.Y) < .5 || draftPoints.Count >= 6000) return;
            draftPoints.Add(p);
            line.Points = new PointCollection(draftPoints.Select(x => new Point(x.X,x.Y)));
        }
    }

    private void Draw_End(object sender, MouseButtonEventArgs e)
    {
        if (draftVisual == null || session == null) return;
        e.Handled = true;
        OverlayItem? item = null;
        var last = Pointer(e);
        if (drawMode == DrawMode.Highlight)
        {
            var first = draftPoints[0];
            double width = Math.Abs(last.X-first.X), height = Math.Abs(last.Y-first.Y);
            if (width >= 2 && height >= 2)
                item = new OverlayItem { Kind = OverlayKind.Highlight, Text = "تظليل", Color = "#FFD600", Opacity = .3,
                    X = Math.Min(first.X,last.X), Y = Math.Min(first.Y,last.Y), Width = width, Height = height };
        }
        else
        {
            if (draftPoints.Count < 6000) draftPoints.Add(last);
            var first = draftPoints[0];
            if (draftPoints.Any(p => Math.Abs(p.X-first.X)+Math.Abs(p.Y-first.Y) >= 2))
            {
                var layout = StrokeGeometry.Layout(draftPoints,CurrentPage.DisplayWidth,CurrentPage.DisplayHeight,3);
                item = new OverlayItem { Kind = OverlayKind.Drawing, Text = "رسم حر", Color = "#1565C0", StrokeWidth = 3,
                    X = layout.X, Y = layout.Y, Width = layout.Width, Height = layout.Height, Points = layout.Points };
            }
        }
        CancelDrawing();
        if (item == null) return;
        BeginChange(); item.Page = pageIndex; items.Add(item); selectedId = item.Id; Redraw();
        EditorStatus.Text = "تمت الإضافة. يمكنك تحريكها وتغيير حجمها ولونها أو التراجع عنها.";
    }

    private void Draw_CaptureLost(object sender, MouseEventArgs e)
    {
        if (draftVisual != null && !OverlayCanvas.IsMouseCaptured) CancelDrawing();
    }

    private void CancelDrawing()
    {
        // Clear first: ReleaseMouseCapture raises LostMouseCapture synchronously.
        var visual = draftVisual; draftVisual = null;
        if (visual != null) OverlayCanvas.Children.Remove(visual);
        draftPoints.Clear(); drawMode = DrawMode.None;
        if (OverlayCanvas.IsMouseCaptured) OverlayCanvas.ReleaseMouseCapture();
        OverlayCanvas.Cursor = Cursors.Arrow; OverlayCanvas.ForceCursor = false;
        PenButton.Background = HighlightButton.Background = Brushes.White;
    }
}
