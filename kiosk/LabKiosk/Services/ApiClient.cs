using System.Net.Http;
using System.Net.Http.Json;

namespace LabKiosk.Services;

/// <summary>The kiosk talks only to the control plane. It holds no Git client and no credentials.</summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string serverUrl) => _http = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromSeconds(30) };

    public async Task<string> RegisterAsync(string name, string kioskId)
    {
        var res = await _http.PostAsJsonAsync("/api/kiosk/register", new { name, kioskId });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<RegisterResponse>())!.ParticipantId;
    }

    public async Task<KioskStatus?> GetStatusAsync(string participantId)
        => await _http.GetFromJsonAsync<KioskStatus>($"/api/kiosk/{participantId}/status");

    public async Task<List<RemoteFile>> ListFilesAsync(string participantId)
        => await _http.GetFromJsonAsync<List<RemoteFile>>($"/api/kiosk/{participantId}/files") ?? new();

    public async Task<List<SnapshotFileDto>> FetchFilesAsync(string participantId, IEnumerable<string> paths)
    {
        var res = await _http.PostAsJsonAsync($"/api/kiosk/{participantId}/files/fetch", new { paths });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FetchResponse>())!.Files;
    }

    /// <summary>Plan section 5: the files an attack changed, from this participant's own server copy.</summary>
    public async Task<List<SnapshotFileDto>> GetAttackPayloadAsync(string participantId, int attackId)
    {
        var body = await _http.GetFromJsonAsync<PayloadResponse>($"/api/kiosk/{participantId}/attack-payload/{attackId}");
        return body?.Files ?? new();
    }

    public async Task UploadSnapshotAsync(string participantId, IEnumerable<SnapshotFileDto> files, IEnumerable<string> deleted)
    {
        var res = await _http.PostAsJsonAsync($"/api/kiosk/{participantId}/snapshot", new { files, deleted });
        res.EnsureSuccessStatusCode();
    }

    public async Task SubmitAsync(string participantId)
    {
        var res = await _http.PostAsync($"/api/kiosk/{participantId}/submit", null);
        res.EnsureSuccessStatusCode();
    }

    public async Task SendTelemetryAsync(string participantId, string type, object? payload = null)
    {
        try { await _http.PostAsJsonAsync($"/api/kiosk/{participantId}/telemetry", new { type, payload }); }
        catch (HttpRequestException) { /* telemetry is best effort; the server also sees prompts through other signals */ }
    }

    private sealed record RegisterResponse(string ParticipantId);
    private sealed record PayloadResponse(int AttackId, List<SnapshotFileDto> Files);
    private sealed record FetchResponse(List<SnapshotFileDto> Files);
}

public sealed record SnapshotFileDto(string Path, string Sha256, string ContentBase64);
public sealed record RemoteFile(string Path, string Sha256);
public sealed record KioskStatus(string Status, List<int> ActiveAttacks, bool Paused);
