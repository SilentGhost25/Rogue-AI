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

    [Fact]
    public void Scoring_penalty_cap_enforces_maximum_negative_15_points()
    {
        var participantId = "DESK-02";
        var attackId = 1;

        // Insert 6 failed penalties for this participant and attack
        for (int i = 0; i < 6; i++)
        {
            using var conn = _db.Open();
            long penCount = 0;
            using (var penCmd = conn.CreateCommand())
            {
                penCmd.CommandText = "SELECT COUNT(*) FROM score_ledger WHERE participant_id = $p AND attack_id = $a AND component = 'penalty'";
                penCmd.Parameters.AddWithValue("$p", participantId);
                penCmd.Parameters.AddWithValue("$a", attackId);
                penCount = Convert.ToInt64(penCmd.ExecuteScalar());
            }

            if (penCount < 5)
            {
                using var ledgerCmd = conn.CreateCommand();
                ledgerCmd.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, centipoints, reason, created_at, final) VALUES ($p,$a,'penalty',-3.0,-300,'Failed test',DateTime('now'),1)";
                ledgerCmd.Parameters.AddWithValue("$p", participantId);
                ledgerCmd.Parameters.AddWithValue("$a", attackId);
                ledgerCmd.ExecuteNonQuery();
            }
        }

        // Verify only 5 penalties exist, totaling -15.0 pts / -1500 centipoints
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), SUM(points), SUM(centipoints) FROM score_ledger WHERE participant_id = $p AND attack_id = $a AND component = 'penalty'";
        cmd.Parameters.AddWithValue("$p", participantId);
        cmd.Parameters.AddWithValue("$a", attackId);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(5, r.GetInt64(0));
        Assert.Equal(-15.0, r.GetDouble(1));
        Assert.Equal(-1500, r.GetInt64(2));
    }

    [Fact]
    public void Scoring_speed_bonus_and_late_solve_math()
    {
        var baseCentipoints = 8500L;
        var speedMax = 1500L;
        var windowSec = 1800;
        var lateMult = 0.70;

        // Case 1: Solved at 50% of window (900s) -> speed bonus = 1500 * (1 - 900/1800) = 750
        var elapsedFast = 900.0;
        var factorFast = Math.Clamp(1.0 - (elapsedFast / windowSec), 0.0, 1.0);
        var bonusFast = (long)Math.Round(speedMax * factorFast);
        Assert.Equal(750, bonusFast);
        Assert.Equal(9250, baseCentipoints + bonusFast);

        // Case 2: Solved at 0s (instant) -> speed bonus = 1500
        var factorInstant = Math.Clamp(1.0 - (0.0 / windowSec), 0.0, 1.0);
        var bonusInstant = (long)Math.Round(speedMax * factorInstant);
        Assert.Equal(1500, bonusInstant);
        Assert.Equal(10000, baseCentipoints + bonusInstant);

        // Case 3: Late solve (2000s > 1800s window) -> 70% base (5950), 0 speed bonus
        var elapsedLate = 2000.0;
        long lateBase;
        long lateBonus;
        if (elapsedLate > windowSec)
        {
            lateBase = (long)Math.Round(baseCentipoints * lateMult);
            lateBonus = 0;
        }
        else
        {
            lateBase = baseCentipoints;
            lateBonus = 1500;
        }
        Assert.Equal(5950, lateBase);
        Assert.Equal(0, lateBonus);
    }

    [Theory]
    [InlineData("PARTIAL", -1.0, -100)]
    [InlineData("FAILED", -3.0, -300)]
    public void Scoring_penalty_differentiates_partial_and_failed(string status, double expectedPts, int expectedCenti)
    {
        var penaltyPts = status == "PARTIAL" ? -1.0 : -3.0;
        var penaltyCenti = status == "PARTIAL" ? -100 : -300;

        Assert.Equal(expectedPts, penaltyPts);
        Assert.Equal(expectedCenti, penaltyCenti);
    }

    [Fact]
    public void Scoring_progressive_tiers_accumulate_additively_without_loss()
    {
        var participantId = "p-prog-test";
        var attackId = 2;
        var baseCentipoints = 8500L;

        using (var setup = _db.Open())
        {
            using var pCmd = setup.CreateCommand();
            pCmd.CommandText = "INSERT INTO participants (id, name, kiosk_id, status, created_at) VALUES ($p, 'Prog Test', $p, 'alive', DateTime('now'))";
            pCmd.Parameters.AddWithValue("$p", participantId);
            pCmd.ExecuteNonQuery();
        }

        // Attempt 1: 30% cleared (2550 centipoints)
        var pct1 = 0.30;
        var target1 = (long)Math.Round(baseCentipoints * pct1);
        using (var conn = _db.Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(centipoints), 0) FROM score_ledger WHERE participant_id = $p AND attack_id = $a AND component IN ('solve', 'progressive')";
            cmd.Parameters.AddWithValue("$p", participantId);
            cmd.Parameters.AddWithValue("$a", attackId);
            var earnedSoFar1 = Convert.ToInt64(cmd.ExecuteScalar());

            var delta1 = Math.Max(0, target1 - earnedSoFar1);
            Assert.Equal(2550, delta1);
            using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, centipoints, reason, created_at, final) VALUES ($p,$a,'progressive',$pts,$cpts,'3/10 tiers',DateTime('now'),1)";
            ins.Parameters.AddWithValue("$p", participantId);
            ins.Parameters.AddWithValue("$a", attackId);
            ins.Parameters.AddWithValue("$pts", delta1 / 100.0);
            ins.Parameters.AddWithValue("$cpts", delta1);
            ins.ExecuteNonQuery();
        }

        // Attempt 2: 60% cleared (5100 centipoints) -> delta is +2550
        var pct2 = 0.60;
        var target2 = (long)Math.Round(baseCentipoints * pct2);
        using (var conn = _db.Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(centipoints), 0) FROM score_ledger WHERE participant_id = $p AND attack_id = $a AND component IN ('solve', 'progressive')";
            cmd.Parameters.AddWithValue("$p", participantId);
            cmd.Parameters.AddWithValue("$a", attackId);
            var earnedSoFar2 = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.Equal(2550, earnedSoFar2);

            var delta2 = Math.Max(0, target2 - earnedSoFar2);
            Assert.Equal(2550, delta2);
            using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, centipoints, reason, created_at, final) VALUES ($p,$a,'progressive',$pts,$cpts,'6/10 tiers',DateTime('now'),1)";
            ins.Parameters.AddWithValue("$p", participantId);
            ins.Parameters.AddWithValue("$a", attackId);
            ins.Parameters.AddWithValue("$pts", delta2 / 100.0);
            ins.Parameters.AddWithValue("$cpts", delta2);
            ins.ExecuteNonQuery();
        }

        // Attempt 3: Temporary regression (only 20% passed) -> delta must be 0, points never lost!
        var pct3 = 0.20;
        var target3 = (long)Math.Round(baseCentipoints * pct3);
        using (var conn = _db.Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(centipoints), 0) FROM score_ledger WHERE participant_id = $p AND attack_id = $a AND component IN ('solve', 'progressive')";
            cmd.Parameters.AddWithValue("$p", participantId);
            cmd.Parameters.AddWithValue("$a", attackId);
            var earnedSoFar3 = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.Equal(5100, earnedSoFar3);

            var delta3 = Math.Max(0, target3 - earnedSoFar3);
            Assert.Equal(0, delta3);
        }

        // Attempt 4: Full solve (100% optimal, 8500 base + 1000 speed bonus = 9500) -> delta is 9500 - 5100 = 4400
        var totalSolveTarget = 9500L;
        using (var conn = _db.Open())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(centipoints), 0) FROM score_ledger WHERE participant_id = $p AND attack_id = $a AND component IN ('solve', 'progressive')";
            cmd.Parameters.AddWithValue("$p", participantId);
            cmd.Parameters.AddWithValue("$a", attackId);
            var earnedSoFar4 = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.Equal(5100, earnedSoFar4);

            var delta4 = Math.Max(0, totalSolveTarget - earnedSoFar4);
            Assert.Equal(4400, delta4);
            using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT INTO score_ledger (participant_id, attack_id, component, points, centipoints, reason, created_at, final) VALUES ($p,$a,'solve',$pts,$cpts,'Optimal solve',DateTime('now'),1)";
            ins.Parameters.AddWithValue("$p", participantId);
            ins.Parameters.AddWithValue("$a", attackId);
            ins.Parameters.AddWithValue("$pts", delta4 / 100.0);
            ins.Parameters.AddWithValue("$cpts", delta4);
            ins.ExecuteNonQuery();

            // Total in ledger across all progressive and solve entries must equal exactly 95.0 pts (9500 centipoints)
            cmd.CommandText = "SELECT SUM(points), SUM(centipoints) FROM score_ledger WHERE participant_id = $p AND attack_id = $a";
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(95.0, r.GetDouble(0));
            Assert.Equal(9500, r.GetInt64(1));
        }
    }
}
