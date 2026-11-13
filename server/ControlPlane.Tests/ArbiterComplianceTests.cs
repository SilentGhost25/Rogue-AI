using ControlPlane.Data;
using ControlPlane.Hubs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ControlPlane.Tests;

public class ArbiterComplianceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly Database _db;

    public ArbiterComplianceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), "arbiter_test_" + Guid.NewGuid().ToString("N") + ".db");
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
        catch { }
    }

    [Fact]
    public void Hub_events_contain_all_required_arbiter_wire_names()
    {
        Assert.Equal("ACCOUNT_SWITCH_REQUEST", HubEvents.AccountSwitchRequest);
        Assert.Equal("ACCOUNT_SWITCH_APPROVED", HubEvents.AccountSwitchApproved);
        Assert.Equal("ACCOUNT_SWITCH_DENIED", HubEvents.AccountSwitchDenied);
        Assert.Equal("PRO_ACCOUNT_FLAGGED", HubEvents.ProAccountFlagged);
        Assert.Equal("PARDON_GRANTED", HubEvents.PardonGranted);
        Assert.Equal("PRO_ACCOUNT_ALLOWED", HubEvents.ProAccountAllowed);
        Assert.Equal("DISQUALIFIED", HubEvents.Disqualified);
    }

    [Fact]
    public void Event_log_records_account_switch_and_compliance_events()
    {
        using (var c = _db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO participants (id, name, kiosk_id, created_at) VALUES ('P-TEST-01', 'Test Agent', 'KIOSK-01', datetime('now'))";
            cmd.ExecuteNonQuery();
        }

        _db.Log("ACCOUNT_SWITCH_REQUESTED", "P-TEST-01", null, "{\"email\":\"user@gmail.com\"}");
        _db.Log("ACCOUNT_SWITCH_APPROVED", "P-TEST-01", null, "{\"email\":\"user@gmail.com\"}");
        _db.Log("PRO_ACCOUNT_FLAGGED", "P-TEST-01", null, "{\"tier\":\"Google AI Pro\"}");
        _db.Log("PARTICIPANT_PARDONED", "P-TEST-01", null, "{\"status\":\"cleared\"}");

        var count = _db.QueryScalar<long>("SELECT COUNT(*) FROM event_log WHERE participant_id = 'P-TEST-01'");
        Assert.Equal(4, count);
    }

    [Fact]
    public void Disqualification_updates_participant_status_and_logs_event()
    {
        using (var c = _db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO participants (id, name, kiosk_id, created_at) VALUES ('P-TEST-02', 'Pro Violator', 'KIOSK-02', datetime('now'))";
            cmd.ExecuteNonQuery();
        }

        using (var c = _db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE participants SET status = 'disqualified' WHERE id = 'P-TEST-02'";
            cmd.ExecuteNonQuery();
        }

        _db.Log("DISQUALIFIED", "P-TEST-02");

        var status = _db.QueryScalar<string>("SELECT status FROM participants WHERE id = 'P-TEST-02'");
        Assert.Equal("disqualified", status);

        var logCount = _db.QueryScalar<long>("SELECT COUNT(*) FROM event_log WHERE type = 'DISQUALIFIED' AND participant_id = 'P-TEST-02'");
        Assert.Equal(1, logCount);
    }
}
