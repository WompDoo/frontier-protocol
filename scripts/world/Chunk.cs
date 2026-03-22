using Godot;
using System;

/// <summary>
/// Renders a single map chunk using ChunkTerrainMesh — a continuous height-field
/// with world-space noise sampling for seamless cross-chunk borders.
/// Debug labels and chunk border are drawn in _Draw().
/// </summary>
public partial class Chunk : Node2D
{
	public const int TileWidth  = 64;
	public const int TileHeight = 48;   // steeper isometric angle (~48° from horizontal)

	public static bool DebugBorders      = false;
	public static bool UseThreeDRenderer = false;  // set by ChunkManager when OverworldRenderer is present

	private ChunkData         _data;
	private ChunkTerrainMesh? _terrain;

	public ChunkData Data => _data;

	public void Init(ChunkData data, Vector2 worldPos, int worldSeed = 12345, RiverCrossing? river = null, float seaLevel = 0.52f)
	{
		_data    = data;
		Position = worldPos;

		if (!UseThreeDRenderer)
		{
			_terrain = new ChunkTerrainMesh();
			AddChild(_terrain);
			_terrain.GenerateFromChunk(data, worldSeed, seaLevel, river);
		}

		QueueRedraw();
	}

	// ── Drawing ───────────────────────────────────────────────────────────────

	public override void _Draw()
	{
		if (_data == null) return;

		// Per-tile labels: Crystal/Ruins always; Mountain/Snow only in debug
		for (int sum = 0; sum < ChunkData.Size * 2 - 1; sum++)
			for (int x = 0; x <= sum; x++)
			{
				int y = sum - x;
				if (x >= ChunkData.Size || y >= ChunkData.Size) continue;

				var    type  = _data.Tiles[x, y];
				string label = TileLabel(type);
				if (label is null) continue;

				bool rare = type is TileType.Crystal or TileType.Ruins;
				if (!rare && !DebugBorders) continue;

				Vector2 c      = TileToLocal(x, y);
				int     height = ElevationOf(type);
				Vector2 S      = c + new Vector2(0f, TileHeight / 2f - height);
				DrawString(ThemeDB.FallbackFont,
				           S + new Vector2(-4f, 2f),
				           label, HorizontalAlignment.Center,
				           -1, 9,
				           Colors.White with { A = 0.7f });
			}

		if (DebugBorders)
			DrawChunkBorder();
	}

	private void DrawChunkBorder()
	{
		int s = ChunkData.Size - 1;
		Vector2[] corners =
		[
			TileToLocal(0, 0),
			TileToLocal(s, 0),
			TileToLocal(s, s),
			TileToLocal(0, s),
		];

		DrawPolyline([corners[0], corners[1], corners[2], corners[3], corners[0]],
			Colors.Yellow with { A = 0.85f }, 2f);

		DrawString(
			ThemeDB.FallbackFont,
			corners[0] + new Vector2(0, -6),
			$"({_data.Coord.X},{_data.Coord.Y}) {_data.Biome}",
			HorizontalAlignment.Center,
			-1, 10,
			Colors.Yellow
		);
	}

	// ── Coordinate helpers ────────────────────────────────────────────────────

	public static Vector2 TileToLocal(int tx, int ty) =>
		new((tx - ty) * (TileWidth / 2f), (tx + ty) * (TileHeight / 2f));

	// ── Tile heights (kept for label positioning) ─────────────────────────────

	private static int ElevationOf(TileType t) => t switch
	{
		TileType.DeepOcean    => 0,
		TileType.Ocean        => 0,
		TileType.ShallowWater => 1,
		TileType.Beach        => 3,
		TileType.MudFlat      => 2,
		TileType.Desert       => 4,
		TileType.Savanna      => 5,
		TileType.Grassland    => 7,
		TileType.Ground       => 6,
		TileType.Forest       => 12,
		TileType.DenseForest  => 15,
		TileType.AlienGrowth  => 14,
		TileType.Rocky        => 20,
		TileType.Mountain     => 36,
		TileType.Snow         => 40,
		TileType.Crystal      => 18,
		TileType.Ruins        => 10,
		_                     => 7,
	};

	private static string TileLabel(TileType t) => t switch
	{
		TileType.Crystal  => "*",
		TileType.Ruins    => "R",
		TileType.Mountain => "^",
		TileType.Snow     => "s",
		_ => null,
	};
}
