public record HistoryYearDto(
    long CalendarYear,
    string? ExceptionYearDescription,
    string? Champion,
    string? ChampionAbbreviation,
    string? BestRecord,
    string? BestRecordAbbreviation,
    string? BestRecordResults,
    bool PlayoffsComplete,
    bool RegularSeasonComplete,
    List<HistoryTournamentDto> Tournaments)
{
    public static HistoryYearDto From(Year year)
    {
        string? champion = null;
        string? championAbbr = null;
        var tournaments = new List<HistoryTournamentDto>();

        if (!string.IsNullOrEmpty(year.ExceptionYearDescription))
        {
            // Exception year — no champion, no best record, no tournaments
        }
        else
        {
            // The champion column lists every title of the year, comma-joined:
            // the playoffs' first, then each mid-season tournament's, labelled by
            // its name. Only stages the league marked Historical count, and only
            // once the tournament they belong to is decided — the same rule the
            // public tournament pages use for their champion banners.
            var titles = new List<string>();

            if (year.PlayoffsAreComplete() && year.PlayoffsTournament != null)
            {
                var playoffTitles = TournamentTitles.Of(year.PlayoffsTournament);
                if (playoffTitles.Count == 1)
                {
                    titles.Add(playoffTitles[0].Team.FullName);
                    championAbbr = playoffTitles[0].Team.Abbreviation;
                }
                else
                {
                    titles.AddRange(playoffTitles.Select(t => $"{t.Label}: {t.Team.FullName}"));
                }
            }

            foreach (var season in year.TournamentSeasons)
            {
                var tournament = season.Tournaments.OrderBy(t => t.ID).FirstOrDefault();
                if (tournament is null) continue;
                var decided = TournamentTitles.IsDecided(tournament);
                var shortName = SeasonKind.ShortName(season);
                tournaments.Add(new HistoryTournamentDto(tournament.ID, season.Name, shortName, decided));
                if (!decided) continue;

                var cupTitles = TournamentTitles.Of(tournament);
                if (cupTitles.Count == 1)
                    titles.Add($"{shortName}: {cupTitles[0].Team.FullName}");
                else
                    titles.AddRange(cupTitles.Select(t => $"{shortName} {t.Label}: {t.Team.FullName}"));
            }

            if (titles.Count > 0) champion = string.Join(", ", titles);
        }

        return new HistoryYearDto(
            year.CalendarYear,
            year.ExceptionYearDescription,
            champion,
            championAbbr,
            year.RegularSeasonWinner?.FullName,
            year.RegularSeasonWinner?.Abbreviation,
            year.RegularSeasonWinnerResults?.ToString(),
            year.PlayoffsAreComplete(),
            year.RegularSeasonIsComplete(),
            tournaments);
    }
}

/// <summary>A mid-season tournament of the year, for the History row's links. ShortName is the name without its year.</summary>
public record HistoryTournamentDto(long Id, string Name, string ShortName, bool Decided);
