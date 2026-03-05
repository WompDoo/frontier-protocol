#nullable enable
using System;
using System.Collections.Generic;

/// <summary>
/// Procedurally generates scouts from seed values.
/// Pure C# — no Godot dependency.
///
/// Generation pipeline:
///   1. Pick background (determines stat lean)
///   2. Roll base stats (8–16 range) + apply background mods
///   3. Determine archetype (Plain Joe / Specialist / Golden Child / Wildcard)
///   4. Draw traits from the appropriate pools
///   5. Assemble name
/// </summary>
public static class ScoutGenerator
{
	// ── Name tables ───────────────────────────────────────────────────────

	private static readonly string[] FirstNames =
	{
		// Female
		"Sloane", "Mira",  "Yara",  "Priya", "Lena",  "Nadia", "Suki",  "Amara",
		"Freya",  "Cass",  "Zora",  "Yuki",  "Dima",  "Lupe",  "Kezia", "Ife",
		// Male
		"Marcus", "Tomás", "Kenji", "Leon",  "Dmitri","Yusuf", "Diego", "Finn",
		"Ravi",   "Chidi", "Elan",  "Femi",  "Sören", "Bram",  "Oren",  "Idris",
		// Neutral
		"Devon",  "Alex",  "Sam",   "Quinn", "Kai",   "Rue",   "Sable", "Echo",
		"Lux",    "Ren",   "Shay",  "Tove",
	};

	private static readonly string[] LastNames =
	{
		"Nwosu",    "Okonkwo",  "Chen",     "Petrova",  "Álvarez",  "Nakamura",
		"Reyes",    "Kowalski", "Diallo",   "Singh",    "Walsh",    "Hassan",
		"Obi",      "Mwangi",   "Torres",   "Müller",   "Lee",      "Johansson",
		"Patel",    "Kimura",   "Novak",    "Espinoza", "Mensah",   "Krause",
		"Ozdemir",  "Delacroix","Vasquez",  "Adebayo",  "Ferreira", "Yamamoto",
		"Dlamini",  "Leblanc",  "Osei",     "Volkov",   "Serrano",  "Lindqvist",
	};

	/// <summary>
	/// Field nicknames — earned the hard way.
	/// ~40% of generated scouts receive one.
	/// </summary>
	private static readonly string[] Nicknames =
	{
		"Lech",    "Jinx",    "Doc",     "Ghost",   "Fossil",  "Torch",
		"Skip",    "Rust",    "Nails",   "Fuse",    "Crunch",  "Blink",
		"Scorch",  "Tangle",  "Crow",    "Patches", "Wrench",  "Lace",
		"Fade",    "Knack",   "Snag",    "Drift",   "Twitch",  "Haze",
	};

	// ── Public API ────────────────────────────────────────────────────────

	/// <summary>
	/// Generates a complete scout from a deterministic seed.
	/// Pass (worldSeed ^ index) for a roster of unique scouts.
	/// </summary>
	public static Scout Generate(int seed)
	{
		var rng = new Random(seed);

		var bg      = PickBackground(rng);
		var stats   = RollStats(bg, rng);
		var traits  = RollTraits(rng);
		var name    = RollName(rng);
		var sponsor = ScoutDatabase.SponsorNames[rng.Next(ScoutDatabase.SponsorNames.Length)];
		var hook    = bg.NarrativeHook.Replace("{corpo}", sponsor);

		var scout = new Scout
		{
			FirstName     = name.first,
			LastName      = name.last,
			Nickname      = name.nick,
			Background    = bg,
			BaseStats     = stats,
			Traits        = traits,
			Sponsor       = sponsor,
			NarrativeHook = hook,
		};
		scout.CurrentHP = scout.MaxHP;
		return scout;
	}

	// ── Generation steps ──────────────────────────────────────────────────

	private static BackgroundDef PickBackground(Random rng) =>
		ScoutDatabase.Backgrounds[rng.Next(ScoutDatabase.Backgrounds.Length)];

	private static Stats RollStats(BackgroundDef bg, Random rng)
	{
		// Base roll: 8 + [0..8] = 8–16, mean ≈ 12
		// Matches example scout Sloane: END:07 PRC:16 ING:15 AWR:10 RES:12 STR:08
		int end = 8 + rng.Next(9);
		int prc = 8 + rng.Next(9);
		int ing = 8 + rng.Next(9);
		int awr = 8 + rng.Next(9);
		int res = 8 + rng.Next(9);
		int str = 8 + rng.Next(9);

		// Background lean: +3 to high stat, −2 to low stat
		(end, prc, ing, awr, res, str) = ApplyMod(bg.HighStat, +3, end, prc, ing, awr, res, str);
		(end, prc, ing, awr, res, str) = ApplyMod(bg.LowStat,  -2, end, prc, ing, awr, res, str);

		return new Stats(end, prc, ing, awr, res, str);
	}

	private static (int e, int p, int i, int a, int r, int s) ApplyMod(
		string stat, int delta, int e, int p, int i, int a, int r, int s) =>
		stat switch
		{
			"END" => (Math.Clamp(e + delta, 1, 20), p, i, a, r, s),
			"PRC" => (e, Math.Clamp(p + delta, 1, 20), i, a, r, s),
			"ING" => (e, p, Math.Clamp(i + delta, 1, 20), a, r, s),
			"AWR" => (e, p, i, Math.Clamp(a + delta, 1, 20), r, s),
			"RES" => (e, p, i, a, Math.Clamp(r + delta, 1, 20), s),
			"STR" => (e, p, i, a, r, Math.Clamp(s + delta, 1, 20)),
			_     => (e, p, i, a, r, s),
		};

	private static List<TraitDef> RollTraits(Random rng)
	{
		var pos = ScoutDatabase.PositiveTraits;
		var de  = ScoutDatabase.DoubleEdgedTraits;

		// Archetype distribution:
		//   Plain Joe    20% — 1 trait
		//   Specialist   40% — Positive + Double-Edged
		//   Golden Child 25% — Positive + Positive
		//   Wildcard     15% — Double-Edged + Double-Edged
		double r = rng.NextDouble();

		if (r < 0.20) // Plain Joe
		{
			var pool = rng.NextDouble() < 0.60 ? pos : de;
			return new List<TraitDef> { pool[rng.Next(pool.Length)] };
		}

		if (r < 0.60) // Specialist
			return new List<TraitDef>
			{
				pos[rng.Next(pos.Length)],
				de[rng.Next(de.Length)],
			};

		if (r < 0.85) // Golden Child — two distinct positives
		{
			var t1 = pos[rng.Next(pos.Length)];
			TraitDef t2;
			do t2 = pos[rng.Next(pos.Length)]; while (t2.Id == t1.Id);
			return new List<TraitDef> { t1, t2 };
		}

		// Wildcard — two distinct double-edged
		{
			var t1 = de[rng.Next(de.Length)];
			TraitDef t2;
			do t2 = de[rng.Next(de.Length)]; while (t2.Id == t1.Id);
			return new List<TraitDef> { t1, t2 };
		}
	}

	private static (string first, string last, string? nick) RollName(Random rng) =>
	(
		FirstNames[rng.Next(FirstNames.Length)],
		LastNames[rng.Next(LastNames.Length)],
		rng.NextDouble() < 0.40 ? Nicknames[rng.Next(Nicknames.Length)] : null
	);
}
