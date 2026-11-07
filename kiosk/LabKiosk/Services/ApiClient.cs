using System.Net.Http;
using System.Net.Http.Json;

namespace LabKiosk.Services;

/// <summary>The kiosk talks only to the control plane. It holds no Git client and no credentials.</summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string serverUrl) => _http = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromSeconds(20) };

    public string? Token { get; private set; }

    public void SetToken(string token)
    {
        Token = token;
        _http.DefaultRequestHeaders.Remove("X-Kiosk-Token");
        _http.DefaultRequestHeaders.Add("X-Kiosk-Token", token);
    }

    public async Task<(string ParticipantId, string DeskId)> EnrollAsync(string token, string kioskId, string name)
    {
        SetToken(token);
        var res = await _http.PostAsJsonAsync("/api/kiosk/enroll", new { token, kioskId, name });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<EnrollResponse>();
        return (body!.ParticipantId, body.DeskId);
    }

    public async Task<string> RegisterAsync(string name, string kioskId, string? deskId = null, string? password = null, string? gitHubHandle = null, string? googleAccount = null)
    {
        var res = await _http.PostAsJsonAsync("/api/kiosk/register", new { name, kioskId, deskId, password, gitHubHandle, googleAccount });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<RegisterResponse>();
        if (!string.IsNullOrEmpty(body?.Token))
        {
            SetToken(body.Token);
        }
        return body!.ParticipantId;
    }

    public async Task UploadSnapshotAsync(string participantId, IEnumerable<SnapshotFileDto> files)
    {
        var res = await _http.PostAsJsonAsync($"/api/kiosk/{participantId}/snapshot", new { files });
        res.EnsureSuccessStatusCode();
    }

    public async Task SubmitAsync(string participantId)
    {
        var res = await _http.PostAsync($"/api/kiosk/{participantId}/submit", null);
        res.EnsureSuccessStatusCode();
    }

    public Task SendTelemetryAsync(string participantId, string type, object? payload = null)
        => _http.PostAsJsonAsync($"/api/kiosk/{participantId}/telemetry", new { type, payload });

    public async Task<AttackPayloadDto?> GetAttackPayloadAsync(string participantId, int attackId)
    {
        var res = await _http.GetAsync($"/api/kiosk/{participantId}/attack-payload/{attackId}");
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<AttackPayloadDto>();
    }

    public async Task<DateTime?> GetServerTimeAsync()
    {
        try
        {
            var res = await _http.GetAsync("/api/time");
            if (!res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadFromJsonAsync<ServerTimeResponse>();
            if (body != null && DateTime.TryParse(body.ServerTimeUtc, out var dt))
            {
                return dt.ToUniversalTime();
            }
        }
        catch
        {
            // Fall back to local clock on network error
        }
        return null;
    }

    public async Task<ActiveBreachDto?> GetActiveBreachAsync(string participantId)
    {
        try
        {
            var res = await _http.GetAsync($"/api/kiosk/{participantId}/active-breach");
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadFromJsonAsync<ActiveBreachDto>();
        }
        catch
        {
            return null;
        }
    }

    private sealed record ServerTimeResponse(string ServerTimeUtc);
    private sealed record RegisterResponse(string ParticipantId, string? Token = null);
    private sealed record EnrollResponse(string ParticipantId, string DeskId);
}

public sealed record SnapshotFileDto(string Path, string Sha256, string ContentBase64);
public sealed record AttackPayloadDto(int Attack_Id, List<SnapshotFileDto> Files);
public sealed record ActiveBreachDto(int? ActiveAttackId, string? Title, string State, int ImminentSeconds, string? LandAtUtc, bool Landed);

