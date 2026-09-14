using FluentAssertions;
using Moq;
using Moq.EntityFrameworkCore;

namespace LeagueSitesBackend.Tests;

/// <summary>
/// Playoff seeding must rank teams under the standings rules OF THE SEASON
/// the ranked games belong to (Season.StandingsJson), so seeding always
/// matches that season's standings page — and historical seeding never
/// changes when a later season adopts different rules.
/// </summary>
public class SeedingStandingsConfigTests
{
    static readonly Team TeamA = TestDataHelper.MakeTeam(1, "Alphas", "ALP");
    static readonly Team TeamB = TestDataHelper.MakeTeam(2, "Betas", "BET");
    static readonly Team TeamC = TestDataHelper.MakeTeam(3, "Gammas", "GAM");
    static readonly Team TeamD = TestDataHelper.MakeTeam(4, "Deltas", "DEL");

    const long SeasonID = 13;

    /// <summary>
    /// D sweeps; C loses out. A and B are tied on points and wins — A has the
    /// far better overall run differential, but B won the head-to-head game.
    /// </summary>
    static Mock<LeagueSitesContext> MockSeason(string standingsJson)
    {
        var games = new List<Game>
        {
            TestDataHelper.MakeGame(TeamA, TeamC, "Played", scoreHost: 10, scoreVisitor: 0),
            TestDataHelper.MakeGame(TeamB, TeamA, "Played", scoreHost: 2, scoreVisitor: 1),
            TestDataHelper.MakeGame(TeamD, TeamB, "Played", scoreHost: 1, scoreVisitor: 0),
            TestDataHelper.MakeGame(TeamD, TeamC, "Played", scoreHost: 5, scoreVisitor: 0),
        };
        games.ForEach(g => g.SeasonID = SeasonID);

        var season = new Season
        {
            ID = SeasonID,
            Year = 2025,
            Subseason = "Regular Season",
            Name = "2025 Regular Season",
            StartDate = new DateTime(2025, 5, 1),
            StandingsJson = standingsJson,
        };

        var dbMock = new Mock<LeagueSitesContext>();
        dbMock.Setup(x => x.Games).ReturnsDbSet(games);
        dbMock.Setup(x => x.Seasons).ReturnsDbSet(new List<Season> { season });
        return dbMock;
    }

    [Fact]
    public async Task StandingsSource_UsesTheSeasonsOwnRules()
    {
        // A season stamped with the pre-2026 rules (overall run differential)
        // must keep seeding by them, regardless of what the current platform
        // defaults are — that is the historical-freeze guarantee.
        var oldRules = "{\"tiebreakers\":[\"Points\",\"Wins\",\"RunDifferential\"]}";
        var source = new SeedingConfigurationStandingsSource($"Season:{SeasonID}:1-4");

        var teams = (await source.GetTeamsAsync(MockSeason(oldRules).Object)).ToList();

        teams.Should().Equal([TeamD, TeamA, TeamB, TeamC],
            "this season's stored rules break the A/B tie by overall run differential");
    }

    [Fact]
    public async Task StandingsSource_UnsetRules_UseHeadToHeadDefaults()
    {
        // An unset StandingsJson means the platform's canonical rules, which
        // break ties head-to-head: B beat A, so B seeds ahead.
        var source = new SeedingConfigurationStandingsSource($"Season:{SeasonID}:1-4");

        var teams = (await source.GetTeamsAsync(MockSeason("").Object)).ToList();

        teams.Should().Equal([TeamD, TeamB, TeamA, TeamC],
            "default rules rank the head-to-head winner ahead of the tied team");
    }

    [Fact]
    public async Task Parse_AssignsSeedsUnderTheSeasonsRules()
    {
        var seeds = await SeedingConfiguration.Parse(
            $"1-4,Standings,Season:{SeasonID}:1-4", MockSeason("").Object);

        seeds[1].Should().Be(TeamD);
        seeds[2].Should().Be(TeamB, "the head-to-head winner takes the higher seed");
        seeds[3].Should().Be(TeamA);
        seeds[4].Should().Be(TeamC);
    }
}
