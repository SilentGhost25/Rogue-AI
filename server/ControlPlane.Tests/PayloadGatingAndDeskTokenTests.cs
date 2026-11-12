using ControlPlane.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ControlPlane.Tests;

public class PayloadGatingAndDeskTokenTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly Database _db;

    public PayloadGatingAndDeskTokenTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), "breach_token_test_" + Guid.NewGuid().ToString("N") + ".db");
        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Breach:DatabasePath", _testDbPath}
        };
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _db = new Database(config);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Ignore temp file cleanup errors on Windows
        }
    }

    private (string token1, string token2) GetFirstTwoDeskTokens()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT token, desk_id FROM desk_tokens WHERE desk_id IN ('DESK-01', 'DESK-02') ORDER BY desk_id ASC;";
        using var rd = cmd.ExecuteReader();
        rd.Read();
        var tok1 = rd.GetString(0);
        rd.Read();
        var tok2 = rd.GetString(0);
        return (tok1, tok2);
    }

    [Fact]
    public void Desk_tokens_are_preseeded_for_50_desks()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM desk_tokens;";
        var count = Convert.ToInt64(cmd.ExecuteScalar());
        Assert.Equal(50, count);
    }

    [Fact]
    public void Desk_enrollment_succeeds_and_binds_token_to_participant()
    {
        var (tok1, tok2) = GetFirstTwoDeskTokens();

        // 1. Enroll DESK-01
        var (s1, m1, pid1, deskId1) = _db.EnrollDesk(tok1, "kiosk-laptop-01", "Alice");
        Assert.True(s1, m1);
        Assert.Equal("DESK-01", deskId1);
        Assert.NotNull(pid1);

        // 2. Token validation works for authorized participant
        Assert.True(_db.ValidateDeskToken(tok1, pid1));

        // 3. Invalid token or cross-participant token fails validation
        Assert.False(_db.ValidateDeskToken("fake-token-123", pid1));
        Assert.False(_db.ValidateDeskToken(tok2, pid1)); // Desk 2 token used for Desk 1 participant

        // 4. Double enrollment with different kiosk fails
        var (sBad, mBad, _, _) = _db.EnrollDesk(tok1, "kiosk-laptop-rogue", "Eve");
        Assert.False(sBad);
        Assert.Contains("already in use", mBad);
    }

    [Fact]
    public void Payload_gating_blocks_pre_landing_requests_and_token_mismatches()
    {
        var (tok1, tok2) = GetFirstTwoDeskTokens();

        // Enroll Desk 1 and Desk 2
        var (_, _, pid1, _) = _db.EnrollDesk(tok1, "kiosk-01", "Desk 1 Runner");
        var (_, _, pid2, _) = _db.EnrollDesk(tok2, "kiosk-02", "Desk 2 Runner");
        Assert.NotNull(pid1);
        Assert.NotNull(pid2);

        // Test 1: From a kiosk with a valid token, request attack 3's payload BEFORE it lands -> must return 403
        var canAccessBeforeLanding = CheckPayloadAccess(pid1, attackId: 3, token: tok1);
        Assert.False(canAccessBeforeLanding, "Kiosk with valid token should NOT access Attack 3 payload before it lands!");

        // Test 2: Request attack 3 with ANOTHER desk's token -> must return 403
        var canAccessWithOtherDeskToken = CheckPayloadAccess(pid1, attackId: 3, token: tok2);
        Assert.False(canAccessWithOtherDeskToken, "Request with another desk's token must return 403!");

        // Test 3: Request with invalid/empty token -> must return 403
        Assert.False(CheckPayloadAccess(pid1, attackId: 3, token: ""));
        Assert.False(CheckPayloadAccess(pid1, attackId: 3, token: "invalid-token"));

        // Now simulate Attack 3 landing for DESK 1 ONLY
        using (var c = _db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO attack_deliveries (attack_id, participant_id, landed_at) VALUES (3, $p, $t);";
            cmd.Parameters.AddWithValue("$p", pid1);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        // Test 4: Desk 2 requests Attack 3 payload with Desk 2 valid token (Attack 3 has NOT landed for Desk 2) -> must return 403
        var desk2Access = CheckPayloadAccess(pid2, attackId: 3, token: tok2);
        Assert.False(desk2Access, "Desk 2 should NOT access Attack 3 payload because it has not landed for Desk 2!");

        // Test 5: Desk 1 requests Attack 3 payload with Desk 1 valid token AFTER landing -> ACCESS GRANTED!
        var desk1AccessAfterLanding = CheckPayloadAccess(pid1, attackId: 3, token: tok1);
        Assert.True(desk1AccessAfterLanding, "Desk 1 MUST access Attack 3 payload after it officially landed!");
    }

    private bool CheckPayloadAccess(string participantId, int attackId, string token)
    {
        // 1. Authenticate Desk Token
        if (string.IsNullOrWhiteSpace(token) || !_db.ValidateDeskToken(token, participantId))
        {
            return false; // 403
        }

        // 2. Check if attack officially landed for this participant
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT landed_at FROM attack_deliveries WHERE participant_id = $p AND attack_id = $a";
        cmd.Parameters.AddWithValue("$p", participantId);
        cmd.Parameters.AddWithValue("$a", attackId);
        var landedAt = cmd.ExecuteScalar();
        if (landedAt == null || landedAt is DBNull || string.IsNullOrEmpty(landedAt.ToString()))
        {
            return false; // 403
        }

        return true; // 200 OK
    }

    [Fact]
    public void Disqualify_updates_participant_status_and_logs_event()
    {
        var (tok1, tok2) = GetFirstTwoDeskTokens();
        var (ok, _, pid1, _) = _db.EnrollDesk(tok1, "kiosk-dq-01", "Bob");
        Assert.True(ok);
        Assert.NotNull(pid1);

        Assert.False(_db.ValidateDeskToken(tok2, pid1));
        Assert.True(_db.ValidateDeskToken(tok1, pid1));

        var updated = _db.DisqualifyParticipant(pid1, "APP_SWITCH_DETECTED");
        Assert.True(updated);

        using var c = _db.Open();
        using var stCmd = c.CreateCommand();
        stCmd.CommandText = "SELECT status FROM participants WHERE id = $p";
        stCmd.Parameters.AddWithValue("$p", pid1);
        Assert.Equal("disqualified", stCmd.ExecuteScalar()?.ToString());

        using var evCmd = c.CreateCommand();
        evCmd.CommandText = "SELECT payload_json FROM event_log WHERE type = 'DISQUALIFIED' AND participant_id = $p ORDER BY id DESC LIMIT 1";
        evCmd.Parameters.AddWithValue("$p", pid1);
        var payload = evCmd.ExecuteScalar()?.ToString() ?? "";
        Assert.Contains("APP_SWITCH_DETECTED", payload);
    }
}
