using HamiPdf.Pages;
using PdfSharp.Pdf.IO;
using System.Text.Json;

if(args.Length>0) {
    if(args[0]=="prepare") {
        var sources=new Dictionary<Guid,SourceRecord>();
        var pages=new List<PageEntry>();
        // CLI specification: each source file followed by comma-separated page:rotation pairs.
        for(int i=2;i<args.Length;i+=2) {
            var id=Guid.NewGuid(); sources[id]=new(id,Path.GetFileName(args[i]),File.ReadAllBytes(args[i]));
            foreach(var pair in args[i+1].Split(',')) {
                var p=pair.Split(':'); pages.Add(new(Guid.NewGuid(),id,int.Parse(p[0]),p.Length>1?int.Parse(p[1]):0));
            }
        }
        var prepared=FormPageExport.Prepare(pages,sources);
        File.WriteAllText(args[1],JsonSerializer.Serialize(prepared));
    } else if(args[0]=="finish") {
        var plan=JsonSerializer.Deserialize<FormPageExport.Prepared>(File.ReadAllText(args[1]))!;
        FormPageExport.Finish(File.ReadAllBytes(args[2]),plan,args[3]);
    }
    return;
}
string fixtures=Path.Combine(AppContext.BaseDirectory,"Fixtures");
string temp=Path.Combine(Path.GetTempPath(),"HamiPdf-FormPages-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try {
    int count=0;
    foreach(string filename in Directory.GetFiles(fixtures,"*.json")) {
        var plan=JsonSerializer.Deserialize<FormPageExport.Prepared>(File.ReadAllText(filename))!;
        var bytes=File.ReadAllBytes(Path.ChangeExtension(filename,"pdf"));
        string target=Path.Combine(temp,Path.GetFileNameWithoutExtension(filename)+".pdf");
        FormPageExport.Finish(bytes,plan,target);
        bool blocked=false;try{FormPageExport.Finish(bytes,plan,target);}catch(IOException){blocked=true;}
        if(!blocked)throw new Exception("Overwrite must be rejected");
        if(plan.Widgets.Any(w=>w.Length>0)) {
            using var memory=new MemoryStream(bytes,false);using var doc=PdfReader.Open(memory,PdfDocumentOpenMode.Modify);
            var widget=doc.Pages.Cast<PdfSharp.Pdf.PdfPage>().SelectMany(FormPageExport.Widgets).First();
            widget.Elements.SetString("/V","CORRUPTED");using var modified=new MemoryStream();doc.Save(modified,false);
            string fail=Path.Combine(temp,"should-not-exist.pdf");blocked=false;
            try{FormPageExport.Finish(modified.ToArray(),plan,fail);}catch(InvalidDataException){blocked=true;}
            if(!blocked || File.Exists(fail))throw new Exception("Changed values must prevent publication");
        }
        count++;
    }
    if(count<3)throw new Exception("Missing form composition fixtures");
    Console.WriteLine($"PASS: {count} form page fixtures, field trees/values, rotation, overwrite and corruption protection.");
}finally{Directory.Delete(temp,true);}
