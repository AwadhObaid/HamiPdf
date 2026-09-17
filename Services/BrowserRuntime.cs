using Microsoft.Web.WebView2.Core;

namespace HamiPdf.Services;

internal static class BrowserRuntime
{
    // Use identical options for every view sharing the same user-data folder.
    // This compatibility release avoids the graphics driver path implicated by
    // the reported native atidxx64.dll access violation. No system setting changes.
    public static CoreWebView2EnvironmentOptions CreateOptions() => new()
    {
        Language = "ar",
        AdditionalBrowserArguments = "--disable-gpu"
    };
}
