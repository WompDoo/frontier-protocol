using Godot;
using System.Collections.Generic;

/// <summary>
/// Renders a single map chunk as raised isometric tiles.
/// All tile geometry is baked into a single ArrayMesh on Init() so the entire
/// chunk is drawn with one GPU draw call via DrawMesh. _Draw() only handles
/// the optional debug border and any per-tile labels.
/// </summary>
public partial class Chunk : Node2D
{
	public const int TileWidth  = 64;
	public const int TileHeight = 32;

	public static bool DebugBorders = false;

	private ChunkData _data;
	private ArrayMesh _tileMesh;

	public ChunkData Data => _data;

	public void Init(ChunkData data, Vector2 worldPos)
	{
		_data    = data;
		Position = worldPos;
		BuildMesh();
		QueueRedraw();
	}

	// ── Mesh building ─────────────────────────────────────────────────────────

	/// <summary>
	/// Bakes all tile geometry (top faces + side faces) into a single ArrayMesh.
	/// Built in painter's order (back-to-front by x+y sum) so later triangles
	/// correctly overdraw earlier ones without requiring depth testing.
	/// </summary>
	private void BuildMesh()
	{
		var verts   = new List<Vector3>();
		var colors  = new List<Color>();
		var indices = new List<int>();

		for (int sum = 0; sum < ChunkData.Size * 2 - 1; sum++)
			for (int x = 0; x <= sum; x++)
			{
				int y = sum - x;
				if (x < ChunkData.Size && y < ChunkData.Size)
					AddTileGeometry(x, y, _data.Tiles[x, y], verts, colors, indices);
			}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		arrays[(int)Mesh.ArrayType.Color]  = colors.ToArray();
		arrays[(int)Mesh.ArrayType.Index]  = indices.ToArray();

		_tileMesh = new ArrayMesh();
		_tileMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
	}

	private static void AddTileGeometry(int tx, int ty, TileType type,
	                                     List<Vector3> verts, List<Color> colors, List<int> indices)
	{
		Vector2 c      = TileToLocal(tx, ty);
		int     height = ElevationOf(type);
		var (fill, side) = TileColors(type);

		float tw = TileWidth  / 2f;
		float th = TileHeight / 2f;

		// Raised corners of the top face
		var N  = new Vector3(c.X,      c.Y - th - height, 0f);
		var E  = new Vector3(c.X + tw, c.Y      - height, 0f);
		var S  = new Vector3(c.X,      c.Y + th - height, 0f);
		var W  = new Vector3(c.X - tw, c.Y      - height, 0f);

		// Ground-level base corners for side faces
		var Sg = new Vector3(c.X,      c.Y + th, 0f);
		var Wg = new Vector3(c.X - tw, c.Y,      0f);
		var Eg = new Vector3(c.X + tw, c.Y,      0f);

		if (height > 0)
		{
			// Left visible side face (W → S, ground base)
			AddQuad(verts, colors, indices, W, S, Sg, Wg, side);
			// Right visible side face (S → E, ground base) — slightly darker
			Color rightSide = new Color(side.R * 0.85f, side.G * 0.85f, side.B * 0.85f, 1f);
			AddQuad(verts, colors, indices, S, E, Eg, Sg, rightSide);
		}

		// Top face
		AddQuad(verts, colors, indices, N, E, S, W, fill);
	}

	private static void AddQuad(List<Vector3> verts, List<Color> colors, List<int> indices,
	                             Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
	{
		int i = verts.Count;
		verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
		colors.Add(col); colors.Add(col); colors.Add(col); colors.Add(col);
		// Split quad into two CCW triangles
		indices.Add(i);   indices.Add(i + 1); indices.Add(i + 2);
		indices.Add(i);   indices.Add(i + 2); indices.Add(i + 3);
	}

	// ── Drawing ───────────────────────────────────────────────────────────────

	public override void _Draw()
	{
		if (_data == null || _tileMesh == null) return;

		// One GPU draw call for the entire chunk
		DrawMesh(_tileMesh, null);

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

	// ── Tile heights ──────────────────────────────────────────────────────────

	private static int ElevationOf(TileType t) => t switch
	{
		TileType.DeepOcean    => 0,
		TileType.Ocean        => 0,
		TileType.ShallowWater => 1,
		TileType.Beach        => 2,
		TileType.MudFlat      => 2,
		TileType.Desert       => 3,
		TileType.Savanna      => 3,
		TileType.Grassland    => 4,
		TileType.Ground       => 4,
		TileType.Forest       => 6,
		TileType.DenseForest  => 7,
		TileType.AlienGrowth  => 7,
		TileType.Rocky        => 8,
		TileType.Mountain     => 12,
		TileType.Snow         => 12,
		TileType.Crystal      => 10,
		TileType.Ruins        => 6,
		_                     => 4,
	};

	// ── Tile appearance ───────────────────────────────────────────────────────

	private static (Color fill, Color side) TileColors(TileType t)
	{
		Color fill = t switch
		{
			TileType.DeepOcean    => new Color(0.03f, 0.07f, 0.25f),
			TileType.Ocean        => new Color(0.05f, 0.15f, 0.42f),
			TileType.ShallowWater => new Color(0.12f, 0.32f, 0.58f),
			TileType.Beach        => new Color(0.78f, 0.70f, 0.44f),
			TileType.MudFlat      => new Color(0.24f, 0.20f, 0.14f),
			TileType.Desert       => new Color(0.78f, 0.60f, 0.26f),
			TileType.Savanna      => new Color(0.58f, 0.52f, 0.22f),
			TileType.Grassland    => new Color(0.22f, 0.48f, 0.16f),
			TileType.Ground       => new Color(0.26f, 0.22f, 0.17f),
			TileType.Forest       => new Color(0.10f, 0.32f, 0.12f),
			TileType.DenseForest  => new Color(0.06f, 0.20f, 0.08f),
			TileType.AlienGrowth  => new Color(0.06f, 0.28f, 0.22f),
			TileType.Rocky        => new Color(0.36f, 0.30f, 0.24f),
			TileType.Mountain     => new Color(0.48f, 0.44f, 0.40f),
			TileType.Snow         => new Color(0.88f, 0.92f, 0.96f),
			TileType.Crystal      => new Color(0.50f, 0.12f, 0.78f),
			TileType.Ruins        => new Color(0.40f, 0.34f, 0.22f),
			_                     => new Color(0.22f, 0.20f, 0.16f),
		};

		Color side = new Color(fill.R * 0.62f, fill.G * 0.62f, fill.B * 0.62f, 1f);
		return (fill, side);
	}

	private static string TileLabel(TileType t) => t switch
	{
		TileType.Crystal  => "*",
		TileType.Ruins    => "R",
		TileType.Mountain => "^",
		TileType.Snow     => "s",
		_ => null,
	};
}
