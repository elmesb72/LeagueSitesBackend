public partial class Season : IEquatable<Season>
{
    public Season()
    {
        Games = [];
        Tournaments = [];
    }

    public long ID { get; set; }
    public long Year { get; set; }
    /// <summary>The kind of season: one of the SeasonKind constants.</summary>
    public required string Subseason { get; set; }
    /// <summary>
    /// The season's display name, stored: "2026 Regular Season", "2026 Playoffs",
    /// "2027 Canada Day Cup". Set at creation (see SeasonKind.NameFor).
    /// </summary>
    public required string Name { get; set; }
    public DateTime StartDate { get; set; }

    /// <summary>
    /// This season's standings rules (StandingsConfig JSON), frozen at
    /// creation by copying the latest season's rules. Scoped per season so
    /// later rule changes never re-rank historical seasons. Empty string
    /// means platform defaults; parse via StandingsConfigService.Parse.
    /// </summary>
    public string StandingsJson { get; set; } = "";

    public virtual ICollection<Game> Games { get; set; }
    public virtual ICollection<Tournament> Tournaments { get; set; }

    public bool Equals(Season? other) => ID == other?.ID;
    public override bool Equals(object? obj) => Equals(obj as Season);
    public override int GetHashCode() => Convert.ToInt32(ID);
    public static bool operator ==(Season? left, Season? right) => left is null ? right is null : left.Equals(right);
    public static bool operator !=(Season? left, Season? right) => !(left == right);
}
