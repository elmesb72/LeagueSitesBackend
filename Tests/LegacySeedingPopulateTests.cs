using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeagueSitesBackend.Tests;

/// <summary>
/// Brackets and pools created before seeding rules existed (2024 and earlier
/// on the live sites) have an empty SeedingConfiguration, which Populate
/// resolves as "everyone in the regular season, in standings order". That
/// fallback loaded the regular-season games without their teams, and
/// Standings refuses a game with no teams — so /api/Playoffs?year= threw for
/// every legacy year while /api/History, which happened to have those games
/// tracked with teams already, did not.
///
/// The Moq-based Populate tests cannot catch this: their games carry their
/// navigations from construction. These run against a real SQLite file and a
/// fresh DbContext, so only what Populate itself loads is available.
/// </summary>
public class LegacySeedingPopulateTests : IDisposable
{
    readonly string dbPath = Path.Combine(Path.GetTempPath(), $"legacy-seeding-{Guid.NewGuid():N}.db");
    string ConnectionString => $"Data Source={dbPath}";

    const long RegularSeasonID = 11;
    const long PlayoffsID = 12;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(dbPath)!, Path.GetFileName(dbPath) + "*"))
        {
            File.Delete(file);
        }
        GC.SuppressFinalize(this);
    }

    LeagueSitesContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LeagueSitesContext>().UseSqlite(ConnectionString).Options;
        return new LeagueSitesContext(options);
    }

    /// <summary>
    /// A 2024 shaped like the live legacy data: four teams, a regular season
    /// in which the lower-numbered team always wins (so standings are 1..4
    /// with no tiebreaks), one of those wins by forfeit, and a playoff
    /// bracket plus a pool with NO seeding configuration.
    /// </summary>
    void SeedLegacyYear()
    {
        DatabaseMigrator.Migrate(ConnectionString, NullLogger.Instance);

        using var db = NewContext();
        var statuses = db.GameStatuses.ToDictionary(s => s.Name, s => s.ID);
        // The baseline seeds a hidden placeholder team; keep clear of its ID.
        var teams = Enumerable.Range(1, 4).Select(i => new Team
        {
            ID = 100 + i, Location = $"Town {i}", Name = $"Team{i}", Abbreviation = $"T{i}",
            Active = true, Hidden = false, BackgroundColor = "FFFFFF", Color = "000000",
        }).ToList();
        db.Teams.AddRange(teams);

        var park = new Location { ID = 50, Active = true, Name = "Park", City = "Town" };
        db.Locations.Add(park);

        db.Seasons.AddRange(
            new Season { ID = RegularSeasonID, Year = 2024, Subseason = "Regular Season", Name = "2024 Regular Season", StartDate = new DateTime(2024, 5, 1) },
            new Season { ID = PlayoffsID, Year = 2024, Subseason = "Playoffs", Name = "2024 Playoffs", StartDate = new DateTime(2024, 8, 1) });

        var gameId = 1000L;
        for (var i = 0; i < 4; i++)
        {
            for (var j = i + 1; j < 4; j++)
            {
                // Team i+1 beats team j+1; the very first meeting is a visitor forfeit,
                // which TeamResultSet can only score if Status is loaded.
                var forfeit = i == 0 && j == 1;
                db.Games.Add(new Game
                {
                    ID = gameId++, SeasonID = RegularSeasonID, Date = new DateTime(2024, 6, 1 + i + j),
                    HostTeamID = teams[i].ID, VisitingTeamID = teams[j].ID, LocationID = park.ID,
                    StatusID = statuses[forfeit ? "Forfeit (Away)" : "Played"],
                    ScoreHost = forfeit ? null : 5, ScoreVisitor = forfeit ? null : 2,
                });
            }
        }

        db.Tournaments.Add(new Tournament
        {
            ID = 7, SeasonID = PlayoffsID,
            Brackets =
            [
                new TournamentBracket
                {
                    ID = 70, TournamentID = 7, Name = "Main", Format = "Fixed", Historical = true,
                    SeedingConfiguration = null, // legacy: no rules stored
                    Rounds =
                    [
                        new BracketRound
                        {
                            ID = 700, BracketID = 70, Name = "Semi-finals",
                            Series =
                            [
                                new RoundSeries { ID = 7000, RoundID = 700, Number = 1, Format = "Best of", HostOrder = "1", Matchup = "#1-#4" },
                                new RoundSeries { ID = 7001, RoundID = 700, Number = 2, Format = "Best of", HostOrder = "1", Matchup = "#2-#3" },
                            ],
                        },
                        new BracketRound
                        {
                            ID = 701, BracketID = 70, Name = "Finals",
                            Series = [new RoundSeries { ID = 7002, RoundID = 701, Number = 3, Format = "Best of", HostOrder = "1", Matchup = "w1-w2" }],
                        },
                    ],
                },
            ],
            RoundRobins =
            [
                new TournamentRoundRobin { ID = 80, TournamentID = 7, Name = "Consolation", Historical = false, SeedingConfiguration = "" },
            ],
        });
        db.SaveChanges();
    }

    /// <summary>Loads the tournament the way Controllers/Playoffs.cs does, then runs Populate.</summary>
    async Task<Tournament> LoadAndPopulateAsync()
    {
        using var db = NewContext();
        var playoffs = await db.Seasons
            .AsSplitQuery()
            .Include(s => s.Tournaments).ThenInclude(t => t.Brackets).ThenInclude(b => b.Rounds).ThenInclude(r => r.Series).ThenInclude(s => s.Games).ThenInclude(g => g.Game)
            .Include(s => s.Tournaments).ThenInclude(t => t.RoundRobins).ThenInclude(r => r.Games).ThenInclude(g => g.Game)
            .Where(s => s.Year == 2024 && s.Subseason == "Playoffs")
            .FirstAsync();
        var playoffGames = await db.Games.AsNoTracking()
            .Include(g => g.HostTeam).Include(g => g.VisitingTeam).Include(g => g.Status).Include(g => g.Location)
            .Where(g => g.SeasonID == playoffs.ID)
            .ToListAsync();

        var tournament = playoffs.Tournaments.Single();
        await tournament.Populate(playoffGames, db);
        return tournament;
    }

    [Fact]
    public async Task BracketWithNoSeedingConfiguration_SeedsFromRegularSeason_OnAFreshContext()
    {
        SeedLegacyYear();

        var tournament = await LoadAndPopulateAsync();

        var bracket = tournament.Brackets.Single();
        bracket.Seeds.Keys.Should().BeEquivalentTo([1, 2, 3, 4]);
        bracket.Seeds.OrderBy(s => s.Key).Select(s => s.Value.Name).Should().Equal("Team1", "Team2", "Team3", "Team4");

        var semi = bracket.Rounds.Single(r => r.Name == "Semi-finals").Series.Single(s => s.Number == 1);
        semi.Spots.Item1.Team!.Name.Should().Be("Team1");
        semi.Spots.Item2.Team!.Name.Should().Be("Team4");
    }

    [Fact]
    public async Task ForfeitInTheRegularSeason_StillCountsTowardLegacySeeding()
    {
        SeedLegacyYear();

        var tournament = await LoadAndPopulateAsync();

        // Team1's win over Team2 was a forfeit with null scores. If Status were
        // not loaded it would be skipped, leaving Team1 and Team2 tied 2-0 and
        // the seeding order down to the name fallback rather than the record.
        tournament.Brackets.Single().Seeds[1].Name.Should().Be("Team1");
    }

    [Fact]
    public async Task PoolWithNoSeedingConfiguration_SeatsEveryRegularSeasonTeam()
    {
        SeedLegacyYear();

        var tournament = await LoadAndPopulateAsync();

        var pool = tournament.RoundRobins.Single();
        pool.Seeds.Should().HaveCount(4);
        pool.Standings!.Keys.Select(t => t.Name).Should().BeEquivalentTo("Team1", "Team2", "Team3", "Team4");
    }
}
