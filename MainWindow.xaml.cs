using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HamiPdf.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace HamiPdf;

public partial class MainWindow : Window
{
    private sealed class DocumentTab(string path)
    {
        public string Path { get; } = path;
        public WebView2 Viewer { get; } = new() { FlowDirection = FlowDirection.LeftToRight };
        public TabItem Header { get; } = new();
        public bool Ready { get; set; }
        public bool Removed { get; set; }
        public string Status { get; set; } = "جارٍ تحميل المستند…";
    }
    private readonly List<DocumentTab> documents = [];
    private readonly Queue<string> pending = new();
    private readonly DispatcherTimer queueTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private Task<CoreWebView2Environment>? environment;
    private bool opening, closed, modalOpen;
    private DocumentTab? Active => (DocumentTabs.SelectedItem as TabItem)?.Tag as DocumentTab;

    public MainWindow()
    {
        InitializeComponent();
        queueTimer.Tick += async (_, _) => await DrainAsync();
        queueTimer.Start();
    }
    public void EnqueueFiles(IEnumerable<string> paths)
    {
        if (closed) return;
        foreach (var path in paths) pending.Enqueue(path);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    private async Task DrainAsync()
    {
        if (closed || opening || modalOpen || !IsEnabled || pending.Count == 0) return;
        opening = true;
        try
        {
            var path = pending.Dequeue();
            if (string.Equals(Path.GetExtension(path), ".hamipdf", StringComparison.OrdinalIgnoreCase)) OpenProject(path);
            else await OpenPdfAsync(path);
        }
        catch (Exception ex) { if (!closed) ShowError("تعذر فتح الملف.\n" + ex.Message); }
        finally { opening = false; }
    }
    private void Window_Loaded(object sender, RoutedEventArgs e) => RefreshActive();
    private void Open_Click(object sender, RoutedEventArgs e) => ChooseFiles();
    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) => ChooseFiles();
    private void ChooseFiles()
    {
        var dialog = new OpenFileDialog { Title = "اختر ملفًا أو عدة ملفات PDF", Filter = "PDF (*.pdf)|*.pdf", CheckFileExists = true, Multiselect = true };
        if (Modal(() => dialog.ShowDialog(this)) == true) EnqueueFiles(dialog.FileNames);
    }
    private async Task OpenPdfAsync(string path)
    {
        string fullPath = PdfFile.Validate(path);
        var existing = documents.FirstOrDefault(x => string.Equals(x.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { DocumentTabs.SelectedItem = existing.Header; return; }
        var tab = new DocumentTab(fullPath);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = Path.GetFileName(fullPath), MaxWidth = 230, FontSize = 12, FlowDirection = FlowDirection.LeftToRight, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", ToolTip = "إغلاق التبويب", Style = (Style)FindResource("TabCloseButton"), Margin = new Thickness(6,0,0,0) };
        close.Click += (_, e) => { e.Handled = true; RemoveTab(tab); };
        header.Children.Add(close);
        tab.Header.Header = header; tab.Header.Tag = tab; tab.Header.ToolTip = fullPath;
        documents.Add(tab);
        // Keep every viewer mounted. Switching tabs only changes visibility, preserving PDF page/zoom state.
        ViewerHost.Children.Add(tab.Viewer);
        DocumentTabs.Items.Add(tab.Header); DocumentTabs.SelectedItem = tab.Header;
        RefreshActive();
        try
        {
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HamiPdf", "WebView2");
            environment ??= CoreWebView2Environment.CreateAsync(null, profile, HamiPdf.Services.BrowserRuntime.CreateOptions());
            var env = await environment;
            if (closed || tab.Removed) return;
            await tab.Viewer.EnsureCoreWebView2Async(env);
            if (closed || tab.Removed) return;
            var core = tab.Viewer.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                if (e.Uri == "about:blank") return;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.IsFile && string.Equals(uri.LocalPath, tab.Path, StringComparison.OrdinalIgnoreCase)) return;
                e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NavigationCompleted += (_, e) =>
            {
                if (closed || tab.Removed) return;
                tab.Ready = e.IsSuccess;
                tab.Status = e.IsSuccess ? "استخدم شريط العارض للتكبير والبحث والطباعة" : "تعذر عرض الملف؛ أغلق تبويبه وأعد فتحه.";
                RefreshActive();
            };
            core.ProcessFailed += (_, _) =>
            {
                if (closed || tab.Removed) return;
                tab.Ready = false; tab.Status = "توقف محرك العرض؛ أغلق التبويب وأعد فتحه."; RefreshActive();
            };
            core.Navigate(new Uri(fullPath).AbsoluteUri);
        }
        catch (Exception ex)
        {
            if (environment?.IsFaulted == true) environment = null;
            if (closed || tab.Removed) return;
            RemoveTab(tab);
            ShowError(ex is WebView2RuntimeNotFoundException ? "يلزم تثبيت Microsoft Edge WebView2 Runtime لتشغيل العارض." : "تعذر فتح الملف.\n" + ex.Message);
        }
    }
    private void RefreshActive()
    {
        if (closed || WelcomePanel == null) return;
        var active = Active;
        foreach (var tab in documents) tab.Viewer.Visibility = tab == active ? Visibility.Visible : Visibility.Collapsed;
        WelcomePanel.Visibility = documents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DocumentTabs.Visibility = documents.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        FormsButton.IsEnabled = PrintButton.IsEnabled = EditButton.IsEnabled = active?.Ready == true;
        CloseButton.IsEnabled = active != null;
        Title = active == null ? "الحامي PDF" : $"{Path.GetFileName(active.Path)} — الحامي PDF";
        StatusLabel.Text = active == null ? "جاهز · افتح ملفات أو اسحبها إلى شريط التبويبات" : $"{documents.Count} ملفات · {active.Status}";
    }
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshActive();
    private void RemoveTab(DocumentTab tab)
    {
        if (tab.Removed) return;
        tab.Removed = true;
        documents.Remove(tab); DocumentTabs.Items.Remove(tab.Header);
        tab.Viewer.CoreWebView2?.Stop();
        tab.Viewer.Dispose();
        ViewerHost.Children.Remove(tab.Viewer);
        if (DocumentTabs.SelectedIndex < 0 && DocumentTabs.Items.Count > 0) DocumentTabs.SelectedIndex = 0;
        RefreshActive();
    }
    private void Close_Click(object sender, RoutedEventArgs e) { if (Active is {} tab) RemoveTab(tab); }
    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "فتح مشروع", Filter = "HamiPdf (*.hamipdf)|*.hamipdf", CheckFileExists = true };
        if (Modal(() => dialog.ShowDialog(this)) == true) EnqueueFiles(dialog.FileNames);
    }
    private void OpenProject(string path)
    {
        var editor = new Editor.EditorWindow(path, openProject: true) { Owner = this };
        Modal(() => editor.ShowDialog());
        if (!closed && editor.SavedPath is string saved) EnqueueFiles([saved]);
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Active is not { Ready: true } tab) return;
        var editor = new Editor.EditorWindow(tab.Path) { Owner = this };
        Modal(() => editor.ShowDialog());
        if (!closed && editor.SavedPath is string saved) EnqueueFiles([saved]);
    }
    private void Forms_Click(object sender, RoutedEventArgs e)
    {
        if (Active is not { Ready: true } tab) return;
        try
        {
            var form = new Forms.FormWindow(tab.Path) { Owner = this };
            Modal(() => form.ShowDialog());
            if (!closed && form.SavedPath is string saved)
            {
                var existing = documents.FirstOrDefault(x => string.Equals(x.Path, saved, StringComparison.OrdinalIgnoreCase));
                if (existing != null) { existing.Viewer.CoreWebView2?.Reload(); DocumentTabs.SelectedItem = existing.Header; }
                else EnqueueFiles([saved]);
            }
        }
        catch (Exception ex) { ShowError("تعذر فتح محرر النماذج.\n" + ex.Message); }
    }
    private void Pages_Click(object sender, RoutedEventArgs e)
    {
        var manager = new Pages.PageManagerWindow(Active?.Path) { Owner = this };
        Modal(() => manager.ShowDialog());
        if (!closed && manager.SavedPath is string saved) EnqueueFiles([saved]);
    }
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (Active is not { Ready: true } tab) return;
        try { tab.Viewer.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
        catch (Exception ex) { ShowError("تعذرت الطباعة.\n" + ex.Message); }
    }
    private void Files_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void Files_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) EnqueueFiles(paths);
        e.Handled = true;
    }
    private void About_Click(object sender, RoutedEventArgs e) => Modal(() => new AboutWindow { Owner = this }.ShowDialog());
    private void Help_Click(object sender, RoutedEventArgs e) => Modal(() => MessageBox.Show(this,
        "فتح أو Ctrl+O لاختيار عدة ملفات PDF. اسحب الملفات إلى الشريط العلوي أو منطقة الترحيب.\nكل ملف في تبويب؛ النقر على ملف مفتوح يحدد تبويبه. × يغلق التبويب فقط.\nالطباعة والتعديل وإدارة الصفحات تخص التبويب النشط.\nالمحرر في نافذة مستقلة: Ctrl+S يحفظ المشروع، وCtrl+Shift+S يصدّر PDF إلى تبويب جديد.\nالبحث من داخل المستند: Ctrl+F. النقر المزدوج من Windows يفتح الملفات في النافذة الموجودة.",
        "الحامي PDF", MessageBoxButton.OK, MessageBoxImage.Information));
    private T Modal<T>(Func<T> action)
    {
        bool prior = modalOpen; modalOpen = true;
        try { return action(); }
        finally { modalOpen = prior; }
    }
    private void ShowError(string message)
    {
        if (!closed) Modal(() => MessageBox.Show(this, message, "الحامي PDF", MessageBoxButton.OK, MessageBoxImage.Warning));
    }
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closed) return;
        // Keep child save/discard decisions in their own windows.
        if (modalOpen) { e.Cancel = true; return; }
        ShutdownTrace.Write("main.closing.begin");
        closed = true; queueTimer.Stop(); pending.Clear();
        // Dispose native child controllers while the parent HWND is still alive.
        foreach (var tab in documents) tab.Removed = true;
        foreach (var tab in documents)
        {
            ShutdownTrace.Write("viewer.dispose.begin");
            tab.Viewer.CoreWebView2?.Stop();
            tab.Viewer.Dispose();
            ShutdownTrace.Write("viewer.dispose.end");
        }
        ViewerHost.Children.Clear();
        documents.Clear();
        ShutdownTrace.Write("main.closing.end");
    }
    private void Window_Closed(object? sender, EventArgs e)
    {
        ShutdownTrace.Write("main.closed");
    }
}
