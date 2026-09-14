public class Year
{
    public long CalendarYear { get; set; }
    public List<Season> Seasons { get; set; } = [];
    public Season? RegularSeason
    {
        get
        {
            return Seasons.FirstOrDefault(s => s.Subseason == "Regular Season");
        }
    }
    public readonly Standings? RegularSeasonStandings;
    public readonly Team? RegularSeasonWinner;
    public readonly TeamResultSet? RegularSeasonWinnerResults;
    public Season? Playoffs
    {
        get
        {
            return Seasons.FirstOrDefault(s => s.Subseason == "Playoffs");
        }
    }
    public Tournament? PlayoffsTournament
    {
        get
        {
            if (Playoffs == null) return null;
            return Playoffs.Tournaments.FirstOrDefault();
        }
    }
    /// <summary>The year's mid-season tournaments (cups), each its own season, in start-date order.</summary>
    public IEnumerable<Season> TournamentSeasons =>
        Seasons.Where(SeasonKind.IsTournament).OrderBy(s => s.StartDate).ThenBy(s => s.ID);
    public string? ExceptionYearDescription { get; set; }

    public Year(long calendarYear, List<Season> seasons)
    {
        CalendarYear = calendarYear;
        Seasons = seasons;
        // Ranked under the rules the season was played under, so rule
        // changes in later years never rewrite a historical pennant.
        RegularSeasonStandings = new Standings(
            RegularSeason?.Games ?? [],
            StandingsConfigService.Parse(RegularSeason?.StandingsJson));
        if (RegularSeasonIsComplete())
        {
            var winner = RegularSeasonStandings.First();
            RegularSeasonWinner = winner.Key;
            RegularSeasonWinnerResults = winner.Value;
        }
    }
    public Year(long calendarYear, string exceptionYearDescription)
    {
        CalendarYear = calendarYear;
        ExceptionYearDescription = exceptionYearDescription;
    }

    public bool HasPlayoffs()
    {
        return Playoffs != null && PlayoffsTournament != null;
    }

    public bool RegularSeasonIsComplete()
    {
        return RegularSeason != null && RegularSeason.Games.Any() && RegularSeason.Games.All(g => g.Status?.Name != "Upcoming");
    }

    public bool PlayoffsAreComplete()
    {
        return HasPlayoffs() && (PlayoffsTournament?.Brackets.SelectMany(b => b.Rounds).SelectMany(r => r.Series).All(s => s.Winner != null) ?? false);
    }

}