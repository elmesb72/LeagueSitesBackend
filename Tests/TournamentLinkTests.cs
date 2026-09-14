using FluentAssertions;

namespace LeagueSitesBackend.Tests;

/// <summary>
/// The schedule, standings and homepage payloads carry "where does this season's
/// tournament live" so rows and callouts can link to it without per-game joins.
/// </summary>
public class TournamentLinkTests
{
    static Season Season(long id, string kind, DateTime start, string title = "", bool withTournament = true) => new()
    {
        ID = id,
        Year = 2027,
        Subseason = kind,
        Name = kind == SeasonKind.Tournament ? $"2027 {title}" : $"2027 {kind}",
        StartDate = start,
        Tournaments = withTournament ? [new Tournament { ID = id * 10, SeasonID = id }] : [],
    };

    [Fact]
    public void Playoffs_first_then_cups_by_start_date_regular_season_and_empty_seasons_skipped()
    {
        var links = TournamentLinkDto.Of(
        [
            Season(1, SeasonKind.RegularSeason, new DateTime(2027, 5, 1)),
            Season(4, SeasonKind.Tournament, new DateTime(2027, 9, 4), "Labour Day Classic"),
            Season(3, SeasonKind.Tournament, new DateTime(2027, 7, 1), "Canada Day Cup"),
            Season(2, SeasonKind.Playoffs, new DateTime(2027, 8, 20)),
            Season(5, SeasonKind.Tournament, new DateTime(2027, 6, 1), "Ghost Cup", withTournament: false),
        ]);

        links.Should().Equal(
            new TournamentLinkDto(2, 20, "2027 Playoffs", "Playoffs", "playoffs"),
            new TournamentLinkDto(3, 30, "2027 Canada Day Cup", "Canada Day Cup", "tournament"),
            new TournamentLinkDto(4, 40, "2027 Labour Day Classic", "Labour Day Classic", "tournament"));
    }

    [Fact]
    public void A_year_with_only_a_regular_season_has_no_links()
    {
        TournamentLinkDto.Of([Season(1, SeasonKind.RegularSeason, new DateTime(2027, 5, 1))]).Should().BeEmpty();
    }
}
