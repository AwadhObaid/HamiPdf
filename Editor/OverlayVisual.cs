using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HamiPdf.Editor;

internal static class OverlayVisual
{
    public static FrameworkElement Create(OverlayItem item)
    {
        var visual = CreateContent(item);
        visual.Opacity = item.Opacity;
        return visual;
    }

    private static FrameworkElement CreateContent(OverlayItem item)
    {
        if (item.Kind == OverlayKind.Image)
            return new Image { Source = item.Image, Width = item.Width, Height = item.Height, Stretch = Stretch.Fill };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Color));
        if (item.Kind == OverlayKind.Highlight)
            return new Border { Width = item.Width, Height = item.Height, Background = brush };
        if (item.Kind == OverlayKind.Drawing)
        {
            double thickness = Math.Min(item.StrokeWidth, Math.Min(item.Width, item.Height));
            double half = thickness / 2;
            var line = new System.Windows.Shapes.Polyline
            {
                Stroke = brush, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Points = new PointCollection(item.Points.Select(p => new Point(
                    Math.Clamp(p.X * item.Width, half, item.Width-half),
                    Math.Clamp(p.Y * item.Height, half, item.Height-half))))
            };
            var canvas = new Canvas { Width = item.Width, Height = item.Height, ClipToBounds = true };
            canvas.Children.Add(line);
            return canvas;
        }
        var text = new TextBlock
        {
            Text = item.Text, FontFamily = new FontFamily(item.FontFamily), FontSize = item.FontSize,
            FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal, Foreground = brush,
            FlowDirection = item.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            TextAlignment = item.Alignment,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = item.Kind == OverlayKind.Stamp ? VerticalAlignment.Center : VerticalAlignment.Top
        };
        return new Border
        {
            Width = item.Width, Height = item.Height, ClipToBounds = true, Child = text,
            Padding = new Thickness(item.Kind is OverlayKind.Stamp or OverlayKind.Note ? 8 : 3),
            Background = item.Kind == OverlayKind.Note ? new SolidColorBrush(Color.FromRgb(255, 248, 197)) : null,
            BorderBrush = brush, BorderThickness = new Thickness(item.Kind == OverlayKind.Stamp ? 2 : 0),
            CornerRadius = new CornerRadius(item.Kind is OverlayKind.Stamp or OverlayKind.Note ? 5 : 0)
        };
    }

    public static BitmapSource Rasterize(OverlayItem item)
    {
        // Same visual tree for preview and export. Only overlays are rasterized; PDF content is retained.
        var visual = Create(item);
        visual.Measure(new Size(item.Width, item.Height));
        visual.Arrange(new Rect(0, 0, item.Width, item.Height));
        visual.UpdateLayout();
        double scale = Math.Min(4, Math.Min(4096 / item.Width, 4096 / item.Height));
        scale = Math.Min(scale, Math.Sqrt(12_000_000 / (item.Width * item.Height)));
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(item.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(item.Height * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 2400;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
