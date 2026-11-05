using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

public partial class AdminWindow : Window
{
    private readonly string _serverUrl;

    public AdminWindow(string? serverUrl = null)
    {
        InitializeComponent();
        var settings = KioskSettings.Load();
        _serverUrl = serverUrl ?? settings.ServerUrl;
        ServerStatusText.Text = $"Control Plane: {_serverUrl}";

        Loaded += async (_, _) =>
        {
            try
            {
                var env = await WebView2Helper.GetSharedEnvironmentAsync();
                await AdminWeb.EnsureCoreWebView2Async(env);
                AdminWeb.Source = new Uri($"{_serverUrl}/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load Admin Dashboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    private void OnReload(object sender, RoutedEventArgs e) => AdminWeb.Reload();

    private void OnLaunchProjector(object sender, RoutedEventArgs e)
    {
        var projWin = new Window
        {
            Title = "ROGUE AI - Stadium Projector Display",
            WindowState = WindowState.Maximized,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(7, 13, 18))
        };
        var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
        projWin.Content = webView;
        projWin.Loaded += async (_, _) =>
        {
            var env = await WebView2Helper.GetSharedEnvironmentAsync();
            await webView.EnsureCoreWebView2Async(env);
            webView.Source = new Uri($"{_serverUrl}/projector.html");
        };
        projWin.Show();
    }

    private void OnOpenParticipantView(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                return;
            }
        }
        catch { }
        var mainWin = new MainWindow();
        mainWin.Show();
    }
}
