using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ControlPlane.Services;

/// <summary>
/// Background service that provides zero-configuration LAN auto-discovery.
/// Broadcasts UDP beacon on port 5082 across all active IPv4 subnets and responds to discovery queries.
/// </summary>
public sealed class HostDiscoveryBroadcaster : BackgroundService
{
    private const int DiscoveryPort = 5082;
    private const int ControlPlanePort = 5080;
    private readonly ILogger<HostDiscoveryBroadcaster> _logger;

    public HostDiscoveryBroadcaster(ILogger<HostDiscoveryBroadcaster> logger)
    {
        _logger = logger;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HostDiscoveryBroadcaster starting on UDP port {Port}", DiscoveryPort);

        UdpClient? udpServer = null;
        try
        {
            udpServer = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort))
            {
                EnableBroadcast = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not bind UDP discovery port {Port}: {Message}. Proceeding with broadcast only.", DiscoveryPort, ex.Message);
        }

        using var client = udpServer ?? new UdpClient { EnableBroadcast = true };
        var beaconBytes = Encoding.UTF8.GetBytes($"ROGUE_AI_BEACON:{ControlPlanePort}");

        // Listener loop for incoming discovery pings
        var receiveTask = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await client.ReceiveAsync(stoppingToken);
                    var msg = Encoding.UTF8.GetString(result.Buffer).Trim();
                    if (msg.StartsWith("ROGUE_AI_DISCOVERY_PING"))
                    {
                        var replyBytes = Encoding.UTF8.GetBytes($"ROGUE_AI_HOST:{ControlPlanePort}");
                        await client.SendAsync(replyBytes, replyBytes.Length, result.RemoteEndPoint);
                        _logger.LogInformation("Replied to discovery ping from {Remote}", result.RemoteEndPoint);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("UDP discovery receive error: {Message}", ex.Message);
                        await Task.Delay(500, stoppingToken);
                    }
                }
            }
        }, stoppingToken);

        // Periodic broadcast loop across all physical subnets
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var ep in GetBroadcastEndpoints())
            {
                try
                {
                    await client.SendAsync(beaconBytes, beaconBytes.Length, ep);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("UDP broadcast tick exception: {Message}", ex.Message);
                }
            }

            try
            {
                await Task.Delay(2000, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        try { await receiveTask; } catch { }
        _logger.LogInformation("HostDiscoveryBroadcaster stopped.");
    }
}
