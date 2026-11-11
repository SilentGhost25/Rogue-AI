using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ControlPlane.Data;
using ControlPlane.Hubs;
using ControlPlane.Models;
using ControlPlane.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ControlPlane.Tests;

public class RogueAgentOrchestratorTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly Database _db;
    private readonly string _attacksDir;

    public RogueAgentOrchestratorTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), "orch_test_" + Guid.NewGuid().ToString("N") + ".db");
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Breach:DatabasePath", _testDbPath}
        };
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _db = new Database(config);

        _attacksDir = Path.Combine(Path.GetTempPath(), "attacks_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_attacksDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_testDbPath)) File.Delete(_testDbPath); } catch { }
        try { if (Directory.Exists(_attacksDir)) Directory.Delete(_attacksDir, true); } catch { }
    }

    private void CreateTestAttack(int id, string title, string mode = "scheduled", string time = "10:00:00")
    {
        var dir = Path.Combine(_attacksDir, $"{id:D2}-test");
        Directory.CreateDirectory(dir);
        var payloadDir = Path.Combine(dir, "payload");
        Directory.CreateDirectory(payloadDir);
        var testsDir = Path.Combine(dir, "tests");
        Directory.CreateDirectory(testsDir);
        File.WriteAllText(Path.Combine(payloadDir, "payload.txt"), "test payload");

        var yaml = $"""
            id: {id}
            title: "{title}"
            codename: "TEST_CODENAME_{id}"
            band: 1
            dispatch:
              mode: {mode}
              time: "{time}"
              imminent_seconds: 3
            payload:
              path: "payload/"
              format: full
            scoring:
              base_points: 8500
            tests:
              path: "tests/"
              tiers:
                - id: tier1
                  weight: 50
                - id: tier2
                  weight: 50
            hints:
              path: "hints.yaml"
              tiers:
                - level: 1
                  cost_centipoints: 200
                  text: "First hint text"
                - level: 2
                  cost_centipoints: 500
                  text: "Second hint text"
            narrative:
              landed: "Landed narrative"
              broadcast_title: "TEST BROADCAST"
            """;
        File.WriteAllText(Path.Combine(dir, "manifest.yaml"), yaml);
    }

    private RogueAgentOrchestrator CreateOrchestrator()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Breach:DatabasePath", _testDbPath},
                {"RogueAgent:AttacksRoot", _attacksDir}
            })
            .Build();

        var hub = new DummyHubContext();
        var orch = new RogueAgentOrchestrator(hub, _db, new SystemClock(), NullLogger<RogueAgentOrchestrator>.Instance, config);
        return orch;
    }

    [Fact]
    public void Manifest_loads_and_validates_successfully()
    {
        CreateTestAttack(0, "Reflected XSS", time: "09:00:00");
        CreateTestAttack(1, "Stored XSS", time: "10:00:00");

        var orch = CreateOrchestrator();
        Assert.Equal(2, orch.Attacks.Count);
        Assert.Equal("Reflected XSS", orch.Attacks[0].Title);
        Assert.Equal(DispatchState.Pending, orch.GetDispatchOverview()[0].State);
    }

    [Fact]
    public void Real_attacks_manifests_all_load_and_validate()
    {
        var cand = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../attacks")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../attacks")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "attacks"))
        };
        var root = cand.FirstOrDefault(Directory.Exists);
        Assert.NotNull(root);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Breach:DatabasePath", _testDbPath},
                {"RogueAgent:AttacksRoot", root}
            })
            .Build();

        var hub = new DummyHubContext();
        var orch = new RogueAgentOrchestrator(hub, _db, new SystemClock(), NullLogger<RogueAgentOrchestrator>.Instance, config);
        Assert.Equal(12, orch.Attacks.Count);
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal(i, orch.Attacks[i].Id);
        }
        Assert.Equal("onboarding", orch.Get(0).SequenceSlot);
        Assert.Equal("1", orch.Get(1).SequenceSlot);
        Assert.Equal("2", orch.Get(4).SequenceSlot);
        Assert.Equal("3", orch.Get(5).SequenceSlot);
        Assert.Equal("4", orch.Get(2).SequenceSlot);
        Assert.Equal("5", orch.Get(9).SequenceSlot);
        Assert.Equal("6", orch.Get(10).SequenceSlot);
        Assert.Equal("7", orch.Get(7).SequenceSlot);
        Assert.Equal("8", orch.Get(11).SequenceSlot);
        Assert.Equal("reserve", orch.Get(3).SequenceSlot);
        Assert.Equal(5, orch.Get(11).Band);
    }

    [Fact]
    public async Task Canary_dispatch_lifecycle_transitions_correctly()
    {
        CreateTestAttack(0, "Canary Test Attack");
        var orch = CreateOrchestrator();

        // 1. Initial State -> Pending
        Assert.Equal(DispatchState.Pending, _db.GetDispatchState(0));

        // 2. Fire Canary
        await orch.FireToCanaryAsync(0);
        Assert.Equal(DispatchState.CanaryFired, _db.GetDispatchState(0));

        // 3. Confirm Canary
        orch.ConfirmCanary(0);
        Assert.Equal(DispatchState.CanaryConfirmed, _db.GetDispatchState(0));

        // 4. Fire to Arena
        await orch.FireToArenaAsync(0);
        Assert.Equal(DispatchState.ArenaImminent, _db.GetDispatchState(0));

        // Wait for imminent countdown to land (imminent_seconds = 3)
        await Task.Delay(3200);
        Assert.Equal(DispatchState.ArenaLanded, _db.GetDispatchState(0));

        // 5. Trying to fire again while ArenaLanded must throw InvalidOperationException (409 Conflict)
        await Assert.ThrowsAsync<InvalidOperationException>(() => orch.FireToArenaAsync(0));
    }

    [Fact]
    public async Task Firing_to_arena_without_canary_confirmation_throws_InvalidOperationException()
    {
        CreateTestAttack(0, "Unconfirmed Attack");
        var orch = CreateOrchestrator();

        // Currently in Pending state
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => orch.FireToArenaAsync(0));
        Assert.Contains("expected 'CanaryConfirmed'", ex.Message);
    }

    [Fact]
    public void Hint_ladder_enforces_ordering_and_deducts_centipoints()
    {
        CreateTestAttack(0, "Hint Test Attack");
        var orch = CreateOrchestrator();

        var pid = "p_test_defender_01";

        // 1. Trying to request Level 2 first must fail
        var (s2_fail, msg2, _, _) = orch.RequestHint(pid, 0, 2);
        Assert.False(s2_fail);
        Assert.Contains("Must request Hint Level 1", msg2);

        // 2. Request Level 1 succeeds
        var (s1, _, text1, cost1) = orch.RequestHint(pid, 0, 1);
        Assert.True(s1);
        Assert.Equal("First hint text", text1);
        Assert.Equal(200, cost1);

        // Check ledger for -200 centipoints
        using (var c = _db.Open())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT centipoints, points, reason FROM score_ledger WHERE participant_id = $p AND reason = 'HINT_L1'";
            cmd.Parameters.AddWithValue("$p", pid);
            using var rd = cmd.ExecuteReader();
            Assert.True(rd.Read());
            Assert.Equal(-200, rd.GetInt32(0));
            Assert.Equal(-2.0, rd.GetDouble(1));
        }

        // 3. Duplicate Level 1 request does not double-charge
        var (s1_dup, msg_dup, _, cost_dup) = orch.RequestHint(pid, 0, 1);
        Assert.True(s1_dup);
        Assert.Equal(0, cost_dup);
        Assert.Contains("already unlocked", msg_dup);

        // 4. Now Level 2 can be unlocked
        var (s2, _, text2, cost2) = orch.RequestHint(pid, 0, 2);
        Assert.True(s2);
        Assert.Equal("Second hint text", text2);
        Assert.Equal(500, cost2);
    }
    [Fact]
    public async Task Wiring_checklist_canary_and_arena_isolation_and_idempotency()
    {
        CreateTestAttack(0, "Attack 0", mode: "manual");
        var hub = new RecordingHubContext();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Breach:DatabasePath", _testDbPath},
                {"RogueAgent:AttacksRoot", _attacksDir}
            })
            .Build();

        var orch = new RogueAgentOrchestrator(hub, _db, new SystemClock(), NullLogger<RogueAgentOrchestrator>.Instance, config);
        await orch.StartAsync(CancellationToken.None);

        var pid = "p_participant_42";
        _db.Exec("INSERT INTO participants (id, name, kiosk_id, created_at) VALUES (@p, 'Defender', 'k42', @t)",
            ("@p", pid), ("@t", DateTime.UtcNow.ToString("O")));

        // Checklist #4: Canary Isolation
        await orch.FireToCanaryAsync(0);
        Assert.Contains(hub.SentMessages, m => m.Group == "canary" && m.Method == "ATTACK_LANDED");
        Assert.DoesNotContain(hub.SentMessages, m => m.Group == "arena");

        // Checklist #5: Delivery not preregistered before arena fire
        Assert.False(_db.IsAttackDeliveryPreRegistered(0, pid));

        // Canary confirm
        orch.ConfirmCanary(0);
        Assert.Equal(DispatchState.CanaryConfirmed, _db.GetDispatchState(0));

        // Checklist #3 & #6: Fire to Arena -> Broadcast to Arena and pre-register delivery
        var landAt = await orch.FireToArenaAsync(0);
        Assert.True(landAt > DateTime.UtcNow);
        Assert.Contains(hub.SentMessages, m => m.Group == "arena" && m.Method == "BREACH_IMMINENT");
        Assert.True(_db.IsAttackDeliveryPreRegistered(0, pid));

        // Checklist #7: Idempotency (calling fire-arena second time throws InvalidOperationException -> 409 Conflict)
        await Assert.ThrowsAsync<InvalidOperationException>(() => orch.FireToArenaAsync(0));

        // Checklist #9: Reconcile active breach
        var reconcile = _db.GetActiveAttackForReconcile();
        Assert.NotNull(reconcile);
        Assert.Equal(0, reconcile.Value.AttackId);
        Assert.Equal(DispatchState.ArenaImminent, reconcile.Value.State);
    }

    [Fact]
    public void Wiring_checklist_payload_budget_under_2mb_all_attacks()
    {
        var cand = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../attacks")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../attacks")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "attacks"))
        };
        var root = cand.FirstOrDefault(Directory.Exists);
        Assert.NotNull(root);

        for (int i = 0; i < 8; i++)
        {
            var attackDir = Directory.GetDirectories(root, $"{i:D2}-*").Single();
            var payloadDir = Path.Combine(attackDir, "payload");
            Assert.True(Directory.Exists(payloadDir));

            var bytes = new DirectoryInfo(payloadDir)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            Assert.True(bytes < 2 * 1024 * 1024, $"Attack #{i} payload exceeds 2MB budget: {bytes} bytes");
        }
    }

    [Fact]
    public void Attack_deliveries_has_unique_index()
    {
        var isUnique = _db.QueryScalar<int>(
            "SELECT \"unique\" FROM pragma_index_list('attack_deliveries') WHERE name = 'ux_attack_deliveries_attack_participant'");
        Assert.Equal(1, isUnique);
    }

    [Fact]
    public async Task Startup_reconciles_ArenaImminent_crash_recovery()
    {
        CreateTestAttack(0, "Attack 0");
        var hub = new RecordingHubContext();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Breach:DatabasePath", _testDbPath},
                {"RogueAgent:AttacksRoot", _attacksDir}
            })
            .Build();

        // Simulate crash state: attack 0 was in ArenaImminent 20s ago with 10s countdown
        _db.EnsureDispatchRow(0);
        _db.TransitionDispatch(0, DispatchState.ArenaImminent);
        var pastImminent = DateTime.UtcNow.AddSeconds(-20).ToString("O");
        _db.Exec("UPDATE attack_dispatch SET arena_imminent_at = @t WHERE attack_id = 0", ("@t", pastImminent));
        _db.Exec("INSERT OR REPLACE INTO attack_dispatch_runtime (attack_id, imminent_seconds) VALUES (0, 10)");
        _db.PreregisterAttackDeliveries(0, new[] { "p1", "p2" });

        var orch = new RogueAgentOrchestrator(hub, _db, new SystemClock(), NullLogger<RogueAgentOrchestrator>.Instance, config);
        await orch.StartAsync(CancellationToken.None);

        // Assert: state reconciled to ArenaLanded and deliveries marked landed
        Assert.Equal(DispatchState.ArenaLanded, _db.GetDispatchState(0));
        var unlanded = _db.QueryScalar<int>("SELECT COUNT(*) FROM attack_deliveries WHERE attack_id = 0 AND landed_at IS NULL");
        Assert.Equal(0, unlanded);
        Assert.Contains(hub.SentMessages, m => m.Group == "arena" && m.Method == "ATTACK_LANDED");
    }

    [Fact]
    public async Task Startup_reconciles_ArenaImminent_in_countdown_rearm()
    {
        CreateTestAttack(0, "Attack 0");
        var hub = new RecordingHubContext();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"Breach:DatabasePath", _testDbPath},
                {"RogueAgent:AttacksRoot", _attacksDir}
            })
            .Build();

        // Simulate crash state: attack 0 was in ArenaImminent 1s ago with 3s countdown (2s remaining in countdown)
        _db.EnsureDispatchRow(0);
        _db.TransitionDispatch(0, DispatchState.ArenaImminent);
        var pastImminent = DateTime.UtcNow.AddSeconds(-1).ToString("O");
        _db.Exec("UPDATE attack_dispatch SET arena_imminent_at = @t WHERE attack_id = 0", ("@t", pastImminent));
        _db.Exec("INSERT OR REPLACE INTO attack_dispatch_runtime (attack_id, imminent_seconds) VALUES (0, 3)");
        _db.PreregisterAttackDeliveries(0, new[] { "p1", "p2" });

        var orch = new RogueAgentOrchestrator(hub, _db, new SystemClock(), NullLogger<RogueAgentOrchestrator>.Instance, config);
        await orch.StartAsync(CancellationToken.None);

        // Immediately after start, it should still be ArenaImminent and re-armed
        Assert.Equal(DispatchState.ArenaImminent, _db.GetDispatchState(0));

        // Wait for landing timer to complete (remaining ~2s, wait 3.5s)
        await Task.Delay(3500);

        // Assert: state transitioned to ArenaLanded and ATTACK_LANDED broadcast to arena
        Assert.Equal(DispatchState.ArenaLanded, _db.GetDispatchState(0));
        Assert.Contains(hub.SentMessages, m => m.Group == "arena" && m.Method == "ATTACK_LANDED");
    }
}

public class RecordingHubContext : IHubContext<BreachHub>
{
    public RecordingHubClients RecordingClients { get; } = new();
    public IHubClients Clients => RecordingClients;
    public IGroupManager Groups { get; } = null!;
    public List<(string? Group, string Method, object?[] Args)> SentMessages => RecordingClients.SentMessages;
}

public class RecordingHubClients : IHubClients
{
    public List<(string? Group, string Method, object?[] Args)> SentMessages { get; } = new();
    public IClientProxy All => new RecordingClientProxy(null, SentMessages);
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy(null, SentMessages);
    public IClientProxy Client(string connectionId) => new RecordingClientProxy(null, SentMessages);
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new RecordingClientProxy(null, SentMessages);
    public IClientProxy Group(string groupName) => new RecordingClientProxy(groupName, SentMessages);
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => new RecordingClientProxy(string.Join(",", groupNames), SentMessages);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy(groupName, SentMessages);
    public IClientProxy User(string userId) => new RecordingClientProxy(null, SentMessages);
    public IClientProxy Users(IReadOnlyList<string> userIds) => new RecordingClientProxy(null, SentMessages);
}

public class RecordingClientProxy : IClientProxy
{
    private readonly string? _group;
    private readonly List<(string? Group, string Method, object?[] Args)> _sink;
    public RecordingClientProxy(string? group, List<(string? Group, string Method, object?[] Args)> sink)
    {
        _group = group;
        _sink = sink;
    }
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        _sink.Add((_group, method, args));
        return Task.CompletedTask;
    }
}

public class DummyHubContext : IHubContext<BreachHub>
{
    public IHubClients Clients { get; } = new DummyHubClients();
    public IGroupManager Groups { get; } = null!;
}

public class DummyHubClients : IHubClients
{
    public IClientProxy All { get; } = new DummyClientProxy();
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new DummyClientProxy();
    public IClientProxy Client(string connectionId) => new DummyClientProxy();
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new DummyClientProxy();
    public IClientProxy Group(string groupName) => new DummyClientProxy();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => new DummyClientProxy();
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new DummyClientProxy();
    public IClientProxy User(string userId) => new DummyClientProxy();
    public IClientProxy Users(IReadOnlyList<string> userIds) => new DummyClientProxy();
}

public class DummyClientProxy : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
