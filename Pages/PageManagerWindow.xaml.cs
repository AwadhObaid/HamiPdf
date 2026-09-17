using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HamiPdf.Editor;
using Microsoft.Win32;

namespace HamiPdf.Pages;

public partial class PageManagerWindow : Window
{
    private sealed record PageRow(Guid Id, int Order, string SourceName, int OriginalPage, string RotationLabel);
    private sealed record History(List<PageEntry> Pages, Guid Revision);
    private readonly string? initialPath;
    private readonly Dictionary<Guid, SourceRecord> sources = new();
    private readonly Dictionary<Guid, PdfSession> sessions = new();
    private List<PageEntry> pages = [];
    private readonly Stack<History> undo = new(), redo = new();
    private Guid revision = Guid.NewGuid(), savedRevision;
    private bool busy, refresh, forceClose, closed;
    private readonly SemaphoreSlim previewGate = new(1, 1);
    private int previewVersion;
    public string? SavedPath { get; private set; }
    private bool Dirty => revision != savedRevision;

    public PageManagerWindow(string? path)
    {
        InitializeComponent(); initialPath = path; savedRevision = revision; UpdateButtons();
    }

    private HashSet<Guid> Selection() => PageList.SelectedItems.Cast<PageRow>().Select(p => p.Id).ToHashSet();
    private void SetBusy(bool value)
    { busy = value; ToolsPanel.IsEnabled = PageList.IsEnabled = !value; UpdateButtons(); }
    private void UpdateButtons()
    {
        bool selected = PageList.SelectedItems.Count > 0;
        UpButton.IsEnabled = DownButton.IsEnabled = RotateButton.IsEnabled = DeleteButton.IsEnabled = ExtractButton.IsEnabled = selected;
        SaveButton.IsEnabled = pages.Count > 0;
        UndoButton.IsEnabled = undo.Count > 0; RedoButton.IsEnabled = redo.Count > 0;
        Title = (Dirty ? "• " : "") + "إدارة الصفحات — الحامي PDF";
    }

    private async void Manager_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialPath != null) await ImportAsync([initialPath], initial: true);
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Title = "اختر ملفات PDF لإضافتها", Filter = "PDF (*.pdf)|*.pdf", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await ImportAsync(dialog.FileNames, initial: false);
    }
    private async Task ImportAsync(string[] paths, bool initial)
    {
        if (busy) return;
        SetBusy(true);
        var added = new List<(SourceRecord Source, PdfSession Session)>();
        bool committed = false;
        try
        {
            foreach (string path in paths)
            {
                StatusLabel.Text = "جارٍ قراءة: " + Path.GetFileName(path);
                var session = await PdfSession.OpenAsync(path);
                try { await Task.Run(() => PageExport.CheckSource(session.SourceBytes)); }
                catch { session.Dispose(); throw; }
                added.Add((new SourceRecord(Guid.NewGuid(), Path.GetFileName(path), session.SourceBytes), session));
            }
            if (!initial) BeginChange();
            var selected = new HashSet<Guid>();
            foreach (var (source, session) in added)
            {
                sources.Add(source.Id, source); sessions.Add(source.Id, session);
                for (int i = 0; i < session.Pages.Count; i++)
                {
                    var entry = new PageEntry(Guid.NewGuid(), source.Id, i);
                    pages.Add(entry);
                    if (selected.Count == 0) selected.Add(entry.Id);
                }
            }
            committed = true;
            RefreshRows(selected);
            await PreviewAsync();
        }
        catch (Exception ex) { Error("تعذرت إضافة الملفات.\n" + ex.Message, ex); }
        finally
        {
            if (!committed) foreach (var pair in added) pair.Session.Dispose();
            SetBusy(false);
        }
    }

    private History Snapshot() => new(pages.ToList(), revision);
    private void BeginChange() { undo.Push(Snapshot()); redo.Clear(); revision = Guid.NewGuid(); }
    private void RefreshRows(HashSet<Guid>? selected = null)
    {
        refresh = true;
        var rows = pages.Select((p, i) => new PageRow(p.Id, i+1, sources[p.SourceId].Name, p.SourcePage+1, p.Rotation+"°")).ToList();
        PageList.ItemsSource = rows;
        if (selected != null) foreach (var row in rows) if (selected.Contains(row.Id)) PageList.SelectedItems.Add(row);
        if (PageList.SelectedItems.Count == 0 && rows.Count > 0) PageList.SelectedIndex = 0;
        refresh = false; UpdateButtons();
    }

    private async Task PreviewAsync()
    {
        int request = ++previewVersion;
        await previewGate.WaitAsync();
        try
        {
            if (closed || request != previewVersion) return;
            var row = PageList.SelectedItem as PageRow;
            PreviewImage.Source = null;
            if (row == null) { PreviewLabel.Text = "معاينة الصفحة المحددة"; StatusLabel.Text = "أضف ملفات PDF للبدء"; return; }
            var entry = pages.FirstOrDefault(p => p.Id == row.Id);
            if (entry == null) return;
            try
            {
                var image = await sessions[entry.SourceId].RenderAsync(entry.SourcePage);
                if (closed || request != previewVersion) return;
                PreviewLabel.Text = $"صفحة {row.Order} — {row.SourceName}";
                PreviewImage.Source = image;
                PreviewRotation.Angle = entry.Rotation;
                StatusLabel.Text = $"الصفحات: {pages.Count} · المحدد: {PageList.SelectedItems.Count} · الحفظ ينشئ ملفًا جديدًا";
            }
            catch (Exception ex)
            {
                if (closed || request != previewVersion) return;
                PreviewLabel.Text = "تعذرت معاينة الصفحة";
                var log = WriteDiagnostic(ex);
                StatusLabel.Text = "تعذرت المعاينة؛ يمكن إدارة الصفحة وحفظها. " + ex.Message + (log == null ? "" : " · السجل: " + log);
            }
        }
        finally { previewGate.Release(); }
    }

    private async void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refresh || busy || closed) return;
        // Keep the list enabled during preview so Ctrl/Shift selection is not interrupted.
        // Serialized rendering and a request number prevent stale previews winning a race.
        UpdateButtons();
        await PreviewAsync();
    }
    private async Task ChangeAsync(List<PageEntry> next, HashSet<Guid> selected)
    {
        if (busy || pages.SequenceEqual(next)) return;
        SetBusy(true);
        try { BeginChange(); pages = next; RefreshRows(selected); await PreviewAsync(); }
        finally { SetBusy(false); }
    }
    private async void Up_Click(object sender, RoutedEventArgs e) { var ids = Selection(); await ChangeAsync(PagePlan.Move(pages, ids, true), ids); }
    private async void Down_Click(object sender, RoutedEventArgs e) { var ids = Selection(); await ChangeAsync(PagePlan.Move(pages, ids, false), ids); }
    private async void Rotate_Click(object sender, RoutedEventArgs e) { var ids = Selection(); await ChangeAsync(PagePlan.Rotate(pages, ids), ids); }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var ids = Selection();
        if (busy || ids.Count == 0) return;
        if (ids.Count == pages.Count) { Error("يجب إبقاء صفحة واحدة على الأقل."); return; }
        await ChangeAsync(pages.Where(p => !ids.Contains(p.Id)).ToList(), []);
    }
    private async Task RestoreAsync(bool forward)
    {
        var from = forward ? redo : undo; var to = forward ? undo : redo;
        if (busy || from.Count == 0) return;
        SetBusy(true);
        try
        {
            var selected = Selection(); to.Push(Snapshot()); var state = from.Pop();
            pages = state.Pages.ToList(); revision = state.Revision; RefreshRows(selected); await PreviewAsync();
        }
        finally { SetBusy(false); }
    }
    private async void Undo_Click(object sender, RoutedEventArgs e) => await RestoreAsync(false);
    private async void Redo_Click(object sender, RoutedEventArgs e) => await RestoreAsync(true);

    private async Task<bool> SaveAsync(bool extract)
    {
        if (busy || pages.Count == 0) return false;
        var selection = extract ? PagePlan.Extract(pages, Selection()) : pages.ToList();
        if (selection.Count == 0) return false;
        var dialog = new SaveFileDialog
        {
            Title = extract ? "استخراج الصفحات المحددة" : "حفظ الصفحات المرتبة", Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf", AddExtension = true, OverwritePrompt = true,
            FileName = "HamiPdf_" + (extract ? "Extract_" : "Pages_") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".pdf"
        };
        if (dialog.ShowDialog(this) != true) return false;
        if (File.Exists(dialog.FileName)) { Error("اختر اسمًا جديدًا غير موجود."); return false; }
        SetBusy(true);
        try
        {
            StatusLabel.Text = "جارٍ حفظ الصفحات والتحقق منها…";
            await Task.Run(() => PageExport.Save(selection, sources, dialog.FileName));
            SavedPath = dialog.FileName;
            if (!extract) savedRevision = revision; // Extract is not a save of the complete arrangement.
            StatusLabel.Text = "تم الحفظ: " + dialog.FileName;
            MessageBox.Show(this, $"حُفظت {selection.Count} صفحة بنجاح.\nستفتح آخر نسخة محفوظة في العارض عند إغلاق هذه النافذة.", "الحامي PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex) { Error("تعذر حفظ الصفحات. الملفات الأصلية لم تتغير.\n" + ex.Message, ex); return false; }
        finally { SetBusy(false); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void Extract_Click(object sender, RoutedEventArgs e) => await SaveAsync(true);
    private async void Manager_Closing(object? sender, CancelEventArgs e)
    {
        if (forceClose) return;
        if (busy) { e.Cancel = true; return; }
        if (!Dirty) return;
        var choice = MessageBox.Show(this, "ترتيب الصفحات يحتوي تغييرات غير محفوظة. هل تريد حفظ جميع الصفحات؟", "الحامي PDF", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.No) return;
        e.Cancel = true;
        if (choice == MessageBoxResult.Yes && await SaveAsync(false)) { forceClose = true; Close(); }
    }
    private async void Manager_Closed(object? sender, EventArgs e)
    {
        closed = true; previewVersion++;
        await previewGate.WaitAsync();
        try { foreach (var session in sessions.Values) session.Dispose(); }
        finally { previewGate.Release(); }
    }
    private static string? WriteDiagnostic(Exception exception)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HamiPdf", "logs");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "pages_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..8] + ".log");
            File.WriteAllText(path, "HamiPdf 0.7.2\n" + DateTime.Now.ToString("O") + "\n" + exception);
            return path;
        }
        catch { return null; }
    }
    private void Error(string message, Exception? exception = null)
    {
        string? log = exception == null ? null : WriteDiagnostic(exception);
        MessageBox.Show(this, message + (log == null ? "" : "\n\nسجل التشخيص:\n" + log), "الحامي PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
