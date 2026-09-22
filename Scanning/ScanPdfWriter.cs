using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace HamiPdf.Scanning;

internal sealed record ScanPage(string ImagePath, double WidthPoints, double HeightPoints, int Rotation = 0);

internal static class ScanPdfWriter
{
    // Always save to a new file. A failed export cannot replace a user's document.
    public static void Save(IReadOnlyList<ScanPage> pages, string path)
    {
        if (pages.Count == 0) throw new InvalidDataException("لا توجد صفحات للحفظ.");
        string destination = Path.GetFullPath(path);
        if (File.Exists(destination)) throw new IOException("اختر اسمًا جديدًا؛ الملف موجود بالفعل.");
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".hami-scan-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            using (var output = new PdfDocument())
            {
                output.Info.Creator = "HamiPdf";
                foreach (var entry in pages)
                {
                    if (!double.IsFinite(entry.WidthPoints) || !double.IsFinite(entry.HeightPoints) ||
                        entry.WidthPoints <= 0 || entry.HeightPoints <= 0 || entry.WidthPoints > 14400 || entry.HeightPoints > 14400 ||
                        entry.Rotation is not (0 or 90 or 180 or 270))
                        throw new InvalidDataException("أبعاد الصفحة أو زاوية التدوير غير صالحة.");
                    using var image = XImage.FromFile(entry.ImagePath);
                    var page = output.AddPage();
                    page.Width = XUnit.FromPoint(entry.WidthPoints);
                    page.Height = XUnit.FromPoint(entry.HeightPoints);
                    page.Rotate = entry.Rotation;
                    using var graphics = XGraphics.FromPdfPage(page);
                    graphics.DrawImage(image, 0, 0, entry.WidthPoints, entry.HeightPoints);
                }
                output.Save(temporary);
            }
            using (var check = PdfReader.Open(temporary, PdfDocumentOpenMode.Import))
            {
                if (check.PageCount != pages.Count) throw new InvalidDataException("فشل التحقق من عدد الصفحات.");
                for (int i = 0; i < pages.Count; i++)
                    if (check.Pages[i].Rotate != pages[i].Rotation ||
                        Math.Abs(check.Pages[i].Width.Point - pages[i].WidthPoints) > 0.1 ||
                        Math.Abs(check.Pages[i].Height.Point - pages[i].HeightPoints) > 0.1)
                        throw new InvalidDataException("فشل التحقق من أبعاد الصفحات أو تدويرها.");
            }
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
