using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ControlPlane.Services;

/// <summary>
/// Background service that provides zero-configuration LAN auto-discovery.
/// Broadcasts UDP beacon on port 5082 and responds to discovery queries from participant kiosks,
/// allowing kiosks on any network or IP to find the host automatically without manual typing.
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
        var broadcastEp = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
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

        // Periodic broadcast loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await client.SendAsync(beaconBytes, beaconBytes.Length, broadcastEp);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("UDP broadcast tick exception: {Message}", ex.Message);
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

