using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace LabKiosk.Services;

public sealed class HubClient : IAsyncDisposable
{
    private readonly HubConnection _conn;
    private readonly string _participantId;
    private readonly string _token;

    public bool IsConnected => _conn.State == HubConnectionState.Connected;

    public event Func<int, int, Task>? Imminent;
    public event Func<int, Task>? Landed;
    public event Func<int, Task>? Reset;
    public event Action<string>? Message;
    public event Action<int, string, string, double>? VerdictDetailed;
    public event Action<int, string, string, double, Dictionary<string, bool>?>? VerdictTiersDetailed;
    public event Action? Paused, Resumed, Eliminated, PardonGranted, ProAccountAllowed, ResetBaseline, FocusPardoned;
    public event Action<string>? AccountSwitchApproved, AccountSwitchDenied, Disqualified;
    public event Action<string, bool, string?>? AccountSwitchResult;
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

        _conn.On<int, string, string, double>("VERDICT", (id, status, reason, score) =>
        {
            VerdictDetailed?.Invoke(id, status, reason, score);
        });
        _conn.On<int, string, string, double, Dictionary<string, bool>?>("VERDICT_DETAILED", (id, status, reason, score, tiers) =>
        {
            VerdictDetailed?.Invoke(id, status, reason, score);
            VerdictTiersDetailed?.Invoke(id, status, reason, score, tiers);
        });
        _conn.On<int, string, string, double>("VERDICT_DETAILED", (id, status, reason, score) =>
        {
            VerdictDetailed?.Invoke(id, status, reason, score);
            VerdictTiersDetailed?.Invoke(id, status, reason, score, null);
        });

        _conn.On("PAUSED", () => Paused?.Invoke());
        _conn.On("RESUMED", () => Resumed?.Invoke());
        _conn.On<string>("ELIMINATED", reason => { Eliminated?.Invoke(); Disqualified?.Invoke(reason); });

        _conn.On<string, string?>("ACCOUNT_SWITCH_APPROVED", (_, reqId) => AccountSwitchApproved?.Invoke(reqId ?? ""));
        _conn.On<string>("ACCOUNT_SWITCH_APPROVED", _ => AccountSwitchApproved?.Invoke(""));
        _conn.On<string, string>("ACCOUNT_SWITCH_DENIED", (_, reason) => AccountSwitchDenied?.Invoke(reason));
        _conn.On<string, bool, string?>("ACCOUNT_SWITCH_RESULT", (reqId, approved, reason) => AccountSwitchResult?.Invoke(reqId, approved, reason));

        _conn.On<string>("PARDON_GRANTED", _ => PardonGranted?.Invoke());
        _conn.On("PARDON_GRANTED", () => PardonGranted?.Invoke());

        _conn.On<string>("FOCUS_PARDONED", _ => FocusPardoned?.Invoke());
        _conn.On("FOCUS_PARDONED", () => FocusPardoned?.Invoke());

        _conn.On<string>("PRO_ACCOUNT_ALLOWED", _ => ProAccountAllowed?.Invoke());
        _conn.On("PRO_ACCOUNT_ALLOWED", () => ProAccountAllowed?.Invoke());

        _conn.On<string, string>("DISQUALIFIED", (_, reason) => { Eliminated?.Invoke(); Disqualified?.Invoke(reason); });
        _conn.On<string>("DISQUALIFIED", reason => { Eliminated?.Invoke(); Disqualified?.Invoke(reason); });
        _conn.On("RESET_BASELINE", () => ResetBaseline?.Invoke());

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

    public async Task<string> RequestAccountSwitchAsync(string currentEmail, string reason = "")
    {
        try
        {
            return await _conn.InvokeAsync<string>("RequestAccountSwitch", currentEmail, reason);
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task ReportProAccountAsync(string currentEmail, string tierDetails)
    {
        try
        {
            await _conn.InvokeAsync("ReportProAccount", currentEmail, tierDetails);
        }
        catch { }
    }

    public async Task ReportFocusViolationAsync(string reason, string? participantId = null)
    {
        try
        {
            if (_conn.State == HubConnectionState.Connected)
            {
                await _conn.InvokeAsync("ReportFocusViolation", reason, participantId ?? _participantId);
            }
        }
        catch { }
    }

    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}

public sealed record BreachImminentPayload(int AttackId, int ImminentSeconds, string? LandAtUtcIso = null, string? Title = null, string? Codename = null);
public sealed record AttackLandedPayload(int AttackId, string? LandedAtUtcIso = null);
