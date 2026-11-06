using System.Text.Json;

namespace ControlPlane.Services;

/// <summary>Typed calls to the Python engine (contract: contracts/openapi.yaml).</summary>
public sealed class EngineClient(IHttpClientFactory http)
{
    private HttpClient Client => http.CreateClient("engine");

    public async Task<bool> InitRepoAsync(string participantId, CancellationToken ct = default)
    {
        try { return (await Client.PostAsJsonAsync("/repos/init", new { participant_id = participantId }, ct)).IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
    }

    public async Task<List<string>?> GetTouchesAsync(int attackId, CancellationToken ct = default)
    {
        try
        {
            var res = await Client.GetAsync($"/attacks/{attackId}/payload", ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.GetProperty("touches").EnumerateArray().Select(e => e.GetString()!).ToList();
        }
        catch (HttpRequestException) { return null; }
    }

    /// <summary>Server-owned Git: commit the participant's snapshot (optionally tag a submission; the engine mirrors if configured).</summary>
    public async Task CommitAsync(string participantId, string message, string? tag = null, CancellationToken ct = default)
    {
        try { await Client.PostAsJsonAsync("/repos/commit", new { participant_id = participantId, message, tag }, ct); }
        catch (HttpRequestException) { /* git history is best effort; scoring never depends on it */ }
    }

    public async Task<bool> ApplyAsync(string participantId, int attackId, CancellationToken ct = default)
    {
        var res = await Client.PostAsJsonAsync("/attacks/apply", new { participant_id = participantId, attack_id = attackId }, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<JsonDocument?> EvaluateAsync(string participantId, string submissionId,
        IEnumerable<(int AttackId, double ElapsedSeconds)> active, int promptsTotal, CancellationToken ct = default)
    {
        var body = new
        {
            participant_id = participantId, submission_id = submissionId, prompts_total = promptsTotal,
            active = active.Select(a => new { attack_id = a.AttackId, elapsed_seconds = a.ElapsedSeconds }).ToArray(),
        };
        var res = await Client.PostAsJsonAsync("/evaluate", body, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }
}
