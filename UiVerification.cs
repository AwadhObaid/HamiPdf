using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace HamiPdf;

internal static class UiVerification
{
    public static void WriteReport(string reportPath)
    {
        MainWindow? main = null;
        AboutWindow? about = null;
        Scanning.ScanWindow? scan = null;
        try
        {
            main = new MainWindow();
            if (main.FindName("AboutButton") is not Button button || button.Visibility != Visibility.Visible || !button.IsEnabled || button.Content?.ToString() != "حول التطبيق")
                throw new InvalidDataException("Published main window does not contain the About button.");
            if (main.FindName("BrandImage") is not Image image || image.Source is not BitmapSource bitmap || bitmap.PixelWidth < 1000)
                throw new InvalidDataException("Published toolbar does not contain the high-resolution PNG.");
            about = new AboutWindow();
            if (about.FindName("DeveloperName") is not TextBlock developer || developer.Text != "Awadh Faghmah")
                throw new InvalidDataException("Published About dialog does not contain the developer credit.");
            if (main.FindName("ScanButton") is not Button scanButton || scanButton.Content?.ToString() != "مسح ضوئي")
                throw new InvalidDataException("Published main window does not contain the scanner button.");
            scan = new Scanning.ScanWindow(false);
            if (scan.FindName("BackendBox") is not ComboBox backends || backends.Items.Count != 5)
                throw new InvalidDataException("Published scanner window is incomplete.");
            if (about.FindName("CheckUpdatesButton") is not Button)
                throw new InvalidDataException("Published About dialog does not contain update checking.");
            string version = Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
            if (about.FindName("VersionText") is not TextBlock versionLabel || !versionLabel.Text.Contains(version))
                throw new InvalidDataException("About dialog version does not match the assembly.");
            string assembly = Assembly.GetExecutingAssembly().Location;
            File.WriteAllText(Path.GetFullPath(reportPath), JsonSerializer.Serialize(new {
                ok = true, version, scannerWindow = true, updateCheck = true, aboutButton = true, developer = developer.Text,
                logoPixelWidth = bitmap.PixelWidth, assembly,
                assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly)))
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { scan?.Close(); about?.Close(); main?.Close(); }
    }
}
