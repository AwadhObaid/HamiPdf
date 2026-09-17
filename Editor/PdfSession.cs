using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace HamiPdf.Editor;

internal sealed class PdfSession : IDisposable
{
    private readonly InMemoryRandomAccessStream stream;
    private PdfDocument? document;
    public byte[] SourceBytes { get; }
    public IReadOnlyList<PageGeometry> Pages { get; }
    private PdfSession(byte[] bytes, InMemoryRandomAccessStream data, PdfDocument pdf, IReadOnlyList<PageGeometry> pages)
    { SourceBytes = bytes; stream = data; document = pdf; Pages = pages; }

    public static async Task<PdfSession> OpenAsync(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        return await OpenBytesAsync(bytes);
    }

    public static async Task<PdfSession> OpenBytesAsync(byte[] bytes)
    {
        var pages = await Task.Run(() => PdfExport.ReadGeometry(bytes));
        var data = new InMemoryRandomAccessStream();
        try
        {
            using (var writer = new DataWriter(data))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            data.Seek(0);
            var pdf = await PdfDocument.LoadFromStreamAsync(data);
            if (pdf.PageCount != (uint)pages.Count || pages.Count == 0)
                throw new InvalidDataException("تعذر التحقق من صفحات المستند.");
            return new PdfSession(bytes, data, pdf, pages);
        }
        catch { data.Dispose(); throw; }
    }

    public async Task<BitmapSource> RenderAsync(int index)
    {
        if (document == null) throw new ObjectDisposedException(nameof(PdfSession));
        using var page = document.GetPage((uint)index);
        var geometry = Pages[index];
        double expected = geometry.DisplayWidth / geometry.DisplayHeight;
        double actual = page.Size.Width / page.Size.Height;
        if (Math.Abs(expected - actual) > Math.Max(.01, expected * .005))
            throw new InvalidDataException("أبعاد هذه الصفحة غير متوافقة مع المحرر؛ يمكن عرضها من النافذة الرئيسية.");
        using var output = new InMemoryRandomAccessStream();
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Min(1800, 1800 * actual)),
            DestinationHeight = (uint)Math.Max(1, Math.Min(1800, 1800 / actual))
        };
        await page.RenderToStreamAsync(output, options);
        output.Seek(0);
        using var reader = new DataReader(output.GetInputStreamAt(0));
        uint size = checked((uint)output.Size);
        await reader.LoadAsync(size);
        byte[] data = new byte[size];
        reader.ReadBytes(data);
        using var memory = new MemoryStream(data);
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = memory; image.EndInit();
        image.Freeze();
        return image;
    }

    public void Dispose() { document = null; stream.Dispose(); }
}
