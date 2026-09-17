using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace HamiPdf.Forms;

// Appearance streams are attached to widgets, never painted into page content.
// The canonical /V and the AcroForm tree remain interactive.
internal static class FormAppearance
{
    private static PdfItem? Resolve(PdfItem? item) => item is PdfReference r ? r.Value : item;
    private static PdfDictionary? Dictionary(PdfItem? item) => Resolve(item) as PdfDictionary;
    private static PdfItem? Inherit(PdfDictionary widget, string key)
    {
        var seen = new HashSet<PdfDictionary>();
        for (PdfDictionary? d = widget; d != null && seen.Add(d); d = Dictionary(d.Elements["/Parent"]))
            if (d.Elements.ContainsKey(key)) return Resolve(d.Elements[key]);
        return null;
    }
    private static IEnumerable<PdfDictionary> Widgets(PdfDocument doc)
    {
        foreach (PdfPage page in doc.Pages)
            if (Resolve(page.Elements["/Annots"]) is PdfArray annots)
                foreach (var item in annots.Elements)
                    if (Dictionary(item) is {} widget && widget.Elements.GetName("/Subtype") == "/Widget") yield return widget;
    }
    private static string[] Snapshot(PdfDocument doc)
    {
        var items = new List<string> { "pages:" + doc.PageCount };
        var form = Dictionary(doc.Internals.Catalog.Elements["/AcroForm"]);
        var seen = new HashSet<PdfDictionary>();
        void Visit(PdfItem item, string prefix)
        {
            if (Dictionary(item) is not {} d || !seen.Add(d)) return;
            string name = prefix + "/" + d.Elements.GetString("/T");
            items.Add("field:" + name + ":" + Inherit(d,"/FT") + ":" + Inherit(d,"/V"));
            if (Resolve(d.Elements["/Kids"]) is PdfArray kids) foreach (var kid in kids.Elements) Visit(kid,name);
        }
        if (form != null && Resolve(form.Elements["/Fields"]) is PdfArray fields)
            foreach (var item in fields.Elements) Visit(item, "");
        int pageIndex=0;
        foreach (PdfPage page in doc.Pages)
        {
            if (Resolve(page.Elements["/Annots"]) is PdfArray annotations)
                foreach (var item in annotations.Elements)
                    if (Dictionary(item) is {} widget && widget.Elements.GetName("/Subtype") == "/Widget")
                        items.Add("widget:"+pageIndex+":"+Inherit(widget,"/T")+":"+Inherit(widget,"/FT")+":"+Inherit(widget,"/V")+":"+widget.Elements["/Rect"]+":"+widget.Elements["/AS"]);
            pageIndex++;
        }
        return items.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    }
    public static byte[] Prepare(byte[] input)
    {
        using var source = new MemoryStream(input, false);
        using var doc = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        var before = Snapshot(doc);
        var form = Dictionary(doc.Internals.Catalog.Elements["/AcroForm"]);
        if (form == null) throw new InvalidDataException("شجرة حقول النموذج غير موجودة.");
        bool regenerate = form.Elements.GetBoolean("/NeedAppearances");
        int changes=0;
        foreach (var widget in Widgets(doc))
        {
            if (Inherit(widget,"/FT") is not PdfName type || type.Value!="/Tx") continue;
            var appearance=Dictionary(widget.Elements["/AP"]);
            if (!regenerate && Dictionary(appearance?.Elements["/N"]) is { Stream: not null }) continue;
            string value=(Inherit(widget,"/V") as PdfString)?.Value ?? "";
            var rect=widget.Elements.GetRectangle("/Rect");
            double width=Math.Abs(rect.X2-rect.X1), height=Math.Abs(rect.Y2-rect.Y1);
            if (!double.IsFinite(width)||!double.IsFinite(height)||width<1||height<1||width>3000||height>3000)
                throw new InvalidDataException("أبعاد أحد الحقول غير مدعومة.");
            var bitmap=Draw(widget,form,value,width,height);
            var image=Editor.PdfExport.Embed(doc,bitmap);
            var xobjects=new PdfDictionary(doc);xobjects.Elements["/Im"]=image.Reference!;
            var resources=new PdfDictionary(doc);resources.Elements["/XObject"]=xobjects;
            var normal=new PdfDictionary(doc);
            normal.Elements.SetName("/Type","/XObject");normal.Elements.SetName("/Subtype","/Form");
            normal.Elements["/BBox"]=new PdfRectangle(new PdfSharp.Drawing.XRect(0,0,width,height));
            normal.Elements["/Resources"]=resources;
            string w=width.ToString("0.########",CultureInfo.InvariantCulture),h=height.ToString("0.########",CultureInfo.InvariantCulture);
            normal.CreateStream(Encoding.ASCII.GetBytes($"q {w} 0 0 {h} 0 0 cm /Im Do Q"));
            doc.Internals.AddObject(normal);
            var ap=new PdfDictionary(doc);ap.Elements["/N"]=normal.Reference!;widget.Elements["/AP"]=ap;
            changes++;
        }
        if (changes==0) return input;
        // All widget normal appearances must exist before clearing the global regeneration flag.
        foreach(var widget in Widgets(doc))
        {
            var type=(Inherit(widget,"/FT") as PdfName)?.Value;
            if (type is "/Tx" or "/Ch" or "/Btn")
            {
                var ap=Dictionary(widget.Elements["/AP"]);
                if (Resolve(ap?.Elements["/N"]) == null)
                    throw new InvalidDataException("تعذر إنشاء مظهر موثوق لأحد الحقول. لم يُحفظ الملف.");
            }
        }
        form.Elements.SetBoolean("/NeedAppearances",false);
        doc.Version=Math.Max(doc.Version,14);
        using var output=new MemoryStream();doc.Save(output,false);
        byte[] result=output.ToArray();
        using var readback=new MemoryStream(result,false);
        using var check=PdfReader.Open(readback,PdfDocumentOpenMode.Import);
        if(!before.SequenceEqual(Snapshot(check))) throw new InvalidDataException("فشل التحقق من بقاء الحقول وقيمها بعد تجهيز المظهر.");
        return result;
    }
    private static double Number(PdfItem? item) => Resolve(item) switch { PdfReal real => real.Value, PdfInteger integer => integer.Value, _ => 0 };
    private static Brush ColorBrush(PdfItem? item, Brush fallback)
    {
        if (Resolve(item) is not PdfArray a) return fallback;
        double[] c = a.Elements.Select(Number).ToArray();
        byte B(double n)=>(byte)Math.Round(255*Math.Clamp(n,0,1));
        return c.Length switch {
            1 => new SolidColorBrush(Color.FromRgb(B(c[0]),B(c[0]),B(c[0]))),
            3 => new SolidColorBrush(Color.FromRgb(B(c[0]),B(c[1]),B(c[2]))),
            4 => new SolidColorBrush(Color.FromRgb(B((1-c[0])*(1-c[3])),B((1-c[1])*(1-c[3])),B((1-c[2])*(1-c[3])))),
            _ => fallback
        };
    }
    private static BitmapSource Draw(PdfDictionary widget,PdfDictionary form,string value,double width,double height)
    {
        int flags=(Inherit(widget,"/Ff") as PdfInteger)?.Value ?? 0;
        bool multiline=(flags&4096)!=0, password=(flags&8192)!=0;
        int maxLen=(Inherit(widget,"/MaxLen") as PdfInteger)?.Value ?? 0;
        bool comb=(flags&16777216)!=0 && maxLen>0;
        int alignment=(Inherit(widget,"/Q") as PdfInteger)?.Value ?? form.Elements.GetInteger("/Q");
        var mk=Dictionary(widget.Elements["/MK"]);
        int rotation=((mk?.Elements.GetInteger("/R")??0)%360+360)%360;
        if(rotation%90!=0)throw new InvalidDataException("تدوير حقل غير مدعوم.");
        double w=rotation is 90 or 270?height:width,h=rotation is 90 or 270?width:height;
        string da=(Inherit(widget,"/DA") as PdfString)?.Value ?? form.Elements.GetString("/DA");
        var match=Regex.Match(da,@"([-+]?\d*\.?\d+)\s+Tf");
        double size=match.Success?double.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture):0;
        if(size<=0)size=Math.Min(12,Math.Max(4,h-4));
        if(password)value=new string('•',value.Length);
        bool rtl=value.Any(c=>c>='\u0590'&&c<='\u08ff');
        Brush foreground=Brushes.Black;
        var rgb=Regex.Match(da,@"([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+rg");
        if(rgb.Success)foreground=new SolidColorBrush(Color.FromRgb((byte)(255*Math.Clamp(double.Parse(rgb.Groups[1].Value,CultureInfo.InvariantCulture),0,1)),(byte)(255*Math.Clamp(double.Parse(rgb.Groups[2].Value,CultureInfo.InvariantCulture),0,1)),(byte)(255*Math.Clamp(double.Parse(rgb.Groups[3].Value,CultureInfo.InvariantCulture),0,1))));
        FrameworkElement content;
        if(comb)
        {
            var grid=new System.Windows.Controls.Grid { FlowDirection=FlowDirection.LeftToRight };
            for(int i=0;i<maxLen;i++)grid.ColumnDefinitions.Add(new ColumnDefinition());
            var chars=StringInfo.GetTextElementEnumerator(value);int pos=0;
            while(chars.MoveNext()&&pos<maxLen){var cell=new TextBlock {Text=chars.GetTextElement(),FontFamily=new FontFamily("Segoe UI"),FontSize=size,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=foreground};System.Windows.Controls.Grid.SetColumn(cell,rtl?maxLen-pos-1:pos);grid.Children.Add(cell);pos++;}
            content=grid;
        }
        else
        {
            var text=new TextBlock { Text=value,FontFamily=new FontFamily("Segoe UI"),FontSize=size,Foreground=foreground,
                FlowDirection=rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight,
                TextAlignment=alignment==1?TextAlignment.Center:alignment==2?TextAlignment.Right:TextAlignment.Left,
                TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalAlignment=multiline?VerticalAlignment.Top:VerticalAlignment.Center };
            if(!multiline){text.Measure(new Size(double.PositiveInfinity,h));if(text.DesiredSize.Width>w-4)text.FontSize=Math.Max(2,size*(w-4)/text.DesiredSize.Width);}
            content=text;
        }
        Brush bg=ColorBrush(mk?.Elements["/BG"],Brushes.Transparent);
        Brush stroke=ColorBrush(mk?.Elements["/BC"],Brushes.Transparent);
        double borderWidth=Number(Dictionary(widget.Elements["/BS"])?.Elements["/W"]);
        if(Resolve(widget.Elements["/Border"]) is PdfArray edges && edges.Elements.Count>=3) borderWidth=Number(edges.Elements[2]);
        var border=new Border {Width=w,Height=h,Child=content,Padding=new Thickness(2,1,2,1),Background=bg,BorderBrush=stroke,BorderThickness=new Thickness(Math.Clamp(borderWidth,0,5)),ClipToBounds=true};
        border.Measure(new Size(w,h));border.Arrange(new Rect(0,0,w,h));border.UpdateLayout();
        double scale=Math.Min(4,Math.Sqrt(8_000_000/(w*h)));
        var original=new RenderTargetBitmap((int)Math.Ceiling(w*scale),(int)Math.Ceiling(h*scale),96*scale,96*scale,PixelFormats.Pbgra32);original.Render(border);
        if(rotation==0){original.Freeze();return original;}
        var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.PushTransform(new TranslateTransform(width/2,height/2));dc.PushTransform(new RotateTransform(-rotation));dc.DrawImage(original,new Rect(-w/2,-h/2,w,h));}
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width*scale),(int)Math.Ceiling(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(drawing);bitmap.Freeze();return bitmap;
    }
}
