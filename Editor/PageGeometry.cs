namespace HamiPdf.Editor;

// PDF default user space is bottom-left/up. Editor space is visible-page top-left/down.
internal sealed record PageGeometry(double Left, double Bottom, double Width, double Height, int Rotation)
{
    public double DisplayWidth => Rotation is 90 or 270 ? Height : Width;
    public double DisplayHeight => Rotation is 90 or 270 ? Width : Height;

    public (double X, double Y) Map(double x, double y)
    {
        double u = x / DisplayWidth, v = y / DisplayHeight;
        return Rotation switch
        {
            90 => (Left + v * Width, Bottom + u * Height),
            180 => (Left + (1 - u) * Width, Bottom + v * Height),
            270 => (Left + (1 - v) * Width, Bottom + (1 - u) * Height),
            _ => (Left + u * Width, Bottom + (1 - v) * Height)
        };
    }

    // PDF image unit square: (0,0)=bottom left; (1,1)=top right.
    public double[] ImageMatrix(double x, double y, double width, double height)
    {
        var tl = Map(x, y);
        var tr = Map(x + width, y);
        var bl = Map(x, y + height);
        return [tr.X - tl.X, tr.Y - tl.Y, tl.X - bl.X, tl.Y - bl.Y, bl.X, bl.Y];
    }
}
