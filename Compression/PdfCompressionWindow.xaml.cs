using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FormsDragEventArgs = System.Windows.DragEventArgs;

namespace HamiPdf.Compression;

public partial class PdfCompressionWindow : Window
{
    private readonly ObservableCollection<CompressionItem> _items = new();
    private readonly PdfCompressionService _service = new();

    public PdfCompressionWindow(Window? owner = null)
    {
        InitializeComponent();
        if (owner != null)
            Owner = owner;

        FilesList.ItemsSource = _items;
        Mode_Changed(null, null);
    }

    public PdfCompressionWindow(string[] files)
        : this((Window?)null)
    {
        if (files != null && files.Length > 0)
            AddFiles(files);
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            AddFiles(dialog.FileNames);
    }

    private void AddFiles(string[] files)
    {
        foreach (var file in files.Where(File.Exists))
        {
            if (_items.Any(x => string.Equals(x.InputPath, file, StringComparison.OrdinalIgnoreCase)))
                continue;

            var info = new FileInfo(file);
            _items.Add(new CompressionItem
            {
                InputPath = file,
                FileName = info.Name,
                OriginalSize = FormatBytes(info.Length),
                Status = "جاهز"
            });
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _items.Clear();

    private void Files_DragOver(object sender, FormsDragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Files_Drop(object sender, FormsDragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            AddFiles(files.Where(x => string.Equals(Path.GetExtension(x), ".pdf", StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        e.Handled = true;
    }

    private void Mode_Changed(object? sender, SelectionChangedEventArgs? e)
    {
        var smart = ModeComboBox?.SelectedIndex == 1;

        if (PresetComboBox != null)
            PresetComboBox.IsEnabled = smart;

        if (GrayscaleCheckBox != null)
            GrayscaleCheckBox.IsEnabled = smart;
    }

    private async void Compress_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
        {
            MessageBox.Show("أضف ملف PDF واحدًا على الأقل.", "ضغط PDF",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mode = ModeComboBox.SelectedIndex == 1
            ? CompressionMode.SmartImage
            : CompressionMode.Lossless;

        var preset = PresetComboBox.SelectedIndex switch
        {
            0 => SmartImagePreset.HighQuality,
            2 => SmartImagePreset.SmallFile,
            _ => SmartImagePreset.Recommended
        };

        var options = new CompressionOptions(mode, preset, GrayscaleCheckBox.IsChecked == true);

        CompressButton.IsEnabled = false;
        AddFilesButton.IsEnabled = false;

        try
        {
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                item.Status = "جاري الضغط...";
                FilesList.Items.Refresh();

                var directory = Path.GetDirectoryName(item.InputPath)!;
                var name = Path.GetFileNameWithoutExtension(item.InputPath);
                var output = Path.Combine(directory, $"{name}_compressed.pdf");

                var result = await _service.CompressAsync(item.InputPath, output, options);

                item.OutputPath = result.OutputPath;
                item.NewSize = FormatBytes(result.OutputBytes);
                item.Status = result.UsedCompressedOutput
                    ? $"تم - وفر {result.SavingsPercent:F2}% ({result.ReplacedImages}/{result.ProcessedImages})"
                    : $"لم ينتج حجم أصغر ({result.ReplacedImages}/{result.ProcessedImages})";

                Progress.Value = (i + 1) * 100d / _items.Count;
                FilesList.Items.Refresh();
            }

            MessageBox.Show("اكتملت عملية ضغط ملفات PDF.", "ضغط PDF",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر ضغط الملف:\n{ex.Message}", "ضغط PDF",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CompressButton.IsEnabled = true;
            AddFilesButton.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes:N0} B";

        var units = new[] { "KB", "MB", "GB" };
        var value = bytes / 1024d;

        foreach (var unit in units)
        {
            if (value < 1024)
                return $"{value:N2} {unit}";
            value /= 1024;
        }

        return $"{value:N2} TB";
    }

    private sealed class CompressionItem
    {
        public string InputPath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string OriginalSize { get; set; } = "";
        public string NewSize { get; set; } = "-";
        public string Status { get; set; } = "";
    }
}
