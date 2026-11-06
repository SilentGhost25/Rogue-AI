using System.IO;
using Microsoft.Web.WebView2.Core;

namespace LabKiosk.Services;

public static class WebView2Helper
{
    private static CoreWebView2Environment? _sharedEnvironment;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public static async Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
    {
        if (_sharedEnvironment != null) return _sharedEnvironment;
        await _lock.WaitAsync();
        try
        {
            if (_sharedEnvironment == null)
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var udata = Path.Combine(localAppData, "RogueAI", "WV2_Profile");
                Directory.CreateDirectory(udata);
                _sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, udata);
            }
            return _sharedEnvironment;
        }
        finally
        {
            _lock.Release();
        }
    }
}

