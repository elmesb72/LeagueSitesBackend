using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The year's playoffs in the public tournament shape. Kept as its own route because
/// the Playoffs page is addressed by year, not by tournament id.
/// </summary>
[ApiController]
[Route("api/Playoffs")]
public class APIPlayoffsController(
    LeagueSitesContext dbContext,
    ISeasonService seasonService,
    IPublicTournamentService publicTournaments) : ControllerBase
{
    [ResponseCache(Duration = 30)]
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? year)
    {
        Season? currentSeason;
        if (year == null)
        {
            currentSeason = await seasonService.GetClosestSeasonAsync();
        }
        else
        {
            currentSeason = await dbContext.Seasons
                .FirstOrDefaultAsync(s => s.Year == year && s.Subseason == SeasonKind.Playoffs);
        }

        if (currentSeason is null)
            return Ok(TournamentDto.Empty(null));

        var playoffs = await dbContext.Seasons
            .AsNoTracking()
            .Include(s => s.Tournaments)
            .Where(s => s.Year == currentSeason.Year && s.Subseason == SeasonKind.Playoffs)
            .FirstOrDefaultAsync();

        // No playoffs yet: answer with the season we do know about (the regular
        // season), so the page can still name the year.
        if (playoffs is null)
            return Ok(TournamentDto.Empty(currentSeason));

        var first = playoffs.Tournaments.OrderBy(t => t.ID).FirstOrDefault();
        var tournament = first is null ? null : await publicTournaments.LoadPopulatedAsync(first.ID);
        if (tournament is null)
            return Ok(TournamentDto.Empty(playoffs));

        return Ok(publicTournaments.ToDto(tournament));
    }
}
