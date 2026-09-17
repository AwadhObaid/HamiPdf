using System.IO;

namespace HamiPdf.Services;

internal static class ShutdownTrace
{
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HamiPdf", "logs", $"shutdown_{Environment.ProcessId}.log");
    public static void Write(string stage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            File.AppendAllText(PathName, DateTime.Now.ToString("O") + " " + stage + Environment.NewLine);
        }
        catch { /* Diagnostics must not prevent shutdown. */ }
    }
}
