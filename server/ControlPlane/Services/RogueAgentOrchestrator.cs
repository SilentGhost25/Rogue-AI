using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ControlPlane.Data;
using ControlPlane.Hubs;
using ControlPlane.Models;

namespace ControlPlane.Services;

public sealed class RogueAgentOrchestrator : BackgroundService
{
    private readonly List<AttackManifest> _attacks;
    private readonly IHubContext<BreachHub> _hub;
    private readonly Database _db;
    private readonly IClock _clock;
    private readonly ILogger<RogueAgentOrchestrator> _log;
    private readonly string _attacksRoot;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> _armedLandings = new();

    public RogueAgentOrchestrator(
        IHubContext<BreachHub> hub,
        Database db,
        IClock clock,
        ILogger<RogueAgentOrchestrator> log,
        IConfiguration cfg)
    {
        _hub = hub;
        _db = db;
        _clock = clock;
        _log = log;

        var configured = cfg["RogueAgent:AttacksRoot"];
        _attacksRoot = ResolveAttacksRoot(configured, _log);
        _attacks = LoadAndValidateAll(_attacksRoot);
    }

    private static string ResolveAttacksRoot(string? configured, ILogger log)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "attacks"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "..", "attacks"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "attacks"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BreachKiosk", "host", "attacks"));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../attacks")));
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../attacks")));

        foreach (var cand in candidates)
        {
            try
            {
                if (Directory.Exists(cand))
                {
                    var files = Directory.GetFiles(cand, "manifest.yaml", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        log.LogInformation("Resolved attacks root to: {Path} ({Count} manifests found)", cand, files.Length);
                        return cand;
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Cannot read attacks from candidate {Path}: {Message}", cand, ex.Message);
            }
        }

        return configured ?? Path.Combine(Directory.GetCurrentDirectory(), "attacks");
    }

    public IReadOnlyList<AttackManifest> Attacks => _attacks;
    public AttackManifest Get(int id) => _attacks.Single(a => a.Id == id);

    private static List<AttackManifest> LoadAndValidateAll(string root)
    {
        var manifests = Directory.GetFiles(root, "manifest.yaml", SearchOption.AllDirectories)
                                 .Select(AttackManifest.LoadFromFile)
                                 .OrderBy(a => a.Id)
                                 .ToList();
        if (manifests.Count == 0)
            throw new InvalidOperationException("No attack manifests found — refusing to start.");
        var ids = manifests.Select(m => m.Id).ToList();
        if (ids.Distinct().Count() != ids.Count)
            throw new InvalidOperationException("Attack ids must be unique.");
        if (!ids.SequenceEqual(Enumerable.Range(0, ids.Count)))
            throw new InvalidOperationException($"Attack ids must be sequential 0..N — got {string.Join(",", ids)}");

        var activeSlots = manifests
            .Where(m => int.TryParse(m.SequenceSlot, out _))
            .Select(m => int.Parse(m.SequenceSlot))
            .ToList();
        if (activeSlots.Distinct().Count() != activeSlots.Count)
            throw new InvalidOperationException("Active sequence_slots must be unique.");

        var prevTime = TimeOnly.MinValue;
        foreach (var m in manifests)
        {
            var matchDirs = Directory.GetDirectories(root, $"{m.Id:D2}-*");
            var dir = matchDirs.Length > 0
                ? matchDirs[0]
                : Path.GetDirectoryName(Directory.GetFiles(root, "manifest.yaml", SearchOption.AllDirectories).First(f => AttackManifest.LoadFromFile(f).Id == m.Id))!;

            m.Validate(dir);
            var t = m.ScheduledTime;
            if (m.Dispatch.Mode == "scheduled" && t <= prevTime)
                throw new InvalidOperationException($"Attack #{m.Id}: scheduled time must strictly increase");
            prevTime = t;
        }
        return manifests;
    }

    // ---------- Startup ----------
    public override async Task StartAsync(CancellationToken ct)
    {
        foreach (var m in _attacks)
        {
            _db.EnsureDispatchRow(m.Id,
                m.Dispatch.Mode == "scheduled"
                    ? m.ScheduledTimeToday(DateTime.Now)
                    : (DateTime?)null);
            _db.Exec("""
                INSERT INTO attack_dispatch_runtime (attack_id, imminent_seconds) VALUES (@id, @s)
                ON CONFLICT(attack_id) DO UPDATE SET imminent_seconds = excluded.imminent_seconds
                """,
                ("@id", m.Id), ("@s", m.Dispatch.ImminentSeconds));
        }

        // Startup reconciliation for mid-countdown restart recovery
        foreach (var m in _attacks)
        {
            var state = _db.GetDispatchState(m.Id);
            if (state == DispatchState.ArenaImminent)
            {
                var imminentAtStr = _db.QueryScalar<string>(
                    "SELECT arena_imminent_at FROM attack_dispatch WHERE attack_id = @id",
                    ("@id", m.Id));
                if (DateTime.TryParse(imminentAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var imminentAt))
                {
                    var landAtUtc = imminentAt.ToUniversalTime().AddSeconds(m.Dispatch.ImminentSeconds);
                    if (_clock.UtcNow >= landAtUtc)
                    {
                        _log.LogWarning("Attack #{Id} was ArenaImminent with overdue land time {LandAt:O}. Marking landed immediately on recovery.", m.Id, landAtUtc);
                        _db.MarkAttackLanded(m.Id);
                        _db.TransitionDispatch(m.Id, DispatchState.ArenaLanded);
                        try
                        {
                            await _hub.Clients.Group("arena").SendAsync("ATTACK_LANDED", new
                            {
                                attackId = m.Id,
                                landedAtUtcIso = DateTime.UtcNow.ToString("O")
                            });
                            await _hub.Clients.Group("arena").SendAsync("ATTACK_LANDED", m.Id);
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Failed to broadcast ATTACK_LANDED during recovery for attack #{Id}", m.Id);
                        }
                    }
                    else
                    {
                        _log.LogInformation("Attack #{Id} was ArenaImminent with future land time {LandAt:O}. Re-arming landing timer.", m.Id, landAtUtc);
                        _ = LandAtAsync(m.Id, landAtUtc);
                    }
                }
            }
        }

        _log.LogInformation("RogueAgent ready: {Count} attacks loaded.", _attacks.Count);
        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Scheduled-mode poll. Manual-mode attacks are triggered via admin endpoints.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var m in _attacks.Where(a => a.Dispatch.Mode == "scheduled"))
                {
                    var state = _db.GetDispatchState(m.Id);
                    if (state == DispatchState.Pending && _clock.UtcNow >= m.ScheduledTimeToday(DateTime.Now))
                        await FireToCanaryAsync(m.Id);
                }
            }
            catch (Exception ex) { _log.LogError(ex, "Scheduler tick failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    // ---------- Admin-triggered state machine ----------

    public async Task FireToCanaryAsync(int attackId)
    {
        var attack = Get(attackId);
        var current = _db.GetDispatchState(attackId);
        if (current != DispatchState.Pending)
            throw new InvalidOperationException(
                $"Cannot fire attack #{attackId} to canary: current state is '{current}', expected 'Pending'.");

        _db.TransitionDispatch(attackId, DispatchState.CanaryFired);
        await _hub.Clients.Group("canary").SendAsync("ATTACK_LANDED", new
        {
            attackId,
            title = attack.Narrative.BroadcastTitle,
            codename = attack.Codename
        });
        _log.LogInformation("Attack #{Id} fired to canary.", attackId);
    }

    public void ConfirmCanary(int attackId)
    {
        var current = _db.GetDispatchState(attackId);
        if (current != DispatchState.CanaryFired)
            throw new InvalidOperationException(
                $"Cannot confirm canary for attack #{attackId}: current state is '{current}', expected 'CanaryFired'.");
        _db.TransitionDispatch(attackId, DispatchState.CanaryConfirmed);
    }

    public async Task<DateTime> FireToArenaAsync(int attackId)
    {
        var attack = Get(attackId);
        var current = _db.GetDispatchState(attackId);
        if (current != DispatchState.CanaryConfirmed)
        {
            throw new InvalidOperationException(
                $"Cannot fire attack #{attackId} to arena: current state is '{current}', expected 'CanaryConfirmed'.");
        }

        var now = _clock.UtcNow;
        var landAtUtc = now.AddSeconds(attack.Dispatch.ImminentSeconds);

        // 1. Transition + persist pre-registered deliveries so payload gate opens during window
        _db.TransitionDispatch(attackId, DispatchState.ArenaImminent);
        _db.PreregisterAttackDeliveries(attackId, _db.AllParticipantIds());

        // 2. Broadcast lightweight imminent message — server-authoritative landAtUtc
        var payload = new
        {
            attackId,
            imminentSeconds = attack.Dispatch.ImminentSeconds,
            landAtUtcIso    = landAtUtc.ToString("O"),
            title           = attack.Narrative.BroadcastTitle,
            codename        = attack.Codename
        };
        await _hub.Clients.Group("arena").SendAsync("BREACH_IMMINENT", payload);
        // Also send 3-arg overload for kiosks with primitive signatures
        await _hub.Clients.Group("arena").SendAsync("BREACH_IMMINENT", attackId, attack.Dispatch.ImminentSeconds, landAtUtc.ToString("O"));

        _log.LogInformation("Attack #{Id} imminent — landAtUtc={LandAt:O}", attackId, landAtUtc);

        // 3. Schedule landing (fire-and-forget; the timer owns the ARENA_LANDED transition)
        _ = LandAtAsync(attackId, landAtUtc);
        return landAtUtc;
    }

    private async Task LandAtAsync(int attackId, DateTime landAtUtc)
    {
        if (!_armedLandings.TryAdd(attackId, 0))
        {
            _log.LogWarning("Landing for attack #{Id} is already armed; skipping duplicate schedule.", attackId);
            return;
        }

        try
        {
            var delay = landAtUtc - _clock.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay);

            var current = _db.GetDispatchState(attackId);
            if (current != DispatchState.ArenaImminent)
            {
                _log.LogInformation("Attack #{Id} is no longer ArenaImminent (current state: {Current}); aborting landing timer.", attackId, current);
                return;
            }

            _db.MarkAttackLanded(attackId);
            _db.TransitionDispatch(attackId, DispatchState.ArenaLanded);
            await _hub.Clients.Group("arena").SendAsync("ATTACK_LANDED", new
            {
                attackId,
                landedAtUtcIso = DateTime.UtcNow.ToString("O")
            });
            await _hub.Clients.Group("arena").SendAsync("ATTACK_LANDED", attackId);
            _log.LogInformation("Attack #{Id} landed.", attackId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Landing failed for attack #{Id}", attackId);
            _db.TransitionDispatch(attackId, DispatchState.Failed, ex.Message);
        }
        finally
        {
            _armedLandings.TryRemove(attackId, out _);
        }
    }

    // Compatibility methods
    public Task ConfirmCanaryAsync(int attackId)
    {
        ConfirmCanary(attackId);
        return Task.CompletedTask;
    }

    public async Task<DateTime> ConfirmAndFireArenaAsync(int attackId)
    {
        var current = _db.GetDispatchState(attackId);
        if (current == DispatchState.Pending)
        {
            _db.TransitionDispatch(attackId, DispatchState.CanaryFired);
            _db.TransitionDispatch(attackId, DispatchState.CanaryConfirmed);
        }
        else if (current == DispatchState.CanaryFired)
        {
            _db.TransitionDispatch(attackId, DispatchState.CanaryConfirmed);
        }
        return await FireToArenaAsync(attackId);
    }

    public (bool Success, string Message, string? Text, int Cost) RequestHint(string participantId, int attackId, int level)
    {
        var attack = Get(attackId);
        var hint = attack.Hints.Tiers.SingleOrDefault(t => t.Level == level);
        if (hint == null) return (false, $"Hint level {level} not configured for Attack #{attackId}", null, 0);

        // Enforce sequential level request (Level 1 before Level 2, etc.)
        using (var c = _db.Open())
        {
            if (level > 1)
            {
                using var checkCmd = c.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM score_ledger WHERE participant_id = $p AND attack_id = $a AND reason = $r";
                checkCmd.Parameters.AddWithValue("$p", participantId);
                checkCmd.Parameters.AddWithValue("$a", attackId);
                checkCmd.Parameters.AddWithValue("$r", $"HINT_L{level - 1}");
                var prevCount = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (prevCount == 0)
                {
                    return (false, $"Must request Hint Level {level - 1} before unlocking Level {level}", null, 0);
                }
            }

            // Check if already requested this level
            using var dupCmd = c.CreateCommand();
            dupCmd.CommandText = "SELECT COUNT(*) FROM score_ledger WHERE participant_id = $p AND attack_id = $a AND reason = $r";
            dupCmd.Parameters.AddWithValue("$p", participantId);
            dupCmd.Parameters.AddWithValue("$a", attackId);
            dupCmd.Parameters.AddWithValue("$r", $"HINT_L{level}");
            if (Convert.ToInt64(dupCmd.ExecuteScalar()) > 0)
            {
                return (true, "Hint already unlocked", hint.Text, 0);
            }

            // Insert negative ledger entry
            using var insCmd = c.CreateCommand();
            insCmd.CommandText = """
                INSERT INTO score_ledger (participant_id, attack_id, component, points, centipoints, reason, created_at, final)
                VALUES ($p, $a, 'hint', $pts, $cpts, $r, $t, 1);
                """;
            insCmd.Parameters.AddWithValue("$p", participantId);
            insCmd.Parameters.AddWithValue("$a", attackId);
            insCmd.Parameters.AddWithValue("$pts", -(hint.CostCentipoints / 100.0));
            insCmd.Parameters.AddWithValue("$cpts", -hint.CostCentipoints);
            insCmd.Parameters.AddWithValue("$r", $"HINT_L{level}");
            insCmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            insCmd.ExecuteNonQuery();
        }

        _db.Log("HINT_REQUESTED", participantId, attackId, System.Text.Json.JsonSerializer.Serialize(new { level, cost = hint.CostCentipoints }));
        return (true, "Hint unlocked", hint.Text, hint.CostCentipoints);
    }

    public List<AttackDispatchStatus> GetDispatchOverview()
    {
        var list = new List<AttackDispatchStatus>();
        foreach (var a in _attacks)
        {
            var state = _db.GetDispatchState(a.Id);
            list.Add(new AttackDispatchStatus
            {
                AttackId = a.Id,
                Name = a.Title,
                Codename = a.Codename,
                State = state,
                ScheduledTime = a.Dispatch.Time,
                DispatchedAt = null
            });
        }
        return list;
    }
}
