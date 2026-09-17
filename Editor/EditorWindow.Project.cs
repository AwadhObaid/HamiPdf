using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace HamiPdf.Editor;

public partial class EditorWindow
{
    private readonly bool isProjectInput;
    private string documentName = "document.pdf";
    private string? currentProjectPath;

    private async void SaveProject_Click(object sender, RoutedEventArgs e) => await SaveProjectAsync();
    private async void SaveProjectAs_Click(object sender, RoutedEventArgs e) => await SaveProjectAsync(saveAs:true);

    private async Task<bool> SaveProjectAsync(bool saveAs = false)
    {
        if (session == null || busy || !CommitProperties()) return false;
        CancelDrawing();
        string? target = saveAs ? null : currentProjectPath;
        if (target == null)
        {
            var dialog = new SaveFileDialog
            {
                Title = "حفظ مشروع قابل للاستكمال", Filter = "HamiPdf project (*.hamipdf)|*.hamipdf",
                DefaultExt = ".hamipdf", AddExtension = true, OverwritePrompt = true,
                FileName = Path.GetFileNameWithoutExtension(documentName) + (saveAs ? "_copy" : "") + ".hamipdf",
                InitialDirectory = Path.GetDirectoryName(currentProjectPath ?? sourcePath)
            };
            if (dialog.ShowDialog(this) != true) return false;
            target = dialog.FileName;
        }
        SetBusy(true);
        try
        {
            EditorStatus.Text = "جارٍ حفظ المستند والإضافات داخل المشروع…";
            var project = ProjectArchive.Capture(documentName,session.SourceBytes,pageIndex,items);
            await Task.Run(() => ProjectArchive.Save(project,target));
            currentProjectPath = target;
            savedRevision = revision;
            DocumentLabel.Text = documentName + " · " + Path.GetFileName(target);
            EditorStatus.Text = "حُفظ المشروع: " + target;
            MessageBox.Show(this,"حُفظ المشروع. يمكنك إغلاقه وفتحه لاحقًا من زر «فتح مشروع» ومتابعة تعديل الإضافات.","الحامي PDF",MessageBoxButton.OK,MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex) { Error("تعذر حفظ المشروع.\n"+ex.Message); return false; }
        finally { SetBusy(false); }
    }
}
