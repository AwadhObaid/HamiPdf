using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NAPS2.Images;
using NAPS2.Images.Wpf;
using NAPS2.Scan;

namespace HamiPdf.Scanning;

public partial class ScanWindow : Window
{
    private sealed record Row(ScanPage Page, string Label);
    private readonly List<ScanPage> pages = [];
    private readonly ScanPreferences preferences = ScanPreferences.Load();
    private readonly string sessionFolder = Path.Combine(Path.GetTempPath(), "HamiPdf", "scan-" + Guid.NewGuid().ToString("N"));
    private readonly bool canAppend;
    private ScanningContext? nativeContext, win32Context;
    private ScanController? controller;
    private CancellationTokenSource? operation;
    private bool loaded, busy, dirty, closingAfterSave;
    public string? SavedPath { get; private set; }
    public bool AppendRequested { get; private set; }

    public ScanWindow(bool allowAppend)
    {
        InitializeComponent();
        canAppend = allowAppend;
        BackendBox.SelectedIndex = Math.Clamp(preferences.Backend, 0, 4);
        SourceBox.SelectedIndex = Math.Clamp(preferences.SourceIndex, 0, 2);
        ColorBox.SelectedIndex = Math.Clamp(preferences.ColorIndex, 0, 2);
        DpiBox.SelectedIndex = Math.Clamp(preferences.DpiIndex, 0, 2);
        SizeBox.SelectedIndex = Math.Clamp(preferences.SizeIndex, 0, 3);
        NativeUiBox.IsChecked = preferences.NativeUi;
        UpdateControls();
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(sessionFolder);
            loaded = true;
            await DiscoverAsync();
        }
        catch (Exception ex) { Report("تعذرت تهيئة المسح الضوئي. أعد بناء البرنامج للتأكد من وجود مكونات المسح.", ex); }
    }
    private ScanController ControllerForBackend()
    {
        // The SDK's default worker executable is x86. Its native worker path points to
        // NAPS2.exe, which is not part of this application. Keep native x64 TWAIN in
        // its own context WITHOUT SetUpWin32Worker; otherwise x64 discovery fails.
        bool needsWin32 = BackendBox.SelectedIndex is 1 or 3;
        if (needsWin32)
        {
            if (win32Context == null)
            {
                var candidate = new ScanningContext(new WpfImageContext()) { TempFolderPath = sessionFolder };
                try { candidate.SetUpWin32Worker(); win32Context = candidate; }
                catch { candidate.Dispose(); throw; }
            }
            return new ScanController(win32Context);
        }
        nativeContext ??= new ScanningContext(new WpfImageContext()) { TempFolderPath = sessionFolder };
        return new ScanController(nativeContext);
    }
    private ScanOptions ConnectionOptions() => new()
    {
        Driver = BackendBox.SelectedIndex switch { 0 => Driver.Wia, 4 => Driver.Escl, _ => Driver.Twain },
        TwainOptions = new TwainOptions
        {
            Dsm = BackendBox.SelectedIndex switch { 2 => TwainDsm.NewX64, 3 => TwainDsm.Old, _ => TwainDsm.New },
            IncludeWiaDevices = true
        },
        DialogParent = new WindowInteropHelper(this).Handle
    };
    private async void Backend_Changed(object sender, SelectionChangedEventArgs e)
    { if (loaded && !busy) await DiscoverAsync(); }
    private void Device_Changed(object sender, SelectionChangedEventArgs e) { if (loaded) UpdateControls(); }
    private async void Discover_Click(object sender, RoutedEventArgs e) => await DiscoverAsync();
    private async Task DiscoverAsync()
    {
        if (busy || !loaded) return;
        var options = ConnectionOptions();
        string selectedId = (DeviceBox.SelectedItem as ScanDevice)?.ID ?? preferences.DeviceId;
        DeviceBox.Items.Clear();
        BeginOperation();
        operation!.CancelAfter(TimeSpan.FromSeconds(30));
        StatusText.Text = "جارٍ البحث عن الماسحات…";
        try
        {
            controller = ControllerForBackend();
            await foreach (var device in controller.GetDevices(options, operation.Token)) DeviceBox.Items.Add(device);
            DeviceBox.SelectedItem = DeviceBox.Items.Cast<ScanDevice>().FirstOrDefault(d => d.ID == selectedId);
            if (DeviceBox.SelectedIndex < 0 && DeviceBox.Items.Count > 0) DeviceBox.SelectedIndex = 0;
            StatusText.Text = DeviceBox.Items.Count == 0
                ? "لم يُعثر على ماسح بهذه الطريقة. تحقق من الاتصال والتعريف ثم جرّب WIA أو TWAIN أو الشبكة."
                : $"تم العثور على {DeviceBox.Items.Count} ماسح. اختر الجهاز ثم ابدأ المسح.";
        }
        catch (OperationCanceledException) { StatusText.Text = "توقف البحث. يمكنك إعادة المحاولة أو اختيار طريقة اتصال أخرى."; }
        catch (Exception ex) { Report("تعذر البحث بهذه الطريقة. جرّب طريقة اتصال أخرى وتحقق من تعريف الماسح.", ex); }
        finally
        {
            if (DeviceBox.SelectedIndex < 0 && DeviceBox.Items.Count > 0) DeviceBox.SelectedIndex = 0;
            EndOperation();
        }
    }
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (busy || controller == null || DeviceBox.SelectedItem is not ScanDevice device) return;
        var options = ConnectionOptions();
        options.Device = device;
        options.Dpi = new[] { 150, 300, 600 }[DpiBox.SelectedIndex];
        options.BitDepth = new[] { BitDepth.Color, BitDepth.Grayscale, BitDepth.BlackAndWhite }[ColorBox.SelectedIndex];
        options.PaperSource = new[] { PaperSource.Flatbed, PaperSource.Feeder, PaperSource.Duplex }[SourceBox.SelectedIndex];
        options.PageSize = new[] { PageSize.A4, PageSize.A5, PageSize.Letter, PageSize.Legal }[SizeBox.SelectedIndex];
        options.UseNativeUI = NativeUiBox.IsChecked == true && options.Driver != Driver.Escl;
        options.MaxQuality = true;
        RememberSettings();
        BeginOperation();
        int before = pages.Count;
        StatusText.Text = "جارٍ المسح… يمكنك إيقاف العملية والاحتفاظ بالصفحات المكتملة.";
        try
        {
            await foreach (var image in controller.Scan(options, operation!.Token))
            {
                using (image)
                {
                    string file = Path.Combine(sessionFolder, Guid.NewGuid().ToString("N") + ".png");
                    // Materialize each page before requesting another: feeder jobs do not retain full images in RAM.
                    await Task.Run(() => image.Save(file));
                    var page = ReadPage(file, options.Dpi);
                    pages.Add(page); dirty = true;
                    RefreshPages(pages.Count - 1);
                    StatusText.Text = $"تم استلام {pages.Count - before} صفحة في هذه العملية؛ الإجمالي {pages.Count}.";
                }
            }
            StatusText.Text = pages.Count == before ? "لم تصل صفحات جديدة؛ ربما أُلغيت العملية من نافذة الماسح." : $"اكتمل المسح. راجع الصفحات ({pages.Count}) ثم احفظ PDF، أو أضف أوراقًا أخرى.";
        }
        catch (OperationCanceledException) { StatusText.Text = $"توقف المسح. بقيت {pages.Count} صفحة للمراجعة والحفظ."; }
        catch (Exception ex) { Report($"توقف المسح؛ الصفحات المكتملة ({pages.Count}) محفوظة في جلسة المعاينة. تحقق من الورق والاتصال، أو جرّب نافذة تعريف الماسح.", ex); }
        finally { EndOperation(); }
    }
    private static ScanPage ReadPage(string path, int fallbackDpi)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        double dx = double.IsFinite(frame.DpiX) && frame.DpiX >= 50 ? frame.DpiX : fallbackDpi;
        double dy = double.IsFinite(frame.DpiY) && frame.DpiY >= 50 ? frame.DpiY : fallbackDpi;
        return new ScanPage(path, frame.PixelWidth * 72.0 / dx, frame.PixelHeight * 72.0 / dy);
    }
    private void RememberSettings()
    {
        preferences.Backend = BackendBox.SelectedIndex;
        preferences.DeviceId = (DeviceBox.SelectedItem as ScanDevice)?.ID ?? "";
        preferences.DpiIndex = DpiBox.SelectedIndex; preferences.ColorIndex = ColorBox.SelectedIndex;
        preferences.SourceIndex = SourceBox.SelectedIndex; preferences.SizeIndex = SizeBox.SelectedIndex;
        preferences.NativeUi = NativeUiBox.IsChecked == true;
        try { preferences.Save(); } catch { /* Preferences must never prevent scanning. */ }
    }
    private void BeginOperation() { busy = true; operation = new(); UpdateControls(); }
    private void EndOperation() { operation?.Dispose(); operation = null; busy = false; UpdateControls(); }
    private void UpdateControls()
    {
        if (SettingsPanel == null) return;
        SettingsPanel.IsEnabled = EditPanel.IsEnabled = PageList.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy && controller != null && DeviceBox.SelectedItem != null;
        SaveButton.IsEnabled = !busy && pages.Count > 0;
        AppendButton.IsEnabled = !busy && canAppend && pages.Count > 0;
        CancelButton.IsEnabled = busy && operation != null;
        NativeUiBox.IsEnabled = BackendBox.SelectedIndex != 4;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        operation?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "جارٍ طلب الإيقاف. إذا كانت نافذة تعريف الماسح مفتوحة، أغلقها بزر الإلغاء وانتظر انتهاء العملية.";
    }
    private void RefreshPages(int selected)
    {
        PageList.ItemsSource = pages.Select((p, i) => new Row(p, $"صفحة {i + 1}   ·   {p.Rotation}°")).ToList();
        PageList.SelectedIndex = pages.Count == 0 ? -1 : Math.Clamp(selected, 0, pages.Count - 1);
        UpdateControls();
    }
    private void Page_Selected(object sender, SelectionChangedEventArgs e)
    {
        PreviewImage.Source = null;
        EmptyPreview.Visibility = PageList.SelectedItem == null ? Visibility.Visible : Visibility.Collapsed;
        if (PageList.SelectedItem is not Row row) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(row.Page.ImagePath); image.DecodePixelWidth = 1100; image.EndInit(); image.Freeze();
            PreviewImage.LayoutTransform = new RotateTransform(row.Page.Rotation);
            PreviewImage.Source = image;
        }
        catch (Exception ex) { Report("تعذر عرض المعاينة.", ex); }
    }
    private void Move(int delta)
    {
        int index = PageList.SelectedIndex, target = index + delta;
        if (busy || index < 0 || target < 0 || target >= pages.Count) return;
        (pages[index], pages[target]) = (pages[target], pages[index]); dirty = true; RefreshPages(target);
    }
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        int i = PageList.SelectedIndex; if (busy || i < 0) return;
        pages[i] = pages[i] with { Rotation = (pages[i].Rotation + 90) % 360 }; dirty = true; RefreshPages(i);
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        int i = PageList.SelectedIndex; if (busy || i < 0) return;
        var page = pages[i]; pages.RemoveAt(i); dirty = pages.Count > 0;
        RefreshPages(i);
        try { File.Delete(page.ImagePath); } catch { }
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void Append_Click(object sender, RoutedEventArgs e) => await SaveAsync(true);
    private async Task SaveAsync(bool append)
    {
        if (busy || pages.Count == 0) return;
        var dialog = new SaveFileDialog { Title = "حفظ الصفحات الممسوحة كملف جديد", Filter = "PDF (*.pdf)|*.pdf", DefaultExt = ".pdf", FileName = "Scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".pdf", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName)) { MessageBox.Show(this, "اختر اسمًا جديدًا لحماية الملفات الموجودة.", "المسح الضوئي"); return; }
        busy = true; UpdateControls();
        StatusText.Text = "جارٍ إنشاء PDF والتحقق منه…";
        try
        {
            var snapshot = pages.ToArray();
            await Task.Run(() => ScanPdfWriter.Save(snapshot, dialog.FileName));
            SavedPath = dialog.FileName; AppendRequested = append; dirty = false; closingAfterSave = true;
        }
        catch (Exception ex) { Report("تعذر الحفظ. الصفحات ما زالت متاحة؛ اختر مسارًا آخر وحاول مجددًا.", ex); }
        finally { busy = false; UpdateControls(); }
        if (closingAfterSave) Close();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (busy)
        {
            e.Cancel = true;
            if (operation != null) Cancel_Click(this, new RoutedEventArgs());
            else StatusText.Text = "انتظر اكتمال حفظ الملف قبل الإغلاق.";
            return;
        }
        if (!closingAfterSave && dirty && MessageBox.Show(this, "توجد صفحات لم تُحفظ. هل تريد تجاهلها وإغلاق النافذة؟", "المسح الضوئي", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            e.Cancel = true;
    }
    private void Window_Closed(object? sender, EventArgs e)
    {
        // Never dispose the context while a driver callback or export is still active.
        try { win32Context?.Dispose(); } catch { }
        try { nativeContext?.Dispose(); } catch { }
        PreviewImage.Source = null;
        try { if (Directory.Exists(sessionFolder)) Directory.Delete(sessionFolder, true); } catch { }
    }
    private void Report(string message, Exception ex)
    {
        StatusText.Text = message;
        string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HamiPdf", "logs", "scanner.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            if (File.Exists(log) && new FileInfo(log).Length > 2_000_000) File.Move(log, log + ".previous", true);
            File.AppendAllText(log, $"{DateTime.Now:O} {ex}\n");
        }
        catch { }
        MessageBox.Show(this, message + "\n\n" + ex.Message, "المسح الضوئي", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
