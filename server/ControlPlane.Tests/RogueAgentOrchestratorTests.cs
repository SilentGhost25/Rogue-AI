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
        var yaml = $"""
            id: {id}
            title: "{title}"
            codename: "TEST_CODENAME_{id}"
            band: 1
            dispatch:
              mode: {mode}
              time: "{time}"
              imminent_seconds: 1
            scoring:
              base_points: 8500
            tests:
              tiers:
                - id: tier1
                  weight: 50
                - id: tier2
                  weight: 50
            hints:
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
                {"Breach:DatabasePath", _testDbPath}
            })
            .Build();

        // Switch CurrentDirectory to include temp attacks folder
        var prevDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path.GetDirectoryName(_attacksDir)!);
        // Rename or link to attacks
        var symAttacks = Path.Combine(Directory.GetCurrentDirectory(), "attacks");
        if (Directory.Exists(symAttacks)) Directory.Delete(symAttacks, true);
        Directory.Move(_attacksDir, symAttacks);

        var hub = new DummyHubContext();
        var orch = new RogueAgentOrchestrator(hub, _db, config, NullLogger<RogueAgentOrchestrator>.Instance);

        // Restore
        Directory.Move(symAttacks, _attacksDir);
        Directory.SetCurrentDirectory(prevDir);

        return orch;
    }

    [Fact]
    public void Manifest_loads_and_validates_successfully()
    {
        CreateTestAttack(1, "Reflected XSS");
        CreateTestAttack(2, "Stored XSS");

        var orch = CreateOrchestrator();
        Assert.Equal(2, orch.Attacks.Count);
        Assert.Equal("Reflected XSS", orch.Attacks[0].Title);
        Assert.Equal(DispatchState.Pending, orch.GetDispatchOverview()[0].State);
    }

    [Fact]
    public async Task Canary_dispatch_lifecycle_transitions_correctly()
    {
        CreateTestAttack(1, "Canary Test Attack");
        var orch = CreateOrchestrator();

        // 1. Initial State -> Pending
        Assert.Equal(DispatchState.Pending.ToString(), _db.GetDispatchState(1));

        // 2. Fire Canary
        await orch.FireToCanaryAsync(1);
        Assert.Equal(DispatchState.CanaryFired.ToString(), _db.GetDispatchState(1));

        // 3. Confirm Canary
        await orch.ConfirmCanaryAsync(1);
        Assert.Equal(DispatchState.CanaryConfirmed.ToString(), _db.GetDispatchState(1));

        // 4. Fire to Arena
        await orch.ConfirmAndFireArenaAsync(1);
        Assert.Equal(DispatchState.ArenaImminent.ToString(), _db.GetDispatchState(1));

        // Wait for imminent countdown to land
        await Task.Delay(1200);
        Assert.Equal(DispatchState.ArenaLanded.ToString(), _db.GetDispatchState(1));
    }

    [Fact]
    public void Hint_ladder_enforces_ordering_and_deducts_centipoints()
    {
        CreateTestAttack(1, "Hint Test Attack");
        var orch = CreateOrchestrator();

        var pid = "p_test_defender_01";

        // 1. Trying to request Level 2 first must fail
        var (s2_fail, msg2, _, _) = orch.RequestHint(pid, 1, 2);
        Assert.False(s2_fail);
        Assert.Contains("Must request Hint Level 1", msg2);

        // 2. Request Level 1 succeeds
        var (s1, _, text1, cost1) = orch.RequestHint(pid, 1, 1);
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
        var (s1_dup, msg_dup, _, cost_dup) = orch.RequestHint(pid, 1, 1);
        Assert.True(s1_dup);
        Assert.Equal(0, cost_dup);
        Assert.Contains("already unlocked", msg_dup);

        // 4. Now Level 2 can be unlocked
        var (s2, _, text2, cost2) = orch.RequestHint(pid, 1, 2);
        Assert.True(s2);
        Assert.Equal("Second hint text", text2);
        Assert.Equal(500, cost2);
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
