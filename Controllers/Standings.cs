using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/Standings")]
public class APIStandingsController(
    LeagueSitesContext dbContext,
    ISeasonService seasonService) : ControllerBase
{
    [ResponseCache(Duration = 30)]
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? year)
    {
        Season? season;
        if (year == null)
        {
            season = await seasonService.GetClosestSeasonAsync();
        }
        else
        {
            season = await dbContext.Seasons
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Year == year && s.Subseason == "Regular Season");
        }

        if (season is null)
            return NotFound();

        var games = await dbContext.Games
            .AsNoTracking()
            .Include(g => g.HostTeam)
            .Include(g => g.VisitingTeam)
            .Include(g => g.Status)
            .Where(g => g.SeasonID == season.ID)
            .Where(g => g.Status!.Name != "Deleted")
            .ToListAsync();

        var standings = new Standings(games, StandingsConfigService.Parse(season.StandingsJson));
        standings.CalculateStreaks();

        // The year's playoffs and mid-season tournaments, so the page can point at them.
        var otherSeasons = await dbContext.Seasons
            .AsNoTracking()
            .Include(s => s.Tournaments)
            .Where(s => s.Year == season.Year && s.Subseason != SeasonKind.RegularSeason)
            .ToListAsync();

        return Ok(new
        {
            season = new SeasonSummaryDto(season),
            standings = standings.ToDto(),
            tournaments = TournamentLinkDto.Of(otherSeasons)
        });
    }
}
