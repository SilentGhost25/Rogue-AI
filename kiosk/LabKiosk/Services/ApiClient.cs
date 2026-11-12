using System.IO;
using System.Net.Http;
using System.Net.Http.Json;

namespace LabKiosk.Services;

/// <summary>The kiosk talks only to the control plane. It holds no Git client and no credentials.</summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string serverUrl)
    {
        var clean = KioskSettings.NormalizeServerUrl(serverUrl);
        if (!Uri.TryCreate(clean, UriKind.Absolute, out var uri))
        {
            uri = new Uri("http://127.0.0.1:5080/");
        }
        _http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(30) };
    }

    public string? Token { get; private set; }

    public void SetToken(string token)
    {
        Token = token;
        _http.DefaultRequestHeaders.Remove("X-Kiosk-Token");
        _http.DefaultRequestHeaders.Add("X-Kiosk-Token", token);
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

    public async Task<KioskStatus?> GetStatusAsync(string participantId)
    {
        try
        {
            return await _http.GetFromJsonAsync<KioskStatus>($"/api/kiosk/{participantId}/status");
        }
        catch
        {
            return null;
        }
    }

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

    public async Task UploadSnapshotAsync(string participantId, IEnumerable<SnapshotFileDto> files, IEnumerable<string>? deleted = null)
    {
        var res = await _http.PostAsJsonAsync($"/api/kiosk/{participantId}/snapshot", new { files, deleted = deleted ?? Enumerable.Empty<string>() });
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
        catch (HttpRequestException) { /* telemetry is best effort */ }
    }

    private sealed record RegisterResponse(string ParticipantId, string? Token = null);
    private sealed record PayloadResponse(int AttackId, List<SnapshotFileDto> Files);
    private sealed record FetchResponse(List<SnapshotFileDto> Files);
}

public sealed record SnapshotFileDto(string Path, string Sha256, string ContentBase64);
public sealed record RemoteFile(string Path, string Sha256);
public sealed record KioskStatus(string Status, List<int> ActiveAttacks, bool Paused);
