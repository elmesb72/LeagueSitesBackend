/// <summary>
/// A tournament as the public pages show it: the playoffs and mid-season cups alike.
/// Kind is "playoffs" or "tournament"; Name is the season's name ("2026 Playoffs",
/// "2027 Canada Day Cup"). Season is null only in the degenerate "no season at all"
/// answer of /api/Playoffs.
/// </summary>
public record TournamentDto(
    long Id,
    string Name,
    string Kind,
    SeasonSummaryDto? Season,
    List<BracketDto> Brackets,
    List<RoundRobinDto> RoundRobins)
{
    /// <summary>
    /// "Nothing to show" for a season (or none). The Playoffs endpoint answers this with
    /// the regular season when the year has no playoffs yet, so the kind stays "playoffs"
    /// unless the season really is a mid-season tournament.
    /// </summary>
    public static TournamentDto Empty(Season? season) => new(
        0,
        season?.Name ?? "",
        season is not null && SeasonKind.IsTournament(season) ? "tournament" : "playoffs",
        season is null ? null : new SeasonSummaryDto(season),
        [],
        []);
}

/// <summary>One line of the year's tournament list.</summary>
public record TournamentSummaryDto(
    long Id,
    string Name,
    string Kind,
    SeasonSummaryDto Season,
    DateTime StartDate,
    DateTime? FirstGame,
    DateTime? LastGame,
    int GamesScheduled,
    int GamesPlayed,
    bool Decided,
    List<TitleDto> Titles);

/// <summary>A marked (Historical) stage's winner: the champion of that bracket or pool.</summary>
public record TitleDto(string Label, TeamSummaryDto Team);

/// <param name="Historical">True when this bracket's winner is the league champion shown on the History page.</param>
/// <param name="Winner">The bracket winner once every series is decided (the same test History applies), otherwise null.</param>
public record BracketDto(
    string Name,
    string Format,
    bool Historical,
    TeamSummaryDto? Winner,
    List<BracketRoundDto> Rounds)
{
    public static BracketDto From(TournamentBracket bracket) => new(
        bracket.Name,
        bracket.Format,
        bracket.Historical,
        bracket.IsDecided() ? new TeamSummaryDto(bracket.GetWinner()) : null,
        bracket.Rounds.Select(r => BracketRoundDto.From(r, bracket.Seeds)).ToList());
}

public record BracketRoundDto(
    string Name,
    List<SeriesDto> Series)
{
    public static BracketRoundDto From(BracketRound round, Dictionary<int, Team> seeds) => new(
        round.Name,
        round.Series.Select(s => SeriesDto.From(s, seeds)).ToList());
}

public record SeriesDto(
    long Number,
    string Format,
    string HostOrder,
    SeriesSpotDto? Spot1,
    SeriesSpotDto? Spot2,
    TeamSummaryDto? Winner,
    TeamSummaryDto? Loser,
    SeriesResultsDto? Results,
    List<SeriesGameDto> Games)
{
    public static SeriesDto From(RoundSeries series, Dictionary<int, Team> seeds)
    {
        var spot1 = series.Spots.Item1 != null
            ? SeriesSpotDto.From(series.Spots.Item1, seeds)
            : null;
        var spot2 = series.Spots.Item2 != null
            ? SeriesSpotDto.From(series.Spots.Item2, seeds)
            : null;

        SeriesResultsDto? results = null;
        if (spot1?.Team != null && spot2?.Team != null)
        {
            var standings = series.GetResults();
            results = SeriesResultsDto.From(standings, series);
        }

        return new(
            series.Number,
            series.Format,
            series.HostOrder,
            spot1, spot2,
            series.Winner != null ? new TeamSummaryDto(series.Winner) : null,
            series.Loser != null ? new TeamSummaryDto(series.Loser) : null,
            results,
            series.Games.Select(g => SeriesGameDto.From(g)).ToList());
    }
}

public record SeriesSpotDto(
    char Source,
    int Seed,
    TeamSummaryDto? Team,
    int? InitialSeed)
{
    public static SeriesSpotDto From(TournamentSeriesSpot spot, Dictionary<int, Team> seeds)
    {
        int? initialSeed = null;
        if (spot.Team != null)
        {
            var seedEntry = seeds.FirstOrDefault(s => s.Value == spot.Team);
            if (seedEntry.Value != null)
                initialSeed = seedEntry.Key;
        }

        return new(
            spot.Source,
            spot.Seed,
            spot.Team != null ? new TeamSummaryDto(spot.Team) : null,
            initialSeed);
    }
}

public record SeriesResultsDto(
    List<SeriesTeamResultDto> TeamResults,
    string StatusText)
{
    public static SeriesResultsDto From(Standings standings, RoundSeries series)
    {
        var teamResults = standings.Select(s => new SeriesTeamResultDto(
            new TeamSummaryDto(s.Key),
            s.Value.Wins,
            s.Value.Losses)).ToList();

        string status;
        if (!standings.Values.Any())
        {
            status = "Series tied 0-0";
        }
        else if (series.Winner != null)
        {
            status = $"{series.Winner.FullName} win {standings.Values.First().Wins}-{standings.Values.Last().Wins}";
        }
        else if (standings.Values.First().Wins == standings.Values.Last().Wins)
        {
            status = $"Series tied {standings.Values.First().Wins}-{standings.Values.Last().Wins}";
        }
        else
        {
            status = $"{standings.Keys.First().FullName} lead {standings.Values.First().Wins}-{standings.Values.Last().Wins}";
        }

        return new(teamResults, status);
    }
}

public record SeriesTeamResultDto(TeamSummaryDto Team, int Wins, int Losses);

public record SeriesGameDto(
    long GameNumber,
    GameSummaryDto? Game)
{
    public static SeriesGameDto From(SeriesGame sg) => new(
        sg.GameNumber,
        sg.Game != null ? new GameSummaryDto(sg.Game) : null);
}

public record RoundRobinDto(
    string Name,
    List<StandingsEntryDto>? Standings,
    List<RoundRobinGameDto> Games)
{
    public static RoundRobinDto From(TournamentRoundRobin rr)
    {
        List<string> excludedStatuses = ["Cancelled", "Deleted"];
        rr.Standings?.CalculateStreaks();
        return new(
            rr.Name,
            rr.Standings?.ToDto(),
            rr.Games
                .Where(g => g.Game != null && !excludedStatuses.Contains(g.Game.Status?.Name ?? ""))
                .OrderBy(g => g.Game!.Date)
                .Select(g => new RoundRobinGameDto(new GameSummaryDto(g.Game!)))
                .ToList());
    }
}

public record RoundRobinGameDto(GameSummaryDto Game);
