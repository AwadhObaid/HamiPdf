using HamiPdf.Scanning;
using PdfSharp.Pdf.IO;
using System.Security.Cryptography;

void Require(bool value, string message) { if (!value) throw new Exception(message); }
string folder = Path.Combine(Path.GetTempPath(), "HamiPdf-ScanChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    var a = new ScanPage(Path.Combine(fixtures, "portrait.png"), 288, 432);
    var b = new ScanPage(Path.Combine(fixtures, "landscape.png"), 432, 288, 90);
    string path = Path.Combine(folder, "scan.pdf");
    ScanPdfWriter.Save([b, a], path);
    using (var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import))
    {
        Require(pdf.PageCount == 2, "page count");
        Require(pdf.Pages[0].Rotate == 90 && pdf.Pages[1].Rotate == 0, "order/rotation");
        Require(Math.Abs(pdf.Pages[0].Width.Point - 432) < .1 && Math.Abs(pdf.Pages[1].Height.Point - 432) < .1, "physical page size");
        foreach (var page in pdf.Pages) Require(page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject")?.Elements.Count > 0, "embedded scan image");
    }
    byte[] original = SHA256.HashData(File.ReadAllBytes(path));
    try { ScanPdfWriter.Save([a], path); throw new Exception("overwrite allowed"); } catch (IOException) { }
    Require(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "existing file unchanged");
    string failed = Path.Combine(folder, "failed.pdf");
    try { ScanPdfWriter.Save([a, b with { ImagePath = Path.Combine(folder, "missing.png") }], failed); throw new Exception("missing image accepted"); }
    catch (FileNotFoundException) { }
    Require(!File.Exists(failed) && !Directory.GetFiles(folder, ".hami-scan-*").Any(), "failure atomicity/temp cleanup");
    try { ScanPdfWriter.Save([], failed); throw new Exception("empty output accepted"); } catch (InvalidDataException) { }
    try { ScanPdfWriter.Save([a with { WidthPoints = double.NaN }], failed); throw new Exception("invalid dimensions accepted"); } catch (InvalidDataException) { }
    ScanPdfWriter.Save([a with { Rotation = 270 }], failed);
    using (var pdf = PdfReader.Open(failed, PdfDocumentOpenMode.Import)) Require(pdf.PageCount == 1 && pdf.Pages[0].Rotate == 270, "delete/rotate export");
    if (args.Length == 1) File.Copy(path, args[0], true);
    Console.WriteLine("PASS: scanned images embedded, page order/size/rotation, single-page export, no overwrite, failure cleanup, invalid input.");
}
finally { Directory.Delete(folder, true); }
