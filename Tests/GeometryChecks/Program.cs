using HamiPdf.Editor;

static void Near(double expected, double actual)
{
    if (Math.Abs(expected-actual) > .00001) throw new Exception($"Expected {expected}, actual {actual}");
}
// Independent expected PDF-space coordinates for the four visible corners.
var expectations = new Dictionary<int, (double X, double Y)[]>
{
    [0] = [(20,330),(220,330),(20,30),(220,30)],
    [90] = [(20,30),(20,330),(220,30),(220,330)],
    [180] = [(220,30),(20,30),(220,330),(20,330)],
    [270] = [(220,330),(220,30),(20,330),(20,30)]
};
int checks = 0;
foreach (var (rotation, corners) in expectations)
{
    var page = new PageGeometry(20,30,200,300,rotation);
    (double X,double Y)[] inputs = [(0,0),(page.DisplayWidth,0),(0,page.DisplayHeight),(page.DisplayWidth,page.DisplayHeight)];
    for (int i=0;i<4;i++)
    {
        var point = page.Map(inputs[i].X,inputs[i].Y);
        Near(corners[i].X,point.X); Near(corners[i].Y,point.Y); checks++;
    }
    var matrix = page.ImageMatrix(10,15,40,25);
    var tl=page.Map(10,15); var tr=page.Map(50,15); var bl=page.Map(10,40);
    Near(bl.X,matrix[4]); Near(bl.Y,matrix[5]);
    Near(tl.X,matrix[2]+matrix[4]); Near(tl.Y,matrix[3]+matrix[5]);
    Near(tr.X,matrix[0]+matrix[2]+matrix[4]); Near(tr.Y,matrix[1]+matrix[3]+matrix[5]);
    // A positive determinant preserves image orientation in PDF user space.
    Near(40*25, matrix[0]*matrix[3]-matrix[1]*matrix[2]); checks++;
}
Console.WriteLine($"PASS: {checks} page-coordinate and image-matrix checks.");

// Freehand coordinates must survive bounding-box normalization and resizing.
var stroke = StrokeGeometry.Layout([new(-2,0),new(50,70),new(1000,1000)],200,300,3);
if (stroke.X<0 || stroke.Y<0 || stroke.X+stroke.Width>200 || stroke.Y+stroke.Height>300)
    throw new Exception("Stroke escaped page bounds");
Near(0, stroke.X+stroke.Points[0].X*stroke.Width);
Near(0, stroke.Y+stroke.Points[0].Y*stroke.Height);
Near(50, stroke.X+stroke.Points[1].X*stroke.Width);
Near(70, stroke.Y+stroke.Points[1].Y*stroke.Height);
Near(200, stroke.X+stroke.Points[2].X*stroke.Width);
Near(300, stroke.Y+stroke.Points[2].Y*stroke.Height);
var horizontal = StrokeGeometry.Layout([new(10,20),new(100,20)],200,300,3);
if(horizontal.Width<=0 || horizontal.Height<=0) throw new Exception("Horizontal stroke has empty bounds");
var vertical = StrokeGeometry.Layout([new(10,20),new(10,120)],200,300,3);
if(vertical.Width<=0 || vertical.Height<=0) throw new Exception("Vertical stroke has empty bounds");
Console.WriteLine("PASS: stroke clipping, coordinate round-trip and horizontal/vertical stroke bounds.");
