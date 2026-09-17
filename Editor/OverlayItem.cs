using System.Windows.Media.Imaging;
using System.Windows;

namespace HamiPdf.Editor;

internal enum OverlayKind { Text, Image, Stamp, Highlight, Note, Drawing }

internal sealed class OverlayItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Page { get; set; }
    public OverlayKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 75;
    public string Text { get; set; } = "اكتب النص هنا";
    public double FontSize { get; set; } = 22;
    public string FontFamily { get; set; } = "Segoe UI";
    public string Color { get; set; } = "#12344B";
    public TextAlignment Alignment { get; set; } = TextAlignment.Right;
    public double Opacity { get; set; } = 1;
    public double StrokeWidth { get; set; } = 3;
    public StrokePoint[] Points { get; set; } = [];
    public bool HasText => Kind is OverlayKind.Text or OverlayKind.Stamp or OverlayKind.Note;
    public bool Bold { get; set; }
    public bool RightToLeft { get; set; } = true;
    public BitmapSource? Image { get; set; }
    public OverlayItem Copy()
    {
        var copy = (OverlayItem)MemberwiseClone();
        copy.Points = (StrokePoint[])Points.Clone();
        return copy; // The bitmap is frozen and can be shared safely.
    }
}
