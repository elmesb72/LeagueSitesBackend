/// <summary>
/// The three kinds of Season, as stored in Season.Subseason. Regular seasons and
/// playoffs are the two the site has always had; a Tournament season is a
/// mid-season cup or weekend event with its own brackets, pools and public page,
/// whose games never count toward the regular season. One tournament per season.
/// </summary>
public static class SeasonKind
{
    public const string RegularSeason = "Regular Season";
    public const string Playoffs = "Playoffs";
    public const string Tournament = "Tournament";

    public static bool IsRegularSeason(Season season) => season.Subseason == RegularSeason;
    public static bool IsPlayoffs(Season season) => season.Subseason == Playoffs;
    public static bool IsTournament(Season season) => season.Subseason == Tournament;

    /// <summary>Playoffs and tournament seasons have a public bracket page; regular seasons do not.</summary>
    public static bool HasTournamentPage(Season season) => IsPlayoffs(season) || IsTournament(season);

    /// <summary>The kind as the public API spells it.</summary>
    public static string Wire(Season season) => IsPlayoffs(season) ? "playoffs" : "tournament";

    /// <summary>
    /// The stored name for a new season. Regular seasons and playoffs are always
    /// "{year} Regular Season" / "{year} Playoffs" — executives never type these, so
    /// every year reads the same way. A tournament is "{year} {name}" from the name
    /// the executive typed; a name that already starts with the year is not doubled.
    /// </summary>
    public static string NameFor(long year, string kind, string? tournamentName = null)
    {
        if (kind != Tournament) return $"{year} {kind}";
        var name = (tournamentName ?? "").Trim();
        var prefix = $"{year} ";
        if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..].Trim();
        return $"{year} {name}";
    }

    /// <summary>
    /// The name without its leading year — "Playoffs", "Canada Day Cup" — for tags
    /// and chips that already sit under a year.
    /// </summary>
    public static string ShortName(Season season)
    {
        var prefix = $"{season.Year} ";
        return season.Name.StartsWith(prefix, StringComparison.Ordinal)
            ? season.Name[prefix.Length..]
            : season.Name;
    }

    /// <summary>Names a cup may not take, because they would read as the other kinds.</summary>
    public static bool IsReservedTitle(string title) =>
        string.Equals(title.Trim(), RegularSeason, StringComparison.OrdinalIgnoreCase)
        || string.Equals(title.Trim(), Playoffs, StringComparison.OrdinalIgnoreCase)
        || string.Equals(title.Trim(), Tournament, StringComparison.OrdinalIgnoreCase);
}
