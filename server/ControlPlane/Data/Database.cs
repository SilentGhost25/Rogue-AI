using Microsoft.Data.Sqlite;

namespace ControlPlane.Data;

/// <summary>SQLite access plus the append-only event log (plan section 9).</summary>
public partial class Database
{
    private readonly string _connectionString;

    public Database(IConfiguration config)
    {
        var path = config["Breach:DatabasePath"] ?? "data/breach.db";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = $"Data Source={path}";
        Migrate();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return c;
    }

    private void Migrate()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS participants (
              id TEXT PRIMARY KEY, name TEXT NOT NULL, kiosk_id TEXT NOT NULL UNIQUE,
              status TEXT NOT NULL DEFAULT 'alive', created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS attacks (
              id INTEGER PRIMARY KEY, name TEXT NOT NULL, fires_at TEXT NOT NULL, state TEXT NOT NULL DEFAULT 'scheduled');
            CREATE TABLE IF NOT EXISTS attack_deliveries (
              attack_id INTEGER NOT NULL, participant_id TEXT NOT NULL, landed_at TEXT, solved_at TEXT,
              PRIMARY KEY (attack_id, participant_id));
            CREATE TABLE IF NOT EXISTS submissions (
              id TEXT PRIMARY KEY, participant_id TEXT NOT NULL, created_at TEXT NOT NULL, status TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS eval_results (
              id INTEGER PRIMARY KEY AUTOINCREMENT, submission_id TEXT NOT NULL, attack_id INTEGER NOT NULL,
              status TEXT NOT NULL, reason TEXT NOT NULL, evaluator_version TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS benchmark_results (
              id INTEGER PRIMARY KEY AUTOINCREMENT, submission_id TEXT NOT NULL, attack_id INTEGER NOT NULL,
              metrics_json TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS telemetry_events (
              id INTEGER PRIMARY KEY AUTOINCREMENT, participant_id TEXT NOT NULL, type TEXT NOT NULL,
              payload_json TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS quota_state (
              participant_id TEXT PRIMARY KEY, agy_exhausted INTEGER NOT NULL DEFAULT 0,
              oc_cap_reached INTEGER NOT NULL DEFAULT 0, oc_prompts INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS score_ledger (
              id INTEGER PRIMARY KEY AUTOINCREMENT, participant_id TEXT NOT NULL, attack_id INTEGER,
              component TEXT NOT NULL, points REAL NOT NULL, reason TEXT NOT NULL, run_id TEXT,
              evaluator_version TEXT, final INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS admin_actions (
              id INTEGER PRIMARY KEY AUTOINCREMENT, action TEXT NOT NULL, detail TEXT, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS desk_tokens (
              token TEXT PRIMARY KEY, desk_id TEXT NOT NULL UNIQUE, participant_id TEXT, enrolled_at TEXT);
            CREATE TABLE IF NOT EXISTS attack_dispatch (
              attack_id INTEGER PRIMARY KEY, state TEXT NOT NULL DEFAULT 'Pending',
              canary_fired_at TEXT, canary_confirmed_at TEXT, arena_imminent_at TEXT,
              arena_landed_at TEXT, scheduled_for_at TEXT, failed_reason TEXT,
              dispatched_at TEXT, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS attack_dispatch_runtime (
              attack_id INTEGER PRIMARY KEY, imminent_seconds INTEGER NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_attack_deliveries_attack_participant
              ON attack_deliveries (attack_id, participant_id);
            CREATE TABLE IF NOT EXISTS event_log (
              id INTEGER PRIMARY KEY AUTOINCREMENT, ts TEXT NOT NULL, type TEXT NOT NULL,
              participant_id TEXT, attack_id INTEGER, payload_json TEXT NOT NULL DEFAULT '{}');
            -- Append-only: block edits and deletes at the database level.
            CREATE TRIGGER IF NOT EXISTS event_log_no_update BEFORE UPDATE ON event_log
              BEGIN SELECT RAISE(ABORT, 'event_log is append-only'); END;
            CREATE TRIGGER IF NOT EXISTS event_log_no_delete BEFORE DELETE ON event_log
              BEGIN SELECT RAISE(ABORT, 'event_log is append-only'); END;
            CREATE TRIGGER IF NOT EXISTS ledger_no_update BEFORE UPDATE ON score_ledger
              BEGIN SELECT RAISE(ABORT, 'score_ledger is append-only'); END;
            CREATE TRIGGER IF NOT EXISTS ledger_no_delete BEFORE DELETE ON score_ledger
              BEGIN SELECT RAISE(ABORT, 'score_ledger is append-only'); END;
            """;
        cmd.ExecuteNonQuery();

        EnsureColumn(c, "attack_dispatch", "canary_fired_at", "TEXT");
        EnsureColumn(c, "attack_dispatch", "canary_confirmed_at", "TEXT");
        EnsureColumn(c, "attack_dispatch", "arena_imminent_at", "TEXT");
        EnsureColumn(c, "attack_dispatch", "arena_landed_at", "TEXT");
        EnsureColumn(c, "attack_dispatch", "scheduled_for_at", "TEXT");
        EnsureColumn(c, "attack_dispatch", "failed_reason", "TEXT");
        EnsureColumn(c, "attack_dispatch", "dispatched_at", "TEXT");

        EnsureColumn(c, "score_ledger", "centipoints", "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(c, "participants", "desk_id", "TEXT");
        EnsureColumn(c, "participants", "password", "TEXT");
        EnsureColumn(c, "participants", "ip_address", "TEXT");
        EnsureColumn(c, "participants", "last_seen_at", "TEXT");
        EnsureColumn(c, "score_ledger", "seq", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(c, "score_ledger", "prev_hash", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(c, "score_ledger", "entry_hash", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(c, "score_ledger", "sig", "TEXT NOT NULL DEFAULT ''");

        SeedDefaultDeskTokens(c);
    }

    public void SetDispatchState(int attackId, string state)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO attack_dispatch (attack_id, state, updated_at) VALUES ($a, $s, $t)
            ON CONFLICT(attack_id) DO UPDATE SET state = $s, updated_at = $t;
            """;
        cmd.Parameters.AddWithValue("$a", attackId);
        cmd.Parameters.AddWithValue("$s", state);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SetAttackDispatchedAt(int attackId, DateTime time)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE attack_dispatch SET dispatched_at = $t WHERE attack_id = $a";
        cmd.Parameters.AddWithValue("$a", attackId);
        cmd.Parameters.AddWithValue("$t", time.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public DateTime? GetAttackDispatchedAt(int attackId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT dispatched_at FROM attack_dispatch WHERE attack_id = $a";
        cmd.Parameters.AddWithValue("$a", attackId);
        var val = cmd.ExecuteScalar();
        if (val != null && val is not DBNull && DateTime.TryParse(val.ToString(), out var dt))
        {
            return dt.ToUniversalTime();
        }
        return null;
    }

    public Dictionary<int, (string State, string? DispatchedAt)> GetAllDispatchStates()
    {
        var res = new Dictionary<int, (string State, string? DispatchedAt)>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT attack_id, state, dispatched_at FROM attack_dispatch";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var aid = rd.GetInt32(0);
            var state = rd.GetString(1);
            var disp = rd.IsDBNull(2) ? null : rd.GetString(2);
            res[aid] = (state, disp);
        }
        return res;
    }

    private static void EnsureColumn(SqliteConnection c, string table, string col, string type)
    {
        using var check = c.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var rd = check.ExecuteReader();
        while (rd.Read())
        {
            if (string.Equals(rd.GetString(1), col, StringComparison.OrdinalIgnoreCase)) return;
        }
        rd.Close();
        using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {col} {type};";
        alter.ExecuteNonQuery();
    }

    private static void SeedDefaultDeskTokens(SqliteConnection c)
    {
        using var countCmd = c.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM desk_tokens";
        var count = Convert.ToInt64(countCmd.ExecuteScalar());
        if (count > 0) return;

        // Seed 50 desks (DESK-01 to DESK-50) with deterministic tokens derived from salt
        using var tx = c.BeginTransaction();
        for (int i = 1; i <= 50; i++)
        {
            var deskId = $"DESK-{i:D2}";
            var raw = $"ROGUE_AI_2026_TOKEN_{deskId}_SALT_X912";
            var token = "dt_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO desk_tokens (token, desk_id) VALUES ($t, $d);";
            ins.Parameters.AddWithValue("$t", token);
            ins.Parameters.AddWithValue("$d", deskId);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public bool ValidateDeskToken(string? token, string participantId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(participantId)) return false;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM desk_tokens WHERE token = $tok AND participant_id = $pid";
        cmd.Parameters.AddWithValue("$tok", token);
        cmd.Parameters.AddWithValue("$pid", participantId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public (bool Success, string Message, string? ParticipantId, string? DeskId) EnrollDesk(string token, string kioskId, string participantName)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();

        using var findTok = c.CreateCommand();
        findTok.Transaction = tx;
        findTok.CommandText = "SELECT desk_id, participant_id FROM desk_tokens WHERE token = $t";
        findTok.Parameters.AddWithValue("$t", token);
        using var rd = findTok.ExecuteReader();
        if (!rd.Read())
        {
            return (false, "Invalid desk token", null, null);
        }
        var deskId = rd.GetString(0);
        var existingPid = rd.IsDBNull(1) ? null : rd.GetString(1);
        rd.Close();

        using var findK = c.CreateCommand();
        findK.Transaction = tx;
        findK.CommandText = "SELECT id FROM participants WHERE kiosk_id = $k";
        findK.Parameters.AddWithValue("$k", kioskId);
        var pByKiosk = findK.ExecuteScalar() as string;

        if (existingPid != null)
        {
            if (pByKiosk != null && pByKiosk == existingPid)
            {
                tx.Commit();
                return (true, "Desk re-enrolled", existingPid, deskId);
            }
            return (false, "Desk token already in use by another kiosk", null, null);
        }

        var pid = pByKiosk ?? ("p" + Guid.NewGuid().ToString("N")[..10]);
        if (pByKiosk == null)
        {
            using var insP = c.CreateCommand();
            insP.Transaction = tx;
            insP.CommandText = "INSERT INTO participants (id, name, kiosk_id, created_at) VALUES ($i,$n,$k,$t)";
            insP.Parameters.AddWithValue("$i", pid);
            insP.Parameters.AddWithValue("$n", participantName);
            insP.Parameters.AddWithValue("$k", kioskId);
            insP.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            insP.ExecuteNonQuery();
        }

        using var upd = c.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE desk_tokens SET participant_id = $p, enrolled_at = $t WHERE token = $tok";
        upd.Parameters.AddWithValue("$p", pid);
        upd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        upd.Parameters.AddWithValue("$tok", token);
        upd.ExecuteNonQuery();

        tx.Commit();
        Log("DESK_ENROLLED", pid, payloadJson: System.Text.Json.JsonSerializer.Serialize(new { deskId, token }));
        return (true, "Enrolled successfully", pid, deskId);
    }

    public void Log(string type, string? participantId = null, int? attackId = null, string payloadJson = "{}")
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO event_log (ts, type, participant_id, attack_id, payload_json) VALUES ($ts,$t,$p,$a,$j)";
        cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$p", (object?)participantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$a", (object?)attackId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$j", payloadJson);
        cmd.ExecuteNonQuery();
    }
}
