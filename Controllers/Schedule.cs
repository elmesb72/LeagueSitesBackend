using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/Schedule")]
public class APIScheduleController(LeagueSitesContext dbContext, IPermissionsService permissionsService) : ControllerBase
{
    [ResponseCache(Duration = 30)]
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? year)
    {
        var targetYear = year ?? DateTime.Now.Year;

        var seasons = await dbContext.Seasons
            .AsNoTracking()
            .Include(s => s.Tournaments)
            .Where(s => s.Year == targetYear)
            .ToListAsync();

        var seasonIDs = seasons.Select(s => s.ID).ToList();
        var seasonStartDate = seasons
            .Where(s => s.Subseason == "Regular Season")
            .Select(s => s.StartDate)
            .FirstOrDefault();

        var games = await dbContext.Games
            .AsNoTracking()
            .Where(g => g.Status!.Name != "Deleted")
            .Where(g => seasonIDs.Contains(g.SeasonID))
            .Include(g => g.Status)
            .Include(g => g.HostTeam)
            .Include(g => g.VisitingTeam)
            .Include(g => g.Location)
            .Include(g => g.Season)
            .ToListAsync();

        // Code-side sort because the date column in SQLite is text
        games = [.. games.OrderBy(g => g.Date)];

        var locations = games
            .GroupBy(g => g.Location)
            .Select(p => p.Key)
            .OrderBy(p => p?.Name)
            .ToList();

        // If no games yet, provide active locations so the empty table has columns
        List<Location> activeLocations = [];
        if (!locations.Any())
        {
            activeLocations = await dbContext.Locations
                .AsNoTracking()
                .Where(l => l.Active)
                .OrderBy(l => l.Name)
                .ToListAsync();
        }

        var permissions = await permissionsService.GetAsync(User);
        var canCreateGame = permissions.Allow("CreateGame");

        return Ok(new
        {
            year = targetYear,
            seasonStartDate = seasonStartDate != default ? seasonStartDate.ToString("yyyy-MM-dd") : null,
            // Playoffs and mid-season tournament seasons, so rows in them can be
            // tagged and linked to their tournament page by game.season.id.
            tournaments = TournamentLinkDto.Of(seasons),
            games = games.Select(g => new GameSummaryDto(g)),
            locations = locations.Any()
                ? locations.Where(l => l != null).Select(l => new LocationSummaryDto(l!))
                : activeLocations.Select(l => new LocationSummaryDto(l)),
            canCreateGame
        });
    }
}
