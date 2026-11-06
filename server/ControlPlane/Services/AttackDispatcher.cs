using ControlPlane.Data;
using ControlPlane.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ControlPlane.Services;

/// <summary>Implements the attack landing algorithm (plan section 5). Firing an attack twice does nothing.</summary>
public sealed class AttackDispatcher(
    Database db, IHubContext<BreachHub> hub, EngineClient engine, IConfiguration config, ILogger<AttackDispatcher> log)
{
    /// <param name="target">A participant id for a single-kiosk test shot; null fires on everyone (and is idempotent).</param>
    public async Task<bool> FireAsync(int attackId, string? target = null, CancellationToken ct = default)
    {
        if (target is null)
        {
            // Atomic claim: only the first caller moves scheduled -> fired.
            if (db.Exec("UPDATE attacks SET state = 'fired' WHERE id = $i AND state <> 'fired'", ("$i", attackId)) == 0) return false;
            db.Log("ATTACK_FIRED", attackId: attackId);
        }
        else db.Log("ATTACK_FIRED", target, attackId, "{\"targeted\":true}");

        var seconds = config.GetValue("Breach:ImminentSeconds", 10);
        if (target is null) await hub.Clients.All.SendAsync(HubEvents.BreachImminent, attackId, seconds, ct);
        else await hub.Clients.Group($"p:{target}").SendAsync(HubEvents.BreachImminent, attackId, seconds, ct);
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);

        var participants = target is null ? db.Query("SELECT id FROM participants", r => r.GetString(0)) : new List<string> { target };
        await Parallel.ForEachAsync(participants, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (pid, token) => await LandAsync(pid, attackId, token));
        return true;
    }

    /// <summary>Applies one attack to one participant. Used by firing, late registration and admin re-apply.</summary>
    public async Task LandAsync(string participantId, int attackId, CancellationToken ct)
    {
        try
        {
            if (!await engine.ApplyAsync(participantId, attackId, ct)) throw new InvalidOperationException("Engine refused the attack.");
            db.Exec("INSERT OR IGNORE INTO attack_deliveries (attack_id, participant_id, landed_at) VALUES ($a, $p, $t)",
                ("$a", attackId), ("$p", participantId), ("$t", Database.Now()));
            db.Log("ATTACK_LANDED", participantId, attackId);
            await hub.Clients.Group($"p:{participantId}").SendAsync(HubEvents.AttackLanded, attackId, ct);
        }
        catch (Exception ex)
        {
            // A kiosk that misses a landing is re-synced from /api/kiosk/{id}/status (attack ids are idempotent).
            log.LogWarning(ex, "Attack {Attack} failed to land for {Participant}", attackId, participantId);
        }
    }

    /// <summary>Resets an attack by restoring clean files, deleting delivery state, and notifying kiosks.</summary>
    public async Task<bool> ResetAsync(int attackId, string? target, WorkspaceStore store, CancellationToken ct = default)
    {
        var touches = await engine.GetTouchesAsync(attackId, ct) ?? new List<string>();
        if (touches.Count == 0)
        {
            touches = attackId switch
            {
                1 => new() { "lib/html.js", "public/style.css" },
                2 => new() { "routes/posts.js", "routes/search.js" },
                3 => new() { "routes/search.js" },
                4 => new() { "routes/api.js" },
                5 => new() { "app.js" },
                6 => new() { "routes/api.js" },
                7 => new() { "package.json", "server.js" },
                8 => new() { "lib/auth.js" },
                9 => new() { "routes/comments.js" },
                10 => new() { "lib/web.js" },
                _ => new()
            };
        }

        var participants = target is null
            ? db.Query("SELECT id FROM participants", r => r.GetString(0))
            : new List<string> { target };

        foreach (var pid in participants)
        {
            store.RestoreFiles(pid, touches);
            db.Exec("DELETE FROM attack_deliveries WHERE attack_id = $a AND participant_id = $p", ("$a", attackId), ("$p", pid));
            db.Log("ATTACK_RESET", pid, attackId, System.Text.Json.JsonSerializer.Serialize(new { targeted = target is not null }));
            await hub.Clients.Group($"p:{pid}").SendAsync(HubEvents.AttackReset, attackId, ct);
        }

        if (target is null)
        {
            db.Exec("UPDATE attacks SET state = 'scheduled' WHERE id = $i", ("$i", attackId));
            await hub.Clients.All.SendAsync(HubEvents.AttackReset, attackId, ct);
        }

        return true;
    }

    /// <summary>Resets ALL attacks across all participants, restoring all original template files and resetting attack states.</summary>
    public async Task<int> ResetAllAsync(string? target, WorkspaceStore store, CancellationToken ct = default)
    {
        var allAttacks = db.Query("SELECT id FROM attacks ORDER BY id", r => r.GetInt32(0));
        var count = 0;
        foreach (var id in allAttacks)
        {
            if (await ResetAsync(id, target, store, ct)) count++;
        }
        return count;
    }

    /// <summary>Randomly picks an un-fired/un-delivered attack and fires it.</summary>
    public async Task<int?> FireRandomAsync(string? target, CancellationToken ct = default)
    {
        var allAttacks = db.Query("SELECT id FROM attacks ORDER BY id", r => r.GetInt32(0));
        if (allAttacks.Count == 0) return null;

        List<int> candidates;
        if (target is not null)
        {
            var activeOrSolved = db.Query("SELECT attack_id FROM attack_deliveries WHERE participant_id = $p", r => r.GetInt32(0), ("$p", target));
            candidates = allAttacks.Except(activeOrSolved).ToList();
        }
        else
        {
            candidates = db.Query("SELECT id FROM attacks WHERE state <> 'fired' ORDER BY id", r => r.GetInt32(0));
        }

        if (candidates.Count == 0) candidates = allAttacks;

        var chosen = candidates[Random.Shared.Next(candidates.Count)];
        await FireAsync(chosen, target, ct);
        return chosen;
    }
}
