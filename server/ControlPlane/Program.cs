using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ControlPlane.Auth;
using ControlPlane.Data;
using ControlPlane.Hubs;
using ControlPlane.Models;
using ControlPlane.Services;
using Microsoft.AspNetCore.SignalR;

string FindWebRoot()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "server", "ControlPlane", "wwwroot"),
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"))
    };
    foreach (var dir in candidates)
    {
        if (Directory.Exists(dir)) return dir;
    }
    return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
}

var resolvedWebRoot = FindWebRoot();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = resolvedWebRoot
});
builder.Services.AddAdminSessionAuth(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<EventState>();
builder.Services.AddSingleton<WorkspaceStore>();
builder.Services.AddSingleton<GitMirrorService>();
builder.Services.AddSingleton<AttackDispatcher>();
builder.Services.AddSingleton<RogueAgentOrchestrator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RogueAgentOrchestrator>());
builder.Services.AddHostedService<BackupService>();
builder.Services.AddHostedService<HostDiscoveryBroadcaster>();
builder.Services.AddHttpClient("engine", (sp, c) =>
    c.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Breach:EngineUrl"] ?? "http://127.0.0.1:8000"));

var app = builder.Build();
app.UseDefaultFiles();

var fileTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
fileTypeProvider.Mappings[".exe"] = "application/vnd.microsoft.portable-executable";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = fileTypeProvider,
    ServeUnknownFileTypes = true
});

app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/download", () => Results.Redirect("/download.html"));

string? FindSetupExe()
{
    var candidates = new[]
    {
        Path.Combine(resolvedWebRoot, "RogueAI-Setup.exe"),
        Path.Combine(Directory.GetCurrentDirectory(), "dist", "RogueAI-Setup.exe"),
        Path.Combine(Directory.GetCurrentDirectory(), "server", "ControlPlane", "wwwroot", "RogueAI-Setup.exe"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", "RogueAI-Setup.exe")),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "dist", "RogueAI-Setup.exe"))
    };
    foreach (var path in candidates)
    {
        if (File.Exists(path)) return path;
    }
    return null;
}

app.MapMethods("/RogueAI-Setup.exe", new[] { "GET", "HEAD" }, () =>
{
    var exePath = FindSetupExe();
    if (exePath == null) return Results.NotFound("RogueAI-Setup.exe not found on server.");
    return Results.File(exePath, "application/octet-stream", "RogueAI-Setup.exe");
});

app.MapMethods("/download/setup", new[] { "GET", "HEAD" }, () =>
{
    var exePath = FindSetupExe();
    if (exePath == null) return Results.NotFound("RogueAI-Setup.exe not found on server.");
    return Results.File(exePath, "application/octet-stream", "RogueAI-Setup.exe");
});

// LAN dynamic discovery endpoint for participants on Wi-Fi and Ethernet
app.MapGet("/api/discovery", () => Results.Ok(new
{
    role = "organizer",
    version = "1.0",
    name = "Breach Control Plane",
    port = 5080
}));

app.MapAdminLogin();
app.MapHub<BreachHub>("/hub");

string ResolveAdminKey()
{
    var env = Environment.GetEnvironmentVariable("BREACH_ADMIN_KEY");
    if (!string.IsNullOrWhiteSpace(env) && env.Length >= 16 &&
        !env.Equals("CHANGE-ME-BEFORE-EVENT", StringComparison.OrdinalIgnoreCase) &&
        !env.Equals("change-me-locally", StringComparison.OrdinalIgnoreCase))
    {
        return env.Trim();
    }

    var cfgKey = app.Configuration["Breach:AdminKey"];
    if (!string.IsNullOrWhiteSpace(cfgKey) && cfgKey.Length >= 16 &&
        !cfgKey.Equals("CHANGE-ME-BEFORE-EVENT", StringComparison.OrdinalIgnoreCase) &&
        !cfgKey.Equals("change-me-locally", StringComparison.OrdinalIgnoreCase))
    {
        return cfgKey.Trim();
    }

    // Shared host admin key file across HostSupervisor and ControlPlane
    var hostDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "host");
    var keyFile = Path.Combine(hostDir, "admin.key");
    try
    {
        Directory.CreateDirectory(hostDir);
        if (File.Exists(keyFile))
        {
            var saved = File.ReadAllText(keyFile).Trim();
            if (saved.Length >= 16) return saved;
        }
        var generated = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        File.WriteAllText(keyFile, generated);
        return generated;
    }
    catch
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }
}

var adminKey = ResolveAdminKey();

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
    // Path 1: Valid admin session cookie — bypasses loopback and rate-limiting
    if (r.HttpContext.User.Identity?.IsAuthenticated == true && r.HttpContext.User.IsInRole("admin"))
    {
        return null; // Authorized via session cookie
    }

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

app.MapPost("/api/kiosk/register", async (RegisterRequest req, HttpRequest httpReq, Database db, IHubContext<BreachHub> hub) =>
{
    var remoteIp = httpReq.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    var (id, _, token, _) = db.RegisterOrUpdateKiosk(
        req.Name,
        req.KioskId,
        req.DeskId,
        req.Password,
        remoteIp,
        req.GitHubHandle,
        req.GoogleAccount);

    await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
    return Results.Ok(new { participantId = id, token = token });
});

app.MapPost("/api/kiosk/{id}/snapshot", async (string id, SnapshotRequest req, HttpRequest httpReq, WorkspaceStore store, Database db, IHubContext<BreachHub> hub) =>
{
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (!string.IsNullOrWhiteSpace(token) && !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }
    var (written, rejected) = store.Save(id, req.Files, req.Deleted);
    db.Log("SNAPSHOT", id, payloadJson: JsonSerializer.Serialize(new { written, rejected }));
    await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
    return Results.Ok(new { written, rejected });
});

app.MapGet("/api/kiosk/{id}/status", (string id, Database db, EventState s) =>
{
    var exists = db.QueryScalar<long>("SELECT COUNT(*) FROM participants WHERE id = @p", ("@p", id)) > 0;
    if (!exists) return Results.NotFound();
    var active = db.Query<int>("SELECT attack_id FROM attack_deliveries WHERE participant_id = @p AND landed_at IS NOT NULL AND solved_at IS NULL ORDER BY attack_id", ("@p", id));
    var status = db.QueryScalar<string>("SELECT status FROM participants WHERE id = @p", ("@p", id)) ?? "active";
    return Results.Ok(new { status, activeAttacks = active, paused = s.Paused });
});

app.MapGet("/api/kiosk/{id}/files", (string id, Database db, WorkspaceStore store) =>
{
    var exists = db.QueryScalar<long>("SELECT COUNT(*) FROM participants WHERE id = @p", ("@p", id)) > 0;
    if (!exists) return Results.NotFound();
    return Results.Ok(store.List(id));
});

app.MapPost("/api/kiosk/{id}/files/fetch", (string id, FetchRequest req, Database db, WorkspaceStore store) =>
{
    var exists = db.QueryScalar<long>("SELECT COUNT(*) FROM participants WHERE id = @p", ("@p", id)) > 0;
    if (!exists) return Results.NotFound();
    return Results.Ok(new { files = store.Read(id, req.Paths) });
});

app.MapGet("/api/time", () => Results.Ok(new { serverTimeUtc = DateTime.UtcNow.ToString("O") }));

bool IsCanaryDesk(string id, Database db)
{
    var canaryDeskId = app.Configuration["RogueAgent:CanaryDeskId"] ?? "CANARY";
    if (string.Equals(id, canaryDeskId, StringComparison.OrdinalIgnoreCase)) return true;
    var participantDesk = db.QueryScalar<string>("SELECT desk_id FROM desk_tokens WHERE participant_id = @p", ("@p", id));
    return string.Equals(participantDesk, canaryDeskId, StringComparison.OrdinalIgnoreCase);
}

app.MapGet("/api/kiosk/{id}/active-breach", (string id, HttpRequest httpReq, Database db, RogueAgentOrchestrator orch) =>
{
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }

    var active = db.GetActiveAttackForReconcile();
    if (active is null)
    {
        return Results.Ok(new
        {
            active = false,
            activeAttackId = (int?)null,
            state = "Idle",
            landed = false
        });
    }

    var attack = orch.Get(active.Value.AttackId);
    return Results.Ok(new
    {
        active = true,
        activeAttackId = (int?)active.Value.AttackId,
        attackId = active.Value.AttackId,
        title = attack.Title,
        state = active.Value.State.ToString(),
        imminentSeconds = attack.Dispatch.ImminentSeconds,
        landAtUtc = active.Value.LandAtUtc?.ToString("O"),
        landAtUtcIso = active.Value.LandAtUtc?.ToString("O"),
        landed = active.Value.State == DispatchState.ArenaLanded
    });
});

app.MapGet("/api/kiosk/{id}/attack-payload/{attackId:int}", async (string id, int attackId, HttpRequest httpReq, Database db, IHttpClientFactory http) =>
{
    // 1. Authenticate Desk Token
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }

    // 2. Canonical gate per Section D:
    //    ArenaImminent / ArenaLanded -> any kiosk may fetch (pre-buffer + late pull)
    //    CanaryFired -> ONLY the canary desk (prevents leak during canary validation)
    //    Pending / CanaryConfirmed -> blocked
    var state = db.GetDispatchState(attackId);
    var allowed =
        state == DispatchState.ArenaImminent ||
        state == DispatchState.ArenaLanded ||
        (state == DispatchState.CanaryFired && IsCanaryDesk(id, db));

    if (!allowed) return Results.StatusCode(403);
    if (!db.IsAttackDeliveryPreRegistered(attackId, id)) return Results.StatusCode(403);

    // 3. Attack is imminent or landed and delivery pre-registered: proxy from engine
    var client = http.CreateClient("engine");
    var res = await client.GetAsync($"/attacks/{attackId}/payload");
    if (!res.IsSuccessStatusCode) return Results.NotFound();
    var content = await res.Content.ReadAsStringAsync();
    return Results.Content(content, "application/json");
});

app.MapPost("/api/kiosk/{id}/submit", (string id, HttpRequest httpReq, Database db, IHttpClientFactory http, IHubContext<BreachHub> hub, EventState eventState, RogueAgentOrchestrator orch, WorkspaceStore wsStore, GitMirrorService gitMirror) =>
{
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }
    if (eventState.Frozen)
    {
        return Results.Conflict(new { error = "Leaderboard is frozen. Submissions are closed.", code = "FREEZE" });
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

            // Catch 4: Extract CLI telemetry with fallback (submissions * 3)
            int promptCount = 0;
            int tokenUsage = 0;
            int subAttempts = 1;
            using (var c = db.Open())
            {
                using (var tCmd = c.CreateCommand())
                {
                    tCmd.CommandText = "SELECT prompt_count, token_usage FROM telemetry_latest WHERE participant_id = $p";
                    tCmd.Parameters.AddWithValue("$p", id);
                    using var tr = tCmd.ExecuteReader();
                    if (tr.Read())
                    {
                        promptCount = tr.GetInt32(0);
                        tokenUsage = tr.GetInt32(1);
                    }
                }
                using (var sCmd = c.CreateCommand())
                {
                    sCmd.CommandText = "SELECT COUNT(*) FROM score_ledger WHERE participant_id = $p AND component = 'penalty'";
                    sCmd.Parameters.AddWithValue("$p", id);
                    var cnt = Convert.ToInt32(sCmd.ExecuteScalar());
                    subAttempts = Math.Max(1, cnt + 1);
                }
            }

            if (promptCount <= 0)
            {
                promptCount = subAttempts * 3; // Fallback multiplier
            }

            var telemetryPayload = new { prompt_count = promptCount, token_usage = tokenUsage, submissions = subAttempts };
            var evalRes = await client.PostAsJsonAsync("/evaluate", new { 
                participant_id = id, 
                submission_id = sid, 
                attack_ids = activeAttacks,
                telemetry = telemetryPayload
            });
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
                            var attackManifest = orch.Attacks.FirstOrDefault(a => a.Id == v.Attack_Id);
                            var baseCentipoints = attackManifest?.Scoring.BasePoints ?? 8500;
                            var speedMax = attackManifest?.Scoring.SpeedBonusMax ?? 1500;
                            var windowSec = attackManifest?.Scoring.SpeedBonusWindowSeconds ?? 1800;
                            var lateMult = attackManifest?.Scoring.LateSolveMultiplier ?? 0.70;

                            string? landedAtStr = null;
                            using (var landCmd = conn.CreateCommand())
                            {
                                landCmd.CommandText = "SELECT landed_at FROM attack_deliveries WHERE participant_id = $p AND attack_id = $a";
                                landCmd.Parameters.AddWithValue("$p", id);
                                landCmd.Parameters.AddWithValue("$a", v.Attack_Id);
                                landedAtStr = landCmd.ExecuteScalar() as string;
                            }

                            long speedBonus = 0;
                            if (!string.IsNullOrEmpty(landedAtStr) && DateTime.TryParse(landedAtStr, out var landedAt))
                            {
                                var elapsedSec = Math.Max(0, (DateTime.UtcNow - landedAt.ToUniversalTime()).TotalSeconds);
                                if (elapsedSec <= windowSec)
                                {
                                    // Linear speed decay: speedMax * (1 - elapsed / window)
                                    var factor = Math.Clamp(1.0 - (elapsedSec / windowSec), 0.0, 1.0);
                                    speedBonus = (long)Math.Round(speedMax * factor);
                                }
                                else
                                {
                                    // Late solve: 70% base, 0 speed bonus
                                    baseCentipoints = (long)Math.Round(baseCentipoints * lateMult);
                                    speedBonus = 0;
                                }
                            }

                            var totalCentipoints = baseCentipoints + speedBonus;
                            score = totalCentipoints / 100.0;

                            using var updateCmd = conn.CreateCommand();
                            updateCmd.CommandText = "UPDATE attack_deliveries SET solved_at = $t WHERE participant_id = $p AND attack_id = $a";
                            updateCmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                            updateCmd.Parameters.AddWithValue("$p", id);
                            updateCmd.Parameters.AddWithValue("$a", v.Attack_Id);
                            updateCmd.ExecuteNonQuery();

                            using var ledgerCmd = conn.CreateCommand();
                            ledgerCmd.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, centipoints, reason, run_id, created_at, final) VALUES ($p,$a,'solve',$pts,$cpts,$r,$sid,$t,1)";
                            ledgerCmd.Parameters.AddWithValue("$p", id);
                            ledgerCmd.Parameters.AddWithValue("$a", v.Attack_Id);
                            ledgerCmd.Parameters.AddWithValue("$pts", score);
                            ledgerCmd.Parameters.AddWithValue("$cpts", totalCentipoints);
                            ledgerCmd.Parameters.AddWithValue("$r", v.Reason);
                            ledgerCmd.Parameters.AddWithValue("$sid", sid);
                            ledgerCmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                            ledgerCmd.ExecuteNonQuery();

                            db.Log("VERDICT", id, v.Attack_Id, JsonSerializer.Serialize(new { status = "SOLVED", score, centipoints = totalCentipoints, baseCentipoints, speedBonus, v.Reason }));
                            _ = gitMirror.SyncBranchAsync(id, wsStore.PathFor(id), $"Solve Attack #{v.Attack_Id} (+{score:0.#} pts)");
                        }
                        else
                        {
                            // Rejection penalty: -300 centipoints (-3.0 pts), capped at -1500 centipoints (-15.0 pts / 5 attempts) per attack
                            long penCount = 0;
                            using (var penCmd = conn.CreateCommand())
                            {
                                penCmd.CommandText = "SELECT COUNT(*) FROM score_ledger WHERE participant_id = $p AND attack_id = $a AND component = 'penalty'";
                                penCmd.Parameters.AddWithValue("$p", id);
                                penCmd.Parameters.AddWithValue("$a", v.Attack_Id);
                                penCount = Convert.ToInt64(penCmd.ExecuteScalar());
                            }

                            if (penCount < 5)
                            {
                                var penaltyPts = v.Status == "PARTIAL" ? -1.0 : -3.0;
                                var penaltyCenti = v.Status == "PARTIAL" ? -100 : -300;
                                score = penaltyPts;
                                using var ledgerCmd = conn.CreateCommand();
                                ledgerCmd.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, centipoints, reason, run_id, created_at, final) VALUES ($p,$a,'penalty',$pts,$cpts,$r,$sid,$t,1)";
                                ledgerCmd.Parameters.AddWithValue("$p", id);
                                ledgerCmd.Parameters.AddWithValue("$a", v.Attack_Id);
                                ledgerCmd.Parameters.AddWithValue("$pts", penaltyPts);
                                ledgerCmd.Parameters.AddWithValue("$cpts", penaltyCenti);
                                ledgerCmd.Parameters.AddWithValue("$r", v.Reason);
                                ledgerCmd.Parameters.AddWithValue("$sid", sid);
                                ledgerCmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                                ledgerCmd.ExecuteNonQuery();
                            }
                            else
                            {
                                score = 0.0; // Penalty cap reached
                            }

                            db.Log("VERDICT", id, v.Attack_Id, JsonSerializer.Serialize(new { status = v.Status, score, v.Reason, penaltyCapped = penCount >= 5 }));
                        }

                        using var evalCmd = conn.CreateCommand();
                        evalCmd.CommandText = "INSERT INTO eval_results (submission_id, attack_id, status, reason, evaluator_version, created_at) VALUES ($sid, $a, $st, $r, $v, $t)";
                        evalCmd.Parameters.AddWithValue("$sid", sid);
                        evalCmd.Parameters.AddWithValue("$a", v.Attack_Id);
                        evalCmd.Parameters.AddWithValue("$st", v.Status);
                        evalCmd.Parameters.AddWithValue("$r", v.Reason);
                        evalCmd.Parameters.AddWithValue("$v", "v2-fast");
                        evalCmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                        evalCmd.ExecuteNonQuery();

                        // Broadcast live verdict to participant kiosk!
                        await hub.Clients.Group($"p:{id}").SendAsync("VERDICT", v.Attack_Id, v.Status, v.Reason, score);
                        await hub.Clients.Group("admins").SendAsync("VERDICT", id, v.Attack_Id, v.Status, v.Reason, score);
                        await hub.Clients.Group("projectors").SendAsync("VERDICT", id, v.Attack_Id, v.Status, v.Reason, score);
                        await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
                    }

                    using var subCmd = conn.CreateCommand();
                    subCmd.CommandText = "UPDATE submissions SET status = 'done' WHERE id = $sid";
                    subCmd.Parameters.AddWithValue("$sid", sid);
                    subCmd.ExecuteNonQuery();
                }
            }
            else
            {
                var errText = await evalRes.Content.ReadAsStringAsync();
                using var conn = db.Open();
                using var subCmd = conn.CreateCommand();
                subCmd.CommandText = "UPDATE submissions SET status = 'failed' WHERE id = $sid";
                subCmd.Parameters.AddWithValue("$sid", sid);
                subCmd.ExecuteNonQuery();

                foreach (var aid in activeAttacks)
                {
                    await hub.Clients.Group($"p:{id}").SendAsync("VERDICT", aid, "FAILED", $"Engine error ({(int)evalRes.StatusCode}): {errText}", 0.0);
                    await hub.Clients.Group("admins").SendAsync("VERDICT", id, aid, "FAILED", $"Engine error: {errText}", 0.0);
                }
                await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
            }
        }
        catch (Exception ex)
        {
            try
            {
                using var conn = db.Open();
                using var subCmd = conn.CreateCommand();
                subCmd.CommandText = "UPDATE submissions SET status = 'error' WHERE id = $sid";
                subCmd.Parameters.AddWithValue("$sid", sid);
                subCmd.ExecuteNonQuery();
            }
            catch { }

            foreach (var aid in activeAttacks)
            {
                await hub.Clients.Group($"p:{id}").SendAsync("VERDICT", aid, "FAILED", $"Evaluator connection error: {ex.Message}", 0.0);
                await hub.Clients.Group("admins").SendAsync("VERDICT", id, aid, "FAILED", $"Evaluator error: {ex.Message}", 0.0);
            }
            await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
        }
    });

    return Results.Accepted(value: new { submissionId = sid, status = "evaluating" });
});

var handleTelemetry = async (string id, TelemetryRequest req, HttpRequest httpReq, Database db, IHubContext<BreachHub> hub) =>
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
    else if (req.Type == "CLI_METRICS" && req.Payload.HasValue)
    {
        try
        {
            var pRoot = req.Payload.Value;
            var promptCount = pRoot.TryGetProperty("prompt_count", out var pc) ? pc.GetInt32() : 0;
            var tokenUsage = pRoot.TryGetProperty("token_usage", out var tu) ? tu.GetInt32() : 0;
            var cadence = pRoot.TryGetProperty("cadence", out var cd) ? cd.GetDouble() : 0.0;

            using var c = db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO telemetry_latest (participant_id, prompt_count, token_usage, cadence, updated_at)
                                VALUES ($p, $pc, $tu, $cd, $t)
                                ON CONFLICT(participant_id) DO UPDATE SET
                                prompt_count = excluded.prompt_count,
                                token_usage = excluded.token_usage,
                                cadence = excluded.cadence,
                                updated_at = excluded.updated_at";
            cmd.Parameters.AddWithValue("$p", id);
            cmd.Parameters.AddWithValue("$pc", promptCount);
            cmd.Parameters.AddWithValue("$tu", tokenUsage);
            cmd.Parameters.AddWithValue("$cd", cadence);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
            await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
        }
        catch { }
    }
    else if (req.Type == "TOUCH_PENALTY")
    {
        try
        {
            var pRoot = req.Payload.HasValue ? req.Payload.Value : default;
            var details = pRoot.ValueKind != JsonValueKind.Undefined && pRoot.TryGetProperty("violations", out var viols)
                ? viols.ToString()
                : "UNAUTHORIZED_FILE_MODIFICATION";

            using var c = db.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, centipoints, reason, run_id, created_at, final) VALUES ($p, 0, 'penalty', -3.0, -300, $r, 'touch-penalty', $t, 1)";
            cmd.Parameters.AddWithValue("$p", id);
            cmd.Parameters.AddWithValue("$r", "Out-of-scope modification: " + (details.Length > 80 ? details[..80] : details));
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();

            await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
            await hub.Clients.Group("projectors").SendAsync("SCORE_UPDATE", id, -3.0);
        }
        catch { }
    }
    return Results.Accepted();
};

app.MapPost("/api/kiosk/{id}/telemetry", handleTelemetry);
app.MapPost("/api/desk/{id}/telemetry", handleTelemetry);

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
        SELECT p.id, p.name, p.kiosk_id, COALESCE(NULLIF(p.desk_id, ''), p.kiosk_id) AS desk_id,
               p.status, p.created_at, COALESCE(p.ip_address, '127.0.0.1') as ip_address,
               COALESCE(p.password, '') as password, COALESCE(p.last_seen_at, p.created_at) as last_seen_at,
               COALESCE(SUM(l.points), 0) AS total_score,
               COUNT(DISTINCT CASE WHEN d.solved_at IS NOT NULL THEN d.attack_id END) AS solved_count,
               COUNT(DISTINCT d.attack_id) AS landed_count,
               COALESCE(q.agy_exhausted, 0) AS agy_exhausted,
               COALESCE(q.oc_prompts, 0) AS oc_prompts,
               (SELECT GROUP_CONCAT(attack_id) FROM attack_deliveries WHERE participant_id = p.id AND solved_at IS NULL) AS active_attacks,
               (SELECT MIN(landed_at) FROM attack_deliveries WHERE participant_id = p.id AND solved_at IS NULL) AS active_landed_at
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
            deskId = rd.GetString(3),
            status = rd.GetString(4),
            createdAt = rd.GetString(5),
            ipAddress = rd.GetString(6),
            password = rd.GetString(7),
            lastSeenAt = rd.GetString(8),
            totalScore = rd.GetDouble(9),
            solvedCount = rd.GetInt32(10),
            landedCount = rd.GetInt32(11),
            agyExhausted = rd.GetInt32(12) == 1,
            openCodePrompts = rd.GetInt32(13),
            activeAttacks = rd.IsDBNull(14) ? "" : rd.GetString(14),
            activeLandedAt = rd.IsDBNull(15) ? null : rd.GetString(15),
            gitBranch = $"participant/{rd.GetString(0)}"
        });
    }
    return Results.Ok(list);
});

app.MapGet("/api/admin/evaluations", (HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;

    using var c = db.Open();
    var allAttacks = orch.Attacks.OrderBy(a => a.Id).ToList();

    // 1. Fetch participants
    var participants = new List<dynamic>();
    using var pCmd = c.CreateCommand();
    pCmd.CommandText = """
        SELECT p.id, p.name, COALESCE(NULLIF(p.desk_id, ''), p.kiosk_id) AS desk_id, p.kiosk_id,
               COALESCE(p.ip_address, '127.0.0.1') as ip_address, COALESCE(p.password, '') as password,
               COALESCE(p.last_seen_at, p.created_at) as last_seen_at,
               COALESCE(SUM(l.points), 0) AS total_score
        FROM participants p
        LEFT JOIN score_ledger l ON l.participant_id = p.id
        GROUP BY p.id
        ORDER BY desk_id ASC
        """;
    using var pRd = pCmd.ExecuteReader();
    while (pRd.Read())
    {
        participants.Add(new
        {
            id = pRd.GetString(0),
            name = pRd.GetString(1),
            deskId = pRd.GetString(2),
            kioskId = pRd.GetString(3),
            ipAddress = pRd.GetString(4),
            password = pRd.GetString(5),
            lastSeenAt = pRd.GetString(6),
            totalScore = pRd.GetDouble(7)
        });
    }

    // 2. Fetch attack deliveries (landed / solved)
    var deliveries = new Dictionary<(string ParticipantId, int AttackId), (string? LandedAt, string? SolvedAt)>();
    using var dCmd = c.CreateCommand();
    dCmd.CommandText = "SELECT participant_id, attack_id, landed_at, solved_at FROM attack_deliveries";
    using var dRd = dCmd.ExecuteReader();
    while (dRd.Read())
    {
        deliveries[(dRd.GetString(0), dRd.GetInt32(1))] = (
            dRd.IsDBNull(2) ? null : dRd.GetString(2),
            dRd.IsDBNull(3) ? null : dRd.GetString(3)
        );
    }

    // 3. Fetch submissions & eval results
    var submissions = new List<dynamic>();
    using var sCmd = c.CreateCommand();
    sCmd.CommandText = """
        SELECT s.id, s.participant_id, e.attack_id, e.status, e.reason, s.created_at
        FROM submissions s
        JOIN eval_results e ON e.submission_id = s.id
        ORDER BY s.created_at DESC
        """;
    using var sRd = sCmd.ExecuteReader();
    while (sRd.Read())
    {
        submissions.Add(new
        {
            submissionId = sRd.GetString(0),
            participantId = sRd.GetString(1),
            attackId = sRd.GetInt32(2),
            status = sRd.GetString(3),
            reason = sRd.GetString(4),
            createdAt = sRd.GetString(5)
        });
    }

    // 4. Fetch local test penalties
    var penalties = new Dictionary<string, int>();
    using var penCmd = c.CreateCommand();
    penCmd.CommandText = "SELECT participant_id, COUNT(*) FROM score_ledger WHERE component = 'penalty' GROUP BY participant_id";
    using var penRd = penCmd.ExecuteReader();
    while (penRd.Read())
    {
        penalties[penRd.GetString(0)] = penRd.GetInt32(1);
    }

    // Build comprehensive response
    var evalList = participants.Select(p =>
    {
        string pid = p.id;
        var pSubs = submissions.Where(s => s.participantId == pid).ToList();

        var attackMetrics = allAttacks.Select(att =>
        {
            var key = (pid, att.Id);
            bool wasDelivered = deliveries.ContainsKey(key);
            var del = wasDelivered ? deliveries[key] : (LandedAt: (string?)null, SolvedAt: (string?)null);
            bool isSolved = del.SolvedAt != null;
            var attSubs = pSubs.Where(s => s.attackId == att.Id).ToList();

            string statusBadge = "PENDING";
            if (isSolved) statusBadge = "PASSED";
            else if (wasDelivered && attSubs.Any(s => s.status == "FAILED")) statusBadge = "FAILED";
            else if (wasDelivered) statusBadge = "IN_PROGRESS";

            return new
            {
                attackId = att.Id,
                title = att.Title,
                band = att.Band,
                status = statusBadge,
                landedAt = del.LandedAt,
                solvedAt = del.SolvedAt,
                attempts = attSubs.Count,
                lastReason = attSubs.FirstOrDefault()?.reason ?? (isSolved ? "Vulnerability neutralized (+points)" : (wasDelivered ? "Breach active (unfixed)" : "Not dispatched yet"))
            };
        }).ToList();

        int passedCount = attackMetrics.Count(a => a.status == "PASSED");
        int failedCount = attackMetrics.Count(a => a.status == "FAILED");

        return new
        {
            participantId = pid,
            deskId = p.deskId,
            name = p.name,
            kioskId = p.kioskId,
            ipAddress = p.ipAddress,
            password = p.password,
            lastSeenAt = p.lastSeenAt,
            totalScore = p.totalScore,
            passedAttacks = passedCount,
            failedAttacks = failedCount,
            localPenalties = penalties.GetValueOrDefault(pid, 0),
            attacks = attackMetrics,
            recentSubmissions = pSubs.Take(5).ToList()
        };
    });

    return Results.Ok(new
    {
        attacks = allAttacks.Select(a => new { id = a.Id, title = a.Title, band = a.Band }),
        evaluations = evalList,
        submissions = submissions.Take(50).ToList()
    });
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
    db.DisqualifyParticipant(id);
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

// RogueAgentOrchestrator Admin and Hint endpoints
app.MapGet("/api/event/status", (EventState s) => Results.Ok(new { frozen = s.Frozen, paused = s.Paused }));

app.MapGet("/api/admin/event/status", (HttpRequest r, Database db, EventState s) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    return Results.Ok(new { frozen = s.Frozen, paused = s.Paused });
});

app.MapPost("/api/admin/event/freeze", async (HttpRequest r, Database db, EventState s, IHubContext<BreachHub> hub) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    s.SetFreeze(true);
    db.Log("EVENT_FROZEN", payloadJson: "{\"frozen\":true}");
    await hub.Clients.All.SendAsync(HubEvents.ScoreboardFrozen, new { frozen = true });
    await hub.Clients.All.SendAsync(HubEvents.EventFrozen, new { frozen = true });
    return Results.Ok(new { frozen = true });
});

app.MapPost("/api/admin/event/unfreeze", async (HttpRequest r, Database db, EventState s, IHubContext<BreachHub> hub) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    s.SetFreeze(false);
    db.Log("EVENT_UNFROZEN", payloadJson: "{\"frozen\":false}");
    await hub.Clients.All.SendAsync(HubEvents.ScoreboardUnfrozen, new { frozen = false });
    await hub.Clients.All.SendAsync(HubEvents.EventUnfrozen, new { frozen = false });
    return Results.Ok(new { frozen = false });
});

app.MapGet("/api/admin/attacks", (HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var list = orch.Attacks.Select(a => new
    {
        id           = a.Id,
        title        = a.Title,
        codename     = a.Codename,
        band         = a.Band,
        sequenceSlot = a.SequenceSlot,
        mode         = a.Dispatch.Mode,
        scheduled    = a.Dispatch.Time,
        state        = db.GetDispatchState(a.Id).ToString()
    });
    return Results.Ok(list);
});

app.MapGet("/api/admin/attacks/{id:int}/state", (int id, HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var a = orch.Get(id);
    return Results.Ok(new
    {
        id       = a.Id,
        title    = a.Title,
        state    = db.GetDispatchState(id).ToString(),
        imminent = a.Dispatch.ImminentSeconds,
        scheduled = a.Dispatch.Time
    });
});

app.MapPost("/api/admin/attacks/{id:int}/canary", async (int id, HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    try
    {
        await orch.FireToCanaryAsync(id);
        return Results.Ok(new { fired = true });
    }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

app.MapPost("/api/admin/attacks/{id:int}/confirm-canary", (int id, HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    try
    {
        orch.ConfirmCanary(id);
        return Results.Ok(new { confirmed = true });
    }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

app.MapPost("/api/admin/attacks/{id:int}/fire-arena", async (int id, HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    try
    {
        var landAt = await orch.ConfirmAndFireArenaAsync(id);
        return Results.Ok(new { firedToArena = true, landAtUtc = landAt.ToString("O") });
    }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

app.MapPost("/api/admin/attacks/{id:int}/reset", async (int id, HttpRequest r, Database db, AttackDispatcher dispatcher, WorkspaceStore store) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var target = r.Query["target"].ToString();
    var ok = await dispatcher.ResetAsync(id, string.IsNullOrEmpty(target) ? null : target, store);
    return Results.Ok(new { success = ok, attackId = id });
});

app.MapPost("/api/admin/attacks/reset-all", async (HttpRequest r, Database db, AttackDispatcher dispatcher, WorkspaceStore store) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var target = r.Query["target"].ToString();
    var count = await dispatcher.ResetAllAsync(string.IsNullOrEmpty(target) ? null : target, store);
    return Results.Ok(new { success = true, resetCount = count });
});

app.MapPost("/api/admin/attacks/fire-random", async (HttpRequest r, Database db, AttackDispatcher dispatcher) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    var target = r.Query["target"].ToString();
    var chosen = await dispatcher.FireRandomAsync(string.IsNullOrEmpty(target) ? null : target);
    return Results.Ok(new { success = chosen.HasValue, attackId = chosen });
});

app.MapGet("/admin/attacks/dispatch-status", (HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    return Results.Ok(orch.GetDispatchOverview());
});

app.MapPost("/admin/attacks/{id:int}/fire-canary", async (int id, HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    try
    {
        await orch.FireToCanaryAsync(id);
        return Results.Ok(new { success = true, attackId = id });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/admin/attacks/{id:int}/confirm-canary", async (int id, HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    try
    {
        await orch.ConfirmCanaryAsync(id);
        return Results.Ok(new { success = true, attackId = id });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/admin/attacks/{id:int}/confirm-and-fire", async (int id, HttpRequest r, Database db, RogueAgentOrchestrator orch) =>
{
    var authErr = AuthorizeAdmin(r, db);
    if (authErr != null) return authErr;
    try
    {
        await orch.ConfirmAndFireArenaAsync(id);
        return Results.Ok(new { success = true, attackId = id, state = "ArenaImminent" });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/kiosk/{id}/disqualify", async (string id, HttpRequest httpReq, Database db, IHubContext<BreachHub> hub) =>
{
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }

    string reason = "APP_SWITCH_DETECTED";
    try
    {
        var body = await httpReq.ReadFromJsonAsync<DisqualifyRequest>();
        if (!string.IsNullOrWhiteSpace(body?.Reason)) reason = body.Reason;
    }
    catch { /* body is optional */ }

    db.DisqualifyParticipant(id, reason);
    await hub.Clients.Group("admins").SendAsync(HubEvents.KioskUpdated, id);
    return Results.Ok();
});

app.MapPost("/api/kiosk/{id}/hint/{attackId:int}/{level:int}", (string id, int attackId, int level, HttpRequest httpReq, Database db, RogueAgentOrchestrator orch) =>
{
    var token = httpReq.Headers["X-Kiosk-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token) || !db.ValidateDeskToken(token, id))
    {
        return Results.StatusCode(403);
    }

    var (success, msg, text, cost) = orch.RequestHint(id, attackId, level);
    if (!success)
    {
        return Results.BadRequest(new { error = msg });
    }
    return Results.Ok(new { text, cost, level, attackId });
});

app.Run();

public sealed record RegisterRequest(string Name, string KioskId, string? DeskId = null, string? Password = null, string? GitHubHandle = null, string? GoogleAccount = null);
public sealed record EnrollRequest(string Token, string KioskId, string Name);
public sealed record TelemetryRequest(string Type, JsonElement? Payload);
public sealed record BroadcastRequest(string Message);
public sealed record EvaluationResponse(string Submission_Id, string Evaluator, List<VerdictDto> Verdicts);
public sealed record VerdictDto(int Attack_Id, string Status, string Reason);
public sealed record DisqualifyRequest(string? Reason = null);
