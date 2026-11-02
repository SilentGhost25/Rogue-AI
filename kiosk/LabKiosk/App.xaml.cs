using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class App : Application
{
#if !KIOSK_ONLY
    private Process? _serverProc;
#endif

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if !KIOSK_ONLY
        var isHost = e.Args.Any(a => a.Equals("--host", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--admin", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("-h", StringComparison.OrdinalIgnoreCase));

        if (isHost)
        {
            TryStartControlPlaneServer();
            var adminWin = new AdminWindow("http://127.0.0.1:5080");
            adminWin.Show();
            return;
        }
#endif
        var mainWin = new MainWindow();
        mainWin.Show();
    }

#if !KIOSK_ONLY
    private void TryStartControlPlaneServer()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var res = client.GetAsync("http://127.0.0.1:5080/api/state").GetAwaiter().GetResult();
            if (res.IsSuccessStatusCode) return;
        }
        catch { }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var serverCandidates = new[]
            {
                Path.Combine(baseDir, "ControlPlane.exe"),
                Path.Combine(baseDir, "..", "..", "..", "..", "server", "ControlPlane", "bin", "Debug", "net9.0", "ControlPlane.exe"),
                Path.Combine(baseDir, "server", "ControlPlane.exe")
            };

            foreach (var cand in serverCandidates)
            {
                var full = Path.GetFullPath(cand);
                if (File.Exists(full))
                {
                    _serverProc = Process.Start(new ProcessStartInfo
                    {
                        FileName = full,
                        WorkingDirectory = Path.GetDirectoryName(full),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    Thread.Sleep(800);
                    break;
                }
            }
        }
        catch { }
    }
#endif

    protected override void OnExit(ExitEventArgs e)
    {
#if !KIOSK_ONLY
        try { _serverProc?.Kill(); } catch { }
#endif
        base.OnExit(e);
    }
}