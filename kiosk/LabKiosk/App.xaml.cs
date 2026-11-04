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

        var args = e.Args;
        var config = KioskConfig.FromArgs(args);

#if !KIOSK_ONLY
        var isHost = args.Any(a => a.Equals("--host", StringComparison.OrdinalIgnoreCase) ||
                                   a.Equals("--admin", StringComparison.OrdinalIgnoreCase) ||
                                   a.Equals("-h", StringComparison.OrdinalIgnoreCase));

        if (isHost)
        {
            TryStartControlPlaneServer();
            var target = $"{config.ServerUrl.TrimEnd('/')}/admin/";
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch
            {
                var adminWin = new AdminWindow(config.ServerUrl);
                adminWin.Show();
                return;
            }
            Shutdown();
            return;
        }
#endif
        var mainWin = new MainWindow(config);
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