using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace LabKiosk.Services;

/// <summary>One shared WebView2 environment (and profile folder) for the site pane and both terminals.</summary>
public static class WebViewEnv
{
    private static CoreWebView2Environment? _resolvedEnv;
    private static Task<CoreWebView2Environment>? _initTask;
    private static readonly object _lock = new();

    public static async Task<CoreWebView2Environment> GetAsync()
    {
        if (_resolvedEnv != null) return _resolvedEnv;

        Task<CoreWebView2Environment> task;
        lock (_lock)
        {
            if (_resolvedEnv != null) return _resolvedEnv;
            if (_initTask == null)
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "webview");
                try { Directory.CreateDirectory(folder); } catch { }

                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                {
                    _initTask = disp.InvokeAsync(async () => await CoreWebView2Environment.CreateAsync(null, folder)).Task.Unwrap();
                }
                else
                {
                    _initTask = CoreWebView2Environment.CreateAsync(null, folder);
                }
            }
            task = _initTask;
        }

        var env = await task;
        _resolvedEnv = env;
        return env;
    }
}
