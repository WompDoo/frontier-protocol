#nullable enable
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// All pure save-data types for the cell/run system.
/// CellContentDef is regenerated each session from (biome, cellSeed) — never stored.
/// CellRecord holds everything that persists to disk.
/// </summary>

// ── Enums ─────────────────────────────────────────────────────────────────────

public enum SpeciesCategory { Flora, Fauna }
public enum SpeciesRarity   { Common, Rare, UltraRare }

// ── Species definition ────────────────────────────────────────────────────────

/// <summary>
/// One entry in a cell's species pool. IDs are local to the cell (0-based index
/// assigned by CellContentDef.Generate — flora first, fauna after).
/// </summary>
public record SpeciesDef(
	int             Id,
	SpeciesCategory Category,
	SpeciesRarity   Rarity,
	float           BarWeight,        // fraction this species contributes to its category bar
	float           BaseSpawnChance   // per-run probability; pity counter adjusts UltraRare at runtime
);

// ── Cell content definition (regenerated, never saved) ────────────────────────

/// <summary>
/// The complete deterministic content definition for one globe grid cell.
/// Generated from (biome, cellSeed). Regenerate freely — it's always identical.
/// </summary>
public class CellContentDef
{
	public readonly SpeciesDef[] Flora;
	public readonly SpeciesDef[] Fauna;
	public readonly int          ResourceNodeCount;    // denominator for ResourceBar
	public readonly int          EnemyCampCount;       // denominator for IntelBar
	public readonly int          AccessibleChunkCount; // denominator for GeoBar

	private CellContentDef(SpeciesDef[] flora, SpeciesDef[] fauna,
	                        int resources, int camps, int chunks)
	{
		Flora                = flora;
		Fauna                = fauna;
		ResourceNodeCount    = resources;
		EnemyCampCount       = camps;
		AccessibleChunkCount = chunks;
	}

	// ── Pool sizes by biome ───────────────────────────────────────────────────

	/// <summary>Returns (common, rare, ultra-rare) species counts for a category in a biome.</summary>
	private static (int c, int r, int u) PoolSize(BiomeType biome, SpeciesCategory cat)
		=> (biome, cat) switch
		{
			// Home base — commons + 1 rare only, no ultra-rare (tutorial cell)
			(BiomeType.SafeZone,   SpeciesCategory.Flora) => (4, 1, 0),
			(BiomeType.SafeZone,   SpeciesCategory.Fauna) => (4, 1, 0),

			// Dense / high-biodiversity biomes
			(BiomeType.Jungle,     SpeciesCategory.Flora) => (10, 3, 2),
			(BiomeType.Jungle,     SpeciesCategory.Fauna) => (9,  3, 1),
			(BiomeType.Forest,     SpeciesCategory.Flora) => (9,  3, 1),
			(BiomeType.Forest,     SpeciesCategory.Fauna) => (8,  2, 1),
			(BiomeType.AlienWilds, SpeciesCategory.Flora) => (8,  2, 1),
			(BiomeType.AlienWilds, SpeciesCategory.Fauna) => (7,  2, 1),

			// Moderate biomes
			(BiomeType.Grassland,  SpeciesCategory.Flora) => (6,  2, 1),
			(BiomeType.Grassland,  SpeciesCategory.Fauna) => (6,  2, 1),
			(BiomeType.Savanna,    SpeciesCategory.Flora) => (5,  2, 1),
			(BiomeType.Savanna,    SpeciesCategory.Fauna) => (6,  2, 1),
			(BiomeType.Coastal,    SpeciesCategory.Flora) => (5,  1, 1),
			(BiomeType.Coastal,    SpeciesCategory.Fauna) => (5,  2, 1),

			// Sparse / harsh biomes (compensated by more resources/POIs)
			(BiomeType.Desert,     SpeciesCategory.Flora) => (4,  1, 1),
			(BiomeType.Desert,     SpeciesCategory.Fauna) => (4,  1, 1),
			(BiomeType.Highland,   SpeciesCategory.Flora) => (4,  2, 1),
			(BiomeType.Highland,   SpeciesCategory.Fauna) => (4,  1, 1),
			(BiomeType.Mountain,   SpeciesCategory.Flora) => (3,  1, 1),
			(BiomeType.Mountain,   SpeciesCategory.Fauna) => (3,  1, 1),
			(BiomeType.Arctic,     SpeciesCategory.Flora) => (2,  1, 1),
			(BiomeType.Arctic,     SpeciesCategory.Fauna) => (3,  1, 1),

			// Water biomes — not deployed to, no species
			_ => (0, 0, 0),
		};

	// ── Pool builder ──────────────────────────────────────────────────────────

	/// <summary>
	/// Builds a species pool for one category.
	/// Bar weight distribution: commons = 45%, rares = 35%, ultra-rares = 20%.
	/// This guarantees: (all commons + all rares) ≈ 80% regardless of pool size.
	/// </summary>
	private static SpeciesDef[] BuildPool(SpeciesCategory cat, BiomeType biome, ref int id)
	{
		var (cCount, rCount, uCount) = PoolSize(biome, cat);
		int total = cCount + rCount + uCount;
		if (total == 0) return Array.Empty<SpeciesDef>();

		float cW = cCount > 0 ? 0.45f / cCount : 0f;
		float rW = rCount > 0 ? 0.35f / rCount : 0f;
		float uW = uCount > 0 ? 0.20f / uCount : 0f;

		var pool = new SpeciesDef[total];
		int  i   = 0;
		for (int n = 0; n < cCount; n++) pool[i++] = new SpeciesDef(id++, cat, SpeciesRarity.Common,    cW, 0.92f);
		for (int n = 0; n < rCount; n++) pool[i++] = new SpeciesDef(id++, cat, SpeciesRarity.Rare,      rW, 0.35f);
		for (int n = 0; n < uCount; n++) pool[i++] = new SpeciesDef(id++, cat, SpeciesRarity.UltraRare, uW, 0.02f);
		return pool;
	}

	// ── Factory ───────────────────────────────────────────────────────────────

	public static CellContentDef Generate(BiomeType biome, int cellSeed)
	{
		var rng = new Random(cellSeed);
		int id  = 0;

		var flora = BuildPool(SpeciesCategory.Flora, biome, ref id);
		var fauna = BuildPool(SpeciesCategory.Fauna, biome, ref id);

		// Resource nodes and enemy camps: more in sparse biomes to compensate for low species counts
		int resources = biome is BiomeType.Mountain or BiomeType.Highland or BiomeType.Arctic
			? rng.Next(8, 14)   // mineral-rich
			: rng.Next(4, 9);   // standard

		int camps  = rng.Next(2, 5);   // 2–4 enemy camps
		int chunks = rng.Next(25, 36); // 25–35 accessible chunks (updated when RunArea is implemented)

		return new CellContentDef(flora, fauna, resources, camps, chunks);
	}
}

// ── Cell record (persisted per save) ─────────────────────────────────────────

/// <summary>
/// All save-persistent state for one globe grid cell.
/// Bar progress is always recomputed from the discovery sets — never stored directly.
/// </summary>
public class CellRecord
{
	// ── Identity ──────────────────────────────────────────────────────────────

	/// <summary>Globe grid position (gi 0..23, gj 0..11).</summary>
	public Vector2I  GridIndex { get; init; }
	public BiomeType Biome     { get; init; }
	/// <summary>Deterministic hash of (worldSeed, gi, gj). Used to regenerate CellContentDef.</summary>
	public int       CellSeed  { get; init; }

	// ── Persistent state ──────────────────────────────────────────────────────

	public bool IsUnlocked { get; set; }
	public bool IsHomeBase { get; set; }
	public int  RunCount   { get; set; }

	// Discovery sets
	public HashSet<Vector2I> ExploredChunks     { get; } = new();
	public HashSet<int>      ScannedFlora        { get; } = new(); // species IDs
	public HashSet<int>      CapturedFauna       { get; } = new(); // species IDs
	public HashSet<int>      TaggedResources     { get; } = new(); // resource node IDs
	public HashSet<int>      ExtractedResources  { get; } = new(); // taken — won't respawn
	public HashSet<int>      RevealedCamps       { get; } = new(); // enemy camp IDs

	/// <summary>Pity counters: speciesId → consecutive-run-miss count.</summary>
	public Dictionary<int, int> UltraRareMisses  { get; } = new();

	// ── Bar computation ───────────────────────────────────────────────────────

	public float ComputeFloraBar(CellContentDef def)
		=> ComputeSpeciesBar(ScannedFlora, def.Flora);

	public float ComputeFaunaBar(CellContentDef def)
		=> ComputeSpeciesBar(CapturedFauna, def.Fauna);

	public float ComputeGeoBar(CellContentDef def)
		=> def.AccessibleChunkCount == 0 ? 0f
		   : Math.Min(1f, ExploredChunks.Count / (float)def.AccessibleChunkCount);

	public float ComputeResourceBar(CellContentDef def)
		=> def.ResourceNodeCount == 0 ? 0f
		   : Math.Min(1f, TaggedResources.Count / (float)def.ResourceNodeCount);

	public float ComputeIntelBar(CellContentDef def)
		=> def.EnemyCampCount == 0 ? 0f
		   : Math.Min(1f, RevealedCamps.Count / (float)def.EnemyCampCount);

	/// <summary>Returns true when all 5 bars are at or above the threshold (default 80%).</summary>
	public bool AllBarsAtThreshold(CellContentDef def, float threshold = 0.8f)
		=> ComputeFloraBar(def)    >= threshold
		&& ComputeFaunaBar(def)    >= threshold
		&& ComputeGeoBar(def)      >= threshold
		&& ComputeResourceBar(def) >= threshold
		&& ComputeIntelBar(def)    >= threshold;

	private static float ComputeSpeciesBar(HashSet<int> found, SpeciesDef[] pool)
	{
		if (pool.Length == 0) return 1f; // vacuously complete (water biomes, etc.)
		float total = 0f;
		foreach (var s in pool)
			if (found.Contains(s.Id))
				total += s.BarWeight;
		return Math.Min(1f, total);
	}

	// ── Pity timer ────────────────────────────────────────────────────────────

	/// <summary>
	/// Per-run adjusted spawn chance for an ultra-rare, including pity escalation.
	/// Call once per ultra-rare at run-start to decide whether it spawns.
	/// </summary>
	public float GetUltraRareChance(SpeciesDef species)
	{
		if (species.Rarity != SpeciesRarity.UltraRare) return species.BaseSpawnChance;
		UltraRareMisses.TryGetValue(species.Id, out int misses);
		return species.BaseSpawnChance + misses * 0.015f;
	}

	/// <summary>
	/// Call at end of a run where this ultra-rare did NOT spawn.
	/// Returns true when the accumulated chance has reached the auto-spawn threshold (≥76%) —
	/// the caller should force-spawn the species on the very next run.
	/// </summary>
	public bool IncrementPity(SpeciesDef species)
	{
		if (species.Rarity != SpeciesRarity.UltraRare) return false;
		UltraRareMisses[species.Id] = UltraRareMisses.GetValueOrDefault(species.Id, 0) + 1;
		return GetUltraRareChance(species) >= 0.76f;
	}

	/// <summary>Reset pity counter after the species has been successfully captured.</summary>
	public void ResetPity(SpeciesDef species)
		=> UltraRareMisses.Remove(species.Id);
}
