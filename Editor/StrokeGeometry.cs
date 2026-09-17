namespace HamiPdf.Editor;

internal readonly record struct StrokePoint(double X, double Y);
internal sealed record StrokeLayout(double X, double Y, double Width, double Height, StrokePoint[] Points);

internal static class StrokeGeometry
{
    public static StrokePoint Clamp(StrokePoint p, double width, double height) =>
        new(Math.Clamp(p.X, 0, width), Math.Clamp(p.Y, 0, height));

    public static StrokeLayout Layout(IReadOnlyList<StrokePoint> points, double pageWidth, double pageHeight, double thickness)
    {
        if (points.Count < 2 || pageWidth < 1 || pageHeight < 1 || !double.IsFinite(thickness) || thickness <= 0)
            throw new ArgumentException("Invalid stroke geometry.");
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new ArgumentException("Invalid point.");
        var clean = points.Select(p => Clamp(p, pageWidth, pageHeight)).ToArray();
        double padding = thickness / 2 + 1;
        double left = Math.Max(0, clean.Min(p => p.X) - padding), top = Math.Max(0, clean.Min(p => p.Y) - padding);
        double right = Math.Min(pageWidth, clean.Max(p => p.X) + padding), bottom = Math.Min(pageHeight, clean.Max(p => p.Y) + padding);
        double width = Math.Max(1, right - left), height = Math.Max(1, bottom - top);
        left = Math.Min(left, pageWidth-width); top = Math.Min(top, pageHeight-height);
        return new(left, top, width, height, clean.Select(p => new StrokePoint((p.X-left)/width, (p.Y-top)/height)).ToArray());
    }
}
