using ControlPlane.Data;

namespace ControlPlane.Services;

public sealed record BoardRow(
    int Rank, string ParticipantId, string Name, string Status, double Score, int Solved, int Active,
    double MedianTimeToFixMinutes, int Prompts, bool AgyExhausted, bool OpenCodeCapReached, string? LastVerdictAt);

public sealed class LeaderboardService(Database db)
{
    private sealed record Row(string Id, string Name, string Status, double Score, int Solved, int Active, double Ttf,
        int Prompts, string? LastValid, bool Agy, bool Oc);

    public IReadOnlyList<BoardRow> Compute()
    {
        var rows = db.Query("""
            SELECT p.id, p.name, p.status,
              COALESCE((SELECT SUM(points) FROM score_ledger WHERE participant_id = p.id), 0),
              (SELECT COUNT(*) FROM attack_deliveries WHERE participant_id = p.id AND solved_at IS NOT NULL),
              (SELECT COUNT(*) FROM attack_deliveries WHERE participant_id = p.id AND landed_at IS NOT NULL AND solved_at IS NULL),
              COALESCE((SELECT SUM(strftime('%s', solved_at) - strftime('%s', landed_at)) FROM attack_deliveries WHERE participant_id = p.id AND solved_at IS NOT NULL), 0),
              (SELECT COUNT(*) FROM event_log WHERE participant_id = p.id AND type = 'PROMPT'),
              (SELECT MAX(solved_at) FROM attack_deliveries WHERE participant_id = p.id),
              COALESCE((SELECT agy_exhausted FROM quota_state WHERE participant_id = p.id), 0),
              COALESCE((SELECT oc_cap_reached FROM quota_state WHERE participant_id = p.id), 0)
            FROM participants p
            """,
            r => new Row(r.GetString(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetInt32(4), r.GetInt32(5),
                r.GetDouble(6), r.GetInt32(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetInt32(9) == 1, r.GetInt32(10) == 1));

        var byId = rows.ToDictionary(r => r.Id);
        var standings = rows.Select(r => new Standing(r.Id, r.Score, r.Solved, r.Ttf, r.Prompts,
            r.LastValid is null ? null : DateTime.Parse(r.LastValid, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal)));

        return Ranking.Rank(standings).Select((s, i) =>
        {
            var r = byId[s.ParticipantId];
            var median = r.Solved == 0 ? 0 : Math.Round(r.Ttf / r.Solved / 60.0, 1); // mean used until per-attack times are listed
            return new BoardRow(i + 1, r.Id, r.Name, r.Status, Math.Round(r.Score, 1), r.Solved, r.Active, median, r.Prompts, r.Agy, r.Oc, r.LastValid);
        }).ToList();
    }
}
