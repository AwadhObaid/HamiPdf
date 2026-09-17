using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HamiPdf.Editor;

public partial class EditorWindow : Window
{
    private sealed record EditState(List<OverlayItem> Items, Guid Revision);
    private readonly string sourcePath;
    private PdfSession? session;
    private List<OverlayItem> items = [];
    private readonly Stack<EditState> undo = new();
    private readonly Stack<EditState> redo = new();
    private readonly Dictionary<Guid, Grid> views = new();
    private Guid revision = Guid.NewGuid();
    private Guid savedRevision;
    private Guid? selectedId;
    private int pageIndex;
    private bool busy = true, refresh, forceClose;
    private Guid? dragId;
    private Point dragOrigin;
    private double initialX, initialY;
    private bool dragChanged;
    public string? SavedPath { get; private set; }
    private OverlayItem? Selected => items.FirstOrDefault(x => x.Id == selectedId);
    private PageGeometry CurrentPage => session!.Pages[pageIndex];
    private bool Dirty => revision != savedRevision;

    public EditorWindow(string path, bool openProject = false)
    {
        InitializeComponent();
        sourcePath = Path.GetFullPath(path);
        isProjectInput = openProject;
        documentName = Path.GetFileName(path);
        savedRevision = revision;
        DocumentLabel.Text = Path.GetFileName(path);
        FontInput.ItemsSource = Fonts.SystemFontFamilies.Select(x => x.Source).OrderBy(x => x).ToList();
        FontInput.SelectedItem = "Segoe UI";
    }

    private async void Editor_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (isProjectInput)
            {
                var project = await Task.Run(() => ProjectArchive.Load(sourcePath));
                session = await PdfSession.OpenBytesAsync(project.SourcePdf);
                items = ProjectArchive.Restore(project, session.Pages);
                pageIndex = project.CurrentPage;
                documentName = project.SourceName;
                currentProjectPath = sourcePath;
                DocumentLabel.Text = documentName + " · " + Path.GetFileName(sourcePath);
            }
            else session = await PdfSession.OpenAsync(sourcePath);
            await RenderPageAsync();
            FitPage();
        }
        catch (Exception ex)
        {
            Error("تعذر فتح الملف للتحرير.\n" + ex.Message);
            forceClose = true;
            Close();
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        ToolsPanel.IsEnabled = NavigationPanel.IsEnabled = session != null && !busy;
        OverlayCanvas.IsHitTestVisible = !busy;
        ItemList.IsEnabled = !busy;
        PropertiesPanel.IsEnabled = !busy && Selected != null;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        UndoButton.IsEnabled = undo.Count > 0;
        RedoButton.IsEnabled = redo.Count > 0;
        DeleteButton.IsEnabled = Selected != null;
        PreviousButton.IsEnabled = pageIndex > 0;
        NextButton.IsEnabled = session != null && pageIndex + 1 < session.Pages.Count;
        Title = (Dirty ? "• " : "") + "إضافة وتعديل — الحامي PDF";
    }

    private async Task RenderPageAsync()
    {
        if (session == null) return;
        PageImage.Source = null;
        OverlayCanvas.Children.Clear();
        EditorStatus.Text = "جارٍ تجهيز الصفحة…";
        var bitmap = await session.RenderAsync(pageIndex);
        PageSurface.Width = CurrentPage.DisplayWidth;
        PageSurface.Height = CurrentPage.DisplayHeight;
        PageImage.Source = bitmap;
        PageInput.Text = (pageIndex + 1).ToString(CultureInfo.InvariantCulture);
        PageCountLabel.Text = "/ " + session.Pages.Count;
        selectedId = null;
        Redraw();
        PageScroll.ScrollToTop();
        EditorStatus.Text = "أضف نصًا أو صورة أو ختمًا، ثم اسحب العنصر إلى مكانه.";
    }

    private EditState Snapshot() => new(items.Select(x => x.Copy()).ToList(), revision);
    private void BeginChange()
    {
        undo.Push(Snapshot());
        redo.Clear();
        revision = Guid.NewGuid();
        UpdateButtons();
    }

    private void Restore(EditState state)
    {
        items = state.Items.Select(x => x.Copy()).ToList();
        revision = state.Revision;
        if (Selected?.Page != pageIndex) selectedId = null;
        Redraw();
        EditorStatus.Text = "تم تحديث الإضافات؛ يشمل التراجع جميع الصفحات.";
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (busy || undo.Count == 0) return;
        CancelDrawing();
        redo.Push(Snapshot()); Restore(undo.Pop());
    }
    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (busy || redo.Count == 0) return;
        CancelDrawing();
        undo.Push(Snapshot()); Restore(redo.Pop());
    }

    private void Add(OverlayItem item)
    {
        if (session == null || busy || !CommitProperties()) return;
        CancelDrawing();
        item.Page = pageIndex;
        item.Width = Math.Min(item.Width, CurrentPage.DisplayWidth);
        item.Height = Math.Min(item.Height, CurrentPage.DisplayHeight);
        item.X = Math.Max(0, (CurrentPage.DisplayWidth - item.Width) / 2);
        item.Y = Math.Max(0, (CurrentPage.DisplayHeight - item.Height) / 3);
        BeginChange(); items.Add(item); selectedId = item.Id; Redraw();
    }
    private void AddText_Click(object sender, RoutedEventArgs e) => Add(new OverlayItem());
    private void AddStamp_Click(object sender, RoutedEventArgs e) => Add(new OverlayItem
    { Kind = OverlayKind.Stamp, Text = "معتمد", Color = "#16836E", Width = 160, Height = 65, FontSize = 28, Bold = true, Alignment = TextAlignment.Center });

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Title = "اختر صورة أو ختمًا بصيغة صورة" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bitmap = OverlayVisual.LoadImage(dialog.FileName);
            double width = Math.Min(220, CurrentPage.DisplayWidth * .7);
            double height = width * bitmap.PixelHeight / bitmap.PixelWidth;
            if (height > CurrentPage.DisplayHeight * .7)
            { height = CurrentPage.DisplayHeight * .7; width = height * bitmap.PixelWidth / bitmap.PixelHeight; }
            Add(new OverlayItem { Kind = OverlayKind.Image, Image = bitmap, Width = width, Height = height });
        }
        catch (Exception ex) { Error("تعذر فتح الصورة.\n" + ex.Message); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Selected == null) return;
        BeginChange(); items.RemoveAll(x => x.Id == selectedId); selectedId = null; Redraw();
    }

    private void Redraw()
    {
        OverlayCanvas.Children.Clear(); views.Clear();
        foreach (var item in items.Where(x => x.Page == pageIndex))
        {
            var view = new Grid { Width = item.Width, Height = item.Height, Cursor = Cursors.SizeAll, Background = Brushes.Transparent, Tag = item.Id };
            view.Children.Add(OverlayVisual.Create(item));
            view.Children.Add(new Border { BorderBrush = Brushes.Teal, BorderThickness = new Thickness(1), IsHitTestVisible = false });
            var grip = new Thumb { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Background = Brushes.Teal };
            var gripTemplate = new ControlTemplate(typeof(Thumb));
            var gripBorder = new FrameworkElementFactory(typeof(Border));
            gripBorder.SetValue(Border.BackgroundProperty, Brushes.Teal);
            gripBorder.SetValue(Border.BorderBrushProperty, Brushes.White);
            gripBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            gripTemplate.VisualTree = gripBorder;
            grip.Template = gripTemplate;
            bool resizing = false;
            double resizeW = 0, resizeH = 0;
            Point resizeOrigin = default;
            grip.DragStarted += (_, _) => { if (!CommitProperties()) { grip.CancelDrag(); return; } Select(item.Id); resizeW = item.Width; resizeH = item.Height; resizeOrigin = Mouse.GetPosition(OverlayCanvas); resizing = false; };
            grip.DragDelta += (_, _) =>
            {
                var now = Mouse.GetPosition(OverlayCanvas);
                double dx = now.X - resizeOrigin.X, dy = now.Y - resizeOrigin.Y;
                if (!resizing && Math.Abs(dx) + Math.Abs(dy) < .5) return;
                if (!resizing) { BeginChange(); resizing = true; }
                double maxW = CurrentPage.DisplayWidth - item.X, maxH = CurrentPage.DisplayHeight - item.Y;
                if (item.Kind == OverlayKind.Image)
                {
                    double factor = Math.Max(.05, 1 + (Math.Abs(dx) >= Math.Abs(dy) ? dx / resizeW : dy / resizeH));
                    factor = Math.Min(factor, Math.Min(maxW / resizeW, maxH / resizeH));
                    item.Width = resizeW * factor; item.Height = resizeH * factor;
                }
                else
                {
                    item.Width = Math.Clamp(resizeW + dx, Math.Min(24, maxW), maxW);
                    item.Height = Math.Clamp(resizeH + dy, Math.Min(24, maxH), maxH);
                }
                UpdateView(item); RefreshProperties();
            };
            grip.DragCompleted += (_, _) => UpdateButtons();
            view.Children.Add(grip);
            view.MouseLeftButtonDown += (sender, e) =>
            {
                if (busy) return;
                if (!CommitProperties()) { e.Handled = true; return; }
                Select(item.Id);
                dragId = item.Id; dragOrigin = e.GetPosition(OverlayCanvas); initialX = item.X; initialY = item.Y; dragChanged = false;
                view.CaptureMouse(); e.Handled = true;
            };
            view.MouseMove += (_, e) =>
            {
                if (busy || dragId != item.Id || !view.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
                var point = e.GetPosition(OverlayCanvas);
                double dx = point.X - dragOrigin.X, dy = point.Y - dragOrigin.Y;
                if (!dragChanged && Math.Abs(dx) + Math.Abs(dy) < .5) return;
                if (!dragChanged) { BeginChange(); dragChanged = true; }
                item.X = Math.Clamp(initialX + dx, 0, Math.Max(0, CurrentPage.DisplayWidth - item.Width));
                item.Y = Math.Clamp(initialY + dy, 0, Math.Max(0, CurrentPage.DisplayHeight - item.Height));
                UpdateView(item); RefreshProperties();
            };
            view.MouseLeftButtonUp += (_, e) => { if (view.IsMouseCaptured) { view.ReleaseMouseCapture(); dragId = null; e.Handled = true; } };
            Canvas.SetLeft(view, item.X); Canvas.SetTop(view, item.Y);
            views[item.Id] = view; OverlayCanvas.Children.Add(view);
        }
        RefreshList(); RefreshSelection(); UpdateButtons();
    }

    private void UpdateView(OverlayItem item)
    {
        if (!views.TryGetValue(item.Id, out var view)) return;
        view.Width = item.Width; view.Height = item.Height;
        view.Children.RemoveAt(0); view.Children.Insert(0, OverlayVisual.Create(item));
        Canvas.SetLeft(view, item.X); Canvas.SetTop(view, item.Y);
    }

    private void Select(Guid? id) { selectedId = id; RefreshSelection(); UpdateButtons(); }
    private void RefreshSelection()
    {
        foreach (var (id, view) in views)
        {
            view.Children[1].Visibility = id == selectedId ? Visibility.Visible : Visibility.Hidden;
            view.Children[2].Visibility = id == selectedId ? Visibility.Visible : Visibility.Hidden;
        }
        refresh = true;
        ItemList.SelectedItem = ItemList.Items.Cast<ListBoxItem>().FirstOrDefault(x => (Guid)x.Tag == selectedId);
        refresh = false;
        RefreshProperties();
    }

    private void RefreshList()
    {
        refresh = true;
        ItemList.Items.Clear();
        foreach (var item in items.Where(x => x.Page == pageIndex))
        {
            string label = item.Kind == OverlayKind.Image ? "صورة / ختم مصور" : item.Text.Replace('\n', ' ');
            if (label.Length > 28) label = label[..28] + "…";
            ItemList.Items.Add(new ListBoxItem { Content = label, Tag = item.Id });
        }
        refresh = false;
    }

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refresh || busy) return;
        var id = ItemList.SelectedItem is ListBoxItem entry ? (Guid?)entry.Tag : null;
        if (!CommitProperties()) { RefreshSelection(); return; }
        Select(id);
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == OverlayCanvas && CommitProperties()) Select(null);
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private void RefreshProperties()
    {
        var item = Selected;
        PropertiesPanel.IsEnabled = item != null && !busy;
        SelectionLabel.Text = item == null ? "حدد عنصرًا لتعديل خصائصه" : item.Kind switch
        {
            OverlayKind.Image => "خصائص الصورة", OverlayKind.Stamp => "خصائص الختم",
            OverlayKind.Highlight => "خصائص التظليل", OverlayKind.Note => "خصائص الملاحظة",
            OverlayKind.Drawing => "خصائص الرسم", _ => "خصائص النص"
        };
        if (item == null) return;
        TextInput.Text = item.Text; FontInput.SelectedItem = item.FontFamily;
        FontSizeInput.Text = Number(item.FontSize);
        ColorInput.SelectedItem = ColorInput.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == item.Color);
        AlignmentInput.SelectedItem = AlignmentInput.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == item.Alignment.ToString());
        BoldInput.IsChecked = item.Bold; RtlInput.IsChecked = item.RightToLeft;
        TextInput.FlowDirection = item.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        TextInput.IsEnabled = FontInput.IsEnabled = FontSizeInput.IsEnabled =
            BoldInput.IsEnabled = RtlInput.IsEnabled = AlignmentInput.IsEnabled = item.HasText;
        ColorInput.IsEnabled = item.Kind != OverlayKind.Image;
        OpacityInput.Text = Number(item.Opacity * 100);
        StrokeInput.Text = Number(item.StrokeWidth);
        StrokeInput.IsEnabled = item.Kind == OverlayKind.Drawing;
        XInput.Text = Number(item.X); YInput.Text = Number(item.Y);
        WidthInput.Text = Number(item.Width); HeightInput.Text = Number(item.Height);
    }

    private static double Parse(string input)
    {
        string normalized = input.Trim();
        for (int i = 0; i < 10; i++) normalized = normalized.Replace((char)('٠' + i), (char)('0' + i)).Replace((char)('۰' + i), (char)('0' + i));
        normalized = normalized.Replace('٫', '.');
        if ((!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            && !double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) || !double.IsFinite(number))
            throw new FormatException("اكتب أرقامًا صحيحة في حقول الموضع والحجم.");
        return number;
    }

    private bool CommitProperties()
    {
        var item = Selected;
        if (item == null || session == null) return true;
        try
        {
            double x = Parse(XInput.Text), y = Parse(YInput.Text), w = Parse(WidthInput.Text), h = Parse(HeightInput.Text);
            double font = item.HasText ? Parse(FontSizeInput.Text) : item.FontSize;
            double opacity = Parse(OpacityInput.Text) / 100;
            double stroke = item.Kind == OverlayKind.Drawing ? Parse(StrokeInput.Text) : item.StrokeWidth;
            if (opacity < .1 || opacity > 1) throw new FormatException("التعتيم من 10 إلى 100 بالمئة.");
            if (stroke < 1 || stroke > 20) throw new FormatException("سُمك القلم من 1 إلى 20.");
            if (w < 1 || h < 1 || x < 0 || y < 0 || x + w > CurrentPage.DisplayWidth + .02 || y + h > CurrentPage.DisplayHeight + .02)
                throw new FormatException("يجب أن يبقى العنصر داخل الصفحة وبحجم أكبر من الصفر.");
            if (font < 6 || font > 200) throw new FormatException("حجم الخط من 6 إلى 200.");
            string text = TextInput.Text;
            if (item.HasText && string.IsNullOrWhiteSpace(text)) throw new FormatException("أدخل نصًا أو عبارة للختم.");
            if (text.Length > 4000) throw new FormatException("الحد الأقصى للنص في العنصر الواحد 4000 حرف.");
            string color = (ColorInput.SelectedItem as ComboBoxItem)?.Tag as string ?? item.Color;
            string family = FontInput.SelectedItem as string ?? item.FontFamily;
            var alignment = Enum.Parse<TextAlignment>((string)((ComboBoxItem)AlignmentInput.SelectedItem).Tag);
            bool bold = BoldInput.IsChecked == true, rtl = RtlInput.IsChecked == true;
            bool changed = Math.Abs(x-item.X) > .011 || Math.Abs(y-item.Y) > .011 || Math.Abs(w-item.Width) > .011 || Math.Abs(h-item.Height) > .011
                || Math.Abs(font-item.FontSize) > .011 || text != item.Text || color != item.Color || family != item.FontFamily || bold != item.Bold || rtl != item.RightToLeft || alignment != item.Alignment || Math.Abs(opacity-item.Opacity) > .0001 || Math.Abs(stroke-item.StrokeWidth) > .011;
            if (!changed) return true;
            BeginChange();
            item.X = x; item.Y = y; item.Width = w; item.Height = h; item.FontSize = font;
            item.Text = text; item.Color = color; item.FontFamily = family; item.Bold = bold; item.RightToLeft = rtl; item.Alignment = alignment; item.Opacity = opacity; item.StrokeWidth = stroke;
            UpdateView(item); RefreshList(); RefreshSelection();
            EditorStatus.Text = "تم تطبيق الخصائص. كبّر مربع النص إذا لم يظهر كاملًا.";
            return true;
        }
        catch (Exception ex) { Error(ex.Message); return false; }
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => CommitProperties();

    private async Task GoToPageAsync(int index)
    {
        if (session == null || busy || index < 0 || index >= session.Pages.Count || index == pageIndex || !CommitProperties()) return;
        CancelDrawing();
        int previous = pageIndex;
        SetBusy(true);
        try { pageIndex = index; await RenderPageAsync(); }
        catch (Exception ex)
        {
            Error("تعذر عرض الصفحة.\n" + ex.Message);
            pageIndex = previous;
            try { await RenderPageAsync(); }
            catch { forceClose = false; EditorStatus.Text = "تعذر العرض. يمكنك حفظ الإضافات الحالية أو إغلاق المحرر."; }
        }
        finally { SetBusy(false); }
    }
    private async void Previous_Click(object sender, RoutedEventArgs e) => await GoToPageAsync(pageIndex - 1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await GoToPageAsync(pageIndex + 1);
    private async void PageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        try
        {
            double number = Parse(PageInput.Text);
            if (number != Math.Truncate(number) || number < 1 || number > session!.Pages.Count) throw new FormatException("رقم الصفحة خارج النطاق.");
            await GoToPageAsync((int)number - 1);
        }
        catch (Exception ex) { Error(ex.Message); PageInput.Text = (pageIndex + 1).ToString(); }
    }

    private void SetZoom(double zoom)
    {
        zoom = Math.Clamp(zoom, .2, 3);
        PageScale.ScaleX = PageScale.ScaleY = zoom;
        ZoomLabel.Text = Math.Round(zoom * 100) + "%";
    }
    private void FitPage() => SetZoom(Math.Max(200, PageScroll.ActualWidth - 42) / CurrentPage.DisplayWidth);
    private void Fit_Click(object sender, RoutedEventArgs e) { if (session != null) FitPage(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(PageScale.ScaleX * 1.2);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(PageScale.ScaleX / 1.2);

    private async Task<bool> SaveAsync()
    {
        if (session == null || busy || !CommitProperties()) return false;
        CancelDrawing();
        var dialog = new SaveFileDialog
        {
            Title = "حفظ نسخة جديدة من المستند", Filter = "PDF (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(documentName) + "_edited_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".pdf",
            InitialDirectory = Path.GetDirectoryName(sourcePath), OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return false;
        if (string.Equals(Path.GetFullPath(dialog.FileName), sourcePath, StringComparison.OrdinalIgnoreCase) || File.Exists(dialog.FileName))
        { Error("اختر اسمًا جديدًا غير موجود؛ يحافظ هذا الإصدار على الملفات السابقة."); return false; }
        SetBusy(true);
        try
        {
            var rasters = new List<RasterOverlay>();
            var snapshot = items.Select(x => x.Copy()).ToList();
            for (int i = 0; i < snapshot.Count; i++)
            {
                EditorStatus.Text = $"تجهيز الإضافة {i+1} من {snapshot.Count}…";
                rasters.Add(new RasterOverlay(snapshot[i], OverlayVisual.Rasterize(snapshot[i])));
                await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
            }
            EditorStatus.Text = "جارٍ حفظ PDF والتحقق منه…";
            await Task.Run(() => PdfExport.Save(session.SourceBytes, dialog.FileName, rasters));
            SavedPath = dialog.FileName;
            EditorStatus.Text = "تم الحفظ: " + dialog.FileName;
            MessageBox.Show(this, "حُفظ ملف PDF للطباعة والمشاركة.\n\nللاحتفاظ بإمكانية تعديل كل إضافة لاحقًا، استخدم «حفظ مشروع» أيضًا.", "الحامي PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex) { Error("تعذر الحفظ. بقي الملف الأصلي دون تغيير.\n" + ex.Message); return false; }
        finally { SetBusy(false); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void Editor_Closing(object? sender, CancelEventArgs e)
    {
        if (forceClose) return;
        if (busy) { e.Cancel = true; return; }
        if (!CommitProperties()) { e.Cancel = true; return; }
        if (!Dirty) return;
        var choice = MessageBox.Show(this, "هل تريد حفظ الإضافات في مشروع قابل للاستكمال قبل الإغلاق؟\nنعم: حفظ مشروع. لا: إغلاق دون حفظ المشروع. إلغاء: متابعة العمل.\nأي ملف PDF صدّرته يبقى محفوظًا.", "الحامي PDF", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (choice == MessageBoxResult.No) return;
        e.Cancel = true;
        if (await SaveProjectAsync()) { forceClose = true; Close(); }
    }
    private void Editor_Closed(object? sender, EventArgs e) => session?.Dispose();

    private async void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (busy) return;
        if (e.Key == Key.Escape) { CancelDrawing(); e.Handled = true; return; }
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.S)
        {
            e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) await SaveAsync();
            else await SaveProjectAsync();
            return;
        }
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
        if (ctrl && e.Key == Key.Z) { e.Handled = true; Undo_Click(sender, e); }
        else if (ctrl && e.Key == Key.Y) { e.Handled = true; Redo_Click(sender, e); }
        else if (e.Key == Key.Delete) { e.Handled = true; Delete_Click(sender, e); }
    }
    private void Error(string message)
    {
        EditorStatus.Text = "تعذر إكمال العملية";
        MessageBox.Show(this, message, "الحامي PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
