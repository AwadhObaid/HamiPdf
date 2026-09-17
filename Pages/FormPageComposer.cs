using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HamiPdf.Pages;

internal static class FormPageComposer
{
    // Called on the WPF UI thread. The control belongs to the page manager.
    public static async Task<byte[]> ComposeAsync(WebView2 browser,FormPageExport.Prepared plan)
    {
        const string url="https://hami-pages.invalid/pages.html";
        var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HamiPdf","WebView2");
        var env=await CoreWebView2Environment.CreateAsync(null,profile,HamiPdf.Services.BrowserRuntime.CreateOptions());
        await browser.EnsureCoreWebView2Async(env);
        var core=browser.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("hami-pages.invalid",Path.Combine(AppContext.BaseDirectory,"Forms","Web"),CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreDevToolsEnabled=false;
        core.Settings.AreDefaultContextMenusEnabled=false;
        void Navigation(object? s,CoreWebView2NavigationStartingEventArgs e) { if(e.Uri!=url) e.Cancel=true; }
        void NewWindow(object? s,CoreWebView2NewWindowRequestedEventArgs e) => e.Handled=true;
        void Permission(object? s,CoreWebView2PermissionRequestedEventArgs e) => e.State=CoreWebView2PermissionState.Deny;
        void Message(object? s,CoreWebView2WebMessageReceivedEventArgs e)
        {
            if(e.Source!=url) return;
            try {
                using var json=JsonDocument.Parse(e.WebMessageAsJson); var root=json.RootElement;
                if(root.TryGetProperty("ready",out _)) { ready.TrySetResult(true); return; }
                if(root.GetProperty("ok").GetBoolean()) result.TrySetResult(Convert.FromBase64String(root.GetProperty("data").GetString()!));
                else result.TrySetException(new InvalidDataException(root.GetProperty("error").GetString()));
            } catch(Exception ex) { result.TrySetException(ex); }
        }
        void Failed(object? s,CoreWebView2ProcessFailedEventArgs e) {
            var ex=new IOException("توقف محرك إدارة النماذج. أعد فتح إدارة الصفحات وحاول مجددًا.");
            ready.TrySetException(ex); result.TrySetException(ex);
        }
        core.NavigationStarting+=Navigation; core.NewWindowRequested+=NewWindow;
        core.PermissionRequested+=Permission; core.WebMessageReceived+=Message; core.ProcessFailed+=Failed;
        try {
            core.Navigate(url);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(45));
            await browser.ExecuteScriptAsync("window.hamiPages.run("+JsonSerializer.Serialize(plan.Sources)+")");
            return await result.Task.WaitAsync(TimeSpan.FromMinutes(3));
        }
        finally {
            core.NavigationStarting-=Navigation; core.NewWindowRequested-=NewWindow;
            core.PermissionRequested-=Permission; core.WebMessageReceived-=Message; core.ProcessFailed-=Failed;
            // Stop timed-out extraction too; the next save uses a fresh control.
            browser.Dispose();
        }
    }
}
