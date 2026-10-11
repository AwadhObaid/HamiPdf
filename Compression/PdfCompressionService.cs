using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAPS2.Images;
using NAPS2.Images.Transforms;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.Scan;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace HamiPdf.Compression;

public enum CompressionMode
{
    Lossless,
    SmartImage
}

public enum SmartImagePreset
{
    HighQuality,
    Recommended,
    SmallFile
}

public sealed record CompressionOptions(
    CompressionMode Mode,
    SmartImagePreset Preset,
    bool Grayscale = false);

public sealed record CompressionResult(
    string InputPath,
    string OutputPath,
    long OriginalBytes,
    long OutputBytes,
    bool UsedCompressedOutput,
    int ProcessedImages,
    int ReplacedImages)
{
    public double SavingsPercent =>
        OriginalBytes <= 0 ? 0 : Math.Max(0, (OriginalBytes - OutputBytes) * 100d / OriginalBytes);

    public long SavingsBytes => Math.Max(0, OriginalBytes - OutputBytes);
}

public sealed class PdfCompressionService
{
    public Task<CompressionResult> CompressAsync(
        string inputPath,
        string outputPath,
        CompressionOptions options)
    {
        return Task.Run(() => CompressInternalAsync(inputPath, outputPath, options));
    }

    private async Task<CompressionResult> CompressInternalAsync(
        string inputPath,
        string outputPath,
        CompressionOptions options)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("PDF file not found.", inputPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        var originalBytes = new FileInfo(inputPath).Length;

        // Smart-image mode writes the NAPS2 export into memory first. This avoids
        // handing a filesystem path to NAPS2/PDFium and then trying to move/delete
        // that file while a native handle may still be alive. The final file is
        // created by HamiPdf only after the NAPS2 export context has been disposed.
        if (options.Mode == CompressionMode.SmartImage)
        {
            return await RebuildScannedPdfAsync(
                inputPath, outputPath, options, originalBytes);
        }

        var tempOutput = outputPath + ".tmp";
        if (File.Exists(tempOutput))
            File.Delete(tempOutput);

        try
        {
            return CompressLossless(inputPath, tempOutput, outputPath, originalBytes);
        }
        finally
        {
            if (File.Exists(tempOutput))
                File.Delete(tempOutput);
        }
    }

    private static CompressionResult CompressLossless(
        string inputPath,
        string tempOutput,
        string outputPath,
        long originalBytes)
    {
        using (var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify))
        {
            document.Save(tempOutput);
        }

        ValidatePdf(tempOutput);

        var candidateBytes = new FileInfo(tempOutput).Length;

        if (candidateBytes >= originalBytes)
        {
            File.Copy(inputPath, outputPath, true);
            return new CompressionResult(inputPath, outputPath, originalBytes, originalBytes, false, 0, 0);
        }

        File.Move(tempOutput, outputPath, true);
        return new CompressionResult(inputPath, outputPath, originalBytes, candidateBytes, true, 0, 0);
    }

    /// <summary>
    /// Smart image compression pipeline:
    /// PDFium/NAPS2 renders each source page, the page is resized and JPEG-encoded,
    /// then NAPS2's own ImageImporter + PdfExporter writes the final PDF.
    ///
    /// We deliberately do not use PDFsharp XImage/XGraphics here. NAPS2 owns the
    /// PDF image embedding path, which avoids the blank-page failure seen in v10-v12.
    /// </summary>
    private static async Task<CompressionResult> RebuildScannedPdfAsync(
        string inputPath,
        string outputPath,
        CompressionOptions options,
        long originalBytes)
    {
        var workRoot = Path.Combine(
            Path.GetTempPath(),
            $"HamiPdf_Compress_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workRoot);

        try
        {
            var exportPages = new List<ProcessedImage>();
            var processedPages = 0;
            var tempJpegs = new List<string>();
            using var exportedPdf = new MemoryStream();

            // The NAPS2 exporter receives a caller-owned MemoryStream. No PDF file is
            // opened by NAPS2, so Windows cannot keep a lock on the eventual output.
            try
            {
                using (var scanningContext = new ScanningContext(
                    new NAPS2.Images.Wpf.WpfImageContext())
                {
                    FileStorageManager = new FileStorageManager(workRoot)
                })
                {
                    var pdfImporter = new PdfImporter(scanningContext);
                    var imageImporter = new ImageImporter(scanningContext);

                    await foreach (var importedPage in pdfImporter.Import(inputPath))
                    {
                        using (importedPage)
                        using (var rendered = importedPage.Render())
                        {
                            processedPages++;
                            rendered.UpdateLogicalPixelFormat();

                            var target = GetTargetSize(
                                rendered.Width,
                                rendered.Height,
                                options.Preset);

                            using var resized = target.Width == rendered.Width &&
                                                target.Height == rendered.Height
                                ? rendered.Copy()
                                : rendered.ImageContext.PerformTransform(
                                    rendered,
                                    new ResizeTransform(target.Width, target.Height));

                            IMemoryImage exportImage = resized;
                            try
                            {
                                if (options.Grayscale)
                                {
                                    exportImage = resized.CopyWithPixelFormat(ImagePixelFormat.Gray8);
                                }

                                var pageSize = importedPage.Metadata.PageSize;
                                if (pageSize != null &&
                                    pageSize.WidthInInches > 0 &&
                                    pageSize.HeightInInches > 0)
                                {
                                    var dpiX = (float)(exportImage.Width / (double)pageSize.WidthInInches);
                                    var dpiY = (float)(exportImage.Height / (double)pageSize.HeightInInches);
                                    exportImage.SetResolution(dpiX, dpiY);
                                }

                                var jpegPath = Path.Combine(
                                    workRoot,
                                    $"page_{processedPages:D5}.jpg");

                                exportImage.Save(
                                    jpegPath,
                                    ImageFileFormat.Jpeg,
                                    new ImageSaveOptions
                                    {
                                        Quality = GetQuality(options.Preset),
                                        PixelFormatHint = ImagePixelFormat.RGB24
                                    });

                                var jpegInfo = new FileInfo(jpegPath);
                                if (!jpegInfo.Exists || jpegInfo.Length < 1024)
                                {
                                    throw new InvalidDataException(
                                        $"تعذر إنشاء صورة JPEG صالحة للصفحة {processedPages}.");
                                }

                                tempJpegs.Add(jpegPath);

                                ProcessedImage? importedJpeg = null;
                                await foreach (var image in imageImporter.Import(jpegPath))
                                {
                                    importedJpeg = image;
                                    break;
                                }

                                if (importedJpeg == null)
                                    throw new InvalidDataException(
                                        $"تعذر إعادة استيراد صورة الصفحة {processedPages}.");

                                exportPages.Add(importedJpeg);
                            }
                            finally
                            {
                                if (!ReferenceEquals(exportImage, resized))
                                    exportImage.Dispose();
                            }
                        }
                    }

                    if (processedPages == 0 || exportPages.Count != processedPages)
                    {
                        throw new InvalidDataException(
                            $"فشل تجهيز صفحات PDF: تمت معالجة {processedPages} صفحة، وتم تجهيز {exportPages.Count} صفحة فقط.");
                    }

                    var exporter = new PdfExporter(scanningContext);
                    var exported = await exporter.Export(exportedPdf, exportPages);
                    if (!exported || exportedPdf.Length <= 0)
                        throw new InvalidDataException("فشل محرك NAPS2 في إنشاء ملف PDF الناتج.");
                }
            }
            finally
            {
                foreach (var page in exportPages)
                    page.Dispose();

                foreach (var jpegPath in tempJpegs)
                {
                    try
                    {
                        if (File.Exists(jpegPath))
                            File.Delete(jpegPath);
                    }
                    catch
                    {
                        // Best effort; the working directory is removed at the end.
                    }
                }
            }

            // At this point the NAPS2 ScanningContext and all ProcessedImages have
            // been disposed. The generated PDF still lives only in memory.
            var candidateBytes = exportedPdf.Length;
            if (candidateBytes <= 0)
                throw new InvalidDataException("ملف PDF الناتج فارغ.");

            if (candidateBytes >= originalBytes)
            {
                File.Copy(inputPath, outputPath, true);
                return new CompressionResult(
                    inputPath,
                    outputPath,
                    originalBytes,
                    originalBytes,
                    false,
                    processedPages,
                    processedPages);
            }

            // Validate the generated PDF before committing it to the final path.
            exportedPdf.Position = 0;
            await ValidatePdfStreamWithNaps2Async(exportedPdf, processedPages);

            // The validator consumes the stream; reset it before writing.
            exportedPdf.Position = 0;

            // Smart-image mode has no .tmp PDF and no File.Move. HamiPdf creates the
            // final file itself only after NAPS2/PDFium has released all native handles.
            using (var destination = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                useAsync: true))
            {
                await exportedPdf.CopyToAsync(destination);
                await destination.FlushAsync();
            }

            return new CompressionResult(
                inputPath,
                outputPath,
                originalBytes,
                candidateBytes,
                true,
                processedPages,
                processedPages);

        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private static async Task ValidatePdfStreamWithNaps2Async(
        MemoryStream pdfStream,
        int expectedPages)
    {
        if (pdfStream.Length <= 0)
            throw new InvalidDataException("ملف PDF الناتج فارغ.");

        // PDFsharp validates the page count directly from the same in-memory bytes.
        pdfStream.Position = 0;
        using (var document = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import))
        {
            if (document.PageCount != expectedPages)
            {
                throw new InvalidDataException(
                    $"التحقق من PDF فشل: المتوقع {expectedPages} صفحة، والناتج {document.PageCount}.");
            }
        }

        // PDFium/NAPS2 validation uses a separate stream over the same bytes.
        // Copying to a temporary file is deliberately avoided: that was the source
        // of the Windows file-lock problem we are removing in this version.
        var validationRoot = Path.Combine(
            Path.GetTempPath(),
            $"HamiPdf_Validate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(validationRoot);
        var validationPdf = Path.Combine(validationRoot, "validation.pdf");
        try
        {
            pdfStream.Position = 0;
            await using (var validationFile = new FileStream(
                validationPdf,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                useAsync: true))
            {
                await pdfStream.CopyToAsync(validationFile);
                await validationFile.FlushAsync();
            }

            using (var validationContext = new ScanningContext(
                new NAPS2.Images.Wpf.WpfImageContext())
            {
                FileStorageManager = new FileStorageManager(validationRoot)
            })
            {
                var importer = new PdfImporter(validationContext);
                await using var enumerator = importer.Import(validationPdf).GetAsyncEnumerator();
                if (!await enumerator.MoveNextAsync())
                    throw new InvalidDataException("PDFium لم يستطع قراءة الصفحة الأولى من الملف الناتج.");

                using var firstPage = enumerator.Current;
                using var rendered = firstPage.Render();
                if (rendered.Width <= 0 || rendered.Height <= 0)
                    throw new InvalidDataException("PDFium أعاد صفحة أولى بدون أبعاد صالحة.");
            }
        }
        finally
        {
            TryDeleteDirectory(validationRoot);
        }
    }

    private static void ValidatePdf(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        if (document.PageCount <= 0)
            throw new InvalidDataException("The generated PDF contains no pages.");
    }

    private static (int Width, int Height) GetTargetSize(
        int width,
        int height,
        SmartImagePreset preset)
    {
        var maxDimension = preset switch
        {
            SmartImagePreset.HighQuality => 2100,
            SmartImagePreset.Recommended => 1800,
            SmartImagePreset.SmallFile => 1500,
            _ => 1800
        };

        var largest = Math.Max(width, height);
        if (largest <= maxDimension)
            return (width, height);

        var scale = maxDimension / (double)largest;
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static int GetQuality(SmartImagePreset preset) => preset switch
    {
        SmartImagePreset.HighQuality => 82,
        SmartImagePreset.Recommended => 68,
        SmartImagePreset.SmallFile => 48,
        _ => 68
    };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Best effort. Windows may briefly hold a NAPS2 native image handle.
        }
    }
}
