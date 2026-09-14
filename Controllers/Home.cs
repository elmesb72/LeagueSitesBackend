using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/Home")]
public class APIHomeController(
    LeagueSitesContext dbContext,
    ISeasonService seasonService,
    IPermissionsService permissionsService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var startOfWeek = DateTime.Today.AddDays(-7);
        var endOfWeek = DateTime.Today.AddDays(7);

        var games = await dbContext.Games
            .AsNoTracking()
            .Include(g => g.Season)
            .Include(g => g.HostTeam)
            .Include(g => g.VisitingTeam)
            .Include(g => g.Location)
            .Include(g => g.Status)
            .Where(g =>
                (g.Date >= startOfWeek && g.Date < endOfWeek
                    && g.Status!.Name != "Cancelled" && g.Status.Name != "Deleted")
                || (g.Date <= DateTime.Now && g.Status!.Name == "Upcoming"))
            .OrderBy(g => g.Date)
            .ToListAsync();

        var news = await GetFilteredNews();

        var closestSeason = await seasonService.GetClosestSeasonAsync();
        Standings? standings = null;
        bool isPlayoffs = false;
        // This year's mid-season tournaments, for a callout beside the playoffs one.
        // Like isPlayoffs, present for the rest of the year once the cup exists.
        List<TournamentLinkDto> tournaments = [];

        if (closestSeason is not null)
        {
            var cupSeasons = await dbContext.Seasons
                .AsNoTracking()
                .Include(s => s.Tournaments)
                .Where(s => s.Year == closestSeason.Year && s.Subseason == SeasonKind.Tournament)
                .ToListAsync();
            tournaments = TournamentLinkDto.Of(cupSeasons);

            var seasonGames = await dbContext.Games
                .AsNoTracking()
                .Include(g => g.HostTeam)
                .Include(g => g.VisitingTeam)
                .Include(g => g.Status)
                .Where(g => g.SeasonID == closestSeason.ID)
                .Where(g => g.Status!.Name != "Deleted")
                .ToListAsync();
            standings = new Standings(seasonGames, StandingsConfigService.Parse(closestSeason.StandingsJson));
            standings.CalculateStreaks();

            isPlayoffs = await dbContext.Seasons
                .AnyAsync(s => s.Year == closestSeason.Year && s.Subseason == "Playoffs");
        }

        var permissions = await permissionsService.GetAsync(User);
        var canPost = permissions.Allow("Post");

        return Ok(new
        {
            games = games.Select(g => new GameSummaryDto(g)),
            news = news.Select(n => new
            {
                news = new NewsSummaryDto(n),
                renderedContents = n.RenderContents(),
                canEdit = permissions.Include([PermissionsScope.Executive, PermissionsScope.Webmaster])
                    || permissions.User?.ID == n.AuthorID
            }),
            standings = standings?.ToDto(),
            isPlayoffs,
            tournaments,
            canPost
        });
    }

    async Task<List<News>> GetFilteredNews()
    {
        var allNews = await dbContext.News
            .AsNoTracking()
            .Include(n => n.Author)
                .ThenInclude(u => u!.UserLogins)
            .Include(n => n.Author)
                .ThenInclude(u => u!.Invitations)
                    .ThenInclude(i => i.Team)
            .Where(n => !n.IsDeleted && !n.IsHidden)
            .OrderByDescending(n => n.Date)
            .ToListAsync();

        IEnumerable<News> filtered = allNews;

        var siteConfig = await dbContext.SiteConfigs.AsNoTracking().FirstOrDefaultAsync();
        if (siteConfig is not null)
        {
            var home = System.Text.Json.JsonSerializer.Deserialize<SiteHomeConfig>(
                siteConfig.HomeJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase })
                ?? new SiteHomeConfig();

            var recent = allNews
                .Where(n => DateTime.Compare(n.Date, DateTime.Now.AddDays(-home.NewsMaxAgeDays)) >= 0)
                .ToList();
            filtered = recent.Count < home.NewsMinItems
                ? allNews.Take(home.NewsMinItems)
                : recent;
        }

        // Include hidden news authored by the current user
        if (User.Identity?.IsAuthenticated == true)
        {
            var uid = Convert.ToInt64(User.Claims.First(c => c.Type == "UserID").Value);
            var hiddenAuthored = await dbContext.News
                .AsNoTracking()
                .Include(n => n.Author)
                    .ThenInclude(u => u!.UserLogins)
                .Include(n => n.Author)
                    .ThenInclude(u => u!.Invitations)
                        .ThenInclude(i => i.Team)
                .Where(n => n.IsHidden && !n.IsDeleted && n.AuthorID == uid)
                .ToListAsync();
            filtered = filtered.Concat(hiddenAuthored).OrderByDescending(n => n.Date);
        }

        return filtered.ToList();
    }
}
