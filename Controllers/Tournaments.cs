using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Public read-only tournaments: the year's list (playoffs and mid-season cups)
/// and one tournament in the shape the bracket page renders.
/// </summary>
[ApiController]
[Route("api/Tournaments")]
public class APITournamentsController(
    IPublicTournamentService publicTournaments,
    ISeasonService seasonService) : ControllerBase
{
    [ResponseCache(Duration = 30)]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? year)
    {
        var targetYear = year;
        if (targetYear is null)
        {
            var closest = await seasonService.GetClosestSeasonAsync();
            if (closest is null) return Ok(new List<TournamentSummaryDto>());
            targetYear = (int)closest.Year;
        }
        return Ok(await publicTournaments.ListAsync(targetYear.Value));
    }

    [ResponseCache(Duration = 30)]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get([FromRoute] long id)
    {
        var tournament = await publicTournaments.LoadPopulatedAsync(id);
        if (tournament is null) return NotFound($"Tournament {id} not found.");
        return Ok(publicTournaments.ToDto(tournament));
    }
}
