using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace HamiPdf.Pages;

// The document composer copies fields, not just page annotations. Validate both
// canonical field membership and each retained widget before committing a new file.
internal static class FormPageExport
{
    internal static PdfItem? Resolve(PdfItem? item) => item is PdfReference r ? r.Value : item;
    internal static PdfDictionary? Dict(PdfItem? item) => Resolve(item) as PdfDictionary;
    private static PdfArray? Array(PdfItem? item) => Resolve(item) as PdfArray;
    private static PdfItem? Inherit(PdfDictionary item, string key)
    {
        var seen = new HashSet<PdfDictionary>();
        for (PdfDictionary? d = item; d != null && seen.Add(d); d = Dict(d.Elements["/Parent"]))
            if (d.Elements.ContainsKey(key)) return Resolve(d.Elements[key]);
        return null;
    }
    private static string Name(PdfDictionary d)
    {
        var names = new List<string>(); var seen = new HashSet<PdfDictionary>();
        for (PdfDictionary? p = d; p != null && seen.Add(p); p = Dict(p.Elements["/Parent"]))
            if (p.Elements.ContainsKey("/T")) names.Insert(0,p.Elements.GetString("/T"));
        return string.Join(".",names);
    }
    private static string Value(PdfItem? item) => Resolve(item) switch {
        null => "null", PdfString s => JsonSerializer.Serialize(s.Value),
        PdfArray a => "[" + string.Join(",",a.Elements.Select(Value)) + "]",
        var v => v.ToString() ?? "null"
    };
    internal static IEnumerable<PdfDictionary> Widgets(PdfPage page)
    {
        if (Array(page.Elements["/Annots"]) is {} annotations)
            foreach (var a in annotations.Elements)
                if (Dict(a) is {} d && d.Elements.GetName("/Subtype") == "/Widget") yield return d;
    }
    private static HashSet<PdfDictionary> FieldTree(PdfDocument doc)
    {
        var nodes = new HashSet<PdfDictionary>();
        var form = Dict(doc.Internals.Catalog.Elements["/AcroForm"]);
        void Visit(PdfItem item, PdfDictionary? parent)
        {
            if (Dict(item) is not {} d || !nodes.Add(d)) throw new InvalidDataException("شجرة حقول غير صالحة أو متكررة.");
            if (Dict(d.Elements["/Parent"]) != parent) throw new InvalidDataException("روابط حقول النموذج غير متطابقة.");
            if (Array(d.Elements["/Kids"]) is {} kids) foreach(var k in kids.Elements) Visit(k,d);
        }
        if (Array(form?.Elements["/Fields"]) is {} fields) foreach(var f in fields.Elements) Visit(f,null);
        return nodes;
    }
    public static bool HasForm(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes,false);
        using var doc = PdfReader.Open(stream,PdfDocumentOpenMode.Import);
        return doc.Internals.Catalog.Elements.ContainsKey("/AcroForm") || doc.Pages.Cast<PdfPage>().Any(p=>Widgets(p).Any());
    }
    internal static void Check(PdfDocument doc)
    {
        if (doc.PageCount == 0) throw new InvalidDataException("الملف لا يحتوي صفحات.");
        if (!doc.SecuritySettings.PermitModifyDocument || !doc.SecuritySettings.PermitAssembleDocument)
            throw new InvalidDataException("صلاحيات المستند لا تسمح بإدارة صفحاته.");
        var form = Dict(doc.Internals.Catalog.Elements["/AcroForm"]);
        if (form?.Elements.ContainsKey("/XFA") == true)
            throw new InvalidDataException("نماذج XFA غير مدعومة لإدارة الصفحات. لم يتغير الملف الأصلي.");
        var tree = FieldTree(doc);
        if (tree.Any(d=>Inherit(d,"/FT") is PdfName n && n.Value=="/Sig") || doc.Internals.Catalog.Elements.ContainsKey("/Perms"))
            throw new InvalidDataException("هذا المستند يحتوي حقول توقيع أو توقيعًا معتمدًا؛ إدارة صفحاته قد تبطل التوقيع، لذلك لم يُعدّل.");
        var names = new Dictionary<string,PdfDictionary>(StringComparer.Ordinal);
        var attached = new HashSet<PdfDictionary>();
        foreach (PdfPage page in doc.Pages) foreach(var w in Widgets(page))
        {
            if (!attached.Add(w) || (Dict(w.Elements["/P"]) is {} owner && owner != page))
                throw new InvalidDataException("ارتباط الحقل بالصفحة غير صالح.");
            if (!tree.Contains(w)) throw new InvalidDataException("يوجد حقل غير مرتبط بشجرة النموذج؛ تعذر ضمان حفظه.");
            string name = Name(w);
            var field = w.Elements.ContainsKey("/T") ? w : Dict(w.Elements["/Parent"]) ?? w;
            if (name.Length==0 || (names.TryGetValue(name,out var prior) && prior != field))
                throw new InvalidDataException("أسماء الحقول داخل المستند متعارضة أو ناقصة.");
            names[name]=field;
        }
        if(tree.Any(d=>d.Elements.GetName("/Subtype")=="/Widget" && !attached.Contains(d)))
            throw new InvalidDataException("شجرة النموذج تحتوي حقولًا غير مرتبطة بصفحات؛ تعذر ضمان نقلها.");
    }
    private static string Appearance(PdfItem? item, int depth=0)
    {
        if(depth>4) throw new InvalidDataException("بنية مظهر الحقل غير صالحة.");
        if(Dict(item) is not {} d) return "none";
        if(d.Stream != null) return Convert.ToHexString(SHA256.HashData(d.Stream.UnfilteredValue));
        return string.Join(";",d.Elements.Keys.OrderBy(k=>k,StringComparer.Ordinal).Select(k=>k+":"+Appearance(d.Elements[k],depth+1)));
    }
    internal static string[] Snapshot(PdfPage page)
    {
        return Widgets(page).Select(w=>JsonSerializer.Serialize(new {
            name=Name(w), type=Value(Inherit(w,"/FT")), value=Value(Inherit(w,"/V")),
            defaultValue=Value(Inherit(w,"/DV")), flags=Value(Inherit(w,"/Ff")),
            options=Value(Inherit(w,"/Opt")), indices=Value(Inherit(w,"/I")),
            maxLength=Value(Inherit(w,"/MaxLen")), appearance=Appearance(w.Elements["/AP"]),
            rect=Value(w.Elements["/Rect"]), state=Value(w.Elements["/AS"])
        })).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
    }
    public sealed record SourceInput(string data, IReadOnlyList<PageMap> pages);
    public sealed record PageMap(int sourcePage,int outputPage);
    public sealed record Prepared(IReadOnlyList<SourceInput> Sources,string[][] Widgets,int[] Rotations);
    public static Prepared Prepare(IReadOnlyList<PageEntry> pages,IReadOnlyDictionary<Guid,SourceRecord> sources)
    {
        if(pages.Count==0) throw new InvalidDataException("أضف صفحة واحدة على الأقل.");
        var groups=pages.Select((p,i)=>(p,i)).GroupBy(x=>x.p.SourceId).ToArray();
        if(groups.Sum(g=>(long)sources[g.Key].Bytes.Length)>256L*1024*1024)
            throw new InvalidDataException("حجم المصادر يتجاوز 256 ميغابايت؛ قسّم العملية إلى ملفات أصغر.");
        var inputs=new List<SourceInput>(); var expected=new string[pages.Count][]; var rotations=new int[pages.Count];
        int sourceNumber=0;
        foreach(var group in groups)
        {
            sourceNumber++;
            using var stream=new MemoryStream(sources[group.Key].Bytes,false);
            using var doc=PdfReader.Open(stream,PdfDocumentOpenMode.Modify);
            Check(doc);
            // Namespace roots from different files so identically named fields stay independent.
            if(groups.Length>1 && Array(Dict(doc.Internals.Catalog.Elements["/AcroForm"])?.Elements["/Fields"]) is {} fields)
                foreach(var item in fields.Elements)
                    if(Dict(item) is {} root) root.Elements.SetString("/T",$"Hami{sourceNumber}_"+root.Elements.GetString("/T"));
            var maps=new List<PageMap>();
            foreach(var (p,i) in group.OrderBy(x=>x.p.SourcePage))
            {
                if(p.SourcePage<0 || p.SourcePage>=doc.PageCount || maps.Any(m=>m.sourcePage==p.SourcePage))
                    throw new InvalidDataException("مرجع صفحة غير صالح أو مكرر داخل المصدر.");
                expected[i]=Snapshot(doc.Pages[p.SourcePage]); rotations[i]=PagePlan.Normalize(doc.Pages[p.SourcePage].Rotate+p.Rotation);
                maps.Add(new(p.SourcePage,i));
            }
            using var saved=new MemoryStream(); doc.Save(saved,false);
            inputs.Add(new(Convert.ToBase64String(saved.ToArray()),maps));
        }
        return new(inputs,expected,rotations);
    }
    private static void Verify(PdfDocument doc,Prepared plan)
    {
        Check(doc);
        if(doc.PageCount!=plan.Widgets.Length) throw new InvalidDataException("فشل التحقق من عدد الصفحات.");
        for(int i=0;i<doc.PageCount;i++)
            if(!Snapshot(doc.Pages[i]).SequenceEqual(plan.Widgets[i]))
                throw new InvalidDataException($"فشل التحقق من حقول الصفحة {i+1} وقيمها. لم يُحفظ الملف.");
    }
    public static void Finish(byte[] data,Prepared plan,string path)
    {
        string target=Path.GetFullPath(path);
        if(File.Exists(target)) throw new IOException("اختر اسمًا جديدًا؛ لن يُستبدل الملف الموجود.");
        string temp=Path.Combine(Path.GetDirectoryName(target)!,".hamipdf-pages-"+Guid.NewGuid().ToString("N")+".pdf");
        try
        {
            using(var stream=new MemoryStream(data,false))
            using(var doc=PdfReader.Open(stream,PdfDocumentOpenMode.Modify))
            {
                Verify(doc,plan);
                for(int i=0;i<doc.PageCount;i++) doc.Pages[i].Rotate=plan.Rotations[i];
                doc.Save(temp);
            }
            using(var check=PdfReader.Open(temp,PdfDocumentOpenMode.Import))
            {
                Verify(check,plan);
                for(int i=0;i<check.PageCount;i++) if(PagePlan.Normalize(check.Pages[i].Rotate)!=plan.Rotations[i])
                    throw new InvalidDataException("فشل التحقق من التدوير.");
            }
            File.Move(temp,target,false);
        }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
}
