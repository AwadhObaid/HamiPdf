using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace HamiPdf.Editor;

internal sealed record RasterOverlay(OverlayItem Item, BitmapSource Bitmap);

internal static class PdfExport
{
    private static PdfItem? Resolve(PdfItem? value) => value is PdfReference reference ? reference.Value : value;

    private static PdfDictionary? OwnerOf(PdfDictionary start, string key)
    {
        PdfDictionary? current = start;
        for (int depth = 0; current != null && depth < 100; depth++)
        {
            if (current.Elements.ContainsKey(key)) return current;
            current = Resolve(current.Elements["/Parent"]) as PdfDictionary;
        }
        return null;
    }

    private static PageGeometry Geometry(PdfPage page)
    {
        var owner = OwnerOf(page, "/MediaBox") ?? throw new InvalidDataException("صفحة بدون أبعاد.");
        var media = owner.Elements.GetRectangle("/MediaBox");
        var cropOwner = OwnerOf(page, "/CropBox");
        var crop = cropOwner == null ? media : cropOwner.Elements.GetRectangle("/CropBox");
        double left = Math.Max(media.X1, crop.X1), bottom = Math.Max(media.Y1, crop.Y1);
        double width = Math.Min(media.X2, crop.X2) - left, height = Math.Min(media.Y2, crop.Y2) - bottom;
        var rotateOwner = OwnerOf(page, "/Rotate");
        int rotation = ((rotateOwner?.Elements.GetInteger("/Rotate") ?? 0) % 360 + 360) % 360;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1 || rotation % 90 != 0)
            throw new InvalidDataException("أبعاد الصفحة أو تدويرها غير مدعوم.");
        return new PageGeometry(left, bottom, width, height, rotation);
    }

    public static IReadOnlyList<PageGeometry> ReadGeometry(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        if (!document.SecuritySettings.PermitModifyDocument)
            throw new InvalidDataException("لا يسمح المستند بالتعديل. استخدم نسخة تسمح بالتعديل.");
        return document.Pages.Cast<PdfPage>().Select(Geometry).ToArray();
    }

    private static PdfDictionary CopyDictionary(PdfDocument document, PdfDictionary? source)
    {
        var copy = new PdfDictionary(document);
        if (source != null)
            foreach (var pair in source.Elements) copy.Elements[pair.Key] = pair.Value;
        return copy;
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true)) zlib.Write(bytes);
        return output.ToArray();
    }

    private static PdfDictionary ImageObject(PdfDocument doc, int width, int height, byte[] bytes, bool gray)
    {
        var image = new PdfDictionary(doc);
        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", width); image.Elements.SetInteger("/Height", height);
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetName("/ColorSpace", gray ? "/DeviceGray" : "/DeviceRGB");
        image.CreateStream(Compress(bytes));
        image.Elements.SetName("/Filter", "/FlateDecode");
        doc.Internals.AddObject(image);
        return image;
    }

    internal static PdfDictionary Embed(PdfDocument doc, BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        byte[] pixels = new byte[checked(width * height * 4)];
        bitmap.CopyPixels(pixels, width * 4, 0);
        byte[] rgb = new byte[width * height * 3], alpha = new byte[width * height];
        for (int i = 0; i < alpha.Length; i++)
        {
            rgb[i * 3] = pixels[i * 4 + 2]; rgb[i * 3 + 1] = pixels[i * 4 + 1]; rgb[i * 3 + 2] = pixels[i * 4];
            alpha[i] = pixels[i * 4 + 3];
        }
        var mask = ImageObject(doc, width, height, alpha, true);
        var image = ImageObject(doc, width, height, rgb, false);
        image.Elements["/SMask"] = mask.Reference!;
        return image;
    }

    public static void Save(byte[] original, string outputPath, IReadOnlyList<RasterOverlay> overlays)
    {
        using var stream = new MemoryStream(original, false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        document.Version = Math.Max(document.Version, 14); // Soft masks require PDF 1.4.
        foreach (var group in overlays.GroupBy(x => x.Item.Page))
        {
            var page = document.Pages[group.Key];
            var geometry = Geometry(page);
            var inherited = OwnerOf(page, "/Resources");
            var resources = CopyDictionary(document, inherited == null ? null : Resolve(inherited.Elements["/Resources"]) as PdfDictionary);
            var objects = CopyDictionary(document, Resolve(resources.Elements["/XObject"]) as PdfDictionary);
            resources.Elements["/XObject"] = objects;
            page.Elements["/Resources"] = resources;
            // Isolate the previous content's graphics state before appending new content.
            var prefix = new PdfDictionary(document);
            prefix.CreateStream(Encoding.ASCII.GetBytes("q\n"));
            document.Internals.AddObject(prefix);
            var commands = new StringBuilder("\nQ\n");
            foreach (var overlay in group)
            {
                var item = overlay.Item;
                string name = "/Hami" + Guid.NewGuid().ToString("N");
                objects.Elements[name] = Embed(document, overlay.Bitmap).Reference!;
                var matrix = geometry.ImageMatrix(item.X, item.Y, item.Width, item.Height);
                commands.Append("q\n");
                commands.Append(string.Join(" ", matrix.Select(n => n.ToString("0.########", CultureInfo.InvariantCulture))));
                commands.Append(" cm\n").Append(name).Append(" Do\nQ\n");
            }
            var suffix = new PdfDictionary(document);
            suffix.CreateStream(Encoding.ASCII.GetBytes(commands.ToString()));
            document.Internals.AddObject(suffix);
            var contents = new PdfArray(document);
            contents.Elements.Add(prefix.Reference!);
            PdfItem? previous = page.Elements["/Contents"];
            if (Resolve(previous) is PdfArray oldContents)
                foreach (var content in oldContents.Elements) contents.Elements.Add(content);
            else if (previous != null) contents.Elements.Add(previous);
            contents.Elements.Add(suffix.Reference!);
            page.Elements["/Contents"] = contents;
        }
        // PDFsharp locks the document after Save, including its PageCount getter.
        int expectedPageCount = document.PageCount;
        // Save completely to a sibling temporary file; commit only after reopening validates page count.
        string fullPath = Path.GetFullPath(outputPath);
        string temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, ".hamipdf-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            document.Save(temporary);
            using (var verify = PdfReader.Open(temporary, PdfDocumentOpenMode.Import))
                if (verify.PageCount != expectedPageCount) throw new InvalidDataException("فشل التحقق من النسخة المحفوظة.");
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
