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
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var udata = Path.Combine(localAppData, "RogueAI", "WV2_Admin_" + Environment.ProcessId);
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, udata);
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
            await webView.EnsureCoreWebView2Async();
            webView.Source = new Uri($"{_serverUrl}/projector.html");
        };
        projWin.Show();
    }

    private void OnOpenParticipantView(object sender, RoutedEventArgs e)
    {
        var mainWin = new MainWindow();
        mainWin.Show();
    }
}
