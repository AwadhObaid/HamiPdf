using System.Diagnostics;
using System.Windows;
namespace HamiPdf.Updates;
public partial class UpdateWindow : Window
{
    private readonly AvailableUpdate update;
    internal UpdateWindow(AvailableUpdate available)
    {
        InitializeComponent(); update = available;
        VersionLabel.Text = "يتوفر الإصدار " + update.Version.ToString(3);
        NotesBox.Text = string.IsNullOrWhiteSpace(update.Notes) ? "لم يضف المطور ملاحظات لهذا الإصدار." : update.Notes;
    }
    private void Download_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(update.Download.AbsoluteUri) { UseShellExecute = true }); Close(); }
        catch (Exception ex) { MessageBox.Show(this,"تعذر فتح المتصفح.\n" + ex.Message,"تحديث التطبيق"); }
    }
    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        var settings = UpdateSettings.Load(); settings.IgnoredVersion = update.Version.ToString(3); settings.Save(); Close();
    }
}
