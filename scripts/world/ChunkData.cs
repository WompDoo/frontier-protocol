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
