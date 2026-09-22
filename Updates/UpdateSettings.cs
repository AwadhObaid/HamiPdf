using System.IO;
using System.Text.Json;
namespace HamiPdf.Updates;
internal sealed class UpdateSettings
{
    public bool Automatic { get; set; } = true;
    public DateTime LastAttemptUtc { get; set; }
    public string IgnoredVersion { get; set; } = "";
    private static string PathName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HamiPdf", "update-settings.json");
    public static UpdateSettings Load() { try { return JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(PathName)) ?? new(); } catch { return new(); } }
    public void Save()
    {
        string temp = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { Directory.CreateDirectory(Path.GetDirectoryName(PathName)!); File.WriteAllText(temp,JsonSerializer.Serialize(this)); File.Move(temp,PathName,true); }
        catch { /* A settings write failure must not close the application. */ }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }
}
