using ControlPlane.Data;
using ControlPlane.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ControlPlane.Tests;

public class SecurityHardeningTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly Database _db;

    public SecurityHardeningTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), "breach_sec_test_" + Guid.NewGuid().ToString("N") + ".db");
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
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
    }

    private (string token1, string token2) GetDeskTokens()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT token FROM desk_tokens WHERE desk_id IN ('DESK-01', 'DESK-02') ORDER BY desk_id ASC;";
        using var rd = cmd.ExecuteReader();
        rd.Read(); var t1 = rd.GetString(0);
        rd.Read(); var t2 = rd.GetString(0);
        return (t1, t2);
    }

    [Fact]
    public void Same_kiosk_re_enrolls_after_restart_and_recovers_identity()
    {
        var (tok1, _) = GetDeskTokens();

        // 1. Initial enrollment on Desk 1 laptop
        var (s1, m1, pid1, deskId1) = _db.EnrollDesk(tok1, "kiosk-laptop-desk01", "Alice");
        Assert.True(s1, m1);
        Assert.Equal("DESK-01", deskId1);
        Assert.NotNull(pid1);

        // 2. Kiosk crashes and reboots. LabKiosk starts up and sends enroll with the SAME kiosk-id and token.
        var (sResume, mResume, pidResume, deskIdResume) = _db.EnrollDesk(tok1, "kiosk-laptop-desk01", "Alice");
        Assert.True(sResume, "Same kiosk MUST be permitted to re-enroll after reboot without organizer intervention");
        Assert.Equal("Desk re-enrolled", mResume);
        Assert.Equal(pid1, pidResume);
        Assert.Equal(deskId1, deskIdResume);

        // 3. Confirm token validation still works seamlessly
        Assert.True(_db.ValidateDeskToken(tok1, pidResume!));
    }

    [Fact]
    public void Different_kiosk_with_same_token_is_rejected_as_spoof()
    {
        var (tok1, _) = GetDeskTokens();

        // 1. Legitimate participant enrolls Desk 1
        var (s1, _, pid1, _) = _db.EnrollDesk(tok1, "kiosk-laptop-desk01", "Alice");
        Assert.True(s1);

        // 2. Attacker / different laptop tries to enroll with Alice's token
        var (sEve, mEve, pidEve, deskIdEve) = _db.EnrollDesk(tok1, "kiosk-laptop-eve", "Eve");
        Assert.False(sEve, "Different laptop with already-enrolled token MUST be rejected");
        Assert.Contains("already in use", mEve);
        Assert.Null(pidEve);
        Assert.Null(deskIdEve);

        // 3. Alice's token still belongs strictly to Alice
        Assert.True(_db.ValidateDeskToken(tok1, pid1!));
    }

    [Fact]
    public void Cross_desk_token_spoofing_fails_validation()
    {
        var (tok1, tok2) = GetDeskTokens();
        var (_, _, pid1, _) = _db.EnrollDesk(tok1, "kiosk-01", "Alice");
        var (_, _, pid2, _) = _db.EnrollDesk(tok2, "kiosk-02", "Bob");

        // Alice cannot validate with Bob's token
        Assert.False(_db.ValidateDeskToken(tok2, pid1!));
        // Bob cannot validate with Alice's token
        Assert.False(_db.ValidateDeskToken(tok1, pid2!));
        // Empty/null token fails
        Assert.False(_db.ValidateDeskToken("", pid1!));
        Assert.False(_db.ValidateDeskToken("fake_token", pid1!));
    }

    [Fact]
    public void Backup_status_detects_fresh_and_stale_backups()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "backup_status_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Empty dir -> warning
            var stEmpty = BackupService.GetBackupStatus(tempDir, null);
            Assert.True(stEmpty.IsWarning);
            Assert.Equal(0, stEmpty.BackupCount);

            // Fresh file (< 10 min)
            var freshFile = Path.Combine(tempDir, BackupService.GenerateFileName(DateTime.UtcNow, false));
            File.WriteAllText(freshFile, "test");
            var stFresh = BackupService.GetBackupStatus(tempDir, null);
            Assert.False(stFresh.IsWarning);
            Assert.Equal(1, stFresh.BackupCount);
            Assert.True(stFresh.BackupAgeMinutes < 1.0);

            // Stale file (> 10 min)
            File.SetLastWriteTimeUtc(freshFile, DateTime.UtcNow.AddMinutes(-15));
            var stStale = BackupService.GetBackupStatus(tempDir, null);
            Assert.True(stStale.IsWarning);
            Assert.True(stStale.BackupAgeMinutes >= 14.0);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Hourly_snapshot_filenames_include_utc_z_indicator()
    {
        var dt = new DateTime(2026, 11, 13, 14, 30, 0, DateTimeKind.Utc);
        var rollingName = BackupService.GenerateFileName(dt, isHourly: false);
        var hourlyName = BackupService.GenerateFileName(dt, isHourly: true);

        Assert.Equal("breach_20261113_143000Z.db", rollingName);
        Assert.Equal("breach_20261113_143000Z_hourly.db", hourlyName);
        Assert.True(BackupService.IsHourlySnapshot(hourlyName));
        Assert.False(BackupService.IsHourlySnapshot(rollingName));
    }
}
