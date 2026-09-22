using System.IO;
using System.Text.Json;

namespace HamiPdf.Scanning;

internal sealed class ScanPreferences
{
    public int Backend { get; set; }
    public string DeviceId { get; set; } = "";
    public int DpiIndex { get; set; } = 1;
    public int ColorIndex { get; set; }
    public int SourceIndex { get; set; }
    public int SizeIndex { get; set; }
    public bool NativeUi { get; set; }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HamiPdf", "scan-settings.json");
    public static ScanPreferences Load()
    {
        try { return JsonSerializer.Deserialize<ScanPreferences>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, FilePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
