using Microsoft.EntityFrameworkCore;

/// <summary>
/// The public, read-only view of tournaments: the playoffs and mid-season cups
/// share one shape and one set of rules (which stages are decided, who holds a
/// title), so the two public pages cannot drift apart.
/// </summary>
public interface IPublicTournamentService
{
    /// <summary>The tournament with everything resolved, or null.</summary>
    Task<Tournament?> LoadPopulatedAsync(long tournamentId);

    TournamentDto ToDto(Tournament tournament);

    /// <summary>
    /// The titles a tournament has produced so far: each marked (Historical) bracket
    /// that is decided names its winner; each marked pool with standings names its
    /// leader. The single rule behind the champion banner, the History page and the
    /// tournament list.
    /// </summary>
    List<TitleDto> TitlesOf(Tournament tournament);

    /// <summary>Every playoffs or tournament season of a year, playoffs first, then by start date.</summary>
    Task<List<TournamentSummaryDto>> ListAsync(int year);
}

public class PublicTournamentService(
    LeagueSitesContext dbContext,
    ITournamentService tournamentService) : IPublicTournamentService
{
    static readonly string[] CountedStatuses = ["Upcoming", "Played", "Forfeit (Home)", "Forfeit (Away)"];
    static readonly string[] PlayedStatuses = ["Played", "Forfeit (Home)", "Forfeit (Away)"];

    public Task<Tournament?> LoadPopulatedAsync(long tournamentId) =>
        tournamentService.GetPopulatedAsync(tournamentId);

    public TournamentDto ToDto(Tournament tournament) => new(
        tournament.ID,
        tournament.Season!.Name,
        SeasonKind.Wire(tournament.Season),
        new SeasonSummaryDto(tournament.Season),
        tournament.Brackets.Select(BracketDto.From).ToList(),
        tournament.RoundRobins.Select(RoundRobinDto.From).ToList());

    public List<TitleDto> TitlesOf(Tournament tournament) => TournamentTitles.Of(tournament);

    public async Task<List<TournamentSummaryDto>> ListAsync(int year)
    {
        var seasons = await dbContext.Seasons
            .AsNoTracking()
            .Include(s => s.Tournaments)
            .Where(s => s.Year == year && s.Subseason != SeasonKind.RegularSeason)
            .ToListAsync();

        var summaries = new List<TournamentSummaryDto>();
        foreach (var season in seasons
            .OrderBy(s => s.Subseason == SeasonKind.Playoffs ? 0 : 1)
            .ThenBy(s => s.StartDate)
            .ThenBy(s => s.ID))
        {
            var first = season.Tournaments.OrderBy(t => t.ID).FirstOrDefault();
            if (first is null) continue;
            var tournament = await tournamentService.GetPopulatedAsync(first.ID);
            if (tournament is null) continue;

            var games = await dbContext.Games
                .AsNoTracking()
                .Include(g => g.Status)
                .Where(g => g.SeasonID == season.ID)
                .ToListAsync();
            var counted = games.Where(g => CountedStatuses.Contains(g.Status!.Name)).ToList();

            // Titles only once the tournament is decided, the same gate the History
            // page applies: a marked pool has a standings leader from its first day.
            var decided = TournamentTitles.IsDecided(tournament);

            summaries.Add(new TournamentSummaryDto(
                tournament.ID,
                season.Name,
                SeasonKind.Wire(season),
                new SeasonSummaryDto(season),
                season.StartDate,
                counted.Count > 0 ? counted.Min(g => g.Date) : null,
                counted.Count > 0 ? counted.Max(g => g.Date) : null,
                counted.Count,
                counted.Count(g => PlayedStatuses.Contains(g.Status!.Name)),
                decided,
                decided ? TitlesOf(tournament) : []));
        }
        return summaries;
    }
}
