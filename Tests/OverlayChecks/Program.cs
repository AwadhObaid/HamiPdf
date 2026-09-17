using System.Windows;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HamiPdf.Editor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var highlight = OverlayVisual.Rasterize(new OverlayItem { Kind=OverlayKind.Highlight,Width=100,Height=30,Color="#FFD600",Opacity=.3 });
        byte[] pixels=Pixels(highlight);
        int center=((highlight.PixelHeight/2)*highlight.PixelWidth+highlight.PixelWidth/2)*4;
        Require(pixels[center+3] is >=75 and <=78,"Highlight opacity must survive raster export");
        Require(pixels[center]<pixels[center+1] && pixels[center+1]<pixels[center+2],"Yellow highlight pixel channels");
        var bounds=new List<int>();
        foreach(var alignment in new[]{TextAlignment.Left,TextAlignment.Center,TextAlignment.Right})
        {
            var b=OverlayVisual.Rasterize(new OverlayItem { Text="ABC",Width=240,Height=60,FontSize=20,RightToLeft=false,Alignment=alignment });
            var bytes=Pixels(b); int left=b.PixelWidth;
            for(int y=0;y<b.PixelHeight;y++) for(int x=0;x<b.PixelWidth;x++)
                if(bytes[(y*b.PixelWidth+x)*4+3]>30) left=Math.Min(left,x);
            Require(left<b.PixelWidth,"Text must render visibly"); bounds.Add(left);
        }
        Require(bounds[0]<bounds[1] && bounds[1]<bounds[2],"Alignment must move rendered text left/center/right");
        var item=new OverlayItem { Kind=OverlayKind.Drawing,Width=100,Height=60,Points=[new(.1,.1),new(.9,.9)],Color="#1565C0" };
        var copy=item.Copy();copy.Points[0]=new(0,0);
        Require(item.Points[0]==new StrokePoint(.1,.1),"Undo snapshot points must be isolated");
        var ink=Pixels(OverlayVisual.Rasterize(item));
        Require(Enumerable.Range(0,ink.Length/4).Count(i=>ink[i*4+3]>30)>20,"Freehand ink must render");
        var noteBitmap=OverlayVisual.Rasterize(new OverlayItem { Kind=OverlayKind.Note,Text="Note",Width=120,Height=70 });
        var note=Pixels(noteBitmap);
        int background=((noteBitmap.PixelHeight-8)*noteBitmap.PixelWidth+noteBitmap.PixelWidth/2)*4;
        Require(note[background+3]>200,"Note must have a printable background");
        CheckProject();
        Console.WriteLine("PASS: WPF highlight alpha/color, rendered alignment, undo snapshot isolation, freehand ink and note background.");
    }
    private static void CheckProject()
    {
        var folder=Path.Combine(Path.GetTempPath(),"HamiPdf-ProjectChecks-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,
                new byte[]{0,0,255,255,0,255,0,255,255,0,0,255,0,0,0,0},8);
            bitmap.Freeze();
            var items=Enum.GetValues<OverlayKind>().Select(kind=>new OverlayItem
            {
                Kind=kind,Text="اختبار مشروع ١٢٣",X=15,Y=20,Width=150,Height=70,
                Alignment=TextAlignment.Center,RightToLeft=true,Opacity=.7,StrokeWidth=4,
                Points=kind==OverlayKind.Drawing ? new StrokePoint[]{new(.1,.2),new(.8,.9)} : [],
                Image=kind==OverlayKind.Image ? bitmap : null
            }).ToList();
            // Archive round-trip checks opaque source bytes; PDF parsing is covered separately.
            byte[] source=Encoding.ASCII.GetBytes("%PDF-1.7\narchive-byte-preservation-fixture");
            var project=ProjectArchive.Capture("source.pdf",source,0,items);
            string path=Path.Combine(folder,"test.hamipdf");
            ProjectArchive.Save(project,path);
            var loaded=ProjectArchive.Load(path);
            Require(loaded.SourcePdf.SequenceEqual(source),"Project embeds original PDF bytes");
            var restored=ProjectArchive.Restore(loaded,new[]{new PageGeometry(0,0,595,842,0)});
            Require(restored.Count==items.Count,"All overlay kinds restored");
            for(int i=0;i<items.Count;i++)
            {
                Require(restored[i].Id==items[i].Id && restored[i].Kind==items[i].Kind,"Identity and kind round-trip");
                Require(restored[i].Text==items[i].Text && restored[i].Alignment==TextAlignment.Center && restored[i].RightToLeft,"Arabic/alignment round-trip");
                Require(restored[i].Opacity==.7 && restored[i].StrokeWidth==4 && restored[i].X==15,"Style and position round-trip");
                Require(restored[i].Points.SequenceEqual(items[i].Points),"Freehand points round-trip");
            }
            Require(restored.Single(i=>i.Kind==OverlayKind.Image).Image?.PixelWidth==2,"Embedded image restored");
            project.Overlays[0].Text="second";ProjectArchive.Save(project,path);
            Require(ProjectArchive.Load(path+".bak").Overlays[0].Text=="اختبار مشروع ١٢٣","Previous project backup");
            project.Overlays[0].Text="third";ProjectArchive.Save(project,path);
            Require(ProjectArchive.Load(path+".bak").Overlays[0].Text=="second","Backup refreshed on repeated saves");
            byte[] prior=File.ReadAllBytes(path);project.Version=99;
            bool rejected=false;try{ProjectArchive.Save(project,path);}catch(InvalidDataException){rejected=true;}
            Require(rejected && prior.SequenceEqual(File.ReadAllBytes(path)),"Invalid project cannot overwrite a valid save");
            Console.WriteLine("PASS: portable project round-trip, Arabic/styles/images/strokes, repeated backup and failed-save preservation.");
        }
        finally { Directory.Delete(folder,true); }
    }

    private static byte[] Pixels(BitmapSource b)
    { var bytes=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(bytes,b.PixelWidth*4,0);return bytes; }
    private static void Require(bool condition,string message)
    { if(!condition)throw new Exception(message); }
}
