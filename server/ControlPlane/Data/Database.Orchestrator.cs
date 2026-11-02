using System;
using System.Collections.Generic;
using System.Globalization;
using ControlPlane.Models;
using Microsoft.Data.Sqlite;

namespace ControlPlane.Data;

public partial class Database
{
    public void Exec(string sql, params (string, object?)[] parameters)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parameters)
        {
            cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        }
        cmd.ExecuteNonQuery();
    }

    public T? QueryScalar<T>(string sql, params (string, object?)[] parameters)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parameters)
        {
            cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        }
        var res = cmd.ExecuteScalar();
        if (res == null || res is DBNull) return default;
        if (typeof(T) == typeof(string)) return (T)(object)res.ToString()!;
        return (T)Convert.ChangeType(res, typeof(T), CultureInfo.InvariantCulture);
    }

    public List<T> Query<T>(string sql, params (string, object?)[] parameters)
    {
        var list = new List<T>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parameters)
        {
            cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        }
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var val = rd.GetValue(0);
            if (val != null && val is not DBNull)
            {
                list.Add((T)Convert.ChangeType(val, typeof(T), CultureInfo.InvariantCulture));
            }
        }
        return list;
    }

    public DispatchState GetDispatchState(int attackId)
    {
        var raw = QueryScalar<string>(
            "SELECT state FROM attack_dispatch WHERE attack_id = @id",
            ("@id", attackId));
        return raw is null ? DispatchState.Pending : Enum.Parse<DispatchState>(raw);
    }

    public void EnsureDispatchRow(int attackId, DateTime? scheduledForAt = null)
    {
        Exec("""
            INSERT INTO attack_dispatch (attack_id, state, scheduled_for_at, updated_at)
            VALUES (@id, 'Pending', @sched, @now)
            ON CONFLICT(attack_id) DO UPDATE SET scheduled_for_at = excluded.scheduled_for_at
            """,
            ("@id", attackId),
            ("@sched", scheduledForAt?.ToString("O")),
            ("@now", DateTime.UtcNow.ToString("O")));
    }

    public void TransitionDispatch(int attackId, DispatchState to, string? failedReason = null)
    {
        EnsureDispatchRow(attackId);
        var column = to switch
        {
            DispatchState.CanaryFired     => "canary_fired_at",
            DispatchState.CanaryConfirmed => "canary_confirmed_at",
            DispatchState.ArenaImminent   => "arena_imminent_at",
            DispatchState.ArenaLanded     => "arena_landed_at",
            _ => null
        };
        var stamp = DateTime.UtcNow.ToString("O");
        var sql = column is null
            ? "UPDATE attack_dispatch SET state = @state, failed_reason = @fr, updated_at = @now WHERE attack_id = @id"
            : $"UPDATE attack_dispatch SET state = @state, {column} = @now, updated_at = @now WHERE attack_id = @id";
        Exec(sql, ("@id", attackId), ("@state", to.ToString()), ("@now", stamp), ("@fr", failedReason));
    }

    public void PreregisterAttackDeliveries(int attackId, IEnumerable<string> participantIds)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var pid in participantIds)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO attack_deliveries (attack_id, participant_id, landed_at)
                VALUES (@aid, @pid, NULL)
                ON CONFLICT(attack_id, participant_id) DO NOTHING
                """;
            cmd.Parameters.AddWithValue("@aid", attackId);
            cmd.Parameters.AddWithValue("@pid", pid);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void MarkAttackLanded(int attackId)
    {
        Exec("UPDATE attack_deliveries SET landed_at = @now WHERE attack_id = @id AND landed_at IS NULL",
            ("@now", DateTime.UtcNow.ToString("O")), ("@id", attackId));
    }

    public bool IsAttackDeliveryPreRegistered(int attackId, string participantId) =>
        (QueryScalar<long>(
            "SELECT COUNT(*) FROM attack_deliveries WHERE attack_id = @a AND participant_id = @p",
            ("@a", attackId), ("@p", participantId))) > 0;

    public IEnumerable<string> AllParticipantIds() =>
        Query<string>("SELECT id FROM participants ORDER BY id");

    // Used by GET /api/kiosk/{id}/active-breach for kiosk reconciliation
    public (int AttackId, DispatchState State, DateTime? LandAtUtc)? GetActiveAttackForReconcile()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT d.attack_id, d.state, d.arena_imminent_at, a.imminent_seconds
            FROM attack_dispatch d
            JOIN (SELECT attack_id, imminent_seconds FROM attack_dispatch_runtime) a USING (attack_id)
            WHERE d.state IN ('ArenaImminent','ArenaLanded')
            ORDER BY d.attack_id DESC LIMIT 1
            """;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        var attackId = rd.GetInt32(0);
        var stateStr = rd.GetString(1);
        var arenaImminentAt = rd.IsDBNull(2) ? null : rd.GetString(2);
        var imminentSeconds = rd.GetInt32(3);

        var state = Enum.Parse<DispatchState>(stateStr);
        DateTime? landAt = null;
        if (arenaImminentAt is not null)
            landAt = DateTime.Parse(arenaImminentAt, null, DateTimeStyles.RoundtripKind)
                              .AddSeconds(imminentSeconds);
        return (attackId, state, landAt);
    }
}
