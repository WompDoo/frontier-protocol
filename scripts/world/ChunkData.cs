using Godot;

public enum PlanetType
{
	ArchipelagoWorld,
	ContinentalWorld,
	JungleWorld,
}

public enum TileType
{
	// Water (deep → shallow)
	DeepOcean,
	Ocean,
	ShallowWater,
	// Transition
	Beach,
	MudFlat,
	// Arid land
	Desert,
	Savanna,
	// Temperate land
	Grassland,
	Ground,
	// Vegetated
	Forest,
	DenseForest,
	// Alien
	AlienGrowth,
	// Elevated
	Rocky,
	Mountain,
	Snow,
	// Special / rare
	Crystal,  // cave entrance / mineral deposit
	Ruins,
}

public enum BiomeType
{
	SafeZone,
	// Water
	DeepOcean,
	Ocean,
	Coastal,
	// Arid
	Desert,
	Savanna,
	// Temperate
	Grassland,
	Forest,
	Jungle,
	// Alien
	AlienWilds,
	// Elevated
	Highland,
	Mountain,
	Arctic,
}

/// <summary>
/// Per-chunk river crossing derived from globe-level river tracing.
/// Sides: 0=North, 1=East, 2=South, 3=West.
/// EntrySide = -1 means river originates here (spring/glacial source).
/// </summary>
public struct RiverCrossing
{
	public int EntrySide;  // -1 = spring source; 0=N, 1=E, 2=S, 3=W
	public int ExitSide;   // 0=N, 1=E, 2=S, 3=W
}

public class ChunkData
{
	public const int Size = 16; // 16x16 tiles per chunk

	public Vector2I    Coord { get; }
	public BiomeType   Biome { get; }
	public TileType[,] Tiles { get; }

	public ChunkData(Vector2I coord, BiomeType biome, TileType[,] tiles)
	{
		Coord = coord;
		Biome = biome;
		Tiles = tiles;
	}
}
