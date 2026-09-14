/// <summary>
/// The one rule for "who won what" in a tournament, shared by the public tournament
/// pages, the year's tournament list and the History page: a stage marked Historical
/// produces a title once it is settled — a bracket when every series has a winner, a
/// pool as soon as it has standings (its leader).
/// </summary>
public static class TournamentTitles
{
    public static List<TitleDto> Of(Tournament tournament)
    {
        var brackets = tournament.Brackets
            .Where(b => b.Historical && b.IsDecided())
            .Select(b => new TitleDto(b.Name, new TeamSummaryDto(b.GetWinner())));
        var pools = tournament.RoundRobins
            .Where(r => r.Historical && r.Standings is { Count: > 0 })
            .Select(r => new TitleDto(r.Name, new TeamSummaryDto(r.Standings!.Keys.First())));
        return [.. brackets, .. pools];
    }

    /// <summary>Every series in every bracket has a winner (a tournament with no brackets is not decided).</summary>
    public static bool IsDecided(Tournament tournament) =>
        tournament.Brackets.Count > 0 && tournament.Brackets.All(b => b.IsDecided());
}

/// <summary>
/// Where a season's games can be followed: the tournament page for playoffs and
/// mid-season tournaments. Regular seasons have none and are skipped.
/// </summary>
public record TournamentLinkDto(long SeasonId, long TournamentId, string Name, string ShortName, string Kind)
{
    public static List<TournamentLinkDto> Of(IEnumerable<Season> seasons) =>
        seasons
            .Where(SeasonKind.HasTournamentPage)
            .OrderBy(s => SeasonKind.IsPlayoffs(s) ? 0 : 1)
            .ThenBy(s => s.StartDate)
            .ThenBy(s => s.ID)
            .Select(s => (Season: s, Tournament: s.Tournaments.OrderBy(t => t.ID).FirstOrDefault()))
            .Where(x => x.Tournament is not null)
            .Select(x => new TournamentLinkDto(
                x.Season.ID, x.Tournament!.ID, x.Season.Name, SeasonKind.ShortName(x.Season), SeasonKind.Wire(x.Season)))
            .ToList();
}
