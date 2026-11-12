using System.Text.Json;
using System.Windows;
using LabKiosk.Services;

namespace LabKiosk;

/// <summary>The Organizer Command Center: the control plane's web console in a window, with the admin key injected.</summary>
public partial class AdminWindow : Window
{
    private readonly string _serverUrl, _adminKey, _path;
    private readonly bool _fullscreen;
    private readonly HostSupervisor? _supervisor;

    public AdminWindow(string serverUrl, string adminKey, string path = "/admin", bool fullscreen = false, HostSupervisor? supervisor = null)
    {
        InitializeComponent();
        _serverUrl = serverUrl.TrimEnd('/'); _adminKey = adminKey; _path = path; _fullscreen = fullscreen; _supervisor = supervisor;
        if (fullscreen) { WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; Title = "Breach Protocol: Projector"; }
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => _supervisor?.Dispose();
    }

    private async Task StartAsync()
    {
        try
        {
            if (_supervisor is not null) await _supervisor.StartAsync(t => Dispatcher.InvokeAsync(() => StatusText.Text = t));
            await Web.EnsureCoreWebView2Async(await WebViewEnv.GetAsync());
            await Web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.__ADMIN_KEY = {JsonSerializer.Serialize(_adminKey)};");
            Web.Source = new Uri(_serverUrl + _path);
            var lanIp = GetLocalIp();
            StatusText.Text = _fullscreen ? "" : $"Connected to {_serverUrl} | LAN IP for Kiosks: {lanIp}";
        }
        catch (Exception ex) { StatusText.Text = $"Could not start: {ex.Message}"; }
    }

    private static string GetLocalIp()
    {
        try
        {
            var candidates = new List<string>();
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

            // First pass: Active physical Ethernet or Wi-Fi adapters with an active default gateway
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;

                var name = ni.Name.ToLowerInvariant();
                var desc = ni.Description.ToLowerInvariant();
                if (name.Contains("vethernet") || name.Contains("vgate") || name.Contains("virtual") ||
                    name.Contains("wsl") || name.Contains("vmware") || name.Contains("bluetooth") ||
                    desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("vmware") ||
                    desc.Contains("wsl") || desc.Contains("bluetooth"))
                    continue;

                var ipProps = ni.GetIPProperties();
                var hasGateway = ipProps.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(g.Address) &&
                    g.Address.ToString() != "0.0.0.0");

                if (!hasGateway) continue;

                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !System.Net.IPAddress.IsLoopback(addr.Address))
                    {
                        candidates.Add($"{addr.Address} ({ni.Name})");
                    }
                }
            }

            if (candidates.Count > 0)
                return string.Join(" | ", candidates);

            // Fallback pass: any non-virtual Up adapter
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                var name = ni.Name.ToLowerInvariant();
                var desc = ni.Description.ToLowerInvariant();
                if (name.Contains("vethernet") || name.Contains("vgate") || name.Contains("virtual") ||
                    desc.Contains("virtual") || desc.Contains("hyper-v"))
                    continue;

                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !System.Net.IPAddress.IsLoopback(addr.Address))
                    {
                        return $"{addr.Address} ({ni.Name})";
                    }
                }
            }
        }
        catch { }

        // Final fallback: DNS host entry
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                    return ip.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private void OnCopyKey(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_adminKey);
        StatusText.Text = "Admin key copied. Kiosks open this console with Ctrl+Shift+A and this key.";
    }

    private void OnOpenProjector(object sender, RoutedEventArgs e)
        => new AdminWindow(_serverUrl, _adminKey, "/projector.html", fullscreen: true).Show();

    private void OnOpenKiosk(object sender, RoutedEventArgs e)
    {
        var s = KioskSettings.Load();
        s.ServerUrl = _serverUrl;
        new MainWindow(s).Show();
    }
}
