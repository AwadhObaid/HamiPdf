using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HamiPdf.Editor;

internal sealed class StoredOverlay
{
    public StoredOverlay() { }
    public Guid Id { get; set; }
    public int Page { get; set; }
    public OverlayKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; }
    public string FontFamily { get; set; } = "Segoe UI";
    public string Color { get; set; } = "#12344B";
    public TextAlignment Alignment { get; set; }
    public double Opacity { get; set; }
    public double StrokeWidth { get; set; }
    public bool Bold { get; set; }
    public bool RightToLeft { get; set; }
    public StrokePoint[] Points { get; set; } = [];
    public byte[]? ImagePng { get; set; }
}

internal sealed class StoredProject
{
    public StoredProject() { }
    public int Version { get; set; } = 1;
    public string SourceName { get; set; } = "document.pdf";
    public int CurrentPage { get; set; }
    public byte[] SourcePdf { get; set; } = [];
    public List<StoredOverlay> Overlays { get; set; } = [];
}

internal static class ProjectArchive
{
    private const long MaxPayload = 256L * 1024 * 1024;
    private const string EntryName = "project.json";
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 32 };

    public static StoredProject Capture(string sourceName, byte[] source, int page, IEnumerable<OverlayItem> items)
    {
        var project = new StoredProject { SourceName = Path.GetFileName(sourceName), SourcePdf = source, CurrentPage = page };
        foreach (var item in items)
        {
            byte[]? image = null;
            if (item.Image != null)
            {
                if ((long)item.Image.PixelWidth * item.Image.PixelHeight > 24_000_000)
                    throw new InvalidDataException("أبعاد إحدى الصور تتجاوز 24 مليون بكسل؛ استخدم نسخة أصغر لحفظ المشروع.");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(item.Image));
                using var stream = new MemoryStream(); encoder.Save(stream); image = stream.ToArray();
            }
            project.Overlays.Add(new StoredOverlay
            {
                Id=item.Id, Page=item.Page, Kind=item.Kind, X=item.X, Y=item.Y, Width=item.Width, Height=item.Height,
                Text=item.Text, FontSize=item.FontSize, FontFamily=item.FontFamily, Color=item.Color, Alignment=item.Alignment,
                Opacity=item.Opacity, StrokeWidth=item.StrokeWidth, Bold=item.Bold, RightToLeft=item.RightToLeft,
                Points=(StrokePoint[])item.Points.Clone(), ImagePng=image
            });
        }
        return project;
    }

    private static bool Range(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
    private static void Check(StoredProject project)
    {
        if (project.Version != 1) throw new InvalidDataException("إصدار ملف المشروع غير مدعوم.");
        if (project.SourcePdf == null || project.SourcePdf.Length < 5 || project.SourcePdf.Length > 128L*1024*1024)
            throw new InvalidDataException("بيانات المستند في المشروع غير صالحة أو تتجاوز الحد المدعوم.");
        if (project.SourceName == null || project.SourceName.Length > 255 || Path.GetFileName(project.SourceName) != project.SourceName)
            throw new InvalidDataException("اسم المستند في المشروع غير صالح.");
        if (project.CurrentPage < 0 || project.Overlays == null || project.Overlays.Count > 1000)
            throw new InvalidDataException("محتويات المشروع غير صالحة أو تتجاوز الحد المدعوم.");
        var ids = new HashSet<Guid>();
        foreach (var item in project.Overlays)
        {
            if (item == null || item.Id == Guid.Empty || !ids.Add(item.Id) || item.Page < 0 || !Enum.IsDefined(item.Kind)
                || !Enum.IsDefined(item.Alignment) || !Range(item.X,0,1_000_000) || !Range(item.Y,0,1_000_000)
                || !Range(item.Width,1,1_000_000) || !Range(item.Height,1,1_000_000) || !Range(item.FontSize,6,200)
                || !Range(item.Opacity,.1,1) || !Range(item.StrokeWidth,1,20)
                || item.Text == null || item.Text.Length>4000 || string.IsNullOrWhiteSpace(item.FontFamily) || item.FontFamily.Length>200
                || item.Color == null || !Regex.IsMatch(item.Color,"^#[0-9A-Fa-f]{6}$"))
                throw new InvalidDataException("أحد عناصر المشروع يحتوي خصائص غير صالحة.");
            if (item.Points == null || item.Points.Length>6000 || item.Points.Any(p=>!Range(p.X,0,1)||!Range(p.Y,0,1)))
                throw new InvalidDataException("نقاط الرسم في المشروع غير صالحة.");
            if (item.Kind == OverlayKind.Drawing && item.Points.Length<2)
                throw new InvalidDataException("عنصر رسم بدون نقاط كافية.");
            if (item.Kind == OverlayKind.Image && (item.ImagePng == null || item.ImagePng.Length == 0 || item.ImagePng.Length>32L*1024*1024))
                throw new InvalidDataException("صورة المشروع غير صالحة أو كبيرة جدًا.");
        }
    }

    public static StoredProject Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length>MaxPayload) throw new InvalidDataException("حجم المشروع يتجاوز 256 ميجابايت.");
        using var zip = new ZipArchive(stream,ZipArchiveMode.Read);
        if (zip.Entries.Count != 1 || zip.Entries[0].FullName != EntryName)
            throw new InvalidDataException("هذا الملف ليس مشروع الحامي PDF صالحًا.");
        var entry = zip.Entries[0];
        if (entry.Length<=0 || entry.Length>MaxPayload) throw new InvalidDataException("حجم بيانات المشروع غير مدعوم.");
        using var data = entry.Open();
        var project = JsonSerializer.Deserialize<StoredProject>(data,Options) ?? throw new InvalidDataException("المشروع فارغ.");
        Check(project); return project;
    }

    public static List<OverlayItem> Restore(StoredProject project, IReadOnlyList<PageGeometry> pages)
    {
        Check(project);
        if (project.CurrentPage>=pages.Count) throw new InvalidDataException("رقم الصفحة المحفوظ غير صالح.");
        var items = new List<OverlayItem>();
        foreach (var saved in project.Overlays)
        {
            if (saved.Page>=pages.Count || saved.X+saved.Width>pages[saved.Page].DisplayWidth+.02 || saved.Y+saved.Height>pages[saved.Page].DisplayHeight+.02)
                throw new InvalidDataException("موضع إضافة خارج حدود الصفحة.");
            BitmapSource? bitmap = null;
            if (saved.Kind == OverlayKind.Image)
            {
                using var memory = new MemoryStream(saved.ImagePng!,false);
                var decoder = BitmapDecoder.Create(memory,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                if ((long)frame.PixelWidth*frame.PixelHeight>24_000_000)
                    throw new InvalidDataException("أبعاد صورة المشروع كبيرة جدًا.");
                memory.Position=0;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;
                image.StreamSource=memory; image.EndInit(); image.Freeze(); bitmap=image;
            }
            items.Add(new OverlayItem
            {
                Id=saved.Id, Page=saved.Page, Kind=saved.Kind, X=saved.X, Y=saved.Y, Width=saved.Width, Height=saved.Height,
                Text=saved.Text, FontSize=saved.FontSize, FontFamily=saved.FontFamily, Color=saved.Color, Alignment=saved.Alignment,
                Opacity=saved.Opacity, StrokeWidth=saved.StrokeWidth, Bold=saved.Bold, RightToLeft=saved.RightToLeft,
                Points=(StrokePoint[])saved.Points.Clone(), Image=bitmap
            });
        }
        return items;
    }

    public static void Save(StoredProject project, string path)
    {
        Check(project);
        if (!string.Equals(Path.GetExtension(path),".hamipdf",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("امتداد ملف المشروع يجب أن يكون .hamipdf");
        string fullPath=Path.GetFullPath(path);
        string temporary=Path.Combine(Path.GetDirectoryName(fullPath)!,".hamipdf-project-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using (var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using (var zip=new ZipArchive(file,ZipArchiveMode.Create))
            using (var data=zip.CreateEntry(EntryName,CompressionLevel.Optimal).Open())
                JsonSerializer.Serialize(data,project,Options);
            // Reopen before commit: a failed save leaves the existing project untouched.
            var verify=Load(temporary);
            if (!verify.SourcePdf.AsSpan().SequenceEqual(project.SourcePdf) || verify.Overlays.Count!=project.Overlays.Count)
                throw new InvalidDataException("فشل التحقق من المشروع المحفوظ.");
            if (File.Exists(fullPath)) File.Replace(temporary,fullPath,fullPath+".bak",ignoreMetadataErrors:true);
            else File.Move(temporary,fullPath,overwrite:false);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
