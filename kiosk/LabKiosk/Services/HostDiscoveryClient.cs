using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace LabKiosk.Services;

/// <summary>
/// Discovers an Organizer ControlPlane host on the LAN via UDP port 5082
/// (paired with ControlPlane.Services.HostDiscoveryBroadcaster) or local loopback.
/// </summary>
public static class HostDiscoveryClient
{
    private const int DiscoveryPort = 5082;
    private static readonly HttpClient ProbeHttp = new() { Timeout = TimeSpan.FromMilliseconds(1200) };

    public static async Task<bool> IsHealthyAsync(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return false;
        try
        {
            using var res = await ProbeHttp.GetAsync(serverUrl.TrimEnd('/') + "/api/state");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static List<IPEndPoint> GetBroadcastEndpoints()
    {
        var endpoints = new List<IPEndPoint> { new(IPAddress.Broadcast, DiscoveryPort) };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var name = ni.Name.ToLowerInvariant();
                var desc = ni.Description.ToLowerInvariant();
                if (name.Contains("vethernet") || name.Contains("wsl") || desc.Contains("hyper-v") || desc.Contains("vmware") || desc.Contains("virtual"))
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask is null) continue;
                    var ipBytes = ua.Address.GetAddressBytes();
                    var maskBytes = ua.IPv4Mask.GetAddressBytes();
                    var bcast = new byte[4];
                    for (var i = 0; i < 4; i++) bcast[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                    endpoints.Add(new IPEndPoint(new IPAddress(bcast), DiscoveryPort));
                }
            }
        }
        catch { }
        return endpoints;
    }

    public static async Task<string?> DiscoverAsync(int timeoutMs = 1800, CancellationToken ct = default)
    {
        if (await IsHealthyAsync("http://127.0.0.1:5080"))
            return "http://127.0.0.1:5080";

        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
            var ping = Encoding.UTF8.GetBytes("ROGUE_AI_DISCOVERY_PING");
            foreach (var ep in GetBroadcastEndpoints())
            {
                try { await udp.SendAsync(ping, ping.Length, ep); } catch { }
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            while (!cts.IsCancellationRequested)
            {
                var res = await udp.ReceiveAsync(cts.Token);
                var msg = Encoding.UTF8.GetString(res.Buffer).Trim();
                if (msg.StartsWith("ROGUE_AI_HOST:", StringComparison.Ordinal) ||
                    msg.StartsWith("ROGUE_AI_BEACON:", StringComparison.Ordinal))
                {
                    var port = msg.Split(':', 2).ElementAtOrDefault(1)?.Trim();
                    if (int.TryParse(port, out var p))
                    {
                        var candidate = $"http://{res.RemoteEndPoint.Address}:{p}";
                        if (await IsHealthyAsync(candidate))
                            return candidate;
                    }
                }
            }
        }
        catch { /* timeout or UDP blocked */ }

        // Fallback: listen briefly on broadcast port 5082
        try
        {
            using var listener = new UdpClient();
            listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Math.Min(timeoutMs, 2200));

            while (!cts.IsCancellationRequested)
            {
                var res = await listener.ReceiveAsync(cts.Token);
                var msg = Encoding.UTF8.GetString(res.Buffer).Trim();
                if (msg.StartsWith("ROGUE_AI_BEACON:", StringComparison.Ordinal) ||
                    msg.StartsWith("ROGUE_AI_HOST:", StringComparison.Ordinal))
                {
                    var port = msg.Split(':', 2).ElementAtOrDefault(1)?.Trim();
                    if (int.TryParse(port, out var p))
                    {
                        var candidate = $"http://{res.RemoteEndPoint.Address}:{p}";
                        if (await IsHealthyAsync(candidate))
                            return candidate;
                    }
                }
            }
        }
        catch { /* port busy or timeout */ }

        return null;
    }
}
