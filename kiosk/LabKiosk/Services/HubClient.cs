using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace LabKiosk.Services;

public sealed class HubClient : IAsyncDisposable
{
    private readonly HubConnection _conn;
    private readonly string _participantId;

    public event Func<int, int, Task>? Imminent;
    public event Func<int, Task>? Landed;
    public event Func<int, Task>? Reset;
    public event Action<string>? Message;
    public event Action<string, JsonElement, string>? Verdict;
    public event Action? Paused, Resumed, Eliminated;
    public event Func<Task>? Reconnected;

    public HubClient(string serverUrl, string participantId)
    {
        _participantId = participantId;
        _conn = new HubConnectionBuilder().WithUrl($"{serverUrl.TrimEnd('/')}/hub").WithAutomaticReconnect().Build();
        _conn.On<int, int>("BREACH_IMMINENT", async (id, secs) => { if (Imminent is not null) await Imminent(id, secs); });
        _conn.On<int>("ATTACK_LANDED", async id => { if (Landed is not null) await Landed(id); });
        _conn.On<int>("ATTACK_RESET", async id => { if (Reset is not null) await Reset(id); });
        _conn.On<string>("BROADCAST", text => Message?.Invoke(text));
        _conn.On<string, JsonElement, string>("VERDICT", (sid, verdicts, note) => Verdict?.Invoke(sid, verdicts, note));
        _conn.On("PAUSED", () => Paused?.Invoke());
        _conn.On("RESUMED", () => Resumed?.Invoke());
        _conn.On<string>("ELIMINATED", _ => Eliminated?.Invoke());
        // After a reconnect: rejoin the group, then let the window re-sync anything it missed.
        _conn.Reconnected += async _ =>
        {
            await _conn.InvokeAsync("RegisterKiosk", _participantId);
            if (Reconnected is not null) await Reconnected();
        };
    }

    public async Task StartAsync()
    {
        await _conn.StartAsync();
        await _conn.InvokeAsync("RegisterKiosk", _participantId);
    }

    public Task AckImminentAsync(int attackId) => _conn.InvokeAsync("AckImminent", attackId, _participantId);
    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}
