using System.IO;
using System.Text;

namespace HamiPdf.Services;

internal static class PdfFile
{
    public static string Validate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("يرجى اختيار ملف بامتداد PDF.");
        using var stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[Math.Min(1024L, stream.Length)];
        int count = stream.Read(bytes, 0, bytes.Length);
        if (!Encoding.ASCII.GetString(bytes, 0, count).Contains("%PDF-", StringComparison.Ordinal))
            throw new InvalidDataException("هذا الملف لا يحتوي على ترويسة PDF صالحة.");
        return fullPath;
    }
}
