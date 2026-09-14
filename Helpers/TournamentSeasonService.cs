using Microsoft.EntityFrameworkCore;

/// <summary>
/// Creating and removing mid-season tournaments. A cup is its own Season of kind
/// Tournament with one Tournament in it, exactly as the playoffs are, so its games
/// never mix with the regular season's.
/// </summary>
public interface ITournamentSeasonService
{
    /// <summary>Creates the season and its tournament; throws TournamentFormatException for a refused request.</summary>
    Task<(Season Season, Tournament Tournament)> CreateAsync(string title, DateTime startDate);

    Task<TournamentSeasonDeletion> DeleteAsync(long seasonId);
}

public abstract record TournamentSeasonDeletion
{
    public sealed record NotFound : TournamentSeasonDeletion;
    /// <summary>The season still has games that are not in the recovery bin.</summary>
    public sealed record LiveGames(int Count) : TournamentSeasonDeletion;
    public sealed record Deleted(string Name, int BinnedGamesPurged, int Brackets, int Pools) : TournamentSeasonDeletion;
}

public class TournamentSeasonService(LeagueSitesContext dbContext) : ITournamentSeasonService
{
    public async Task<(Season Season, Tournament Tournament)> CreateAsync(string name, DateTime startDate)
    {
        var year = startDate.Year;
        var seasonName = SeasonKind.NameFor(year, SeasonKind.Tournament, name);
        // What the executive actually named it, without the year we add in front.
        name = seasonName[($"{year} ").Length..];
        if (name.Length == 0)
            throw new TournamentFormatException("Give the tournament a name.");
        if (SeasonKind.IsReservedTitle(name))
            throw new TournamentFormatException($"\"{name}\" is what the site calls its other seasons. Pick another name.");

        var existing = await dbContext.Seasons
            .Where(s => s.Year == year)
            .Select(s => s.Name)
            .ToListAsync();
        if (existing.Any(n => string.Equals(n, seasonName, StringComparison.OrdinalIgnoreCase)))
            throw new TournamentFormatException($"There is already a {year} tournament called \"{name}\".");

        // Same year, same rules: pool standings inside the cup rank the way the league
        // ranks. A year with no regular season (a tournament-only tenant) gets the defaults.
        var regularSeason = await dbContext.Seasons
            .FirstOrDefaultAsync(s => s.Subseason == SeasonKind.RegularSeason && s.Year == year);

        var season = new Season
        {
            Year = year,
            Subseason = SeasonKind.Tournament,
            Name = seasonName,
            StartDate = startDate.Date,
            StandingsJson = string.IsNullOrWhiteSpace(regularSeason?.StandingsJson)
                ? StandingsConfigService.Serialize(new StandingsConfig())
                : regularSeason.StandingsJson,
        };
        dbContext.Seasons.Add(season);
        await dbContext.SaveChangesAsync();

        var tournament = new Tournament { SeasonID = season.ID };
        dbContext.Tournaments.Add(tournament);
        await dbContext.SaveChangesAsync();

        return (season, tournament);
    }

    public async Task<TournamentSeasonDeletion> DeleteAsync(long seasonId)
    {
        var season = await dbContext.Seasons
            .Include(s => s.Tournaments)
                .ThenInclude(t => t.Brackets)
                    .ThenInclude(b => b.Rounds)
                        .ThenInclude(r => r.Series)
                            .ThenInclude(s => s.Games)
            .Include(s => s.Tournaments)
                .ThenInclude(t => t.RoundRobins)
                    .ThenInclude(r => r.Games)
            .FirstOrDefaultAsync(s => s.ID == seasonId && s.Subseason == SeasonKind.Tournament);
        if (season is null) return new TournamentSeasonDeletion.NotFound();

        var games = await dbContext.Games
            .Include(g => g.Status)
            .Where(g => g.SeasonID == seasonId)
            .ToListAsync();
        var live = games.Count(g => g.Status!.Name != "Deleted");
        if (live > 0) return new TournamentSeasonDeletion.LiveGames(live);

        // The binned games belong to nothing else and point at this season; the
        // foreign key means they go with it. This is the one place the site
        // hard-deletes Game rows, and only for a cup an executive is discarding.
        using var transaction = await dbContext.Database.BeginTransactionAsync();
        var name = season.Name;
        var brackets = 0;
        var pools = 0;
        foreach (var tournament in season.Tournaments)
        {
            foreach (var bracket in tournament.Brackets)
            {
                foreach (var round in bracket.Rounds)
                {
                    foreach (var series in round.Series)
                        dbContext.SeriesGames.RemoveRange(series.Games);
                    dbContext.RoundSeries.RemoveRange(round.Series);
                }
                dbContext.BracketRounds.RemoveRange(bracket.Rounds);
                brackets++;
            }
            dbContext.TournamentBrackets.RemoveRange(tournament.Brackets);
            foreach (var pool in tournament.RoundRobins)
            {
                dbContext.RoundRobinGames.RemoveRange(pool.Games);
                pools++;
            }
            dbContext.TournamentRoundRobins.RemoveRange(tournament.RoundRobins);
        }
        dbContext.Tournaments.RemoveRange(season.Tournaments);
        await dbContext.SaveChangesAsync();

        dbContext.Games.RemoveRange(games);
        dbContext.Seasons.Remove(season);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return new TournamentSeasonDeletion.Deleted(name, games.Count, brackets, pools);
    }
}
