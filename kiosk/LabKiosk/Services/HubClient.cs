using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace LabKiosk.Services;

public sealed class HubClient : IAsyncDisposable
{
    private readonly HubConnection _conn;
    private readonly string _participantId;
    private readonly string _token;

    public event Func<int, int, Task>? Imminent;
    public event Func<int, Task>? Landed;
    public event Func<int, Task>? Reset;
    public event Action<string>? Message;
    public event Action<string, JsonElement, string>? Verdict;
    public event Action? Paused, Resumed, Eliminated;
    public event Func<Task>? Reconnected;

    public HubClient(string serverUrl, string participantId, string token = "")
    {
        _participantId = participantId;
        _token = token;
        var cleanUrl = KioskSettings.NormalizeServerUrl(serverUrl);
        _conn = new HubConnectionBuilder().WithUrl($"{cleanUrl.TrimEnd('/')}/hub").WithAutomaticReconnect().Build();

        _conn.On<int, int>("BREACH_IMMINENT", async (id, secs) => { if (Imminent is not null) await Imminent(id, secs); });
        _conn.On<int, int, string>("BREACH_IMMINENT", async (id, secs, _) => { if (Imminent is not null) await Imminent(id, secs); });
        _conn.On<BreachImminentPayload>("BREACH_IMMINENT", async p => { if (Imminent is not null) await Imminent(p.AttackId, p.ImminentSeconds); });

        _conn.On<int>("ATTACK_LANDED", async id => { if (Landed is not null) await Landed(id); });
        _conn.On<AttackLandedPayload>("ATTACK_LANDED", async p => { if (Landed is not null) await Landed(p.AttackId); });

        _conn.On<int>("ATTACK_RESET", async id => { if (Reset is not null) await Reset(id); });
        _conn.On<string>("BROADCAST", text => Message?.Invoke(text));

        _conn.On<string, JsonElement, string>("VERDICT", (sid, verdicts, note) => Verdict?.Invoke(sid, verdicts, note));
        _conn.On<int, string, string, double>("VERDICT", (id, status, reason, score) =>
        {
            var json = $"[{{\"attackId\":{id},\"status\":\"{status}\",\"reason\":\"{reason.Replace("\"", "\\\"")}\",\"points\":{score}}}]";
            using var doc = JsonDocument.Parse(json);
            Verdict?.Invoke(id.ToString(), doc.RootElement.Clone(), status);
        });

        _conn.On("PAUSED", () => Paused?.Invoke());
        _conn.On("RESUMED", () => Resumed?.Invoke());
        _conn.On<string>("ELIMINATED", _ => Eliminated?.Invoke());

        _conn.Reconnected += async _ =>
        {
            try { await _conn.InvokeAsync("RegisterKiosk", _participantId, _token); } catch { }
            if (Reconnected is not null) await Reconnected();
        };
    }

    public async Task StartAsync()
    {
        await _conn.StartAsync();
        try { await _conn.InvokeAsync("RegisterKiosk", _participantId, _token); } catch { }
    }

    public Task AckImminentAsync(int attackId) => _conn.InvokeAsync("AckImminent", attackId);
    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}

public sealed record BreachImminentPayload(int AttackId, int ImminentSeconds, string? LandAtUtcIso = null, string? Title = null, string? Codename = null);
public sealed record AttackLandedPayload(int AttackId, string? LandedAtUtcIso = null);
