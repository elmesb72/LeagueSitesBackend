using FluentAssertions;
using Moq;
using Moq.EntityFrameworkCore;

namespace LeagueSitesBackend.Tests;

/// <summary>
/// A bracket's later rounds are not stored — Tournament.Populate re-derives
/// them from the earlier rounds' results on every request. So anything that
/// stops one series from naming a winner stalls, or worse silently misfills,
/// every round behind it. These tests pin the two failures seen on a live
/// 8-team re-seeded bracket: a series decided by forfeit, and a re-seeded
/// round read while the round feeding it was still in progress.
/// </summary>
public class PlayoffBracketProgressionTests
{
    const long SeasonID = 13;

    static readonly Team[] Teams = [.. Enumerable.Range(1, 8)
        .Select(i => TestDataHelper.MakeTeam(i, $"Team{i}", $"T{i}"))];

    static Team Seed(int seed) => Teams[seed - 1];

    /// <summary>
    /// A regular season in which the lower-numbered team always wins, giving a
    /// strict 1..8 standings order that no tiebreaker has to touch.
    /// </summary>
    static Mock<LeagueSitesContext> MockContext()
    {
        var games = new List<Game>();
        for (var i = 1; i <= 8; i++)
        {
            for (var j = i + 1; j <= 8; j++)
            {
                games.Add(TestDataHelper.MakeGame(Seed(i), Seed(j), "Played", scoreHost: 1, scoreVisitor: 0));
            }
        }
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

    static RoundSeries MakeSeries(long number, string matchup) => new()
    {
        ID = number,
        Number = number,
        Format = "Best of",
        HostOrder = "121", // best of three
        Matchup = matchup,
        Games = [],
    };

    /// <summary>
    /// The live CLFB shape: quarter-finals by seed, then two re-seeded rounds.
    /// Rounds are supplied in playing order unless <paramref name="shuffleRounds"/>
    /// is set, which is only used to cover Populate's round ordering. The
    /// progression tests keep the natural order so that the round-ordering
    /// behaviour cannot mask what they are actually asserting.
    /// </summary>
    static Tournament MakeTournament(out Dictionary<long, RoundSeries> series, bool shuffleRounds = false)
    {
        series = new[]
        {
            MakeSeries(1, "#1-#8"),
            MakeSeries(2, "#2-#7"),
            MakeSeries(3, "#3-#6"),
            MakeSeries(4, "#4-#5"),
            MakeSeries(5, "r1-r4"),
            MakeSeries(6, "r2-r3"),
            MakeSeries(7, "r1-r2"),
        }.ToDictionary(s => s.Number);

        var quarterFinals = new BracketRound
        {
            ID = 1,
            Name = "Quarter-finals",
            Series = [series[1], series[2], series[3], series[4]],
        };
        var semiFinals = new BracketRound { ID = 2, Name = "Semi-finals", Series = [series[5], series[6]] };
        var finals = new BracketRound { ID = 3, Name = "Finals", Series = [series[7]] };

        var bracket = new TournamentBracket
        {
            ID = 1,
            Name = "Main",
            Format = "Re-seed",
            Historical = true,
            SeedingConfiguration = $"1-8,Standings,Season:{SeasonID}:1-8",
            Rounds = shuffleRounds
                ? [finals, semiFinals, quarterFinals]
                : [quarterFinals, semiFinals, finals],
        };

        // Back-references, as EF would fix them up. The Losers seeding source
        // walks series -> round -> bracket to recover the original seeds.
        foreach (var round in bracket.Rounds)
        {
            round.Bracket = bracket;
            round.BracketID = bracket.ID;
            foreach (var s in round.Series)
            {
                s.Round = round;
                s.RoundID = round.ID;
            }
        }

        return new Tournament
        {
            ID = 1,
            SeasonID = 14,
            Season = new Season { ID = 14, Year = 2026, Subseason = "Playoffs", Name = "2026 Playoffs" },
            Brackets = [bracket],
        };
    }

    /// <summary>
    /// Every quarter-final decided with the higher seed advancing, one of them
    /// on a forfeit — the live CLFB state once the forfeit fix shipped.
    /// </summary>
    static List<Game> QuarterFinalsDecided(Dictionary<long, RoundSeries> series) =>
    [
        .. Sweep(series[1], 101, Seed(1), Seed(8)),
        .. Sweep(series[2], 111, Seed(2), Seed(7)),
        AddGame(series[3], 121, Seed(3), Seed(6), "Forfeit (Away)"),
        AddGame(series[3], 122, Seed(6), Seed(3), "Played", 5, 2),
        AddGame(series[3], 123, Seed(3), Seed(6), "Played", 9, 5),
        .. Sweep(series[4], 131, Seed(4), Seed(5)),
    ];

    /// <summary>
    /// Attaches a game to a series and returns it so it can be handed to
    /// Populate as part of the season's playoff game list.
    /// </summary>
    static Game AddGame(
        RoundSeries series, long id, Team host, Team visitor,
        string status, long? scoreHost = null, long? scoreVisitor = null)
    {
        var game = TestDataHelper.MakeGame(host, visitor, status, scoreHost, scoreVisitor);
        game.ID = id;
        series.Games.Add(new SeriesGame
        {
            ID = id,
            SeriesID = series.ID,
            GameNumber = series.Games.Count + 1,
            GameID = id,
            Game = game,
        });
        return game;
    }

    /// <summary>Sweeps a series in two straight played games.</summary>
    static IEnumerable<Game> Sweep(RoundSeries series, long firstGameId, Team winner, Team loser)
    {
        yield return AddGame(series, firstGameId, winner, loser, "Played", 5, 1);
        yield return AddGame(series, firstGameId + 1, loser, winner, "Played", 1, 5);
    }

    static async Task<TournamentBracket> Run(Tournament tournament, List<Game> playoffGames)
    {
        await tournament.Populate(playoffGames, MockContext().Object);
        return tournament.Brackets.First();
    }

    [Fact]
    public async Task RoundsAreOrderedWidestFirst()
    {
        // Rounds arrive from EF in no particular order; the widest is round one.
        var tournament = MakeTournament(out _, shuffleRounds: true);

        var bracket = await Run(tournament, []);

        bracket.Rounds.Select(r => r.Name)
            .Should().Equal(["Quarter-finals", "Semi-finals", "Finals"]);
    }

    [Fact]
    public async Task SeriesWonOnAForfeit_NamesAWinner()
    {
        var tournament = MakeTournament(out var series);
        var three = series[3];

        // Seed 3 takes it 2-1: game one by forfeit, game two lost, game three won.
        List<Game> games =
        [
            AddGame(three, 101, Seed(3), Seed(6), "Forfeit (Away)"),
            AddGame(three, 102, Seed(6), Seed(3), "Played", 5, 2),
            AddGame(three, 103, Seed(3), Seed(6), "Played", 9, 5),
        ];

        await Run(tournament, games);

        three.GetResults()[Seed(3)].Wins.Should().Be(2, "the forfeit is a win for the team that turned up");
        three.Winner.Should().Be(Seed(3));
        three.Loser.Should().Be(Seed(6));
    }

    [Fact]
    public async Task ForfeitOnlySeries_StillNamesAWinner()
    {
        var tournament = MakeTournament(out var series);
        var three = series[3];

        // A best-of-three conceded outright.
        List<Game> games =
        [
            AddGame(three, 101, Seed(3), Seed(6), "Forfeit (Away)"),
            AddGame(three, 102, Seed(6), Seed(3), "Forfeit (Home)"),
        ];

        await Run(tournament, games);

        three.Winner.Should().Be(Seed(3));
        three.Loser.Should().Be(Seed(6));
    }

    [Fact]
    public async Task UnplayedStatuses_DoNotCountTowardASeries()
    {
        var tournament = MakeTournament(out var series);
        var three = series[3];

        // Scores left on a game that was later cancelled or postponed must not
        // decide anything.
        List<Game> games =
        [
            AddGame(three, 101, Seed(3), Seed(6), "Cancelled", 9, 0),
            AddGame(three, 102, Seed(3), Seed(6), "Postponed", 9, 0),
            AddGame(three, 103, Seed(3), Seed(6), "Upcoming"),
        ];

        await Run(tournament, games);

        three.Winner.Should().BeNull();
        three.GetResults().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // the re-seed pool must come from the opening round, not whichever round loaded first
    public async Task CompletedQuarterFinals_ReseedTheSemiFinalsBySeedOrder(bool shuffleRounds)
    {
        var tournament = MakeTournament(out var series, shuffleRounds);

        await Run(tournament, QuarterFinalsDecided(series));

        // Best remaining seed plays the worst: 1v4 and 2v3.
        series[5].Spots.Item1.Team.Should().Be(Seed(1));
        series[5].Spots.Item2.Team.Should().Be(Seed(4));
        series[6].Spots.Item1.Team.Should().Be(Seed(2));
        series[6].Spots.Item2.Team.Should().Be(Seed(3));

        // The final re-seeds off the semis, which have not been played.
        series[7].Spots.Item1.Team.Should().BeNull();
        series[7].Spots.Item2.Team.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuarterFinalStillInProgress_LeavesTheSemiFinalsUnresolved(bool shuffleRounds)
    {
        var tournament = MakeTournament(out var series, shuffleRounds);

        // Three quarter-finals are done; seed 3 v seed 6 is level at 1-1. Until
        // it finishes nobody knows who is 3rd or 4th best remaining, so no
        // re-seeded spot may be filled — not even the ones above the gap, since
        // r1/r2 are ranks within a pool that is still missing a team.
        List<Game> games =
        [
            .. Sweep(series[1], 101, Seed(1), Seed(8)),
            .. Sweep(series[2], 111, Seed(2), Seed(7)),
            AddGame(series[3], 121, Seed(3), Seed(6), "Played", 4, 1),
            AddGame(series[3], 122, Seed(6), Seed(3), "Played", 7, 2),
            .. Sweep(series[4], 131, Seed(4), Seed(5)),
        ];

        await Run(tournament, games);

        series[3].Winner.Should().BeNull("the series is tied 1-1");

        series[5].Spots.Item1.Team.Should().BeNull();
        series[5].Spots.Item2.Team.Should().BeNull();
        series[6].Spots.Item1.Team.Should().BeNull();
        series[6].Spots.Item2.Team.Should().BeNull(
            "a team from a decided series must not slide up into a rank it has not earned");
    }

    // ------------------------------------------------------- Bracket champion

    [Fact]
    public async Task BracketIsNotDecided_WhileLaterRoundsAreUnplayed()
    {
        // With only the quarter-finals in, four teams each own one series win.
        // The executive pages used to declare "Won by" whichever of them came
        // first — a champion named before the semi-finals had been played.
        var tournament = MakeTournament(out var series);

        var bracket = await Run(tournament, QuarterFinalsDecided(series));

        bracket.Rounds.SelectMany(r => r.Series).Count(s => s.Winner != null)
            .Should().Be(4, "sanity: every quarter-final has a winner");
        bracket.IsDecided().Should().BeFalse();
    }

    [Fact]
    public async Task BracketIsDecided_OnceTheFinalIsPlayed()
    {
        var tournament = MakeTournament(out var series);

        // Higher seed wins throughout: semis are 1v4 and 2v3, the final is 1v2.
        List<Game> games =
        [
            .. QuarterFinalsDecided(series),
            .. Sweep(series[5], 141, Seed(1), Seed(4)),
            .. Sweep(series[6], 151, Seed(2), Seed(3)),
            .. Sweep(series[7], 161, Seed(1), Seed(2)),
        ];

        var bracket = await Run(tournament, games);

        bracket.IsDecided().Should().BeTrue();
        bracket.GetWinner().Should().Be(Seed(1));
    }

    [Fact]
    public async Task EmptyBracket_IsNotDecided()
    {
        var bracket = new TournamentBracket { Name = "Empty", Format = "Fixed", Historical = false };

        bracket.IsDecided().Should().BeFalse("a bracket with no series has nothing to have won");
    }

    // ------------------------------------------------------ Consolation pool

    [Fact]
    public async Task ConsolationPool_SeatsEveryKnockedOutTeam_BeforeTheirGamesExist()
    {
        // The "B side": a pool seeded from the quarter-final losers. All four
        // quarter-finals are done, but only one pool game has been scheduled so
        // far, between two of the four losers. Built from games alone, the pool
        // table showed just those two — so the other two teams knocked out
        // could not see themselves in the B side, and the exec's add-game team
        // list (fed by the same table) only offered the same two.
        var tournament = MakeTournament(out var series);
        var bracket = tournament.Brackets.First();
        var quarterFinals = bracket.Rounds.First(r => r.Name == "Quarter-finals");

        var pool = new TournamentRoundRobin
        {
            ID = 1,
            Name = "B Side",
            Historical = false,
            SeedingConfiguration = $"1-4,Losers,BracketRound:{quarterFinals.ID}:1-4",
            Games = [],
        };
        tournament.RoundRobins = [pool];

        var games = QuarterFinalsDecided(series);
        var poolGame = TestDataHelper.MakeGame(Seed(5), Seed(7), "Upcoming");
        poolGame.ID = 201;
        pool.Games.Add(new RoundRobinGame { ID = 1, TournamentRoundRobinID = pool.ID, GameID = poolGame.ID, Game = poolGame });
        games.Add(poolGame);

        var db = MockContext();
        db.Setup(x => x.RoundSeries).ReturnsDbSet(quarterFinals.Series.ToList());
        await tournament.Populate(games, db.Object);

        pool.Seeds.Values.Should().BeEquivalentTo([Seed(5), Seed(6), Seed(7), Seed(8)],
            "the four quarter-final losers, in original seed order");

        var standings = pool.Standings!;
        standings.Keys.Should().BeEquivalentTo([Seed(5), Seed(6), Seed(7), Seed(8)],
            "every entrant has a row, not just the two with a game scheduled");
        standings.Values.Should().OnlyContain(r => r.GamesPlayed == 0, "the one pool game is still upcoming");
    }
}
