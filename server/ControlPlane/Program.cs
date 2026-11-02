using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
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
builder.Services.AddSingleton<AttackDispatcher>();
builder.Services.AddHostedService<BackupService>();
builder.Services.AddHttpClient("engine", (sp, c) =>
    c.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Breach:EngineUrl"] ?? "http://127.0.0.1:8000"));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<BreachHub>("/hub");

var adminKey = Environment.GetEnvironmentVariable("BREACH_ADMIN_KEY") ?? app.Configuration["Breach:AdminKey"] ?? "";
var enforceEventSecurity = app.Configuration.GetValue<bool>("Breach:EnforceEventSecurity", true);

if (enforceEventSecurity)
{
    if (string.IsNullOrWhiteSpace(adminKey) ||
        adminKey.Equals("CHANGE-ME-BEFORE-EVENT", StringComparison.OrdinalIgnoreCase) ||
        adminKey.Equals("change-me-locally", StringComparison.OrdinalIgnoreCase) ||
        adminKey.Length < 16)
    {
        throw new InvalidOperationException(
            "FATAL SECURITY FAILURE: ControlPlane refused startup. Breach:AdminKey (or BREACH_ADMIN_KEY env var) must be set to a cryptographically random string of at least 16 characters.");
    }
}

var failedAdminAttempts = new ConcurrentDictionary<string, (int Count, DateTime WindowStart)>();
var adminLoopbackOnly = app.Configuration.GetValue<bool>("Breach:AdminLoopbackOnly", true);
var adminAllowedIps = app.Configuration.GetSection("Breach:AdminAllowedIps").Get<string[]>() ?? Array.Empty<string>();

bool IsAuthorizedIp(IPAddress? ip)
{
    if (ip == null) return true;
    if (IPAddress.IsLoopback(ip)) return true;
    if (ip.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(ip.MapToIPv4())) return true;
    var s = ip.ToString();
    if (s == "::1" || s == "127.0.0.1" || s == "::ffff:127.0.0.1") return true;
    if (adminAllowedIps.Contains(s)) return true;
    return false;
}

bool FixedTimeEquals(string a, string b)
{
    var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
    var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
    return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
}

IResult? AuthorizeAdmin(HttpRequest r, Database db)
{
    var remoteIp = r.HttpContext.Connection.RemoteIpAddress;
    var ipStr = remoteIp?.ToString() ?? "127.0.0.1";

    // 1. IP boundary check: block unauthorized LAN connections
    if (adminLoopbackOnly && !IsAuthorizedIp(remoteIp))
    {
        db.Log("ADMIN_ACCESS_DENIED_LAN", payloadJson: JsonSerializer.Serialize(new { ip = ipStr, path = r.Path.Value }));
        return Results.StatusCode(403);
    }

    // 2. Extract Key
    var key = r.Headers["X-Admin-Key"].ToString();
    if (string.IsNullOrEmpty(key) && r.Query.TryGetValue("key", out var qKey))
    {
        key = qKey.ToString();
    }

    // 3. Authenticate Key FIRST (Fix for rate limiter lockout weapon):
    // A valid admin key NEVER gets blocked or locked out by bad actor spam.
    var isValidKey = !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(adminKey) && FixedTimeEquals(key, adminKey);
    if (isValidKey)
    {
        // Successful login resets any failure count from this IP
        failedAdminAttempts.TryRemove(ipStr, out _);
        return null; // Authorized
    }

    // 4. Rate-limiting applies ONLY to callers presenting invalid/missing keys
    var now = DateTime.UtcNow;
    if (failedAdminAttempts.TryGetValue(ipStr, out var attempt))
    {
        if (now - attempt.WindowStart > TimeSpan.FromSeconds(60))
        {
            failedAdminAttempts.TryRemove(ipStr, out _);
        }
        else if (attempt.Count >= 5)
        {
            db.Log("ADMIN_RATE_LIMITED", payloadJson: JsonSerializer.Serialize(new { ip = ipStr, attempts = attempt.Count }));
            return Results.StatusCode(429); // Too Many Requests
        }
    }

    // Track failed attempt
    failedAdminAttempts.AddOrUpdate(ipStr,
        _ => (1, now),
        (_, old) => now - old.WindowStart > TimeSpan.FromSeconds(60) ? (1, now) : (old.Count + 1, old.WindowStart));

    db.Log("ADMIN_AUTH_FAILED", payloadJson: JsonSerializer.Serialize(new { ip = ipStr, path = r.Path.Value }));
    return Results.Unauthorized();
}

// ---- Kiosk API -------------------------------------------------------------
app.MapGet("/api/state", (EventState s) => new { paused = s.Paused, frozen = s.Frozen, serverTime = DateTime.UtcNow });

app.MapGet("/api/leaderboard", (EventState state, Database db) =>
{
    var rows = new List<Standing>();
    using var c = db.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = """
        SELECT p.id,
               COALESCE(SUM(l.points), 0) AS total_score,
               COUNT(DISTINCT CASE WHEN d.solved_at IS NOT NULL THEN d.attack_id END) AS solved_count,
               p.name,
               p.status
        FROM participants p
        LEFT JOIN score_ledger l ON l.participant_id = p.id
        LEFT JOIN attack_deliveries d ON d.participant_id = p.id
        WHERE p.status != 'disqualified'
        GROUP BY p.id
        """;
    using var rd = cmd.ExecuteReader();
    var names = new Dictionary<string, string>();
    while (rd.Read())
    {
        var pid = rd.GetString(0);
        var score = rd.GetDouble(1);
        var solved = rd.GetInt32(2);
        names[pid] = rd.GetString(3);
        rows.Add(new Standing(pid, score, solved, 0, 0, null));
    }

    var ranked = Ranking.Rank(rows);
    return Results.Ok(new
    {
        frozen = state.Frozen,
        leaderboard = ranked.Select((s, i) => new
        {
            rank = i + 1,
            participantId = s.ParticipantId,
            name = names.GetValueOrDefault(s.ParticipantId, s.ParticipantId),
            score = state.Frozen && i < 5 ? -1.0 : s.Score, // Mask top 5 scores during final hour freeze
            scoreMasked = state.Frozen && i < 5,
            solved = s.Solved
        })
    });
});

app.MapPost("/api/kiosk/enroll", async (EnrollRequest req, Database db, IHubContext<BreachHub> hub) =>
{
    var (success, message, pid, deskId) = db.EnrollDesk(req.Token, req.KioskId, req.Name);
    if (!success)
    {
        return Results.Json(new { error = message }, statusCode: 403);
    }
    await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, pid);
    return Results.Ok(new { participantId = pid, deskId });
});

app.MapPost("/api/kiosk/register", async (RegisterRequest req, Database db, IHubContext<BreachHub> hub) =>
{
    using var c = db.Open();
    using var find = c.CreateCommand();
    find.CommandText = "SELECT id FROM participants WHERE kiosk_id = $k";
    find.Parameters.AddWithValue("$k", req.KioskId);
    if (find.ExecuteScalar() is string existing) return Results.Ok(new { participantId = existing });

    var id = "p" + Guid.NewGuid().ToString("N")[..10];
    using var ins = c.CreateCommand();
    ins.CommandText = "INSERT INTO participants (id, name, kiosk_id, created_at) VALUES ($i,$n,$k,$t)";
    ins.Parameters.AddWithValue("$i", id);
    ins.Parameters.AddWithValue("$n", req.Name);
    ins.Parameters.AddWithValue("$k", req.KioskId);
    ins.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
    ins.ExecuteNonQuery();
    db.Log("KIOSK_REGISTERED", id);
    await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
    return Results.Ok(new { participantId = id });
});

app.MapPost("/api/kiosk/{id}/snapshot", async (string id, SnapshotRequest req, HttpRequest httpReq, WorkspaceStore store, Database db, IHubContext<BreachHub> hub) =>
{
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }
    var (written, rejected) = store.Save(id, req.Files);
    db.Log("SNAPSHOT", id, payloadJson: JsonSerializer.Serialize(new { written, rejected }));
    await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
    return Results.Ok(new { written, rejected });
});

app.MapGet("/api/kiosk/{id}/attack-payload/{attackId:int}", async (string id, int attackId, HttpRequest httpReq, Database db, IHttpClientFactory http) =>
{
    // 1. Authenticate Desk Token
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }

    // 2. Gate by Attack Delivery (Must have officially landed)
    using (var c = db.Open())
    using (var cmd = c.CreateCommand())
    {
        cmd.CommandText = "SELECT landed_at FROM attack_deliveries WHERE participant_id = $p AND attack_id = $a";
        cmd.Parameters.AddWithValue("$p", id);
        cmd.Parameters.AddWithValue("$a", attackId);
        var landedAt = cmd.ExecuteScalar();
        if (landedAt == null || landedAt is DBNull || string.IsNullOrEmpty(landedAt.ToString()))
        {
            return Results.StatusCode(403); // Forbidden: attack has not landed yet
        }
    }

    // 3. Attack has landed and token is valid: proxy from engine
    var client = http.CreateClient("engine");
    var res = await client.GetAsync($"/attacks/{attackId}/payload");
    if (!res.IsSuccessStatusCode) return Results.NotFound();
    var content = await res.Content.ReadAsStringAsync();
    return Results.Content(content, "application/json");
});

app.MapPost("/api/kiosk/{id}/submit", (string id, HttpRequest httpReq, Database db, IHttpClientFactory http, IHubContext<BreachHub> hub) =>
{
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }
    var sid = "s" + Guid.NewGuid().ToString("N")[..10];
    using (var c = db.Open())
    using (var cmd = c.CreateCommand())
    {
        cmd.CommandText = "INSERT INTO submissions (id, participant_id, created_at, status) VALUES ($i,$p,$t,'evaluating')";
        cmd.Parameters.AddWithValue("$i", sid);
        cmd.Parameters.AddWithValue("$p", id);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }
    db.Log("SUBMISSION", id, payloadJson: JsonSerializer.Serialize(new { submissionId = sid }));

    // Find active attacks for this participant
    var activeAttacks = new List<int>();
    using (var c = db.Open())
    using (var findCmd = c.CreateCommand())
    {
        findCmd.CommandText = "SELECT attack_id FROM attack_deliveries WHERE participant_id = $p AND solved_at IS NULL";
        findCmd.Parameters.AddWithValue("$p", id);
        using var r = findCmd.ExecuteReader();
        while (r.Read()) activeAttacks.Add(r.GetInt32(0));
    }

    if (activeAttacks.Count == 0)
    {
        // If none marked unsolved, evaluate all landed attacks
        using var c = db.Open();
        using var findCmd = c.CreateCommand();
        findCmd.CommandText = "SELECT attack_id FROM attack_deliveries WHERE participant_id = $p";
        findCmd.Parameters.AddWithValue("$p", id);
        using var r = findCmd.ExecuteReader();
        while (r.Read()) activeAttacks.Add(r.GetInt32(0));
    }

    if (activeAttacks.Count == 0) activeAttacks.Add(1);

    // Asynchronously dispatch evaluation to Python engine
    _ = Task.Run(async () =>
    {
        try
        {
            var client = http.CreateClient("engine");
            var evalRes = await client.PostAsJsonAsync("/evaluate", new { participant_id = id, submission_id = sid, attack_ids = activeAttacks });
            if (evalRes.IsSuccessStatusCode)
            {
                var body = await evalRes.Content.ReadFromJsonAsync<EvaluationResponse>();
                if (body?.Verdicts != null)
                {
                    using var conn = db.Open();
                    foreach (var v in body.Verdicts)
                    {
                        var score = 0.0;
                        if (v.Status == "SOLVED")
                        {
                            score = 85.0; // Base solve score
                            using var updateCmd = conn.CreateCommand();
                            updateCmd.CommandText = "UPDATE attack_deliveries SET solved_at = $t WHERE participant_id = $p AND attack_id = $a";
                            updateCmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                            updateCmd.Parameters.AddWithValue("$p", id);
                            updateCmd.Parameters.AddWithValue("$a", v.Attack_Id);
                            updateCmd.ExecuteNonQuery();

                            using var ledgerCmd = conn.CreateCommand();
                            ledgerCmd.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, reason, run_id, created_at) VALUES ($p,$a,'solve',$pts,$r,$sid,$t)";
                            ledgerCmd.Parameters.AddWithValue("$p", id);
                            ledgerCmd.Parameters.AddWithValue("$a", v.Attack_Id);
                            ledgerCmd.Parameters.AddWithValue("$pts", score);
                            ledgerCmd.Parameters.AddWithValue("$r", v.Reason);
                            ledgerCmd.Parameters.AddWithValue("$sid", sid);
                            ledgerCmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                            ledgerCmd.ExecuteNonQuery();

                            db.Log("VERDICT", id, v.Attack_Id, JsonSerializer.Serialize(new { status = "SOLVED", score, v.Reason }));
                        }
                        else
                        {
                            score = -3.0; // Failed penalty
                            using var ledgerCmd = conn.CreateCommand();
                            ledgerCmd.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, reason, run_id, created_at) VALUES ($p,$a,'penalty',-3.0,$r,$sid,$t)";
                            ledgerCmd.Parameters.AddWithValue("$p", id);
                            ledgerCmd.Parameters.AddWithValue("$a", v.Attack_Id);
                            ledgerCmd.Parameters.AddWithValue("$r", v.Reason);
                            ledgerCmd.Parameters.AddWithValue("$sid", sid);
                            ledgerCmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                            ledgerCmd.ExecuteNonQuery();

                            db.Log("VERDICT", id, v.Attack_Id, JsonSerializer.Serialize(new { status = v.Status, score = -3.0, v.Reason }));
                        }

                        // Broadcast live verdict to participant kiosk!
                        await hub.Clients.Group($"p:{id}").SendAsync("VERDICT", v.Attack_Id, v.Status, v.Reason, score);
                        await hub.Clients.Group("admins").SendAsync("VERDICT", id, v.Attack_Id, v.Status, v.Reason, score);
                        await hub.Clients.Group("projectors").SendAsync("VERDICT", id, v.Attack_Id, v.Status, v.Reason, score);
                        await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await hub.Clients.Group($"p:{id}").SendAsync("VERDICT", 1, "FAILED", $"Evaluator error: {ex.Message}", 0.0);
        }
    });

    return Results.Accepted(value: new { submissionId = sid, status = "evaluating" });
});

app.MapPost("/api/kiosk/{id}/telemetry", async (string id, TelemetryRequest req, HttpRequest httpReq, Database db, IHubContext<BreachHub> hub) =>
{
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }
    db.Log(req.Type, id, payloadJson: req.Payload?.GetRawText() ?? "{}");
    if (req.Type == "AGY_EXHAUSTED")
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO quota_state (participant_id, agy_exhausted) VALUES ($p, 1) ON CONFLICT(participant_id) DO UPDATE SET agy_exhausted = 1";
        cmd.Parameters.AddWithValue("$p", id);
        cmd.ExecuteNonQuery();
        await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
    }
    else if (req.Type == "PROMPT")
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO quota_state (participant_id, oc_prompts) VALUES ($p, 1) ON CONFLICT(participant_id) DO UPDATE SET oc_prompts = oc_prompts + 1";
        cmd.Parameters.AddWithValue("$p", id);
        cmd.ExecuteNonQuery();
        await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
    }
    return Results.Accepted();
});

// ---- Admin API (X-Admin-Key header; Loopback only) ---------------------------
app.MapPost("/admin/attacks/{n:int}/fire", (int n, string? targetId, HttpRequest r, Database db, AttackDispatcher d) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    _ = Task.Run(() => d.FireAsync(n, targetId));
    return Results.Accepted();
});

app.MapPost("/admin/pause", async (HttpRequest r, EventState s, Database db, IHubContext<BreachHub> hub) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    s.Pause(); db.Log("PAUSE");
    await hub.Clients.All.SendAsync(HubEvents.Paused);
    return Results.Ok();
});

app.MapPost("/admin/resume", async (HttpRequest r, EventState s, Database db, IHubContext<BreachHub> hub) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    s.Resume(); db.Log("RESUME");
    await hub.Clients.All.SendAsync(HubEvents.Resumed);
    return Results.Ok();
});

app.MapPost("/admin/scoreboard/freeze", async (HttpRequest r, EventState s, Database db, IHubContext<BreachHub> hub) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    s.ToggleFreeze();
    db.Log(s.Frozen ? "SCOREBOARD_FROZEN" : "SCOREBOARD_UNFROZEN");
    await hub.Clients.All.SendAsync(s.Frozen ? HubEvents.ScoreboardFrozen : HubEvents.ScoreboardUnfrozen);
    return Results.Ok(new { frozen = s.Frozen });
});

app.MapPost("/admin/broadcast", async (HttpRequest r, BroadcastRequest req, Database db, IHubContext<BreachHub> hub) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    db.Log("ADMIN_ACTION", payloadJson: JsonSerializer.Serialize(new { action = "broadcast", req.Message }));
    await hub.Clients.All.SendAsync(HubEvents.Broadcast, req.Message);
    return Results.Ok();
});

app.MapGet("/admin/leaderboard", (HttpRequest r, Database db) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var rows = new List<Standing>();
    using var c = db.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = """
        SELECT p.id,
               COALESCE(SUM(l.points), 0) AS total_score,
               COUNT(DISTINCT CASE WHEN d.solved_at IS NOT NULL THEN d.attack_id END) AS solved_count
        FROM participants p
        LEFT JOIN score_ledger l ON l.participant_id = p.id
        LEFT JOIN attack_deliveries d ON d.participant_id = p.id
        WHERE p.status != 'disqualified'
        GROUP BY p.id
        """;
    using var rd = cmd.ExecuteReader();
    while (rd.Read())
    {
        rows.Add(new Standing(rd.GetString(0), rd.GetDouble(1), rd.GetInt32(2), 0, 0, null));
    }
    return Results.Ok(Ranking.Rank(rows).Select((s, i) => new { rank = i + 1, participantId = s.ParticipantId, score = s.Score, solved = s.Solved }));
});

app.MapGet("/admin/kiosks", (HttpRequest r, Database db) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var list = new List<object>();
    using var c = db.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = """
        SELECT p.id, p.name, p.kiosk_id, p.status, p.created_at,
               COALESCE(SUM(l.points), 0) AS total_score,
               COUNT(DISTINCT CASE WHEN d.solved_at IS NOT NULL THEN d.attack_id END) AS solved_count,
               COUNT(DISTINCT d.attack_id) AS landed_count,
               COALESCE(q.agy_exhausted, 0) AS agy_exhausted,
               COALESCE(q.oc_prompts, 0) AS oc_prompts
        FROM participants p
        LEFT JOIN score_ledger l ON l.participant_id = p.id
        LEFT JOIN attack_deliveries d ON d.participant_id = p.id
        LEFT JOIN quota_state q ON q.participant_id = p.id
        GROUP BY p.id
        ORDER BY total_score DESC
        """;
    using var rd = cmd.ExecuteReader();
    while (rd.Read())
    {
        list.Add(new
        {
            id = rd.GetString(0),
            name = rd.GetString(1),
            kioskId = rd.GetString(2),
            status = rd.GetString(3),
            createdAt = rd.GetString(4),
            totalScore = rd.GetDouble(5),
            solvedCount = rd.GetInt32(6),
            landedCount = rd.GetInt32(7),
            agyExhausted = rd.GetInt32(8) == 1,
            openCodePrompts = rd.GetInt32(9)
        });
    }
    return Results.Ok(list);
});

app.MapGet("/admin/kiosks/{id}", (string id, HttpRequest r, Database db) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    using var c = db.Open();

    // 1. Participant info
    using var pCmd = c.CreateCommand();
    pCmd.CommandText = "SELECT id, name, kiosk_id, status, created_at FROM participants WHERE id = $p";
    pCmd.Parameters.AddWithValue("$p", id);
    using var pRd = pCmd.ExecuteReader();
    if (!pRd.Read()) return Results.NotFound();
    var participant = new
    {
        id = pRd.GetString(0),
        name = pRd.GetString(1),
        kioskId = pRd.GetString(2),
        status = pRd.GetString(3),
        createdAt = pRd.GetString(4),
        gitBranch = $"participant/{id}"
    };
    pRd.Close();

    // 2. Deliveries
    var deliveries = new List<object>();
    using var dCmd = c.CreateCommand();
    dCmd.CommandText = "SELECT attack_id, landed_at, solved_at FROM attack_deliveries WHERE participant_id = $p ORDER BY attack_id";
    dCmd.Parameters.AddWithValue("$p", id);
    using var dRd = dCmd.ExecuteReader();
    while (dRd.Read())
    {
        deliveries.Add(new
        {
            attackId = dRd.GetInt32(0),
            landedAt = dRd.IsDBNull(1) ? null : dRd.GetString(1),
            solvedAt = dRd.IsDBNull(2) ? null : dRd.GetString(2)
        });
    }
    dRd.Close();

    // 3. Ledger
    var ledger = new List<object>();
    using var lCmd = c.CreateCommand();
    lCmd.CommandText = "SELECT id, attack_id, component, points, reason, created_at FROM score_ledger WHERE participant_id = $p ORDER BY id DESC";
    lCmd.Parameters.AddWithValue("$p", id);
    using var lRd = lCmd.ExecuteReader();
    while (lRd.Read())
    {
        ledger.Add(new
        {
            id = lRd.GetInt64(0),
            attackId = lRd.IsDBNull(1) ? (int?)null : lRd.GetInt32(1),
            component = lRd.GetString(2),
            points = lRd.GetDouble(3),
            reason = lRd.GetString(4),
            createdAt = lRd.GetString(5)
        });
    }
    lRd.Close();

    return Results.Ok(new { participant, deliveries, ledger });
});

app.MapPost("/admin/kiosks/{id}/disqualify", async (string id, HttpRequest r, Database db, IHubContext<BreachHub> hub) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    using var c = db.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "UPDATE participants SET status = 'disqualified' WHERE id = $p";
    cmd.Parameters.AddWithValue("$p", id);
    cmd.ExecuteNonQuery();
    db.Log("DISQUALIFIED", id);
    await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
    return Results.Ok();
});

app.MapGet("/admin/events", (HttpRequest r, Database db) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var events = new List<object>();
    using var c = db.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT id, ts, type, participant_id, attack_id, payload_json FROM event_log ORDER BY id DESC LIMIT 100";
    using var rd = cmd.ExecuteReader();
    while (rd.Read())
    {
        events.Add(new
        {
            id = rd.GetInt64(0),
            ts = rd.GetString(1),
            type = rd.GetString(2),
            participantId = rd.IsDBNull(3) ? null : rd.GetString(3),
            attackId = rd.IsDBNull(4) ? (int?)null : rd.GetInt32(4),
            payload = rd.GetString(5)
        });
    }
    return Results.Ok(events);
});

app.MapGet("/admin/export/ledger", (HttpRequest r, Database db) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("id,participant_id,attack_id,component,points,reason,run_id,created_at");
    using var c = db.Open();
    using var cmd = c.CreateCommand();
    cmd.CommandText = "SELECT id, participant_id, attack_id, component, points, reason, run_id, created_at FROM score_ledger ORDER BY id";
    using var rd = cmd.ExecuteReader();
    while (rd.Read())
    {
        var reason = rd.GetString(5).Replace("\"", "\"\"");
        sb.AppendLine($"{rd.GetInt64(0)},{rd.GetString(1)},{(rd.IsDBNull(2) ? "" : rd.GetInt32(2).ToString())},{rd.GetString(3)},{rd.GetDouble(4)},\"{reason}\",{(rd.IsDBNull(6) ? "" : rd.GetString(6))},{rd.GetString(7)}");
    }
    return Results.Text(sb.ToString(), "text/csv");
});

app.MapGet("/admin/backup/status", (HttpRequest r, Database db, IConfiguration config) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var dbPath = config["Breach:DatabasePath"] ?? "data/breach.db";
    var backupDir = config["Breach:BackupPath"] ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "backups");
    var mirrorDir = config["Breach:BackupMirrorPath"];
    var status = BackupService.GetBackupStatus(backupDir, mirrorDir);
    return Results.Ok(status);
});

app.Run();

public sealed record RegisterRequest(string Name, string KioskId);
public sealed record EnrollRequest(string Token, string KioskId, string Name);
public sealed record TelemetryRequest(string Type, JsonElement? Payload);
public sealed record BroadcastRequest(string Message);
public sealed record EvaluationResponse(string Submission_Id, string Evaluator, List<VerdictDto> Verdicts);
public sealed record VerdictDto(int Attack_Id, string Status, string Reason);
