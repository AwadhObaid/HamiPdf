using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Windows;
using HamiPdf.Services;

namespace HamiPdf;

public partial class App : Application
{
    // Set before InitializeComponent/any HWND is created, including --verify-ui.
    static App()
    {
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
    }
    private Mutex? instance;
    private bool ownsInstance;
    private readonly CancellationTokenSource stop = new();
    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--verify-ui")
        {
            int code = 0;
            try { UiVerification.WriteReport(e.Args[1]); }
            catch (Exception ex)
            {
                code = 1;
                try { File.WriteAllText(e.Args[1], System.Text.Json.JsonSerializer.Serialize(new { ok = false, error = ex.ToString() })); }
                catch { /* The exit code also signals report-write failure. */ }
            }
            Shutdown(code); return;
        }
        string identity = WindowsIdentity.GetCurrent().User!.Value + "-" + Process.GetCurrentProcess().SessionId;
        string pipeName = "HamiPdf-" + identity;
        instance = new Mutex(true, "Local\\" + pipeName, out ownsInstance);
        if (!ownsInstance)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(timeout.Token);
                await LaunchProtocol.WriteAsync(client, e.Args, timeout.Token);
                byte[] ack = new byte[1]; await client.ReadExactlyAsync(ack, timeout.Token);
                if (ack[0] != 1) throw new IOException("لم يتم قبول طلب الفتح.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("تعذر إرسال الملف إلى نافذة الحامي PDF المفتوحة. افتحه من زر فتح، أو أغلق البرنامج وأعد المحاولة.\n" + ex.Message, "الحامي PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Shutdown(); return;
        }
        var window = new MainWindow(); MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show(); window.EnqueueFiles(e.Args);
        _ = Task.Run(() => ListenAsync(pipeName, window));
    }
    private async Task ListenAsync(string pipeName, MainWindow window)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var paths = await LaunchProtocol.ReadAsync(server, timeout.Token);
                await Dispatcher.InvokeAsync(() => window.EnqueueFiles(paths));
                await server.WriteAsync(new byte[] { 1 }, timeout.Token);
                await server.FlushAsync(timeout.Token);
            }
            catch (OperationCanceledException) { if (stop.IsCancellationRequested) return; }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { /* A failed sender must not stop subsequent requests. */ }
            catch (Exception)
            {
                if (!stop.IsCancellationRequested)
                    await Dispatcher.InvokeAsync(() => MessageBox.Show("توقف استقبال الملفات من Windows. يمكنك فتحها من زر فتح داخل البرنامج.", "الحامي PDF", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        ShutdownTrace.Write("app.exit.begin code=" + e.ApplicationExitCode);
        stop.Cancel();
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
        ShutdownTrace.Write("app.exit.end");
    }
}
