using System.IO;
using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            LogCrash(ev.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (_, ev) =>
        {
            LogCrash(ev.Exception);
            MessageBox.Show($"Rogue AI LabKiosk encountered a fatal error:\n\n{ev.Exception.Message}\n\nStack Trace:\n{ev.Exception.StackTrace}\n\nCheck logs at: %LOCALAPPDATA%\\BreachKiosk\\logs\\crash.log",
                "Rogue AI - Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
            Shutdown(1);
        };

        var args = e.Args;
        var isHost = args.Any(a => a.Equals("--host", StringComparison.OrdinalIgnoreCase) ||
                                   a.Equals("--admin", StringComparison.OrdinalIgnoreCase) ||
                                   a.Equals("-h", StringComparison.OrdinalIgnoreCase));

        var isProjector = args.Any(a => a.Equals("--projector", StringComparison.OrdinalIgnoreCase) ||
                                       a.Equals("-p", StringComparison.OrdinalIgnoreCase));

        var s = KioskSettings.Load();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                s.ServerUrl = args[i + 1];
            }
        }

        if (isHost)
        {
            // Organizer mode: start the event services (control plane + engine) and open the Command Center.
            var supervisor = new HostSupervisor();
            var admin = new AdminWindow(supervisor.ServerUrl, supervisor.AdminKey, "/", fullscreen: false, supervisor);
            MainWindow = admin;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            admin.Show();
            return;
        }

        if (isProjector)
        {
            var admin = new AdminWindow(s.ServerUrl, "", "/projector.html", fullscreen: true);
            MainWindow = admin;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            admin.Show();
            return;
        }

        // Explicitly control application shutdown so closing dialogs during onboarding does not exit the app
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!s.Onboarded || string.IsNullOrWhiteSpace(s.DisplayName) || s.DisplayName == Environment.MachineName)
        {
            var onboard = new OnboardingWindow(s);
            if (onboard.ShowDialog() != true)
            {
                Shutdown(0);
                return;
            }
        }

        s.ServerUrl = KioskSettings.NormalizeServerUrl(s.ServerUrl);
        s.WorkspacePath = WorkspaceBackup.ResolveWorkspace(s.WorkspacePath);

        try
        {
            var main = new MainWindow(s);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show($"Failed to launch main kiosk workspace:\n\n{ex.Message}\n\nStack:\n{ex.StackTrace}",
                "Rogue AI - Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        var entry = $"[{DateTime.UtcNow:O}] FATAL CRASH:\n{ex}\n\n";
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.log"), entry);
        }
        catch { }

        try
        {
            var baseDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(baseDir);
            File.AppendAllText(Path.Combine(baseDir, "crash.log"), entry);
        }
        catch { }

        try
        {
            File.AppendAllText(@"C:\BreachWorkspace\crash.log", entry);
        }
        catch { }
    }
}
