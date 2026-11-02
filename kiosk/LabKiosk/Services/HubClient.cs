using Microsoft.AspNetCore.SignalR.Client;

namespace LabKiosk.Services;

public sealed class HubClient : IAsyncDisposable
{
    private readonly HubConnection _conn;
    private readonly string _participantId;
    private readonly string _token;

    public event Func<int, int, Task>? Imminent;
    public event Action<int>? Landed;
    public event Action<string>? Message;
    public event Action? Paused;
    public event Action<int, string, string, double>? Verdict;

    public HubClient(string serverUrl, string participantId, string token = "")
    {
        _participantId = participantId;
        _token = token;
        _conn = new HubConnectionBuilder().WithUrl($"{serverUrl.TrimEnd('/')}/hub").WithAutomaticReconnect().Build();
        _conn.On<int, int>("BREACH_IMMINENT", async (id, secs) => { if (Imminent is not null) await Imminent(id, secs); });
        _conn.On<int>("ATTACK_LANDED", id => Landed?.Invoke(id));
        _conn.On<string>("BROADCAST", text => Message?.Invoke(text));
        _conn.On("PAUSED", () => Paused?.Invoke());
        _conn.On<int, string, string, double>("VERDICT", (id, status, reason, score) => Verdict?.Invoke(id, status, reason, score));
        // Re-join the participant group after every reconnect so no attack is missed.
        _conn.Reconnected += async _ => await _conn.InvokeAsync("RegisterKiosk", _participantId, _token);
    }

    public async Task StartAsync()
    {
        await _conn.StartAsync();
        await _conn.InvokeAsync("RegisterKiosk", _participantId, _token);
    }

    public Task AckImminentAsync(int attackId) => _conn.InvokeAsync("AckImminent", attackId);

    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}
