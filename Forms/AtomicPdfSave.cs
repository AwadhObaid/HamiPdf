using System.IO;
namespace HamiPdf.Forms;
internal static class AtomicPdfSave
{
    public static void Write(string target, byte[] bytes)
    {
        string temp = Path.Combine(Path.GetDirectoryName(target)!, ".hami-form-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(target)) File.Replace(temp, target, target + ".bak");
            else File.Move(temp, target);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
