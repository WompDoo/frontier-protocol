#nullable enable
using System.Collections.Generic;
using System.Text;

/// <summary>
/// All pure-data types for the scout system.
/// No Godot dependencies — freely serialisable and unit-testable.
/// </summary>

// ── Enums ──────────────────────────────────────────────────────────────────

public enum ScoutStatus
{
	Recruit,
	FieldScout,
	Veteran,
	Retired,
	Dead,
}

public enum TraitType { Positive, DoubleEdged }

// ── Value records ──────────────────────────────────────────────────────────

/// <summary>A scout's six core stats. HP is derived.</summary>
public record Stats(int END, int PRC, int ING, int AWR, int RES, int STR)
{
	/// <summary>HP = 50 + (STR × 3) + (END × 2) per GDD §5.2.</summary>
	public int MaxHP => 50 + STR * 3 + END * 2;
}

/// <summary>A trait definition — positive or double-edged.</summary>
public record TraitDef(
	string    Id,
	TraitType Type,
	string    Name,
	string    Description,
	string?   Pro = null,   // upside label for double-edged traits
	string?   Con = null    // downside label for double-edged traits
);

/// <summary>
/// A scout background. HighStat / LowStat are stat names (END/PRC/ING/AWR/RES/STR)
/// that receive +3 / −2 at generation, or "" if the lean is non-stat.
/// </summary>
public record BackgroundDef(
	int    Id,
	string Name,
	string NarrativeHook,
	string MechanicalLean,
	string HighStat,
	string LowStat
);

/// <summary>A trait clash — activated when both scouts are in the same party.</summary>
public record ClashDef(
	string TraitIdA,
	string TraitIdB,
	string Effect,
	string Flavour
);

// ── Scout class ────────────────────────────────────────────────────────────

/// <summary>
/// A living scout. Accumulates experience, traits, injuries, bonds, and scars
/// across the campaign. The central emotional unit of the game.
/// </summary>
public class Scout
{
	// ── Identity ───────────────────────────────────────────────────────────

	/// <summary>Short unique ID for save/load keying.</summary>
	public string        Id         { get; init; } = System.Guid.NewGuid().ToString("N")[..8];
	public string        FirstName  { get; init; } = "";
	public string        LastName   { get; init; } = "";
	/// <summary>Optional field nickname, e.g. "Lech". Null if none.</summary>
	public string?       Nickname   { get; init; }
	public BackgroundDef Background { get; init; } = null!;

	/// <summary>
	/// The corporate sponsor that owns this scout's debt.
	/// Resolved at generation; substituted into NarrativeHook where {corpo} appears.
	/// </summary>
	public string        Sponsor      { get; init; } = "";
	/// <summary>Background.NarrativeHook with {corpo} replaced by this scout's Sponsor.</summary>
	public string        NarrativeHook { get; init; } = "";

	// ── Stats & traits ────────────────────────────────────────────────────

	public Stats          BaseStats  { get; init; }  = null!;
	public List<TraitDef> Traits     { get; init; }  = new();

	// ── Lifecycle state ───────────────────────────────────────────────────

	public ScoutStatus    Status        { get; set; } = ScoutStatus.Recruit;
	public int            Level         { get; set; } = 1;
	public int            Xp            { get; set; } = 0;
	public int            RunsCompleted { get; set; } = 0;

	// ── Health state ──────────────────────────────────────────────────────

	public int CurrentHP { get; set; }

	/// <summary>
	/// Effective max HP, accounting for trait bonuses (e.g. Sturdy +20).
	/// In the future a full stat-modifier pipeline replaces this.
	/// </summary>
	public int MaxHP
	{
		get
		{
			int hp = BaseStats.MaxHP;
			foreach (var t in Traits)
				if (t.Id == "Sturdy") hp += 20;
			return hp;
		}
	}

	// ── Injury & bond lists (populated during play) ───────────────────────

	/// <summary>Active injuries (by TraitDef.Id). Empty at generation.</summary>
	public List<string> Injuries { get; } = new();

	/// <summary>Bond partner IDs and bond type. Empty at generation.</summary>
	public List<(string ScoutId, string BondType)> Bonds { get; } = new();

	// ── Derived labels ────────────────────────────────────────────────────

	/// <summary>"FirstName 'Nickname' LastName" or "FirstName LastName".</summary>
	public string FullName => Nickname is not null
		? $"{FirstName} \"{Nickname}\" {LastName}"
		: $"{FirstName} {LastName}";

	/// <summary>
	/// Archetype based on trait composition (GDD §5.6):
	/// Golden Child | Specialist | Wildcard | Plain Joe
	/// </summary>
	public string Archetype => Traits.Count switch
	{
		0 => "Unknown",
		1 => "Plain Joe",
		_ => (Traits[0].Type, Traits[1].Type) switch
		{
			(TraitType.Positive,   TraitType.Positive)   => "Golden Child",
			(TraitType.DoubleEdged, TraitType.DoubleEdged) => "Wildcard",
			_ => "Specialist",
		},
	};

	public override string ToString()
	{
		var sb = new StringBuilder();
		sb.AppendLine($"[{Archetype}] {FullName}  —  {Background.Name}  [{Sponsor}]");
		sb.AppendLine($"  HP: {MaxHP}   " +
		              $"END:{BaseStats.END:D2}  PRC:{BaseStats.PRC:D2}  ING:{BaseStats.ING:D2}  " +
		              $"AWR:{BaseStats.AWR:D2}  RES:{BaseStats.RES:D2}  STR:{BaseStats.STR:D2}");
		foreach (var t in Traits)
		{
			if (t.Type == TraitType.DoubleEdged)
				sb.AppendLine($"  ⚡ {t.Name}  |  Pro: {t.Pro}  |  Con: {t.Con}");
			else
				sb.AppendLine($"  ✦ {t.Name}  —  {t.Description}");
		}
		return sb.ToString().TrimEnd();
	}
}
