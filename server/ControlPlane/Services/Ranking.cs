namespace ControlPlane.Services;

/// <summary>One participant's totals, summed from the ledger and event log.</summary>
public sealed record Standing(
    string ParticipantId, double Score, int Solved, double TotalTimeToFixSeconds,
    int TotalPrompts, DateTime? LastValidSubmission);

/// <summary>
/// Rank is a pure function of its input (plan section 10). Tie-break chain:
/// score (within 1 point is tied), solved desc, time-to-fix asc, prompts asc, last valid submission asc.
/// </summary>
public static class Ranking
{
    public const double ScoreTolerance = 1.0;

    public static IReadOnlyList<Standing> Rank(IEnumerable<Standing> standings)
    {
        var byScore = standings.OrderByDescending(s => s.Score).ThenBy(s => s.ParticipantId, StringComparer.Ordinal).ToList();
        var result = new List<Standing>(byScore.Count);
        var i = 0;
        while (i < byScore.Count)
        {
            // A tie group is anchored on its first (highest) score; members are within tolerance of that anchor.
            var anchor = byScore[i].Score;
            var group = new List<Standing>();
            while (i < byScore.Count && anchor - byScore[i].Score <= ScoreTolerance) group.Add(byScore[i++]);
            result.AddRange(group
                .OrderByDescending(s => s.Solved)
                .ThenBy(s => s.TotalTimeToFixSeconds)
                .ThenBy(s => s.TotalPrompts)
                .ThenBy(s => s.LastValidSubmission ?? DateTime.MaxValue)
                .ThenBy(s => s.ParticipantId, StringComparer.Ordinal));
        }
        return result;
    }
}
