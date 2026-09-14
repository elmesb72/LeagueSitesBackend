using FluentAssertions;
using Moq;
using Moq.EntityFrameworkCore;

namespace LeagueSitesBackend.Tests;

/// <summary>
/// Consolation pools ("B side") are round robins whose game slots are created
/// up front and filled in as teams are knocked out, so an unassigned slot is a
/// normal intermediate state rather than bad data. Pool standings have to
/// tolerate it, and must count the same games the pool's game list shows.
/// </summary>
public class PlayoffPoolStandingsTests
{
    const long SeasonID = 13;

    static readonly Team TeamA = TestDataHelper.MakeTeam(1, "Alphas", "ALP");
    static readonly Team TeamB = TestDataHelper.MakeTeam(2, "Betas", "BET");

    static Mock<LeagueSitesContext> MockContext()
    {
        var games = new List<Game>
        {
            TestDataHelper.MakeGame(TeamA, TeamB, "Played", scoreHost: 1, scoreVisitor: 0),
        };
        games.ForEach(g => g.SeasonID = SeasonID);

        var season = new Season
        {
            ID = SeasonID,
            Year = 2026,
            Subseason = "Regular Season",
            Name = "2026 Regular Season",
            StartDate = new DateTime(2026, 5, 1),
        };

        var db = new Mock<LeagueSitesContext>();
        db.Setup(x => x.Games).ReturnsDbSet(games);
        db.Setup(x => x.Seasons).ReturnsDbSet(new List<Season> { season });
        return db;
    }

    /// <summary>
    /// Builds a tournament with one pool holding the given slots. A null game
    /// stands for a slot the executive has created but not yet assigned.
    /// </summary>
    static Tournament MakePool(params Game?[] slots)
    {
        var pool = new TournamentRoundRobin
        {
            ID = 1,
            Name = "B Side",
            Historical = false,
            SeedingConfiguration = $"1-2,Standings,Season:{SeasonID}:1-2",
            Games = [.. slots.Select((g, i) => new RoundRobinGame
            {
                ID = i + 1,
                TournamentRoundRobinID = 1,
                GameID = g?.ID,
                Game = g,
            })],
        };

        return new Tournament
        {
            ID = 1,
            SeasonID = 14,
            Season = new Season { ID = 14, Year = 2026, Subseason = "Playoffs", Name = "2026 Playoffs" },
            RoundRobins = [pool],
        };
    }

    static Game PoolGame(long id, string status, long? scoreHost = null, long? scoreVisitor = null)
    {
        var game = TestDataHelper.MakeGame(TeamA, TeamB, status, scoreHost, scoreVisitor);
        game.ID = id;
        return game;
    }

    /// <summary>
    /// Mirrors the controller: Populate re-binds every pool slot against the
    /// season's playoff games by ID, so the assigned ones have to be handed in.
    /// </summary>
    static async Task<Standings> Run(Tournament tournament, params Game?[] slots)
    {
        await tournament.Populate([.. slots.OfType<Game>()], MockContext().Object);
        return tournament.RoundRobins.First().Standings!;
    }

    [Fact]
    public async Task PoolSlotAwaitingAGame_DoesNotBreakTheStandings()
    {
        // The regression that takes the whole playoffs page down: an unassigned
        // slot used to become a blank Game with no teams, which Standings
        // rejects outright — surfacing as "the playoffs have not yet started".
        Game?[] slots = [PoolGame(201, "Played", 6, 2), null];
        var tournament = MakePool(slots);

        var act = async () => await Run(tournament, slots);

        await act.Should().NotThrowAsync();

        var standings = tournament.RoundRobins.First().Standings!;
        standings.Should().HaveCount(2);
        standings[TeamA].Wins.Should().Be(1);
        standings[TeamA].GamesPlayed.Should().Be(1, "the empty slot is not a game played");
    }

    [Fact]
    public async Task PoolWithNoGamesAssignedYet_SeatsEveryEntrantAtZero()
    {
        // Membership comes from the seeding, not from the games. A pool whose
        // games have not been scheduled is still a pool of known teams.
        Game?[] slots = [null, null];

        var standings = await Run(MakePool(slots), slots);

        standings.Keys.Should().BeEquivalentTo([TeamA, TeamB]);
        standings.Values.Should().OnlyContain(r => r.GamesPlayed == 0 && r.Points == 0);
    }

    [Fact]
    public async Task PoolStandings_RankPlayedTeamsAboveUnplayedOnes()
    {
        // A pool seeded 1-3 where only A and B have met so far: A leads on
        // points, and C (no games) still holds a row rather than vanishing.
        var teamC = TestDataHelper.MakeTeam(3, "Gammas", "GAM");
        var tournament = MakePool(PoolGame(201, "Played", 6, 2));
        tournament.RoundRobins.First().SeedingConfiguration = $"1-3,Standings,Season:{SeasonID}:1-3";

        var db = MockContext();
        var regularSeason = new List<Game>
        {
            TestDataHelper.MakeGame(TeamA, TeamB, "Played", scoreHost: 1, scoreVisitor: 0),
            TestDataHelper.MakeGame(TeamB, teamC, "Played", scoreHost: 1, scoreVisitor: 0),
        };
        regularSeason.ForEach(g => g.SeasonID = SeasonID);
        db.Setup(x => x.Games).ReturnsDbSet(regularSeason);

        var poolGame = tournament.RoundRobins.First().Games.First().Game!;
        await tournament.Populate([poolGame], db.Object);
        var standings = tournament.RoundRobins.First().Standings!;

        standings.Keys.Should().HaveCount(3);
        standings.Keys.First().Should().Be(TeamA, "the only team with a win ranks first");
        standings[teamC].GamesPlayed.Should().Be(0);
        standings.Keys.Should().Contain(teamC, "a seeded team with no games yet still has a row");
    }

    [Fact]
    public async Task PoolStandings_IgnoreCancelledAndDeletedGames()
    {
        // These are filtered out of the pool's game list, so counting them in
        // the table would show a record for games the page does not display.
        Game?[] slots = [
            PoolGame(201, "Played", 6, 2),
            PoolGame(202, "Cancelled", 9, 0),
            PoolGame(203, "Deleted", 9, 0),
        ];

        var standings = await Run(MakePool(slots), slots);

        standings[TeamA].GamesPlayed.Should().Be(1);
        standings[TeamA].Wins.Should().Be(1);
        standings[TeamA].RunsScored.Should().Be(6, "only the played game counts");
    }

    [Fact]
    public async Task PoolStandings_CountForfeits()
    {
        Game?[] slots = [PoolGame(201, "Forfeit (Away)")];

        var standings = await Run(MakePool(slots), slots);

        standings[TeamA].Wins.Should().Be(1, "the visitor forfeited, so the host takes it");
        standings[TeamB].Losses.Should().Be(1);
    }
}
