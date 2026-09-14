using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeagueSitesBackend.Tests;

/// <summary>
/// A mid-season tournament is its own season of kind Tournament. These run
/// against a real migrated SQLite file because deletion crosses seven tables
/// and the foreign keys are what make the ordering matter.
/// </summary>
public class TournamentSeasonServiceTests : IDisposable
{
    readonly string dbPath = Path.Combine(Path.GetTempPath(), $"cup-test-{Guid.NewGuid()}.db");
    string ConnectionString => $"Data Source={dbPath}";

    const long RegularSeasonID = 200;
    const long TeamA = 101, TeamB = 102, ParkID = 50;

    public TournamentSeasonServiceTests()
    {
        DatabaseMigrator.Migrate(ConnectionString, NullLogger.Instance);
        using var db = NewContext();
        db.Teams.AddRange(
            new Team { ID = TeamA, Location = "Town A", Name = "Alphas", Abbreviation = "ALP", Active = true, BackgroundColor = "FFFFFF", Color = "000000" },
            new Team { ID = TeamB, Location = "Town B", Name = "Betas", Abbreviation = "BET", Active = true, BackgroundColor = "FFFFFF", Color = "000000" });
        db.Locations.Add(new Location { ID = ParkID, Active = true, Name = "Park", City = "Town" });
        db.Seasons.Add(new Season
        {
            ID = RegularSeasonID,
            Year = 2027,
            Subseason = SeasonKind.RegularSeason,
            Name = "2027 Regular Season",
            StartDate = new DateTime(2027, 5, 1),
            StandingsJson = "{\"winsValue\":3,\"tiesValue\":1,\"tiebreakers\":[\"Points\"]}",
        });
        db.SaveChanges();
    }

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

    async Task<(long SeasonID, long TournamentID)> CreateCup(string title = "Canada Day Cup", DateTime? start = null)
    {
        using var db = NewContext();
        var (season, tournament) = await new TournamentSeasonService(db).CreateAsync(title, start ?? new DateTime(2027, 7, 1));
        return (season.ID, tournament.ID);
    }

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task Creates_a_tournament_season_with_the_regular_seasons_rules_and_one_tournament()
    {
        var (seasonId, tournamentId) = await CreateCup("  Canada Day Cup ", new DateTime(2027, 7, 1, 18, 30, 0));

        using var db = NewContext();
        var season = db.Seasons.Include(s => s.Tournaments).Single(s => s.ID == seasonId);
        season.Subseason.Should().Be(SeasonKind.Tournament);
        season.Name.Should().Be("2027 Canada Day Cup", "trimmed, year added once");
        season.Name.Should().Be("2027 Canada Day Cup");
        season.Year.Should().Be(2027);
        season.StartDate.Should().Be(new DateTime(2027, 7, 1), "date only");
        season.StandingsJson.Should().Contain("\"winsValue\":3", "inherited from the regular season");
        season.Tournaments.Should().ContainSingle(t => t.ID == tournamentId);
    }

    [Fact]
    public async Task Refuses_blank_reserved_and_duplicate_titles()
    {
        using var db = NewContext();
        var service = new TournamentSeasonService(db);
        var start = new DateTime(2027, 7, 1);

        await service.Invoking(s => s.CreateAsync("   ", start))
            .Should().ThrowAsync<TournamentFormatException>().WithMessage("Give the tournament a name.");
        await service.Invoking(s => s.CreateAsync("playoffs", start))
            .Should().ThrowAsync<TournamentFormatException>().WithMessage("*other seasons*");

        await service.CreateAsync("Canada Day Cup", start);
        await service.Invoking(s => s.CreateAsync("canada day cup", new DateTime(2027, 8, 1)))
            .Should().ThrowAsync<TournamentFormatException>().WithMessage("*already a 2027 tournament called*");
        // The same name in another year is fine — years are separate.
        db.Seasons.Add(new Season { Year = 2028, Subseason = SeasonKind.RegularSeason, Name = "2028 Regular Season", StartDate = new DateTime(2028, 5, 1) });
        await db.SaveChangesAsync();
        await service.Invoking(s => s.CreateAsync("Canada Day Cup", new DateTime(2028, 7, 1))).Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_year_without_a_regular_season_gets_the_default_rules()
    {
        // A tournament-only tenant has cups and nothing else; the cup still needs
        // standings rules for its pools, so it takes the platform defaults.
        using var db = NewContext();
        var (season, _) = await new TournamentSeasonService(db).CreateAsync("Winter Classic", new DateTime(2031, 1, 15));

        season.Name.Should().Be("2031 Winter Classic");
        StandingsConfigService.Parse(season.StandingsJson).Should().BeEquivalentTo(new StandingsConfig());
    }

    [Fact]
    public async Task A_name_that_already_starts_with_the_year_is_not_doubled()
    {
        var (seasonId, _) = await CreateCup("2027 Canada Day Cup");
        using var db = NewContext();
        db.Seasons.Single(s => s.ID == seasonId).Name.Should().Be("2027 Canada Day Cup");
    }

    // ------------------------------------------------------------------ delete

    async Task<long> AddGame(long seasonId, string status, long? seriesId = null)
    {
        using var db = NewContext();
        var statusId = db.GameStatuses.Single(s => s.Name == status).ID;
        var game = new Game
        {
            SeasonID = seasonId, Date = new DateTime(2027, 7, 1, 19, 0, 0),
            HostTeamID = TeamA, VisitingTeamID = TeamB, LocationID = ParkID, StatusID = statusId,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        if (seriesId is not null)
        {
            db.SeriesGames.Add(new SeriesGame { SeriesID = seriesId.Value, GameNumber = 1, GameID = game.ID });
            await db.SaveChangesAsync();
        }
        return game.ID;
    }

    async Task<long> AddBracketWithOneSeries(long tournamentId)
    {
        using var db = NewContext();
        var bracket = new TournamentBracket
        {
            TournamentID = tournamentId, Name = "Main", Format = "Fixed", Historical = true,
            SeedingConfiguration = $"1-2,Standings,Season:{RegularSeasonID}:1-2",
            Rounds = [new BracketRound { Name = "Final", Series = [new RoundSeries { Number = 1, Format = "Best of", HostOrder = "1", Matchup = "#1-#2" }] }],
        };
        db.TournamentBrackets.Add(bracket);
        db.TournamentRoundRobins.Add(new TournamentRoundRobin { TournamentID = tournamentId, Name = "B Side", Historical = false, SeedingConfiguration = $"1-2,Standings,Season:{RegularSeasonID}:3-4" });
        await db.SaveChangesAsync();
        return bracket.Rounds.Single().Series.Single().ID;
    }

    [Fact]
    public async Task Deleting_a_cup_with_a_live_game_is_refused()
    {
        var (seasonId, tournamentId) = await CreateCup();
        var seriesId = await AddBracketWithOneSeries(tournamentId);
        await AddGame(seasonId, "Upcoming", seriesId);

        using var db = NewContext();
        var outcome = await new TournamentSeasonService(db).DeleteAsync(seasonId);

        outcome.Should().BeOfType<TournamentSeasonDeletion.LiveGames>().Which.Count.Should().Be(1);
        db.Seasons.Count(s => s.ID == seasonId).Should().Be(1, "nothing is removed");
    }

    [Fact]
    public async Task Deleting_an_empty_or_binned_only_cup_removes_everything_it_owns()
    {
        var (seasonId, tournamentId) = await CreateCup();
        var seriesId = await AddBracketWithOneSeries(tournamentId);
        var binned = await AddGame(seasonId, "Deleted", seriesId);
        var regularGame = await AddGame(RegularSeasonID, "Played");

        using var db = NewContext();
        var outcome = await new TournamentSeasonService(db).DeleteAsync(seasonId);

        var deleted = outcome.Should().BeOfType<TournamentSeasonDeletion.Deleted>().Subject;
        deleted.Name.Should().Be("2027 Canada Day Cup");
        deleted.BinnedGamesPurged.Should().Be(1);
        deleted.Brackets.Should().Be(1);
        deleted.Pools.Should().Be(1);

        using var check = NewContext();
        check.Seasons.Any(s => s.ID == seasonId).Should().BeFalse();
        check.Tournaments.Any(t => t.ID == tournamentId).Should().BeFalse();
        check.TournamentBrackets.Any(b => b.TournamentID == tournamentId).Should().BeFalse();
        check.TournamentRoundRobins.Any(r => r.TournamentID == tournamentId).Should().BeFalse();
        check.RoundSeries.Any(s => s.ID == seriesId).Should().BeFalse();
        check.SeriesGames.Any(sg => sg.GameID == binned).Should().BeFalse();
        check.Games.Any(g => g.ID == binned).Should().BeFalse("the binned cup game goes with its season");
        check.Games.Any(g => g.ID == regularGame).Should().BeTrue("other seasons' games are untouched");
        check.Seasons.Any(s => s.ID == RegularSeasonID).Should().BeTrue();
    }

    [Fact]
    public async Task Only_tournament_seasons_can_be_deleted()
    {
        using var db = NewContext();
        var service = new TournamentSeasonService(db);
        (await service.DeleteAsync(RegularSeasonID)).Should().BeOfType<TournamentSeasonDeletion.NotFound>();
        (await service.DeleteAsync(9999)).Should().BeOfType<TournamentSeasonDeletion.NotFound>();
        db.Seasons.Any(s => s.ID == RegularSeasonID).Should().BeTrue();
    }
}
