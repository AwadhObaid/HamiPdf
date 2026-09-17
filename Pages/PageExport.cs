using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace HamiPdf.Pages;

internal static class PageExport
{
    // Do not call PdfDocument.AcroForm: PDFsharp 6.2.4 throws when the entry is absent.
    private static bool HasFormDeclaration(PdfDocument document) =>
        document.Internals.Catalog.Elements.ContainsKey("/AcroForm");

    public static void CheckSource(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        FormPageExport.Check(document);
    }

    public static void Save(IReadOnlyList<PageEntry> pages, IReadOnlyDictionary<Guid, SourceRecord> sources, string path)
    {
        if (pages.Count == 0) throw new InvalidDataException("أضف صفحة واحدة على الأقل قبل الحفظ.");
        string destination = Path.GetFullPath(path);
        if (File.Exists(destination)) throw new IOException("اختر اسمًا جديدًا؛ لن يُستبدل الملف الموجود.");
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".hamipdf-pages-" + Guid.NewGuid().ToString("N") + ".pdf");
        var inputs = new Dictionary<Guid, PdfDocument>();
        var streams = new List<MemoryStream>();
        try
        {
            using var output = new PdfDocument();
            int[] expectedRotations = new int[pages.Count];
            for (int i = 0; i < pages.Count; i++)
            {
                var entry = pages[i];
                if (!inputs.TryGetValue(entry.SourceId, out var input))
                {
                    var source = sources[entry.SourceId];
                    var stream = new MemoryStream(source.Bytes, false); streams.Add(stream);
                    input = PdfReader.Open(stream, PdfDocumentOpenMode.Import); inputs.Add(entry.SourceId, input);
                    if (HasFormDeclaration(input)) throw new InvalidDataException("النماذج التفاعلية غير مدعومة لإدارة الصفحات.");
                }
                if (entry.SourcePage < 0 || entry.SourcePage >= input.PageCount) throw new InvalidDataException("مرجع الصفحة غير صالح.");
                var imported = output.AddPage(input.Pages[entry.SourcePage]);
                imported.Rotate = PagePlan.Normalize(imported.Rotate + entry.Rotation);
                expectedRotations[i] = imported.Rotate;
            }
            int expectedCount = pages.Count; // Never access output after Save (PDFsharp's saved-document guard).
            output.Save(temporary);
            using (var verify = PdfReader.Open(temporary, PdfDocumentOpenMode.Import))
            {
                if (verify.PageCount != expectedCount) throw new InvalidDataException("فشل التحقق من عدد الصفحات.");
                for (int i = 0; i < expectedCount; i++)
                    if (PagePlan.Normalize(verify.Pages[i].Rotate) != expectedRotations[i])
                        throw new InvalidDataException("فشل التحقق من تدوير الصفحات.");
            }
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            foreach (var input in inputs.Values) input.Dispose();
            foreach (var stream in streams) stream.Dispose();
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
