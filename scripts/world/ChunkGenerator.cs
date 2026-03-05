using Godot;
using System;

/// <summary>
/// Generates deterministic chunk data from a world seed + chunk coordinates.
/// Two-layer system: geography (stable, seeded) + activity (rolled fresh each run).
/// This class handles the geography layer only.
///
/// Biome assignment uses three independent noise layers:
///   Elevation  (FBm Perlin, low freq)     → broad continents, ocean basins
///   Moisture   (FBm Perlin, medium freq)  → vegetation / aridity
///   Temperature (latitude proxy)          → warm toward planet centre, cold at edges
/// </summary>
public static class ChunkGenerator
{
	/// <summary>Radius of the explorable planet in chunks (= half the latitude height).</summary>
	public const int PlanetRadius = 60;

	/// <summary>World width in chunks east-west. Longitude wraps at this boundary.</summary>
	public const int LonWidth  = 240;
	/// <summary>World height in chunks north-south. Latitude is bounded (clamped, no wrap).</summary>
	public const int LatHeight = 120;
	/// <summary>Southernmost (coldest) chunk Y. Centered at equator Y=0.</summary>
	public const int LatMin    = -PlanetRadius;     // -30
	/// <summary>Northernmost (coldest) chunk Y.</summary>
	public const int LatMax    =  PlanetRadius - 1; //  29

	// ── Planet parameters ─────────────────────────────────────────────────────

	public record PlanetParams(
		PlanetType Type,
		float      SeaLevel,
		float      VegBias,
		float      AlienFactor,
		float      RuinsDensity,
		float      GlobalTempMod   // applied to temp before Classify(); not inside Classify()
	);

	public static PlanetParams DeriveParams(int worldSeed)
	{
		var rng = new Random(worldSeed);
		float R() => (float)rng.NextDouble();

		// Deterministic type from seed — evenly distributed across 3 archetypes.
		var type = (PlanetType)(((worldSeed % 3) + 3) % 3);

		float seaLevel, vegBias, alienFactor, globalTempMod;
		switch (type)
		{
			case PlanetType.ArchipelagoWorld:
				// Mostly ocean, scattered islands. Threshold ~0.55-0.62 → ~65-75% surface water.
				seaLevel      = R() * 0.07f + 0.55f;
				vegBias       = R() * 0.20f + 0.55f;
				alienFactor   = R() * 0.25f + 0.30f;
				globalTempMod = R() * 0.10f + 0.05f;
				break;
			case PlanetType.ContinentalWorld:
				// Large landmasses with oceans. Threshold ~0.50-0.55 → ~50-62% surface water.
				seaLevel      = R() * 0.05f + 0.50f;
				vegBias       = R() * 0.30f + 0.35f;
				alienFactor   = R() * 0.25f + 0.20f;
				globalTempMod = R() * 0.10f - 0.05f;
				break;
			default: // JungleWorld
				// Dense vegetation, warm and wet. Threshold ~0.52-0.58 → ~58-67% surface water.
				seaLevel      = R() * 0.06f + 0.52f;
				vegBias       = R() * 0.15f + 0.75f;
				alienFactor   = R() * 0.30f + 0.40f;
				globalTempMod = R() * 0.12f + 0.10f;
				break;
		}

		float ruinsDensity = R() * 0.12f + 0.02f;
		return new PlanetParams(type, seaLevel, vegBias, alienFactor, ruinsDensity, globalTempMod);
	}

	// ── Biome assignment ──────────────────────────────────────────────────────

	/// <summary>Normalises a raw chunk X to [0, LonWidth). Works for negative values too.</summary>
	public static int NormX(int cx) => ((cx % LonWidth) + LonWidth) % LonWidth;

	public static BiomeType GetBiome(Vector2I coord, int worldSeed, PlanetParams planet)
	{
		// SafeZone near spawn (0,0), accounting for longitude wrap.
		int normX = NormX(coord.X);
		int dx    = normX > LonWidth / 2 ? normX - LonWidth : normX; // centre to [-60,59]
		if (Math.Abs(dx) + Math.Abs(coord.Y) < 2)
			return BiomeType.SafeZone;

		// Y out of bounds → polar cap (should only occur via direct calls, not chunk loading)
		if (coord.Y < LatMin || coord.Y > LatMax)
			return BiomeType.Arctic;

		float elev  = SampleElev(coord.X, coord.Y, worldSeed);
		float moist = SampleMoist(coord.X, coord.Y, worldSeed);

		// Temperature: warm at equator (Y=0), cold at poles. GlobalTempMod shifts whole planet.
		float temp = Mathf.Clamp(
			1f - MathF.Pow(MathF.Abs(coord.Y) / (float)PlanetRadius, 2f) * 0.85f + planet.GlobalTempMod,
			0f, 1f);

		moist = Mathf.Clamp(moist * (0.6f + planet.VegBias * 0.8f), 0f, 1f);

		return Classify(elev, moist, temp, planet);
	}

	public static BiomeType Classify(float elev, float moist, float temp, PlanetParams p)
	{
		float sea = p.SeaLevel;

		if (elev < sea - 0.10f) return BiomeType.DeepOcean;
		if (elev < sea)         return BiomeType.Ocean;
		if (elev < sea + 0.05f) return BiomeType.Coastal;

		if (elev > 0.85f) return temp < 0.45f ? BiomeType.Arctic : BiomeType.Mountain;
		if (elev > 0.72f) return BiomeType.Highland;

		if (temp < 0.35f) return BiomeType.Arctic;

		float alienThreshold = 0.65f + (1f - p.AlienFactor) * 0.25f;

		if (moist < 0.25f)          return BiomeType.Desert;
		if (moist < 0.42f)          return BiomeType.Savanna;
		if (moist > alienThreshold) return BiomeType.AlienWilds;
		if (moist > 0.65f)          return BiomeType.Jungle;
		if (moist > 0.52f)          return BiomeType.Forest;
		return BiomeType.Grassland;
	}

	// ── Chunk tile generation ─────────────────────────────────────────────────

	public static ChunkData Generate(Vector2I coord, int worldSeed, PlanetParams planet)
	{
		BiomeType biome = GetBiome(coord, worldSeed, planet);
		var tiles = new TileType[ChunkData.Size, ChunkData.Size];

		// World-space noise — same seed for every chunk so noise is continuous
		// across chunk borders (no seams). Sample with global tile coordinates.
		var tileNoise   = MakeNoise(worldSeed ^ 0x74696C65, 0.14f,
		                            FastNoiseLite.FractalTypeEnum.Fbm, 4);
		var detailNoise = MakeNoise(worldSeed ^ 0x64657461, 0.28f,
		                            FastNoiseLite.FractalTypeEnum.Fbm, 3);
		var lakeNoise   = MakeNoise(worldSeed ^ 0x6C616B65, 0.08f,
		                            FastNoiseLite.FractalTypeEnum.Fbm, 2);

		float chunkElev = SampleElev(coord.X, coord.Y, worldSeed);

		// Global tile origin of this chunk
		int gx = coord.X * ChunkData.Size;
		int gy = coord.Y * ChunkData.Size;

		// Tile-longitude wrap period: same as chunk wrap but in tile units.
		// Normalising tile X ensures tile-level detail is seamless at the east-west seam.
		const int TileLon = LonWidth * ChunkData.Size; // 240 chunks × 16 tiles = 3840

		for (int x = 0; x < ChunkData.Size; x++)
			for (int y = 0; y < ChunkData.Size; y++)
			{
				int normTx = ((gx + x) % TileLon + TileLon) % TileLon;
				float n      = Normalize(tileNoise.GetNoise2D(normTx, gy + y));
				float detail = Normalize(detailNoise.GetNoise2D(normTx, gy + y));
				float lake   = Normalize(lakeNoise.GetNoise2D(normTx, gy + y));
				tiles[x, y] = PickTile(biome, n, detail, lake, chunkElev, planet);
			}

		return new ChunkData(coord, biome, tiles);
	}

	private static TileType PickTile(BiomeType biome, float n, float detail,
	                                  float lake, float chunkElev, PlanetParams planet)
	{
		bool isWater = biome is BiomeType.DeepOcean or BiomeType.Ocean or BiomeType.Coastal;
		if (!isWater && biome != BiomeType.SafeZone && detail > 1f - planet.RuinsDensity)
			return TileType.Ruins;

		return biome switch
		{
			BiomeType.SafeZone =>
				n < 0.12f ? TileType.Rocky : TileType.Grassland,

			BiomeType.DeepOcean =>
				n < 0.30f ? TileType.DeepOcean : TileType.Ocean,

			BiomeType.Ocean =>
				n < 0.55f ? TileType.Ocean : TileType.ShallowWater,

			BiomeType.Coastal =>
				PickCoastal(n, chunkElev, planet.SeaLevel),

			BiomeType.Desert =>
				n < 0.55f ? TileType.Desert  :
				n < 0.80f ? TileType.Savanna :
				            TileType.Rocky,

			BiomeType.Savanna =>
				n < 0.50f ? TileType.Savanna   :
				n < 0.75f ? TileType.Grassland :
				            TileType.Desert,

			BiomeType.Grassland =>
				lake < 0.12f ? TileType.ShallowWater :
				n    < 0.60f ? TileType.Grassland    :
				n    < 0.82f ? TileType.Ground        :
				               TileType.Rocky,

			BiomeType.Forest =>
				lake < 0.10f ? TileType.ShallowWater :
				n    < 0.50f ? TileType.Forest       :
				n    < 0.72f ? TileType.DenseForest  :
				               TileType.Grassland,

			BiomeType.Jungle =>
				n < 0.35f ? TileType.DenseForest :
				n < 0.65f ? TileType.Forest      :
				            TileType.AlienGrowth,

			BiomeType.AlienWilds =>
				n < 0.50f ? TileType.AlienGrowth :
				n < 0.78f ? TileType.DenseForest :
				            TileType.Forest,

			BiomeType.Highland =>
				detail > 0.97f ? TileType.Crystal :
				n      < 0.45f ? TileType.Rocky   :
				n      < 0.72f ? TileType.Ground  :
				                 TileType.Forest,

			BiomeType.Mountain =>
				detail > 0.94f ? TileType.Crystal  :
				n      < 0.50f ? TileType.Mountain :
				n      < 0.78f ? TileType.Rocky    :
				                 TileType.Snow,

			BiomeType.Arctic =>
				n < 0.45f ? TileType.Snow     :
				n < 0.78f ? TileType.Mountain :
				            TileType.Rocky,

			_ => TileType.Ground,
		};
	}

	/// <summary>
	/// Coastal blend: more water closer to sea level, more beach further inland.
	/// Wider transition zone than before for natural-looking shorelines.
	/// </summary>
	private static TileType PickCoastal(float n, float chunkElev, float seaLevel)
	{
		float waterFrac = Mathf.Clamp((seaLevel - chunkElev + 0.05f) / 0.10f, 0f, 1f);

		if (n < waterFrac)              return TileType.ShallowWater;
		if (n < waterFrac + 0.22f)      return TileType.Beach;
		if (n < waterFrac + 0.40f)      return TileType.MudFlat;
		return TileType.Grassland;
	}

	// ── Planet metadata ───────────────────────────────────────────────────────

	public static string PlanetTypeLabel(PlanetParams p) => p.Type switch
	{
		PlanetType.ArchipelagoWorld => "Archipelago World",
		PlanetType.ContinentalWorld => "Continental World",
		PlanetType.JungleWorld      => "Jungle World",
		_                           => "Unknown",
	};

	public static string BiomeDistributionInfo(PlanetParams p) =>
		$"Type={p.Type}  Sea={p.SeaLevel:F2}  Veg={p.VegBias:F2}  " +
		$"Alien={p.AlienFactor:F2}  TempMod={p.GlobalTempMod:+0.00;-0.00;0.00}";

	// ── Noise helpers ─────────────────────────────────────────────────────────

	/// <summary>
	/// Elevation: FBm Perlin sampled on a cylinder so the noise tiles seamlessly at
	/// the east-west seam (lon 0 = lon 360). The cylinder radius R = LonWidth/(2π)
	/// preserves the same chunk-scale spatial frequency as the old 2D sampling.
	/// </summary>
	private static float SampleElev(int cx, int cy, int seed)
	{
		float lonRad = NormX(cx) / (float)LonWidth * 2f * MathF.PI;
		float R      = LonWidth  / (2f * MathF.PI); // ≈ 19.1 — cylinder radius
		var   n      = MakeNoise(seed ^ 0x1A2B3C4D, 0.09f, FastNoiseLite.FractalTypeEnum.Fbm, 5);
		return Normalize(n.GetNoise3D(R * MathF.Cos(lonRad), R * MathF.Sin(lonRad), cy));
	}

	/// <summary>
	/// Moisture: FBm Perlin, cylindrical sampling (same seam-free approach as elevation).
	/// </summary>
	private static float SampleMoist(int cx, int cy, int seed)
	{
		float lonRad = NormX(cx) / (float)LonWidth * 2f * MathF.PI;
		float R      = LonWidth  / (2f * MathF.PI);
		var   n      = MakeNoise(seed ^ 0x5E6F7A8B, 0.18f, FastNoiseLite.FractalTypeEnum.Fbm, 3);
		return Normalize(n.GetNoise3D(R * MathF.Cos(lonRad), R * MathF.Sin(lonRad), cy));
	}

	private static FastNoiseLite MakeNoise(int seed, float frequency,
	                                        FastNoiseLite.FractalTypeEnum fractal, int octaves)
	{
		var n = new FastNoiseLite();
		n.Seed           = seed;
		n.Frequency      = frequency;
		n.NoiseType      = FastNoiseLite.NoiseTypeEnum.Perlin;
		n.FractalType    = fractal;
		n.FractalOctaves = octaves;
		return n;
	}

	private static float Normalize(float v) => (v + 1f) * 0.5f;
}
