using FluentAssertions;

namespace LeagueSitesBackend.Tests;

public class SeasonKindTests
{
    static Season Make(string kind, string name) => new()
    {
        Year = 2027,
        Subseason = kind,
        Name = name,
        StartDate = new DateTime(2027, 5, 1),
    };

    [Fact]
    public void Regular_seasons_and_playoffs_are_always_named_the_same_way()
    {
        SeasonKind.NameFor(2027, SeasonKind.RegularSeason).Should().Be("2027 Regular Season");
        SeasonKind.NameFor(2027, SeasonKind.Playoffs).Should().Be("2027 Playoffs");
        // Whatever an executive might pass along is ignored for these kinds.
        SeasonKind.NameFor(2027, SeasonKind.Playoffs, "the playoffs!").Should().Be("2027 Playoffs");
    }

    [Fact]
    public void A_tournament_gets_the_year_in_front_of_the_name_the_executive_typed_once()
    {
        SeasonKind.NameFor(2027, SeasonKind.Tournament, "Canada Day Cup").Should().Be("2027 Canada Day Cup");
        SeasonKind.NameFor(2027, SeasonKind.Tournament, "  Canada Day Cup ").Should().Be("2027 Canada Day Cup");
        SeasonKind.NameFor(2027, SeasonKind.Tournament, "2027 Canada Day Cup").Should().Be("2027 Canada Day Cup", "not doubled");
        SeasonKind.NameFor(2027, SeasonKind.Tournament, "2026 Canada Day Cup").Should().Be("2027 2026 Canada Day Cup", "another year is just part of the name");
    }

    [Fact]
    public void Short_name_drops_the_leading_year()
    {
        SeasonKind.ShortName(Make(SeasonKind.Playoffs, "2027 Playoffs")).Should().Be("Playoffs");
        SeasonKind.ShortName(Make(SeasonKind.Tournament, "2027 Canada Day Cup")).Should().Be("Canada Day Cup");
        SeasonKind.ShortName(Make(SeasonKind.Tournament, "Odd Name")).Should().Be("Odd Name", "a name without the year is left alone");
    }

    [Fact]
    public void Only_playoffs_and_tournaments_have_a_public_bracket_page()
    {
        SeasonKind.HasTournamentPage(Make(SeasonKind.RegularSeason, "2027 Regular Season")).Should().BeFalse();
        SeasonKind.HasTournamentPage(Make(SeasonKind.Playoffs, "2027 Playoffs")).Should().BeTrue();
        SeasonKind.HasTournamentPage(Make(SeasonKind.Tournament, "2027 Cup")).Should().BeTrue();
        SeasonKind.Wire(Make(SeasonKind.Playoffs, "2027 Playoffs")).Should().Be("playoffs");
        SeasonKind.Wire(Make(SeasonKind.Tournament, "2027 Cup")).Should().Be("tournament");
    }

    [Fact]
    public void Reserved_titles_are_the_kind_names_case_insensitively()
    {
        SeasonKind.IsReservedTitle("playoffs").Should().BeTrue();
        SeasonKind.IsReservedTitle(" Regular Season ").Should().BeTrue();
        SeasonKind.IsReservedTitle("TOURNAMENT").Should().BeTrue();
        SeasonKind.IsReservedTitle("Canada Day Cup").Should().BeFalse();
    }
}
