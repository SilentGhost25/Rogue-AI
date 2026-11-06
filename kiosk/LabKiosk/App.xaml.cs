using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--host"))
        {
            // Organizer mode: start the event services (control plane + engine) and open the Command Center.
            var supervisor = new HostSupervisor();
            var admin = new AdminWindow(supervisor.ServerUrl, supervisor.AdminKey, "/", fullscreen: false, supervisor);
            MainWindow = admin;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            admin.Show();
        }
        else
        {
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
    }
}
