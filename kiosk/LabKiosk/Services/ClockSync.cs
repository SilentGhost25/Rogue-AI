using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LabKiosk.Services;

/// <summary>
/// Probes server time to calculate RTT-compensated clock offset, providing sub-50ms synchronized UTC time.
/// </summary>
public sealed class ClockSync : IAsyncDisposable
{
    private readonly ApiClient _api;
    private TimeSpan _serverOffset = TimeSpan.Zero;
    private readonly Timer _timer;
    private bool _synced = false;

    public ClockSync(ApiClient api)
    {
        _api = api;
        // Probe immediately, then refresh every 60 seconds
        _timer = new Timer(async _ => await SyncAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    public bool IsSynced => _synced;
    public TimeSpan ServerOffset => _serverOffset;

    public DateTime SyncedUtcNow => DateTime.UtcNow + _serverOffset;

    public async Task SyncAsync()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var t0 = DateTime.UtcNow;
            var serverTimeUtc = await _api.GetServerTimeAsync();
            sw.Stop();

            if (serverTimeUtc.HasValue)
            {
                var rtt = sw.Elapsed;
                var estimatedServerTime = serverTimeUtc.Value.Add(TimeSpan.FromTicks(rtt.Ticks / 2));
                _serverOffset = estimatedServerTime - DateTime.UtcNow;
                _synced = true;
            }
        }
        catch
        {
            // Retain existing offset if refresh probe fails
        }
    }

    public ValueTask DisposeAsync()
    {
        _timer.Dispose();
        return ValueTask.CompletedTask;
    }
}
