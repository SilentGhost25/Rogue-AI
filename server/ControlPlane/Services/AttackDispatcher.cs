using ControlPlane.Data;
using ControlPlane.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ControlPlane.Services;

/// <summary>Implements the attack landing algorithm (plan section 5). Idempotent per attack id.</summary>
public sealed class AttackDispatcher(
    Database db, IHubContext<BreachHub> hub, IHttpClientFactory http, IConfiguration config, ILogger<AttackDispatcher> log)
{
    public async Task FireAsync(int attackId, string? targetParticipantId = null, CancellationToken ct = default)
    {
        db.Log("ATTACK_FIRED", participantId: targetParticipantId, attackId: attackId);
        var seconds = config.GetValue("Breach:ImminentSeconds", 5);
        if (string.IsNullOrEmpty(targetParticipantId))
        {
            await hub.Clients.All.SendAsync(HubEvents.BreachImminent, attackId, seconds, ct);
        }
        else
        {
            await hub.Clients.Group($"p:{targetParticipantId}").SendAsync(HubEvents.BreachImminent, attackId, seconds, ct);
        }
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);

        var participants = new List<string>();
        if (!string.IsNullOrEmpty(targetParticipantId))
        {
            participants.Add(targetParticipantId);
        }
        else
        {
            using (var c = db.Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM participants WHERE status != 'disqualified'";
                using var r = cmd.ExecuteReader();
                while (r.Read()) participants.Add(r.GetString(0));
            }
        }

        var client = http.CreateClient("engine");
        await Parallel.ForEachAsync(participants, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (pid, token) =>
        {
            try
            {
                var res = await client.PostAsJsonAsync("/attacks/apply", new { participant_id = pid, attack_id = attackId }, token);
                res.EnsureSuccessStatusCode();

                using (var conn = db.Open())
                using (var ins = conn.CreateCommand())
                {
                    ins.CommandText = "INSERT OR REPLACE INTO attack_deliveries (attack_id, participant_id, landed_at) VALUES ($a, $p, $t)";
                    ins.Parameters.AddWithValue("$a", attackId);
                    ins.Parameters.AddWithValue("$p", pid);
                    ins.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                    ins.ExecuteNonQuery();
                }

                db.Log("ATTACK_LANDED", pid, attackId);
                await hub.Clients.Group($"p:{pid}").SendAsync(HubEvents.AttackLanded, attackId, token);
                await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, pid, token);
            }
            catch (Exception ex)
            {
                // A kiosk that misses the landing is re-synced on reconnect (attack ids are idempotent).
                log.LogWarning(ex, "Attack {Attack} failed to land for {Participant}", attackId, pid);
            }
        });
    }

    /// <summary>Resets an attack by restoring clean files, deleting delivery state, and notifying kiosks.</summary>
    public async Task<bool> ResetAsync(int attackId, string? target, WorkspaceStore store, CancellationToken ct = default)
    {
        var touches = attackId switch
        {
            0 => new List<string> { "lib/html.js" },
            1 => new List<string> { "routes/traffic.js" },
            2 => new List<string> { "routes/traffic.js" },
            3 => new List<string> { "routes/traffic.js" },
            4 => new List<string> { "routes/traffic.js", "routes/power.js" },
            5 => new List<string> { "routes/vault.js", "routes/overview.js" },
            6 => new List<string> { "routes/report.js" },
            7 => new List<string> { "server.js" },
            8 => new List<string> { "lib/auth.js" },
            9 => new List<string> { "routes/auth.js", "public/app.js" },
            10 => new List<string> { "routes/security.js" },
            11 => new List<string> { "lib/auth.js", "routes/overview.js" },
            _ => new List<string>()
        };

        var participants = new List<string>();
        if (!string.IsNullOrEmpty(target))
        {
            participants.Add(target);
        }
        else
        {
            using var c = db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT id FROM participants";
            using var r = cmd.ExecuteReader();
            while (r.Read()) participants.Add(r.GetString(0));
        }

        foreach (var pid in participants)
        {
            store.RestoreFiles(pid, touches);
            using (var c = db.Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM attack_deliveries WHERE attack_id = $a AND participant_id = $p";
                cmd.Parameters.AddWithValue("$a", attackId);
                cmd.Parameters.AddWithValue("$p", pid);
                cmd.ExecuteNonQuery();
            }
            db.Log("ATTACK_RESET", pid, attackId);
            await hub.Clients.Group($"p:{pid}").SendAsync(HubEvents.AttackReset, attackId, ct);
        }

        if (string.IsNullOrEmpty(target))
        {
            using (var c = db.Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "UPDATE attacks SET state = 'scheduled' WHERE id = $i";
                cmd.Parameters.AddWithValue("$i", attackId);
                cmd.ExecuteNonQuery();
            }
            await hub.Clients.All.SendAsync(HubEvents.AttackReset, attackId, ct);
        }

        return true;
    }

    /// <summary>Resets ALL attacks across all participants, restoring all original template files and resetting attack states.</summary>
    public async Task<int> ResetAllAsync(string? target, WorkspaceStore store, CancellationToken ct = default)
    {
        var allAttacks = new List<int>();
        using (var c = db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM attacks ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) allAttacks.Add(r.GetInt32(0));
        }

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
        var allAttacks = new List<int>();
        using (var c = db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM attacks ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) allAttacks.Add(r.GetInt32(0));
        }
        if (allAttacks.Count == 0) return null;

        List<int> candidates;
        if (!string.IsNullOrEmpty(target))
        {
            var activeOrSolved = new HashSet<int>();
            using (var c = db.Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT attack_id FROM attack_deliveries WHERE participant_id = $p";
                cmd.Parameters.AddWithValue("$p", target);
                using var r = cmd.ExecuteReader();
                while (r.Read()) activeOrSolved.Add(r.GetInt32(0));
            }
            candidates = allAttacks.Where(a => !activeOrSolved.Contains(a)).ToList();
        }
        else
        {
            candidates = new List<int>();
            using (var c = db.Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM attacks WHERE state <> 'fired' ORDER BY id";
                using var r = cmd.ExecuteReader();
                while (r.Read()) candidates.Add(r.GetInt32(0));
            }
        }

        if (candidates.Count == 0) candidates = allAttacks;

        var chosen = candidates[Random.Shared.Next(candidates.Count)];
        await FireAsync(chosen, target, ct);
        return chosen;
    }
}
