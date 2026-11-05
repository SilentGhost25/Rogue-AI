using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace LabKiosk.Services;

/// <summary>
/// Provides zero-configuration network discovery for the Rogue AI participant kiosk.
/// Automatically detects the Host Organizer Control Plane on any Wi-Fi, Ethernet,
/// or hotspot network using UDP broadcast beaconing and fast subnet probing.
/// </summary>
public static class NetworkDiscovery
{
    private const int DiscoveryPort = 5082;
    private const int DefaultControlPlanePort = 5080;

    public static async Task<bool> ProbeHostAsync(string url, int timeoutMs = 1000)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            var normalized = url.TrimEnd('/');
            var resp = await http.GetAsync($"{normalized}/api/state", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string?> AutoDiscoverHostAsync(string? fallbackUrl = null, Action<string>? onStatus = null)
    {
        // 1. Probe the saved/current URL first
        if (!string.IsNullOrWhiteSpace(fallbackUrl))
        {
            onStatus?.Invoke($"Checking saved server ({fallbackUrl})...");
            if (await ProbeHostAsync(fallbackUrl, 800))
            {
                return fallbackUrl;
            }
        }

        // 2. Probe loopback (single machine test)
        if (await ProbeHostAsync("http://127.0.0.1:5080", 500))
        {
            return "http://127.0.0.1:5080";
        }

        onStatus?.Invoke("Auto-discovering Breach Command on local network...");

        // 3. UDP Broadcast Discovery
        try
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            var pingBytes = Encoding.UTF8.GetBytes("ROGUE_AI_DISCOVERY_PING");
            var broadcastEp = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            await udp.SendAsync(pingBytes, pingBytes.Length, broadcastEp);

            // Listen for beacon/reply with 2.5s timeout
            using var cts = new CancellationTokenSource(2500);
            while (!cts.IsCancellationRequested)
            {
                var receiveTask = udp.ReceiveAsync(cts.Token).AsTask();
                var completed = await Task.WhenAny(receiveTask, Task.Delay(2500, cts.Token));
                if (completed == receiveTask && receiveTask.IsCompletedSuccessfully)
                {
                    var result = receiveTask.Result;
                    var reply = Encoding.UTF8.GetString(result.Buffer);
                    if (reply.Contains("ROGUE_AI_HOST") || reply.Contains("ROGUE_AI_BEACON"))
                    {
                        var hostIp = result.RemoteEndPoint.Address.ToString();
                        var port = DefaultControlPlanePort;
                        var parts = reply.Split(':');
                        if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var parsedPort))
                        {
                            port = parsedPort;
                        }
                        var candidate = $"http://{hostIp}:{port}";
                        if (await ProbeHostAsync(candidate, 800))
                        {
                            onStatus?.Invoke($"Found Breach Command at {candidate}!");
                            return candidate;
                        }
                    }
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UDP discovery error: {ex.Message}");
        }

        // 4. Fast Subnet Parallel Probe (covers networks where router blocks UDP broadcast)
        onStatus?.Invoke("Scanning local network subnet for host...");
        var localIps = GetLocalIpv4Addresses();
        foreach (var localIp in localIps)
        {
            var found = await ScanSubnetForControlPlaneAsync(localIp, DefaultControlPlanePort);
            if (!string.IsNullOrEmpty(found))
            {
                onStatus?.Invoke($"Found Breach Command at {found}!");
                return found;
            }
        }

        return null;
    }

    private static List<IPAddress> GetLocalIpv4Addresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var desc = ni.Description.ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("pseudo")) continue;

                var ipProps = ni.GetIPProperties();
                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                    {
                        list.Add(ua.Address);
                    }
                }
            }
        }
        catch { }
        return list;
    }

    private static async Task<string?> ScanSubnetForControlPlaneAsync(IPAddress localIp, int port)
    {
        var bytes = localIp.GetAddressBytes();
        var prefix = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.";
        var localLastByte = bytes[3];

        // Priority candidate list: Gateway (.1, .254), adjacent IPs around participant (+-10), and .2 to .20
        var candidates = new HashSet<int> { 1, 254 };
        for (var i = Math.Max(2, localLastByte - 10); i <= Math.Min(253, localLastByte + 10); i++)
        {
            if (i != localLastByte) candidates.Add(i);
        }
        for (var i = 2; i <= 20; i++)
        {
            if (i != localLastByte) candidates.Add(i);
        }

        using var cts = new CancellationTokenSource(2500);
        var tasks = candidates.Select(async octet =>
        {
            var targetIp = $"{prefix}{octet}";
            var url = $"http://{targetIp}:{port}";
            if (await ProbeHostAsync(url, 400))
            {
                return url;
            }
            return null;
        }).ToList();

        while (tasks.Count > 0 && !cts.IsCancellationRequested)
        {
            var finished = await Task.WhenAny(tasks);
            tasks.Remove(finished);
            var result = await finished;
            if (!string.IsNullOrEmpty(result))
            {
                cts.Cancel();
                return result;
            }
        }

        return null;
    }
}
