using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using ControlPlane.Data;
using ControlPlane.Hubs;
using ControlPlane.Models;

namespace ControlPlane.Services;

public class RogueAgentOrchestrator : BackgroundService
{
    private readonly IHubContext<BreachHub> _hub;
    private readonly Database _db;
    private readonly IConfiguration _config;
    private readonly ILogger<RogueAgentOrchestrator> _logger;
    private readonly List<AttackManifest> _attacks = new();
    private readonly string _attacksRoot;

    public RogueAgentOrchestrator(
        IHubContext<BreachHub> hub,
        Database db,
        IConfiguration config,
        ILogger<RogueAgentOrchestrator> logger)
    {
        _hub = hub;
        _db = db;
        _config = config;
        _logger = logger;

        // Resolve attacks directory
        var root = AppContext.BaseDirectory;
        var cand = new[]
        {
            Path.Combine(root, "attacks"),
            Path.Combine(Directory.GetCurrentDirectory(), "attacks"),
            Path.GetFullPath(Path.Combine(root, "../../../../../attacks")),
            Path.GetFullPath(Path.Combine(root, "../../../../attacks"))
        };
        _attacksRoot = cand.FirstOrDefault(Directory.Exists) ?? Path.Combine(Directory.GetCurrentDirectory(), "attacks");

        LoadAndValidateManifests();
    }

    public IReadOnlyList<AttackManifest> Attacks => _attacks.AsReadOnly();

    private void LoadAndValidateManifests()
    {
        if (!Directory.Exists(_attacksRoot))
        {
            _logger.LogWarning("Attacks directory not found at {Path}", _attacksRoot);
            return;
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var manifestFiles = Directory.GetFiles(_attacksRoot, "manifest.yaml", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();

        foreach (var file in manifestFiles)
        {
            try
            {
                var yaml = File.ReadAllText(file);
                var manifest = deserializer.Deserialize<AttackManifest>(yaml);
                if (string.IsNullOrWhiteSpace(manifest.Title) && !string.IsNullOrWhiteSpace(manifest.Name))
                {
                    manifest.Title = manifest.Name;
                }
                _attacks.Add(manifest);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"FATAL: Failed to parse attack manifest at {file}: {ex.Message}", ex);
            }
        }

        ValidateManifests();

        // Seed dispatch state table
        foreach (var attack in _attacks)
        {
            var cur = _db.GetDispatchState(attack.Id);
            if (string.IsNullOrWhiteSpace(cur) || cur == "Pending")
            {
                _db.SetDispatchState(attack.Id, DispatchState.Pending.ToString());
            }
        }

        _logger.LogInformation("RogueAgentOrchestrator loaded and validated {Count} attack manifests.", _attacks.Count);
    }

    private void ValidateManifests()
    {
        // 1. Unique IDs
        var dupId = _attacks.GroupBy(a => a.Id).FirstOrDefault(g => g.Count() > 1);
        if (dupId != null)
        {
            throw new InvalidOperationException($"FATAL: Duplicate attack ID #{dupId.Key} detected in manifests.");
        }

        // 2. Validate Tier Weights
        foreach (var a in _attacks)
        {
            if (a.Tests.Tiers != null && a.Tests.Tiers.Count > 0)
            {
                var sum = a.Tests.Tiers.Sum(t => t.Weight);
                if (sum != 100)
                {
                    throw new InvalidOperationException($"FATAL: Attack #{a.Id} ({a.Title}) tier weights sum to {sum}, expected exactly 100.");
                }
            }
        }

        // 3. Validate Scheduled times if multiple scheduled
        TimeSpan lastTime = TimeSpan.MinValue;
        foreach (var a in _attacks.Where(a => a.Dispatch.Mode == "scheduled").OrderBy(a => a.Id))
        {
            if (TimeSpan.TryParse(a.Dispatch.Time, out var t))
            {
                if (t < lastTime)
                {
                    _logger.LogWarning("Attack #{Id} scheduled time ({Time}) is earlier than previous attack ({Last}).", a.Id, t, lastTime);
                }
                lastTime = t;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("RogueAgentOrchestrator background worker active.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow.TimeOfDay;
                foreach (var attack in _attacks.Where(a => a.Dispatch.Mode == "scheduled"))
                {
                    if (Enum.TryParse<DispatchState>(_db.GetDispatchState(attack.Id), out var state) && state == DispatchState.Pending)
                    {
                        if (TimeSpan.TryParse(attack.Dispatch.Time, out var sched) && now >= sched)
                        {
                            await FireToCanaryAsync(attack.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during RogueAgentOrchestrator scheduled poll.");
            }

            await Task.Delay(1000, ct);
        }
    }

    public async Task FireToCanaryAsync(int attackId)
    {
        var attack = _attacks.SingleOrDefault(a => a.Id == attackId);
        if (attack == null) throw new ArgumentException($"Attack #{attackId} not found");

        _db.SetDispatchState(attack.Id, DispatchState.CanaryFired.ToString());
        _db.Log("CANARY_FIRED", attackId: attack.Id, payloadJson: JsonSerializer.Serialize(new { attack.Title, attack.Codename }));

        await _hub.Clients.Group("canary").SendAsync("ATTACK_LANDED", new
        {
            attackId = attack.Id,
            title = attack.Title,
            codename = attack.Codename,
            landedAtUtc = DateTime.UtcNow
        });

        _logger.LogInformation("Attack #{Id} ({Title}) fired to Canary group.", attack.Id, attack.Title);
    }

    public Task ConfirmCanaryAsync(int attackId)
    {
        var attack = _attacks.SingleOrDefault(a => a.Id == attackId);
        if (attack == null) throw new ArgumentException($"Attack #{attackId} not found");

        _db.SetDispatchState(attack.Id, DispatchState.CanaryConfirmed.ToString());
        _db.Log("CANARY_CONFIRMED", attackId: attack.Id);
        _logger.LogInformation("Attack #{Id} Canary confirmed by organizer.", attack.Id);
        return Task.CompletedTask;
    }

    public async Task ConfirmAndFireArenaAsync(int attackId)
    {
        var attack = _attacks.SingleOrDefault(a => a.Id == attackId);
        if (attack == null) throw new ArgumentException($"Attack #{attackId} not found");

        var cur = _db.GetDispatchState(attackId);
        if (cur != DispatchState.CanaryConfirmed.ToString())
        {
            throw new InvalidOperationException($"Cannot fire attack #{attackId} to arena: current state is '{cur}', expected '{DispatchState.CanaryConfirmed}'.");
        }

        _db.SetDispatchState(attackId, DispatchState.ArenaImminent.ToString());

        var imminentAt = DateTime.UtcNow;
        var landAt = imminentAt.AddSeconds(attack.Dispatch.ImminentSeconds);

        // Pre-register attack deliveries for participants so they can pre-fetch payload during countdown
        using (var c = _db.Open())
        {
            using var pCmd = c.CreateCommand();
            pCmd.CommandText = "SELECT id FROM participants WHERE status != 'disqualified'";
            using var rd = pCmd.ExecuteReader();
            var pids = new List<string>();
            while (rd.Read()) pids.Add(rd.GetString(0));
            rd.Close();

            using var tx = c.BeginTransaction();
            foreach (var pid in pids)
            {
                using var ins = c.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO attack_deliveries (attack_id, participant_id, landed_at)
                    VALUES ($a, $p, NULL)
                    ON CONFLICT(attack_id, participant_id) DO NOTHING;
                    """;
                ins.Parameters.AddWithValue("$a", attackId);
                ins.Parameters.AddWithValue("$p", pid);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // 1. Lightweight broadcast FIRST with landAtUtc timestamp
        await _hub.Clients.Group("arena").SendAsync("BREACH_IMMINENT", attack.Id, attack.Dispatch.ImminentSeconds, landAt.ToString("O"));

        _db.Log("BREACH_IMMINENT", attackId: attack.Id, payloadJson: JsonSerializer.Serialize(new { landAtUtc = landAt }));
        _db.SetAttackDispatchedAt(attackId, imminentAt);

        // 2. Schedule actual landing when countdown expires
        _ = Task.Run(async () =>
        {
            var delay = landAt - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            var landedTimeStr = DateTime.UtcNow.ToString("O");
            using (var c = _db.Open())
            using (var upd = c.CreateCommand())
            {
                upd.CommandText = "UPDATE attack_deliveries SET landed_at = $t WHERE attack_id = $a AND landed_at IS NULL";
                upd.Parameters.AddWithValue("$t", landedTimeStr);
                upd.Parameters.AddWithValue("$a", attackId);
                upd.ExecuteNonQuery();
            }

            _db.SetDispatchState(attackId, DispatchState.ArenaLanded.ToString());
            _db.Log("ATTACK_LANDED", attackId: attack.Id);

            await _hub.Clients.Group("arena").SendAsync("ATTACK_LANDED", attack.Id);

            _logger.LogInformation("Attack #{Id} officially landed across arena.", attack.Id);
        });
    }

    public (bool Success, string Message, string? Text, int Cost) RequestHint(string participantId, int attackId, int level)
    {
        var attack = _attacks.SingleOrDefault(a => a.Id == attackId);
        if (attack == null) return (false, "Attack not found", null, 0);

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

        _db.Log("HINT_REQUESTED", participantId, attackId, JsonSerializer.Serialize(new { level, cost = hint.CostCentipoints }));
        return (true, "Hint unlocked", hint.Text, hint.CostCentipoints);
    }

    public List<AttackDispatchStatus> GetDispatchOverview()
    {
        var dbStates = _db.GetAllDispatchStates();
        var list = new List<AttackDispatchStatus>();

        foreach (var a in _attacks)
        {
            dbStates.TryGetValue(a.Id, out var stateInfo);
            Enum.TryParse<DispatchState>(stateInfo.State ?? "Pending", out var ds);

            list.Add(new AttackDispatchStatus
            {
                AttackId = a.Id,
                Name = a.Title,
                Codename = a.Codename,
                State = ds,
                ScheduledTime = a.Dispatch.Time,
                DispatchedAt = stateInfo.DispatchedAt
            });
        }

        return list;
    }
}
