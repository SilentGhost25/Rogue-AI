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
    private static readonly HttpClient FastProbeHttp = new() { Timeout = TimeSpan.FromMilliseconds(400) };

    public static async Task<bool> IsHealthyAsync(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return false;
        try
        {
            var clean = serverUrl.TrimEnd('/');
            using var res = await ProbeHttp.GetAsync(clean + "/api/discovery");
            if (res.IsSuccessStatusCode) return true;
        }
        catch { }

        try
        {
            var clean = serverUrl.TrimEnd('/');
            using var res = await ProbeHttp.GetAsync(clean + "/api/state");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static string GetActiveNetworkSummary()
    {
        try
        {
            var parts = new List<string>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var name = ni.Name.ToLowerInvariant();
                var desc = ni.Description.ToLowerInvariant();
                if (name.Contains("vethernet") || name.Contains("wsl") || desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("vmware"))
                    continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                    {
                        var typeLabel = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "LAN";
                        parts.Add($"{typeLabel} ({ua.Address})");
                    }
                }
            }
            if (parts.Count > 0) return string.Join(" · ", parts);
        }
        catch { }
        return "Network: Local / Offline";
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

        // Fallback: fast parallel HTTP subnet sweep (bypasses Wi-Fi AP client isolation & UDP drops)
        try
        {
            var subnetHit = await ProbeSubnetFastAsync(ct);
            if (!string.IsNullOrEmpty(subnetHit))
                return subnetHit;
        }
        catch { }

        return null;
    }

    private static async Task<string?> ProbeSubnetFastAsync(CancellationToken ct)
    {
        var activeSubnets = new List<(string Prefix, IPAddress? Gateway)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var name = ni.Name.ToLowerInvariant();
                var desc = ni.Description.ToLowerInvariant();
                if (name.Contains("vethernet") || name.Contains("wsl") || desc.Contains("virtual") || desc.Contains("hyper-v"))
                    continue;

                var ipProps = ni.GetIPProperties();
                var gw = ipProps.GatewayAddresses.FirstOrDefault(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(g.Address))?.Address;

                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask != null)
                    {
                        var ipBytes = ua.Address.GetAddressBytes();
                        var maskBytes = ua.IPv4Mask.GetAddressBytes();
                        if (maskBytes[0] == 255 && maskBytes[1] == 255 && maskBytes[2] == 255)
                        {
                            var prefix = $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}";
                            activeSubnets.Add((prefix, gw));
                        }
                    }
                }
            }
        }
        catch { }

        if (activeSubnets.Count == 0) return null;

        using var fastCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fastCts.CancelAfter(1500);

        foreach (var (prefix, gw) in activeSubnets)
        {
            if (gw != null && await IsHealthyAsync($"http://{gw}:5080"))
                return $"http://{gw}:5080";

            var candidates = Enumerable.Range(1, 254)
                .Select(i => $"http://{prefix}.{i}:5080")
                .ToList();

            var throttler = new SemaphoreSlim(48);
            var foundTcs = new TaskCompletionSource<string?>();

            var tasks = candidates.Select(async url =>
            {
                await throttler.WaitAsync(fastCts.Token);
                try
                {
                    if (foundTcs.Task.IsCompleted) return;
                    using var req = new HttpRequestMessage(HttpMethod.Get, url + "/api/discovery");
                    using var res = await FastProbeHttp.SendAsync(req, fastCts.Token);
                    if (res.IsSuccessStatusCode)
                    {
                        foundTcs.TrySetResult(url);
                    }
                }
                catch { }
                finally
                {
                    throttler.Release();
                }
            }).ToList();

            await Task.WhenAny(foundTcs.Task, Task.WhenAll(tasks));
            if (foundTcs.Task.IsCompleted)
            {
                var winner = await foundTcs.Task;
                if (!string.IsNullOrEmpty(winner)) return winner;
            }
        }

        return null;
    }
}
