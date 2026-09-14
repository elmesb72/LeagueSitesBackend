using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/History")]
public class APIHistoryController(LeagueSitesContext dbContext) : ControllerBase
{
    [ResponseCache(Duration = 30)]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var seasons = await dbContext.Seasons
            .AsSplitQuery()
            .Include(s => s.Games)
                .ThenInclude(g => g.HostTeam)
            .Include(s => s.Games)
                .ThenInclude(g => g.VisitingTeam)
            .Include(s => s.Games)
                .ThenInclude(g => g.Status)
            .Include(s => s.Tournaments)
                .ThenInclude(t => t.Brackets)
                    .ThenInclude(b => b.Rounds)
                        .ThenInclude(r => r.Series)
                            .ThenInclude(s => s.Games)
            .Include(s => s.Tournaments)
                .ThenInclude(t => t.RoundRobins)
                    .ThenInclude(r => r.Games)
            .ToArrayAsync();

        var years = seasons
            .GroupBy(s => s.Year)
            .Select(yg => new Year(yg.Key, [.. yg]))
            .ToList();

        foreach (var year in years)
        {
            if (year.HasPlayoffs() && year.PlayoffsTournament != null
                && year.Playoffs != null && year.RegularSeasonStandings != null)
            {
                await year.PlayoffsTournament.Populate([.. year.Playoffs.Games], dbContext);
            }
            // Mid-season tournaments are their own seasons; resolve each one so its
            // titles can join the champion column.
            foreach (var cup in year.TournamentSeasons)
            {
                var tournament = cup.Tournaments.OrderBy(t => t.ID).FirstOrDefault();
                if (tournament is not null)
                    await tournament.Populate([.. cup.Games], dbContext);
            }
        }

        // Add/overwrite from DB-stored site config history
        var siteConfig = await dbContext.SiteConfigs.AsNoTracking().FirstOrDefaultAsync();
        if (siteConfig is not null)
        {
            var history = System.Text.Json.JsonSerializer.Deserialize<List<ConfigurationYear>>(
                siteConfig.HistoryJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
                ?? [];
            foreach (var entry in history)
            {
                var existing = years.FirstOrDefault(y => y.CalendarYear == entry.Year);
                if (existing != null)
                {
                    existing.ExceptionYearDescription = entry.Result;
                }
                else
                {
                    years.Add(new Year(entry.Year, entry.Result));
                }
            }
        }

        years = [.. years.OrderByDescending(y => y.CalendarYear)];

        return Ok(years.Select(HistoryYearDto.From));
    }
}

record ConfigurationYear(int Year, string Result);
