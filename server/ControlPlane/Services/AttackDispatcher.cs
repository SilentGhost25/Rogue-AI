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
}
