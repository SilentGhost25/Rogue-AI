using System.Net;
using System.Text.Json;
using ControlPlane.Data;
using ControlPlane.Hubs;
using ControlPlane.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ControlPlane.Tests;

public class EventFreezeTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly Database _db;
    private readonly IConfiguration _cfg;

    public EventFreezeTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), "event_freeze_test_" + Guid.NewGuid().ToString("N") + ".db");
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Breach:DatabasePath", _testDbPath},
            {"Breach:AdminKey", "admin-key-123"}
        };
        _cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _db = new Database(_cfg);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_testDbPath)) File.Delete(_testDbPath); } catch { }
    }

    [Fact]
    public void EventState_freeze_and_unfreeze_lifecycle_and_idempotency()
    {
        var state = new EventState();
        Assert.False(state.Frozen);

        // 1. Freeze
        state.SetFreeze(true);
        Assert.True(state.Frozen);

        // 2. Double-freeze (idempotent, does not throw)
        state.SetFreeze(true);
        Assert.True(state.Frozen);

        // 3. Unfreeze
        state.SetFreeze(false);
        Assert.False(state.Frozen);

        // 4. Double-unfreeze (idempotent, does not throw)
        state.SetFreeze(false);
        Assert.False(state.Frozen);

        // 5. Toggle freeze
        state.ToggleFreeze();
        Assert.True(state.Frozen);
        state.ToggleFreeze();
        Assert.False(state.Frozen);
    }

    [Fact]
    public void Freeze_guard_rejects_with_409_and_FREEZE_code_when_frozen()
    {
        var state = new EventState();
        state.SetFreeze(true);

        // Simulate submission endpoint check
        IResult result;
        if (state.Frozen)
        {
            result = Results.Conflict(new { error = "Leaderboard is frozen. Submissions are closed.", code = "FREEZE" });
        }
        else
        {
            result = Results.Ok(new { submissionId = "s123" });
        }

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal((int)HttpStatusCode.Conflict, statusResult.StatusCode);

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var json = JsonSerializer.Serialize(valueResult.Value);
        Assert.Contains("FREEZE", json);
        Assert.Contains("Leaderboard is frozen", json);
    }

    [Fact]
    public void Freeze_guard_permits_submission_when_unfrozen()
    {
        var state = new EventState();
        state.SetFreeze(false);

        IResult result;
        if (state.Frozen)
        {
            result = Results.Conflict(new { error = "Leaderboard is frozen. Submissions are closed.", code = "FREEZE" });
        }
        else
        {
            result = Results.Ok(new { submissionId = "s123" });
        }

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal((int)HttpStatusCode.OK, statusResult.StatusCode);
    }

    [Fact]
    public void In_flight_submission_completes_in_database_after_freeze_occurs()
    {
        var state = new EventState();
        var participantId = "DESK-01";
        var sid = "s" + Guid.NewGuid().ToString("N")[..10];

        // 1. Submission posted before freeze
        using (var c = _db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO submissions (id, participant_id, created_at, status) VALUES ($i,$p,$t,'evaluating')";
            cmd.Parameters.AddWithValue("$i", sid);
            cmd.Parameters.AddWithValue("$p", participantId);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        // 2. Freeze occurs while evaluation is in-flight
        state.SetFreeze(true);
        _db.Log("EVENT_FROZEN", payloadJson: "{\"frozen\":true}");

        // 3. In-flight evaluation returns and writes verdict + score to ledger
        using (var c = _db.Open())
        {
            using (var updateCmd = c.CreateCommand())
            {
                updateCmd.CommandText = "UPDATE submissions SET status = 'solved' WHERE id = $i";
                updateCmd.Parameters.AddWithValue("$i", sid);
                updateCmd.ExecuteNonQuery();
            }

            using (var ledgerCmd = c.CreateCommand())
            {
                ledgerCmd.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, reason, run_id, created_at) VALUES ($p, 1, 'solve', 85.0, 'In-flight solve accepted', $sid, $t)";
                ledgerCmd.Parameters.AddWithValue("$p", participantId);
                ledgerCmd.Parameters.AddWithValue("$sid", sid);
                ledgerCmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
                ledgerCmd.ExecuteNonQuery();
            }
        }

        // 4. Verify in-flight submission was recorded in ledger and submissions table
        using (var c = _db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT status FROM submissions WHERE id = $i";
            cmd.Parameters.AddWithValue("$i", sid);
            var status = (string)cmd.ExecuteScalar()!;
            Assert.Equal("solved", status);
        }

        using (var c = _db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT points FROM score_ledger WHERE run_id = $i";
            cmd.Parameters.AddWithValue("$i", sid);
            var pts = Convert.ToDouble(cmd.ExecuteScalar());
            Assert.Equal(85.0, pts);
        }
    }

    [Fact]
    public void Freeze_and_unfreeze_log_to_event_log()
    {
        _db.Log("EVENT_FROZEN", payloadJson: "{\"frozen\":true}");
        _db.Log("EVENT_UNFROZEN", payloadJson: "{\"frozen\":false}");

        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT type FROM event_log WHERE type IN ('EVENT_FROZEN', 'EVENT_UNFROZEN') ORDER BY id ASC";
        using var r = cmd.ExecuteReader();

        Assert.True(r.Read());
        Assert.Equal("EVENT_FROZEN", r.GetString(0));

        Assert.True(r.Read());
        Assert.Equal("EVENT_UNFROZEN", r.GetString(0));
    }
}
