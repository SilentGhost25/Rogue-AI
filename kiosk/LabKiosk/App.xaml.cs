using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                args.Handled = true;
                System.Diagnostics.Debug.WriteLine($"[LabKiosk Unhandled Exception] {args.Exception}");
                MessageBox.Show($"Application Notice: {args.Exception.Message}", "LabKiosk Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
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

        if (!s.Onboarded || string.IsNullOrWhiteSpace(s.DisplayName) || s.DisplayName == Environment.MachineName)
        {
            var onboard = new OnboardingWindow(s);
            if (onboard.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        s.WorkspacePath = WorkspaceBackup.ResolveWorkspace(s.WorkspacePath);
        WorkspaceBackup.EnsurePristineWorkspace(s.WorkspacePath, s.SiteTemplatePath);

        var main = new MainWindow(s);
        MainWindow = main;
        main.Show();
    }
}
