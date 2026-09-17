using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using HamiPdf.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace HamiPdf.Forms;

public partial class FormWindow : Window
{
    private const string Origin = "https://hami-forms.invalid";
    private const string ViewerUrl = Origin + "/index.html";
    private readonly string sourcePath;
    private bool dirty, busy, closed;
    public string? SavedPath { get; private set; }

    public FormWindow(string path)
    {
        sourcePath = PdfFile.Validate(path);
        InitializeComponent();
        Title = $"تعبئة النماذج — {Path.GetFileName(sourcePath)}";
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (new FileInfo(sourcePath).Length > 64 * 1024 * 1024)
                throw new IOException("حد حجم النموذج في هذه المرحلة 64 ميغابايت.");
            var folder = Path.Combine(AppContext.BaseDirectory, "Forms", "Web");
            if (!File.Exists(Path.Combine(folder, "index.html")))
                throw new IOException("ملفات عارض النماذج ناقصة. أعد بناء البرنامج أو تثبيته.");
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HamiPdf", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, profile, HamiPdf.Services.BrowserRuntime.CreateOptions());
            if (closed) return;
            await Browser.EnsureCoreWebView2Async(env);
            if (closed) return;
            var core = Browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("hami-forms.invalid", folder, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NavigationStarting += (_, args) => { if (args.Uri != ViewerUrl) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.WebMessageReceived += MessageReceived;
            core.ProcessFailed += (_, _) => { if (!closed) MessageBox.Show(this, "توقف عارض النماذج. التعديلات غير المحفوظة قد لا تكون متاحة؛ أغلق النافذة وأعد فتحها.", "الحامي PDF"); };
            core.Navigate(ViewerUrl);
        }
        catch (Exception ex) { if (!closed) MessageBox.Show(this, ex.Message, "تعذر فتح النموذج", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (closed || e.Source != ViewerUrl) return;
        try
        {
            using var json = JsonDocument.Parse(e.WebMessageAsJson);
            var root = json.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    var bytes = await File.ReadAllBytesAsync(sourcePath);
                    if (!closed) await Browser.ExecuteScriptAsync($"window.hami.open({JsonSerializer.Serialize(Convert.ToBase64String(bytes))})");
                    break;
                case "dirty": dirty = true; break;
                case "save":
                    if (busy) return;
                    busy = true;
                    try
                    {
                        var data = Convert.FromBase64String(root.GetProperty("data").GetString()!);
                        if (data.Length < 8 || data.Length > 96 * 1024 * 1024 || System.Text.Encoding.ASCII.GetString(data, 0, 5) != "%PDF-")
                            throw new IOException("بيانات الحفظ غير صالحة.");
                        var dialog = new SaveFileDialog {
                            Title = "حفظ النموذج التفاعلي باسم", Filter = "PDF (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true,
                            FileName = SavedPath == null ? Path.GetFileNameWithoutExtension(sourcePath) + "_معبأ.pdf" : Path.GetFileName(SavedPath),
                            InitialDirectory = Path.GetDirectoryName(SavedPath ?? sourcePath), OverwritePrompt = true
                        };
                        if (dialog.ShowDialog(this) != true) { Reply(false, "أُلغي الحفظ؛ التعديلات ما زالت في النافذة."); break; }
                        var target = Path.GetFullPath(dialog.FileName);
                        if (string.Equals(target, sourcePath, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("اختر اسمًا مختلفًا للمحافظة على الملف الأصلي.");
                        var prepared = FormAppearance.Prepare(data);
                        AtomicPdfSave.Write(target, prepared);
                        SavedPath = target; dirty = false;
                        Reply(true, "حُفظت نسخة تفاعلية بنجاح. يمكنك متابعة تعديل الحقول.");
                    }
                    finally { busy = false; }
                    break;
            }
        }
        catch (Exception ex) { Reply(false, "تعذر الحفظ أو فتح النموذج: " + ex.Message); }
    }
    private void Reply(bool ok, string message)
    {
        if (!closed && Browser.CoreWebView2 != null)
            Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "saved", ok, message }));
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; return; }
        if (dirty && MessageBox.Show(this, "توجد تعديلات غير محفوظة. هل تريد إغلاق النافذة وفقدها؟\nاختر «لا» ثم «حفظ نسخة» للاحتفاظ بها.", "تعبئة النماذج", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        if (closed) return;
        closed = true;
        ShutdownTrace.Write("form.closing.begin");
        if (Browser.CoreWebView2 is {} core)
        {
            core.WebMessageReceived -= MessageReceived;
            core.Stop();
        }
        Browser.Dispose();
        Content = null;
        ShutdownTrace.Write("form.closing.end");
    }
    private void OnClosed(object? sender, EventArgs e) => ShutdownTrace.Write("form.closed");
}
