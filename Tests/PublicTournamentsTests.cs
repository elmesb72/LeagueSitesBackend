using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeagueSitesBackend.Tests;

/// <summary>
/// The public tournament view shared by the playoffs page, the cup page and the
/// year's list. Real SQLite: Populate resolves seeds through the database.
/// </summary>
public class PublicTournamentsTests : IDisposable
{
    readonly string dbPath = Path.Combine(Path.GetTempPath(), $"public-tournaments-{Guid.NewGuid()}.db");
    string ConnectionString => $"Data Source={dbPath}";

    const long RegularSeasonID = 300, PlayoffsID = 301, CupID = 302, LateCupID = 303;
    const long PlayoffTournamentID = 30, CupTournamentID = 31, LateCupTournamentID = 32;
    const long TeamA = 101, TeamB = 102, TeamC = 103, TeamD = 104, ParkID = 50;

    public PublicTournamentsTests()
    {
        DatabaseMigrator.Migrate(ConnectionString, NullLogger.Instance);
        using var db = NewContext();
        var statuses = db.GameStatuses.ToDictionary(s => s.Name, s => s.ID);
        db.Teams.AddRange(Enumerable.Range(1, 4).Select(i => new Team
        {
            ID = 100 + i, Location = $"Town {i}", Name = $"Team{i}", Abbreviation = $"T{i}",
            Active = true, BackgroundColor = "FFFFFF", Color = "000000",
        }));
        db.Locations.Add(new Location { ID = ParkID, Active = true, Name = "Park", City = "Town" });
        db.Seasons.AddRange(
            new Season { ID = RegularSeasonID, Year = 2027, Subseason = SeasonKind.RegularSeason, Name = "2027 Regular Season", StartDate = new DateTime(2027, 5, 1) },
            new Season { ID = PlayoffsID, Year = 2027, Subseason = SeasonKind.Playoffs, Name = "2027 Playoffs", StartDate = new DateTime(2027, 8, 20) },
            new Season { ID = CupID, Year = 2027, Subseason = SeasonKind.Tournament, Name = "2027 Canada Day Cup", StartDate = new DateTime(2027, 7, 1) },
            new Season { ID = LateCupID, Year = 2027, Subseason = SeasonKind.Tournament, Name = "2027 Labour Day Classic", StartDate = new DateTime(2027, 9, 4) });

        // Regular season: the lower-numbered team always wins → standings 1..4.
        var gameId = 1000L;
        for (var i = 1; i <= 4; i++)
            for (var j = i + 1; j <= 4; j++)
                db.Games.Add(new Game
                {
                    ID = gameId++, SeasonID = RegularSeasonID, Date = new DateTime(2027, 6, i + j),
                    HostTeamID = 100 + i, VisitingTeamID = 100 + j, LocationID = ParkID,
                    StatusID = statuses["Played"], ScoreHost = 5, ScoreVisitor = 2,
                });

        // Playoffs: a marked one-game final between seeds 1 and 2, played and won by Team1.
        db.Tournaments.Add(new Tournament
        {
            ID = PlayoffTournamentID, SeasonID = PlayoffsID,
            Brackets = [Final(70, PlayoffTournamentID, "Main", historical: true)],
        });
        db.Games.Add(new Game
        {
            ID = 2000, SeasonID = PlayoffsID, Date = new DateTime(2027, 8, 21),
            HostTeamID = TeamA, VisitingTeamID = TeamB, LocationID = ParkID,
            StatusID = statuses["Played"], ScoreHost = 4, ScoreVisitor = 1,
        });
        db.SeriesGames.Add(new SeriesGame { SeriesID = 7000, GameNumber = 1, GameID = 2000 });

        // Cup: a marked final, scheduled but not played; plus an unmarked pool with no games.
        db.Tournaments.Add(new Tournament
        {
            ID = CupTournamentID, SeasonID = CupID,
            Brackets = [Final(80, CupTournamentID, "Cup Final", historical: true)],
            RoundRobins = [new TournamentRoundRobin { ID = 85, TournamentID = CupTournamentID, Name = "Round Robin", Historical = false, SeedingConfiguration = $"1-4,Standings,Season:{RegularSeasonID}:1-4" }],
        });
        db.Games.Add(new Game
        {
            ID = 3000, SeasonID = CupID, Date = new DateTime(2027, 7, 1, 19, 0, 0),
            HostTeamID = TeamA, VisitingTeamID = TeamB, LocationID = ParkID,
            StatusID = statuses["Upcoming"],
        });
        db.SeriesGames.Add(new SeriesGame { SeriesID = 8000, GameNumber = 1, GameID = 3000 });

        // A later cup with nothing in it yet.
        db.Tournaments.Add(new Tournament { ID = LateCupTournamentID, SeasonID = LateCupID });
        db.SaveChanges();
    }

    static TournamentBracket Final(long id, long tournamentId, string name, bool historical) => new()
    {
        ID = id, TournamentID = tournamentId, Name = name, Format = "Fixed", Historical = historical,
        SeedingConfiguration = $"1-2,Standings,Season:{RegularSeasonID}:1-2",
        Rounds =
        [
            new BracketRound
            {
                ID = id * 10, BracketID = id, Name = "Final",
                Series = [new RoundSeries { ID = id * 100, RoundID = id * 10, Number = 1, Format = "Best of", HostOrder = "1", Matchup = "#1-#2" }],
            },
        ],
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
        foreach (var bak in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(dbPath) + "*.bak"))
            File.Delete(bak);
        GC.SuppressFinalize(this);
    }

    LeagueSitesContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LeagueSitesContext>().UseSqlite(ConnectionString).Options;
        return new LeagueSitesContext(options);
    }

    PublicTournamentService Service(LeagueSitesContext db) => new(db, new TournamentService(db));

    [Fact]
    public async Task Detail_carries_the_seasons_name_and_kind_over_the_playoffs_shape()
    {
        using var db = NewContext();
        var service = Service(db);

        var cup = service.ToDto((await service.LoadPopulatedAsync(CupTournamentID))!);
        cup.Id.Should().Be(CupTournamentID);
        cup.Name.Should().Be("2027 Canada Day Cup");
        cup.Kind.Should().Be("tournament");
        cup.Season!.Name.Should().Be("2027 Canada Day Cup");
        cup.Brackets.Should().ContainSingle(b => b.Name == "Cup Final" && b.Historical && b.Winner == null);
        cup.Brackets[0].Rounds[0].Series[0].Spot1!.Team!.Abbreviation.Should().Be("T1", "seeded from the regular season");
        cup.RoundRobins.Should().ContainSingle(r => r.Name == "Round Robin");

        var playoffs = service.ToDto((await service.LoadPopulatedAsync(PlayoffTournamentID))!);
        playoffs.Kind.Should().Be("playoffs");
        playoffs.Name.Should().Be("2027 Playoffs");
        playoffs.Brackets[0].Winner!.Abbreviation.Should().Be("T1");

        (await service.LoadPopulatedAsync(999)).Should().BeNull();
    }

    [Fact]
    public async Task Titles_come_only_from_marked_decided_stages()
    {
        using var db = NewContext();
        var service = Service(db);

        var playoffs = (await service.LoadPopulatedAsync(PlayoffTournamentID))!;
        service.TitlesOf(playoffs).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Label = "Main", Team = new { Abbreviation = "T1" } });

        var cup = (await service.LoadPopulatedAsync(CupTournamentID))!;
        service.TitlesOf(cup).Should().BeEmpty("the final is marked but not yet played, and the pool is not marked");
    }

    [Fact]
    public async Task The_years_list_puts_the_playoffs_first_then_cups_by_start_date_with_progress()
    {
        using var db = NewContext();
        var list = await Service(db).ListAsync(2027);

        list.Select(t => t.Name).Should().Equal("2027 Playoffs", "2027 Canada Day Cup", "2027 Labour Day Classic");
        list.Select(t => t.Kind).Should().Equal("playoffs", "tournament", "tournament");

        var playoffs = list[0];
        playoffs.Id.Should().Be(PlayoffTournamentID);
        playoffs.GamesScheduled.Should().Be(1);
        playoffs.GamesPlayed.Should().Be(1);
        playoffs.Decided.Should().BeTrue();
        playoffs.Titles.Should().ContainSingle(t => t.Label == "Main" && t.Team.Abbreviation == "T1");
        playoffs.FirstGame.Should().Be(new DateTime(2027, 8, 21));

        var cup = list[1];
        cup.StartDate.Should().Be(new DateTime(2027, 7, 1));
        cup.GamesScheduled.Should().Be(1);
        cup.GamesPlayed.Should().Be(0);
        cup.Decided.Should().BeFalse();
        cup.Titles.Should().BeEmpty();

        var late = list[2];
        late.GamesScheduled.Should().Be(0);
        late.FirstGame.Should().BeNull();
        late.Decided.Should().BeFalse("no brackets at all is not decided");
    }

    [Fact]
    public async Task A_year_without_tournaments_lists_nothing()
    {
        using var db = NewContext();
        (await Service(db).ListAsync(2019)).Should().BeEmpty();
    }

    [Fact]
    public void The_empty_answer_keeps_the_playoffs_kind_for_a_regular_season()
    {
        var regular = new Season { Year = 2027, Subseason = SeasonKind.RegularSeason, Name = "2027 Regular Season", StartDate = new DateTime(2027, 5, 1) };
        var empty = TournamentDto.Empty(regular);
        empty.Kind.Should().Be("playoffs");
        empty.Season!.Name.Should().Be("2027 Regular Season");
        empty.Brackets.Should().BeEmpty();
        TournamentDto.Empty(null).Season.Should().BeNull();
    }
}
