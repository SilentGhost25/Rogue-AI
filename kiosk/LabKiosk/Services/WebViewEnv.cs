using System.IO;
using Microsoft.Web.WebView2.Core;

namespace LabKiosk.Services;

/// <summary>One shared WebView2 environment (and profile folder) for the site pane and both terminals.</summary>
public static class WebViewEnv
{
    private static Task<CoreWebView2Environment>? _env;

    public static Task<CoreWebView2Environment> GetAsync()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "webview");
        return _env ??= CoreWebView2Environment.CreateAsync(null, folder);
    }
}
