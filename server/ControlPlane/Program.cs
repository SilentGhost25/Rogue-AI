using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlane.Data;
using ControlPlane.Hubs;
using ControlPlane.Services;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<EventState>();
builder.Services.AddSingleton<WorkspaceStore>();
builder.Services.AddSingleton<EngineClient>();
builder.Services.AddSingleton<AttackDispatcher>();
builder.Services.AddSingleton<SubmissionService>();
builder.Services.AddSingleton<LeaderboardService>();
builder.Services.AddHostedService<SchedulerService>();
builder.Services.AddHttpClient("engine", (sp, c) =>
{
    c.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Breach:EngineUrl"]!);
    c.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IConfiguration>().GetValue("Breach:EvaluationTimeoutSeconds", 180));
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<BreachHub>("/hub");

var adminKey = app.Configuration["Breach:AdminKey"] ?? "";
bool IsAdmin(HttpRequest r)
{
    var given = r.Headers["X-Admin-Key"].ToString();
    return adminKey.Length > 0 && given.Length > 0
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(adminKey));
}

var db = app.Services.GetRequiredService<Database>();
SchedulerService.Seed(db);
bool KnownParticipant(string id) => db.Scalar<int>("SELECT COUNT(*) FROM participants WHERE id = $i", ("$i", id)) == 1;
string StatusOf(string id) => db.Scalar<string>("SELECT status FROM participants WHERE id = $i", ("$i", id)) ?? "unknown";

// ===== Kiosk API ======================================================================================
app.MapGet("/api/state", (EventState s) => new { paused = s.Paused, frozen = s.Frozen, serverTime = DateTime.UtcNow });

app.MapPost("/api/kiosk/register", async (RegisterRequest req, WorkspaceStore store, EngineClient engine, AttackDispatcher dispatcher) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.KioskId) || req.Name.Length > 80 || req.KioskId.Length > 80)
        return Results.BadRequest(new { error = "name and kioskId are required" });

    if (db.Scalar<string>("SELECT id FROM participants WHERE kiosk_id = $k", ("$k", req.KioskId)) is { } existing)
        return Results.Ok(new { participantId = existing });

    var id = "p" + Guid.NewGuid().ToString("N")[..10];
    db.Exec("INSERT INTO participants (id, name, kiosk_id, created_at) VALUES ($i,$n,$k,$t)",
        ("$i", id), ("$n", req.Name.Trim()), ("$k", req.KioskId), ("$t", Database.Now()));
    db.Log("KIOSK_REGISTERED", id);

    // Seed the clean site, create the server-side Git clone, then catch up on attacks that already fired.
    store.EnsureSeeded(id);
    await engine.InitRepoAsync(id);
    foreach (var attackId in db.Query("SELECT id FROM attacks WHERE state = 'fired' ORDER BY id", r => r.GetInt32(0)))
        await dispatcher.LandAsync(id, attackId, CancellationToken.None);
    return Results.Ok(new { participantId = id });
});

// Re-sync info for a kiosk that starts or reconnects: status, attacks it must have, pause flag.
app.MapGet("/api/kiosk/{id}/status", (string id, EventState s) =>
{
    if (!KnownParticipant(id)) return Results.NotFound();
    var active = db.Query("SELECT attack_id FROM attack_deliveries WHERE participant_id = $p AND landed_at IS NOT NULL AND solved_at IS NULL ORDER BY attack_id",
        r => r.GetInt32(0), ("$p", id));
    return Results.Ok(new { status = StatusOf(id), activeAttacks = active, paused = s.Paused });
});

app.MapGet("/api/kiosk/{id}/files", (string id, WorkspaceStore store) =>
    KnownParticipant(id) ? Results.Ok(store.List(id)) : Results.NotFound());

// Plan section 5: the kiosk asks for the files an attack changed. They come from the participant's own server copy
// (after the engine applied the patch), so the participant's other work is never overwritten.
app.MapGet("/api/kiosk/{id}/attack-payload/{n:int}", async (string id, int n, WorkspaceStore store, EngineClient engine) =>
{
    if (!KnownParticipant(id)) return Results.NotFound();
    var touches = await engine.GetTouchesAsync(n);
    return touches is null ? Results.Problem("The engine does not know this attack.", statusCode: 502)
                           : Results.Ok(new { attackId = n, files = store.Read(id, touches) });
});

app.MapPost("/api/kiosk/{id}/files/fetch", (string id, FetchRequest req, WorkspaceStore store) =>
    KnownParticipant(id) ? Results.Ok(new { files = store.Read(id, req.Paths) }) : Results.NotFound());

var lastCommit = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();
app.MapPost("/api/kiosk/{id}/snapshot", (string id, SnapshotRequest req, WorkspaceStore store, EngineClient engine) =>
{
    if (!KnownParticipant(id)) return Results.NotFound();
    if (StatusOf(id) != "alive") return Results.Ok(new { written = 0, rejected = Array.Empty<string>(), frozen = true }); // branch frozen
    var (written, rejected) = store.Save(id, req.Files ?? new(), req.Deleted);
    db.Log("SNAPSHOT", id, payloadJson: JsonSerializer.Serialize(new { written, rejected }));
    // Server-owned Git: commit this participant's branch at most every 20 seconds.
    if (written + (req.Deleted?.Count ?? 0) > 0 && DateTime.UtcNow - lastCommit.GetOrAdd(id, DateTime.MinValue) > TimeSpan.FromSeconds(20))
    {
        lastCommit[id] = DateTime.UtcNow;
        _ = Task.Run(() => engine.CommitAsync(id, "snapshot"));
    }
    return Results.Ok(new { written, rejected, frozen = false });
});

app.MapPost("/api/kiosk/{id}/submit", (string id, SubmissionService submissions) =>
{
    if (!KnownParticipant(id)) return Results.NotFound();
    if (StatusOf(id) != "alive") return Results.Conflict(new { error = "This participant is no longer active." });
    var sid = submissions.Create(id);
    _ = Task.Run(() => submissions.EvaluateAsync(id, sid));
    return Results.Accepted(value: new { submissionId = sid, status = "queued" });
});

var telemetryTypes = new HashSet<string> { "PROMPT", "AGY_EXHAUSTED", "OC_CAP_REACHED", "PROVIDER_THROTTLED", "QUOTA_SIGNAL" };
app.MapPost("/api/kiosk/{id}/telemetry", async (string id, TelemetryRequest req, IHubContext<BreachHub> hub) =>
{
    if (!KnownParticipant(id)) return Results.NotFound();
    if (!telemetryTypes.Contains(req.Type)) return Results.BadRequest(new { error = "unknown telemetry type" });
    db.Log(req.Type, id, payloadJson: req.Payload?.GetRawText() ?? "{}");

    db.Exec("INSERT OR IGNORE INTO quota_state (participant_id) VALUES ($p)", ("$p", id));
    if (req.Type == "AGY_EXHAUSTED") db.Exec("UPDATE quota_state SET agy_exhausted = 1 WHERE participant_id = $p", ("$p", id));
    if (req.Type == "OC_CAP_REACHED") db.Exec("UPDATE quota_state SET oc_cap_reached = 1 WHERE participant_id = $p", ("$p", id));
    if (req.Type == "PROMPT") db.Exec("UPDATE quota_state SET oc_prompts = oc_prompts + 1 WHERE participant_id = $p", ("$p", id));

    // Both budgets spent: flag it. An admin confirms elimination with one click.
    if (db.Scalar<int>("SELECT COUNT(*) FROM quota_state WHERE participant_id = $p AND agy_exhausted = 1 AND oc_cap_reached = 1", ("$p", id)) == 1
        && db.Scalar<int>("SELECT COUNT(*) FROM event_log WHERE participant_id = $p AND type = 'QUOTA_EXHAUSTED'", ("$p", id)) == 0)
    {
        db.Log("QUOTA_EXHAUSTED", id);
        await hub.Clients.Group("admins").SendAsync("quota_flag", id);
    }
    return Results.Accepted();
});

// Read-only leaderboard for the projector. When frozen, the top five scores are masked but ranks stay live.
app.MapGet("/api/leaderboard", (LeaderboardService board, EventState state) =>
{
    var rows = board.Compute().Select(r => new
    {
        r.Rank, r.Name, r.Status,
        score = state.Frozen && r.Rank <= 5 ? (double?)null : r.Score,
        r.Solved, r.Active, solvedOf = $"{r.Solved}/7",
    });
    return Results.Ok(new { frozen = state.Frozen, paused = state.Paused, rows });
});

// Incident feed for the projector ticker: attacks fired, solves, eliminations.
app.MapGet("/api/feed", (long? after) =>
{
    var events = db.Query("""
        SELECT e.id, e.type, e.attack_id, COALESCE(p.name, ''), a.name
        FROM event_log e
        LEFT JOIN participants p ON p.id = e.participant_id
        LEFT JOIN attacks a ON a.id = e.attack_id
        WHERE e.id > $after
          AND (e.type IN ('ATTACK_FIRED', 'ELIMINATED') OR (e.type = 'VERDICT' AND e.payload_json LIKE '%"status":"SOLVED"%'))
          AND NOT (e.type = 'ATTACK_FIRED' AND e.participant_id IS NOT NULL)
        ORDER BY e.id DESC LIMIT 15
        """,
        r =>
        {
            var type = r.GetString(1); var attack = r.IsDBNull(2) ? 0 : r.GetInt32(2); var who = r.GetString(3); var atk = r.IsDBNull(4) ? "" : r.GetString(4);
            return new
            {
                id = r.GetInt64(0),
                kind = type == "ATTACK_FIRED" ? "attack" : type == "VERDICT" ? "solve" : "out",
                text = type == "ATTACK_FIRED" ? $"BREACH #{attack}: {atk}" : type == "VERDICT" ? $"{who} neutralised attack #{attack}" : $"{who} was eliminated",
            };
        },
        ("$after", after ?? 0));
    events.Reverse();
    return Results.Ok(new { lastId = db.Scalar<long>("SELECT COALESCE(MAX(id), 0) FROM event_log"), events });
});

// ===== Admin API (X-Admin-Key header; LAN only) ==========================================================
app.MapPost("/admin/attacks/{n:int}/fire", (int n, string? target, HttpRequest r, AttackDispatcher d) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    if (target is not null && !KnownParticipant(target)) return Results.NotFound();
    db.Log("ADMIN_ACTION", target, n, JsonSerializer.Serialize(new { action = "fire", target }));
    _ = Task.Run(() => d.FireAsync(n, target));
    return Results.Accepted();
});

app.MapPost("/admin/attacks/{n:int}/reset", async (int n, string? target, HttpRequest r, AttackDispatcher d, WorkspaceStore s) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    if (target is not null && !KnownParticipant(target)) return Results.NotFound();
    db.Log("ADMIN_ACTION", target, n, JsonSerializer.Serialize(new { action = "reset", target }));
    var ok = await d.ResetAsync(n, target, s);
    return ok ? Results.Ok(new { reset = true, attackId = n, target }) : Results.BadRequest();
});

app.MapPost("/admin/attacks/random", async (string? target, HttpRequest r, AttackDispatcher d) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    if (target is not null && !KnownParticipant(target)) return Results.NotFound();
    db.Log("ADMIN_ACTION", target, null, JsonSerializer.Serialize(new { action = "fire_random", target }));
    var chosen = await d.FireRandomAsync(target);
    return chosen is not null ? Results.Ok(new { attackId = chosen, target }) : Results.BadRequest(new { error = "no available attacks" });
});

app.MapPut("/admin/attacks/{n:int}/schedule", (int n, HttpRequest r, ScheduleRequest req) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    if (!TimeOnly.TryParse(req.FiresAt, out var t)) return Results.BadRequest(new { error = "use HH:mm" });
    db.Exec("UPDATE attacks SET fires_at = $a WHERE id = $i AND state = 'scheduled'", ("$a", t.ToString("HH:mm")), ("$i", n));
    db.Log("ADMIN_ACTION", attackId: n, payloadJson: JsonSerializer.Serialize(new { action = "schedule", firesAt = t.ToString("HH:mm") }));
    return Results.Ok();
});

app.MapGet("/admin/attacks", (HttpRequest r) =>
    !IsAdmin(r) ? Results.Unauthorized()
        : Results.Ok(db.Query("SELECT id, name, fires_at, state FROM attacks ORDER BY id", x => new { id = x.GetInt32(0), name = x.GetString(1), firesAt = x.GetString(2), state = x.GetString(3) })));

app.MapPost("/admin/pause", async (HttpRequest r, EventState s, IHubContext<BreachHub> hub) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    s.Pause(); db.Log("PAUSE");
    await hub.Clients.All.SendAsync(HubEvents.Paused);
    return Results.Ok();
});

app.MapPost("/admin/resume", async (HttpRequest r, EventState s, IHubContext<BreachHub> hub) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    s.Resume(); db.Log("RESUME");
    await hub.Clients.All.SendAsync(HubEvents.Resumed);
    return Results.Ok();
});

app.MapPost("/admin/broadcast", async (HttpRequest r, BroadcastRequest req, IHubContext<BreachHub> hub) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    db.Log("ADMIN_ACTION", payloadJson: JsonSerializer.Serialize(new { action = "broadcast", req.Message }));
    await hub.Clients.All.SendAsync(HubEvents.Broadcast, req.Message);
    return Results.Ok();
});

app.MapPost("/admin/freeze", (HttpRequest r, EventState s, bool? on) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    s.SetFrozen(on ?? true);
    db.Log("ADMIN_ACTION", payloadJson: JsonSerializer.Serialize(new { action = "freeze", on = s.Frozen }));
    return Results.Ok(new { frozen = s.Frozen });
});

app.MapGet("/admin/state", (HttpRequest r, EventState s) =>
    IsAdmin(r) ? Results.Ok(new { paused = s.Paused, frozen = s.Frozen }) : Results.Unauthorized());

app.MapGet("/admin/participants/{id}/scorecard", (string id, HttpRequest r, IConfiguration cfg) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    if (!KnownParticipant(id)) return Results.NotFound();
    var info = db.Query("SELECT name, kiosk_id, status, created_at FROM participants WHERE id = $i",
        x => new { name = x.GetString(0), kioskId = x.GetString(1), status = x.GetString(2), createdAt = x.GetString(3) }, ("$i", id)).First();
    var deliveries = db.Query("SELECT d.attack_id, a.name, d.landed_at, d.solved_at FROM attack_deliveries d JOIN attacks a ON a.id = d.attack_id WHERE d.participant_id = $i ORDER BY d.attack_id",
        x => new { attackId = x.GetInt32(0), name = x.GetString(1), landedAt = x.IsDBNull(2) ? null : x.GetString(2), solvedAt = x.IsDBNull(3) ? null : x.GetString(3) }, ("$i", id));
    var ledger = db.Query("SELECT id, attack_id, component, points, reason, created_at FROM score_ledger WHERE participant_id = $i ORDER BY id",
        x => new { id = x.GetInt64(0), attackId = x.IsDBNull(1) ? (int?)null : x.GetInt32(1), component = x.GetString(2), points = x.GetDouble(3), reason = x.GetString(4), createdAt = x.GetString(5) }, ("$i", id));
    var quota = db.Query("SELECT agy_exhausted, oc_cap_reached, oc_prompts FROM quota_state WHERE participant_id = $i",
        x => new { agyExhausted = x.GetInt32(0) == 1, openCodeCapReached = x.GetInt32(1) == 1, openCodePrompts = x.GetInt32(2) }, ("$i", id)).FirstOrDefault();
    var repo = cfg["Breach:GitHubRepo"];
    var compare = string.IsNullOrWhiteSpace(repo) ? null : $"https://github.com/{repo}/compare/main...participant/{id}";
    return Results.Ok(new { participantId = id, info, deliveries, ledger, quota, compareUrl = compare });
});

app.MapGet("/admin/export/ledger", (HttpRequest r) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    string Csv(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
    var sb = new StringBuilder("id,participant_id,participant,attack_id,component,points,reason,run_id,evaluator_version,created_at\n");
    var rows = db.Query("SELECT l.id, l.participant_id, COALESCE(p.name,''), l.attack_id, l.component, l.points, l.reason, l.run_id, l.evaluator_version, l.created_at FROM score_ledger l LEFT JOIN participants p ON p.id = l.participant_id ORDER BY l.id",
        x => (x.GetInt64(0), x.GetString(1), x.GetString(2), x.IsDBNull(3) ? "" : x.GetInt32(3).ToString(), x.GetString(4), x.GetDouble(5), x.GetString(6), x.IsDBNull(7) ? "" : x.GetString(7), x.IsDBNull(8) ? "" : x.GetString(8), x.GetString(9)));
    foreach (var (id, pid, name, atk, comp, pts, reason, run, ev, at) in rows)
        sb.AppendLine($"{id},{pid},{Csv(name)},{atk},{comp},{pts},{Csv(reason)},{run},{ev},{at}");
    return Results.Text(sb.ToString(), "text/csv");
});

app.MapGet("/admin/leaderboard", (HttpRequest r, LeaderboardService board) =>
    IsAdmin(r) ? Results.Ok(board.Compute()) : Results.Unauthorized());

app.MapGet("/admin/matrix", (HttpRequest r) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    var participants = db.Query("SELECT id, name, status FROM participants ORDER BY name",
        x => new { id = x.GetString(0), name = x.GetString(1), status = x.GetString(2) });
    var deliveries = db.Query("SELECT participant_id, attack_id, landed_at, solved_at FROM attack_deliveries",
        x => new { pid = x.GetString(0), attackId = x.GetInt32(1), landedAt = x.IsDBNull(2) ? null : x.GetString(2), solvedAt = x.IsDBNull(3) ? null : x.GetString(3) });
    var scores = db.Query("SELECT participant_id, attack_id, component, points, reason FROM score_ledger",
        x => new { pid = x.GetString(0), attackId = x.IsDBNull(1) ? (int?)null : x.GetInt32(1), component = x.GetString(2), points = x.GetDouble(3), reason = x.GetString(4) });
    var attacks = db.Query("SELECT id, name, state FROM attacks ORDER BY id",
        x => new { id = x.GetInt32(0), name = x.GetString(1), state = x.GetString(2) });
    return Results.Ok(new { participants, deliveries, scores, attacks });
});

app.MapPost("/admin/participants/{id}/eliminate", async (string id, HttpRequest r, IHubContext<BreachHub> hub) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    if (!KnownParticipant(id)) return Results.NotFound();
    db.Exec("UPDATE participants SET status = 'eliminated' WHERE id = $i", ("$i", id));
    db.Log("ELIMINATED", id);
    db.Exec("INSERT INTO admin_actions (action, detail, created_at) VALUES ('eliminate', $d, $t)", ("$d", id), ("$t", Database.Now()));
    await hub.Clients.Group($"p:{id}").SendAsync(HubEvents.Eliminated, Database.Now());
    return Results.Ok();
});

app.MapPost("/admin/participants/{id}/reapply/{n:int}", async (string id, int n, HttpRequest r, AttackDispatcher d) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    if (!KnownParticipant(id)) return Results.NotFound();
    db.Log("ADMIN_ACTION", id, n, "{\"action\":\"reapply\"}");
    await d.LandAsync(id, n, CancellationToken.None);
    return Results.Ok();
});

// Manual adjustment or disqualification: always a new ledger entry with a mandatory reason; nothing is edited.
app.MapPost("/admin/scores/adjust", (HttpRequest r, AdjustRequest req) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.Reason)) return Results.BadRequest(new { error = "A reason is required." });
    if (!KnownParticipant(req.ParticipantId)) return Results.NotFound();
    db.Exec("INSERT INTO score_ledger (participant_id, attack_id, component, points, reason, created_at) VALUES ($p,NULL,'adjustment',$pt,$r,$t)",
        ("$p", req.ParticipantId), ("$pt", req.Points), ("$r", req.Reason.Trim()), ("$t", Database.Now()));
    if (req.Disqualify) db.Exec("UPDATE participants SET status = 'disqualified' WHERE id = $i", ("$i", req.ParticipantId));
    db.Log("ADMIN_ACTION", req.ParticipantId, payloadJson: JsonSerializer.Serialize(new { action = "adjust", req.Points, req.Reason, req.Disqualify }));
    return Results.Ok();
});

app.MapGet("/admin/export", (HttpRequest r, LeaderboardService board) =>
{
    if (!IsAdmin(r)) return Results.Unauthorized();
    string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    var sb = new StringBuilder("rank,participant,status,score,solved,active,prompts\n");
    foreach (var x in board.Compute()) sb.AppendLine($"{x.Rank},{Csv(x.Name)},{x.Status},{x.Score},{x.Solved},{x.Active},{x.Prompts}");
    return Results.Text(sb.ToString(), "text/csv");
});

app.Run();

public sealed record RegisterRequest(string Name, string KioskId);
public sealed record TelemetryRequest(string Type, JsonElement? Payload);
public sealed record BroadcastRequest(string Message);
public sealed record ScheduleRequest(string FiresAt);
public sealed record AdjustRequest(string ParticipantId, double Points, string Reason, bool Disqualify = false);
