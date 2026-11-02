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

    public async Task<string> RegisterAsync(string name, string kioskId)
    {
        var res = await _http.PostAsJsonAsync("/api/kiosk/register", new { name, kioskId });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<RegisterResponse>();
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

    private sealed record RegisterResponse(string ParticipantId);
    private sealed record EnrollResponse(string ParticipantId, string DeskId);
}

public sealed record SnapshotFileDto(string Path, string Sha256, string ContentBase64);
public sealed record AttackPayloadDto(int Attack_Id, List<SnapshotFileDto> Files);
