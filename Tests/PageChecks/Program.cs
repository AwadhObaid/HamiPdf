using HamiPdf.Pages;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Text;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static byte[] Source(params (int Width, int Rotation)[] pageData)
{
    using var doc = new PdfDocument();
    foreach (var (width, rotation) in pageData)
    {
        var page = doc.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(width);
        page.Height = PdfSharp.Drawing.XUnit.FromPoint(500); page.Rotate = rotation;
        // A small visible rectangle; tests do not require fonts or native Windows APIs.
        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("q 0 0.5 0 rg 10 10 20 20 re f Q\n"));
    }
    using var stream = new MemoryStream(); doc.Save(stream, false); return stream.ToArray();
}
var sourceA = new SourceRecord(Guid.NewGuid(), "A.pdf", Source((210,0),(220,90),(230,0)));
var sourceB = new SourceRecord(Guid.NewGuid(), "B.pdf", Source((310,270)));
var sources = new Dictionary<Guid, SourceRecord> { [sourceA.Id] = sourceA, [sourceB.Id] = sourceB };
// Regression: an ordinary PDF has no /AcroForm entry and must not throw.
PageExport.CheckSource(sourceA.Bytes);
PageExport.CheckSource(sourceB.Bytes);
// Regression: empty form declarations are accepted, without calling the throwing getter.
using (var formDocument = new PdfDocument())
{
    formDocument.AddPage();
    var form = new PdfDictionary(formDocument);
    form.Elements["/Fields"] = new PdfArray(formDocument);
    formDocument.Internals.Catalog.Elements["/AcroForm"] = form;
    using var formStream = new MemoryStream();
    formDocument.Save(formStream, false);
    bool rejected = false;
    try { PageExport.CheckSource(formStream.ToArray()); }
    catch (InvalidDataException) { rejected = true; }
    Check(!rejected, "Accept an empty AcroForm safely");
}
var a=new PageEntry(Guid.NewGuid(),sourceA.Id,0);
var b=new PageEntry(Guid.NewGuid(),sourceA.Id,1);
var c=new PageEntry(Guid.NewGuid(),sourceA.Id,2);
var d=new PageEntry(Guid.NewGuid(),sourceB.Id,0);
List<PageEntry> pages=[a,b,c,d];
Check(PagePlan.Move(pages,[b.Id,c.Id],true).SequenceEqual(new[]{b,c,a,d}),"Move selected block up");
Check(PagePlan.Move(pages,[b.Id,c.Id],false).SequenceEqual(new[]{a,d,b,c}),"Move selected block down");
Check(PagePlan.Move(pages,[a.Id,c.Id],true).SequenceEqual(new[]{a,c,b,d}),"Move separated selection");
Check(PagePlan.Move(pages,[a.Id,b.Id],true).SequenceEqual(pages),"Top boundary");
Check(PagePlan.Extract(pages,[d.Id,a.Id]).SequenceEqual(new[]{a,d}),"Extract in current order");
var rotated=pages;
for(int i=0;i<4;i++) rotated=PagePlan.Rotate(rotated,[b.Id]);
Check(rotated.SequenceEqual(pages),"Four rotations restore original state");
var directory=Path.Combine(Path.GetTempPath(),"HamiPdf-PageChecks-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    List<PageEntry> plan=[d with { Rotation=90 },c,a with { Rotation=180 }];
    string path=Path.Combine(directory,"merged.pdf");
    PageExport.Save(plan,sources,path);
    using(var verify=PdfReader.Open(path,PdfDocumentOpenMode.Import))
    {
        Check(verify.PageCount==3,"Saved page count");
        int[] widths=[310,230,210], rotations=[0,0,180];
        for(int i=0;i<3;i++)
        {
            Check(Math.Abs(verify.Pages[i].MediaBox.Width-widths[i])<.01,"Page source/order/content dimensions");
            Check(PagePlan.Normalize(verify.Pages[i].Rotate)==rotations[i],"Base rotation plus added rotation");
        }
    }
    // A second save must open fresh document instances, not reuse already-saved objects.
    PageExport.Save(plan,sources,Path.Combine(directory,"second.pdf"));
    byte[] before=File.ReadAllBytes(path);
    bool blocked=false;
    try { PageExport.Save([a],sources,path); } catch(IOException) { blocked=true; }
    Check(blocked && before.SequenceEqual(File.ReadAllBytes(path)),"Never overwrite an existing file");
    blocked=false;
    try { PageExport.Save([],sources,Path.Combine(directory,"empty.pdf")); } catch(InvalidDataException) { blocked=true; }
    Check(blocked,"Reject empty output");
    PageExport.Save(PagePlan.Extract(pages,[b.Id,d.Id]),sources,Path.Combine(directory,"extracted.pdf"));
    using(var extracted=PdfReader.Open(Path.Combine(directory,"extracted.pdf"),PdfDocumentOpenMode.Import))
        Check(extracted.PageCount==2,"Extract count");
    Console.WriteLine("PASS: missing/declared AcroForm handling, page moves, boundaries, rotation, merge/order, extraction, repeated save and overwrite protection.");
}
finally { Directory.Delete(directory,true); }
