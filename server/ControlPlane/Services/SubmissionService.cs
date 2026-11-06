using System.Text.Json;
using ControlPlane.Data;
using ControlPlane.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ControlPlane.Services;

/// <summary>Submit Fix: evaluate the server-side workspace, write verdicts and ledger entries, tell the kiosk.</summary>
public sealed class SubmissionService(Database db, EngineClient engine, IHubContext<BreachHub> hub, ILogger<SubmissionService> log)
{
    private static readonly string[] Components = { "base", "time_bonus", "prompt_penalty" };

    public string Create(string participantId)
    {
        var id = "s" + Guid.NewGuid().ToString("N")[..10];
        db.Exec("INSERT INTO submissions (id, participant_id, created_at, status) VALUES ($i,$p,$t,'queued')",
            ("$i", id), ("$p", participantId), ("$t", Database.Now()));
        db.Log("SUBMISSION", participantId, payloadJson: JsonSerializer.Serialize(new { submissionId = id }));
        return id;
    }

    public async Task EvaluateAsync(string participantId, string submissionId, CancellationToken ct = default)
    {
        var group = hub.Clients.Group($"p:{participantId}");
        await engine.CommitAsync(participantId, $"submission {submissionId}", $"submission-{submissionId}", ct);
        try
        {
            var now = DateTime.UtcNow;
            var active = db.Query(
                "SELECT attack_id, landed_at FROM attack_deliveries WHERE participant_id = $p AND landed_at IS NOT NULL AND solved_at IS NULL ORDER BY attack_id",
                r => (AttackId: r.GetInt32(0), Landed: DateTime.Parse(r.GetString(1), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal)),
                ("$p", participantId));
            if (active.Count == 0)
            {
                db.Exec("UPDATE submissions SET status = 'done' WHERE id = $i", ("$i", submissionId));
                await group.SendAsync(HubEvents.Verdict, submissionId, new object[0], "There are no active attacks to fix right now.", ct);
                return;
            }

            // Prompts since the previous completed submission are attributed to the attacks fixed in this one.
            var since = db.Scalar<string>("SELECT MAX(created_at) FROM submissions WHERE participant_id = $p AND status = 'done' AND id <> $i",
                ("$p", participantId), ("$i", submissionId)) ?? "";
            var prompts = db.Scalar<int>("SELECT COUNT(*) FROM event_log WHERE participant_id = $p AND type = 'PROMPT' AND ts > $s",
                ("$p", participantId), ("$s", since));

            using var doc = await engine.EvaluateAsync(participantId, submissionId,
                active.Select(a => (a.AttackId, Math.Max(0, (now - a.Landed).TotalSeconds))), prompts, ct);
            if (doc is null) throw new InvalidOperationException("Engine returned an error.");

            var evaluator = doc.RootElement.TryGetProperty("evaluator", out var ev) ? ev.GetString() ?? "" : "";
            var summaries = new List<object>();
            var solvedAny = false;
            foreach (var v in doc.RootElement.GetProperty("verdicts").EnumerateArray())
            {
                var attackId = v.GetProperty("attack_id").GetInt32();
                var status = v.GetProperty("status").GetString() ?? "FAILED";
                var reason = v.GetProperty("reason").GetString() ?? "";
                db.Exec("INSERT INTO eval_results (submission_id, attack_id, status, reason, evaluator_version, created_at) VALUES ($s,$a,$st,$r,$e,$t)",
                    ("$s", submissionId), ("$a", attackId), ("$st", status), ("$r", reason), ("$e", evaluator), ("$t", Database.Now()));
                db.Log("VERDICT", participantId, attackId, JsonSerializer.Serialize(new { submissionId, status, reason }));

                double total = 0;
                if (status == "SOLVED")
                {
                    solvedAny = true;
                    var comps = v.GetProperty("components");
                    foreach (var name in Components)
                    {
                        if (!comps.TryGetProperty(name, out var pts)) continue;
                        total += pts.GetDouble();
                        AddLedger(participantId, attackId, name, pts.GetDouble(), $"Attack {attackId} solved", submissionId, evaluator);
                    }
                    db.Exec("UPDATE attack_deliveries SET solved_at = $t WHERE attack_id = $a AND participant_id = $p AND solved_at IS NULL",
                        ("$t", Database.Now()), ("$a", attackId), ("$p", participantId));
                    if (v.TryGetProperty("metrics", out var m) && m.ValueKind == JsonValueKind.Object && m.EnumerateObject().Any())
                        db.Exec("INSERT INTO benchmark_results (submission_id, attack_id, metrics_json, created_at) VALUES ($s,$a,$m,$t)",
                            ("$s", submissionId), ("$a", attackId), ("$m", m.GetRawText()), ("$t", Database.Now()));
                }
                summaries.Add(new { attackId, status, reason, points = Math.Round(total, 1) });
            }

            // Plan revision 3, section 10: -3 for every rejected patch.
            if (!solvedAny) AddLedger(participantId, null, "penalty", -3, "Rejected submission", submissionId, evaluator);

            db.Exec("UPDATE submissions SET status = 'done' WHERE id = $i", ("$i", submissionId));
            await group.SendAsync(HubEvents.Verdict, submissionId, summaries, "", ct);
        }
        catch (Exception ex)
        {
            // Evaluator trouble is never charged to the participant.
            log.LogError(ex, "Evaluation failed for {Submission}", submissionId);
            db.Exec("UPDATE submissions SET status = 'error' WHERE id = $i", ("$i", submissionId));
            await group.SendAsync(HubEvents.Verdict, submissionId, new object[0], "The checker is unavailable. Press Submit Fix again in a minute.", ct);
        }
    }

    private void AddLedger(string participantId, int? attackId, string component, double points, string reason, string runId, string evaluator)
        => db.Exec("INSERT INTO score_ledger (participant_id, attack_id, component, points, reason, run_id, evaluator_version, created_at) VALUES ($p,$a,$c,$pt,$r,$run,$e,$t)",
            ("$p", participantId), ("$a", attackId), ("$c", component), ("$pt", points), ("$r", reason), ("$run", runId), ("$e", evaluator), ("$t", Database.Now()));
}
