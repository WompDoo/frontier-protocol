#nullable enable
using System.Collections.Generic;

/// <summary>
/// All pure-data types for the combat system.
/// No Godot dependencies — freely serialisable and unit-testable.
/// Trickles down from: Planet Seed → PlanetParams → ChunkData → CombatTileData
/// </summary>

// ── Tile properties ────────────────────────────────────────────────────────

/// <summary>Cover a tile provides to a unit standing on it.</summary>
public enum CoverType { None, Half, Full }

/// <summary>Elevation tier — High ground gives +10% hit, -10% to be hit.</summary>
public enum ElevationLevel { Low = 0, Mid = 1, High = 2 }

/// <summary>Movement cost through a tile.</summary>
public enum TilePassability { Open, Difficult, Impassable }

// ── Combat state ───────────────────────────────────────────────────────────

public enum CombatSide { Player, Enemy }

public enum CombatResult { Ongoing, Victory, Defeat, Escaped }

public enum CombatPhase
{
	Setup,
	PlayerTurn,
	EnemyTurn,
	Resolving,   // awaiting animation/event resolution
	Victory,
	Defeat,
}

// ── Status effects (flags so multiple can stack) ──────────────────────────

[System.Flags]
public enum StatusEffect
{
	None       = 0,
	Suppressed = 1 << 0,  // -1 AP next turn (pinned by fire)
	Burning    = 1 << 1,  // 3 damage at start of turn
	Stunned    = 1 << 2,  // skip entire turn
	Overwatch  = 1 << 3,  // react-fire on first enemy that moves into LoS
}

// ── Weapons ────────────────────────────────────────────────────────────────

public enum WeaponCategory { Unarmed, Pistol, Rifle, Shotgun, SniperRifle, Explosive, Melee }

/// <summary>
/// Immutable weapon definition. Instantiated from WeaponDB.
/// RangeMin = 0 means melee (adjacent).
/// AmmoCapacity = -1 means unlimited (melee / alien limbs).
/// </summary>
public record WeaponDef(
	string         Id,
	string         Name,
	WeaponCategory Category,
	int            DamageMin,
	int            DamageMax,
	int            RangeMin,        // tiles (0 = must be adjacent)
	int            RangeMax,        // tiles
	int            ApCost,
	float          BaseHitChance,   // 0.0–1.0 before modifiers
	int            AmmoCapacity,    // -1 = unlimited
	bool           AoE,             // 1-tile blast radius
	string         Description
);

// ── Abilities ──────────────────────────────────────────────────────────────

public record AbilityDef(
	string Id,
	string Name,
	int    ApCost,
	int    CooldownTurns,
	string Description
);

// ── Enemies ────────────────────────────────────────────────────────────────

public record EnemyDef(
	string    Id,
	string    Name,
	int       MaxHP,
	int       MoveRange,        // tiles moved per AP spent on Move action
	int       BaseInitiative,   // base value before random roll
	WeaponDef Weapon,
	string    Description
);

// ── Combat tile ────────────────────────────────────────────────────────────

/// <summary>
/// A single cell in the combat arena, derived from ChunkData.TileType.
/// Generated once at combat start by CombatGrid.GenerateFrom().
/// </summary>
public record CombatTileData(
	TileType        SourceTile,
	CoverType       Cover,
	ElevationLevel  Elevation,
	TilePassability Passability
);

// ── Combat action (used by AI and player input pipeline) ──────────────────

public enum ActionKind { Move, Attack, Overwatch, UseAbility, EndTurn }

public record CombatAction(
	ActionKind  Kind,
	Godot.Vector2I? TargetCell  = null,   // grid position for Move/Attack
	string?     AbilityId   = null    // ability to use
);

// ═══════════════════════════════════════════════════════════════════════════
// Weapon database
// ═══════════════════════════════════════════════════════════════════════════

public static class WeaponDB
{
	// ── Player weapons ─────────────────────────────────────────────────────

	public static readonly WeaponDef Fists = new(
		"fists", "Fists", WeaponCategory.Unarmed,
		DamageMin: 2, DamageMax: 4, RangeMin: 0, RangeMax: 1,
		ApCost: 1, BaseHitChance: 0.90f, AmmoCapacity: -1, AoE: false,
		"Bare hands. Better than nothing, barely."
	);

	public static readonly WeaponDef Pistol = new(
		"pistol", "Pistol", WeaponCategory.Pistol,
		DamageMin: 8, DamageMax: 14, RangeMin: 1, RangeMax: 4,
		ApCost: 1, BaseHitChance: 0.60f, AmmoCapacity: 12, AoE: false,
		"Reliable sidearm. Sweetspot: ranges 1-2."
	);

	public static readonly WeaponDef Rifle = new(
		"rifle", "Assault Rifle", WeaponCategory.Rifle,
		DamageMin: 10, DamageMax: 18, RangeMin: 1, RangeMax: 5,
		ApCost: 1, BaseHitChance: 0.50f, AmmoCapacity: 30, AoE: false,
		"Versatile mid-range weapon. Sweetspot: range 3."
	);

	public static readonly WeaponDef Shotgun = new(
		"shotgun", "Shotgun", WeaponCategory.Shotgun,
		DamageMin: 15, DamageMax: 25, RangeMin: 0, RangeMax: 3,
		ApCost: 1, BaseHitChance: 0.60f, AmmoCapacity: 8, AoE: false,
		"Devastating up close. Falls off hard past 2 tiles. Sweetspot: ranges 0-1."
	);

	public static readonly WeaponDef SniperRifle = new(
		"sniper", "Sniper Rifle", WeaponCategory.SniperRifle,
		DamageMin: 20, DamageMax: 35, RangeMin: 3, RangeMax: 7,
		ApCost: 2, BaseHitChance: 0.50f, AmmoCapacity: 5, AoE: false,
		"Long-range precision. Costs 2 AP. Ignores half cover. Sweetspot: ranges 4-6."
	);

	public static readonly WeaponDef Grenade = new(
		"grenade", "Frag Grenade", WeaponCategory.Explosive,
		DamageMin: 20, DamageMax: 30, RangeMin: 2, RangeMax: 5,
		ApCost: 2, BaseHitChance: 1.00f, AmmoCapacity: 2, AoE: true,
		"Area blast. Destroys half cover on hit tile. Limited supply."
	);

	// ── Enemy weapons ──────────────────────────────────────────────────────

	public static readonly WeaponDef AlienClaw = new(
		"alien_claw", "Alien Claw", WeaponCategory.Melee,
		DamageMin: 12, DamageMax: 20, RangeMin: 0, RangeMax: 1,
		ApCost: 1, BaseHitChance: 0.80f, AmmoCapacity: -1, AoE: false,
		"Serrated limb. Shreds armour on flanking hits."
	);

	public static readonly WeaponDef AlienSpit = new(
		"alien_spit", "Corrosive Spit", WeaponCategory.Rifle,
		DamageMin: 8, DamageMax: 14, RangeMin: 1, RangeMax: 4,
		ApCost: 1, BaseHitChance: 0.55f, AmmoCapacity: -1, AoE: false,
		"Acidic ranged attack. Chance to apply Burning."
	);

	public static readonly WeaponDef AlienThorax = new(
		"alien_thorax", "Thorax Slam", WeaponCategory.Melee,
		DamageMin: 18, DamageMax: 28, RangeMin: 0, RangeMax: 1,
		ApCost: 1, BaseHitChance: 0.65f, AmmoCapacity: -1, AoE: false,
		"Heavy crush. 25% chance to apply Stunned."
	);

	/// <summary>
	/// Range sweetspot modifier. Each weapon category has an ideal range band;
	/// hit chance rises toward the sweetspot and falls off outside it.
	/// </summary>
	public static float GetRangeMod(WeaponDef w, int range) => w.Category switch
	{
		// Rifle:     sweetspot range 3  (+20%), penalty at 1 and 5+
		WeaponCategory.Rifle => range switch
		{
			1    =>  0.00f,
			2    =>  0.10f,
			3    =>  0.20f,
			4    => -0.10f,
			5    => -0.20f,
			_    => range > 5 ? -0.30f : 0.00f,
		},
		// Shotgun:   sweetspot 0-1 (+25%), heavy falloff past 2
		WeaponCategory.Shotgun => range switch
		{
			<= 1 =>  0.25f,
			2    =>  0.00f,
			3    => -0.25f,
			_    => -0.45f,
		},
		// Sniper:    sweetspot 6-8 (+20%), penalty at short range
		WeaponCategory.SniperRifle => range switch
		{
			<= 3 => -0.20f,
			4    => -0.10f,
			5    =>  0.00f,
			6    =>  0.10f,
			7    =>  0.20f,
			8    =>  0.20f,
			_    =>  0.10f,
		},
		// Pistol:    sweetspot 1-2 (+15%), falloff past 3
		WeaponCategory.Pistol => range switch
		{
			<= 2 =>  0.15f,
			3    =>  0.00f,
			4    => -0.15f,
			_    => -0.25f,
		},
		// Melee / unarmed: no range penalty (must be adjacent anyway)
		_ => 0f,
	};

	public static IReadOnlyDictionary<string, WeaponDef> All { get; } = new Dictionary<string, WeaponDef>
	{
		{ Fists.Id,        Fists        },
		{ Pistol.Id,       Pistol       },
		{ Rifle.Id,        Rifle        },
		{ Shotgun.Id,      Shotgun      },
		{ SniperRifle.Id,  SniperRifle  },
		{ Grenade.Id,      Grenade      },
		{ AlienClaw.Id,    AlienClaw    },
		{ AlienSpit.Id,    AlienSpit    },
		{ AlienThorax.Id,  AlienThorax  },
	};
}

// ═══════════════════════════════════════════════════════════════════════════
// Enemy database
// ═══════════════════════════════════════════════════════════════════════════

public static class EnemyDB
{
	/// <summary>Fast and fragile. Harasses from range.</summary>
	public static readonly EnemyDef AlienScout = new(
		"alien_scout", "Alien Scout",
		MaxHP: 25, MoveRange: 4, BaseInitiative: 12,
		Weapon: WeaponDB.AlienSpit,
		"Fast and fragile. Prefers to spit from cover."
	);

	/// <summary>Armoured bruiser. Slow but hits hard.</summary>
	public static readonly EnemyDef AlienWarrior = new(
		"alien_warrior", "Alien Warrior",
		MaxHP: 50, MoveRange: 2, BaseInitiative: 7,
		Weapon: WeaponDB.AlienThorax,
		"Slow. Can stun on slam. Will march straight at you."
	);

	/// <summary>Flanker. Tries to attack from side or rear.</summary>
	public static readonly EnemyDef AlienLurker = new(
		"alien_lurker", "Alien Lurker",
		MaxHP: 35, MoveRange: 3, BaseInitiative: 10,
		Weapon: WeaponDB.AlienClaw,
		"Pathfinds around you. Gains cover-ignore from flanks."
	);

	public static IReadOnlyList<EnemyDef> All { get; } =
		new[] { AlienScout, AlienWarrior, AlienLurker };

	public static EnemyDef? Get(string id) =>
		id switch
		{
			"alien_scout"   => AlienScout,
			"alien_warrior" => AlienWarrior,
			"alien_lurker"  => AlienLurker,
			_               => null,
		};
}
