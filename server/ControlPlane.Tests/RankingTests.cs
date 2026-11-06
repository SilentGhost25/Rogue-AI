using ControlPlane.Services;
using Xunit;

public class RankingTests
{
    private static Standing S(string id, double score, int solved = 0, double ttf = 0, int prompts = 0, DateTime? last = null)
        => new(id, score, solved, ttf, prompts, last);

    [Fact]
    public void Same_input_gives_same_ranks_twice()
    {
        var input = new[] { S("a", 50), S("b", 70), S("c", 70.5, solved: 2) };
        Assert.Equal(Ranking.Rank(input).Select(s => s.ParticipantId), Ranking.Rank(input).Select(s => s.ParticipantId));
    }

    [Fact]
    public void Higher_score_wins_when_outside_tolerance()
    {
        var ranked = Ranking.Rank(new[] { S("a", 50, solved: 5), S("b", 52) });
        Assert.Equal("b", ranked[0].ParticipantId);
    }

    [Fact]
    public void Within_one_point_is_tied_and_solved_count_decides()
    {
        var ranked = Ranking.Rank(new[] { S("a", 70, solved: 1), S("b", 69.5, solved: 3) });
        Assert.Equal("b", ranked[0].ParticipantId);
    }

    [Fact]
    public void Tiebreaks_fall_through_time_then_prompts_then_last_submission()
    {
        var t = DateTime.UtcNow;
        Assert.Equal("a", Ranking.Rank(new[] { S("b", 10, 1, 200), S("a", 10, 1, 100) })[0].ParticipantId);
        Assert.Equal("a", Ranking.Rank(new[] { S("b", 10, 1, 100, 9), S("a", 10, 1, 100, 4) })[0].ParticipantId);
        Assert.Equal("a", Ranking.Rank(new[] { S("b", 10, 1, 100, 4, t.AddMinutes(5)), S("a", 10, 1, 100, 4, t) })[0].ParticipantId);
    }
}
