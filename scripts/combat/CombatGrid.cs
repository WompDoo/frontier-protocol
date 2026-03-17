#nullable enable
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Square-isometric combat arena. 64×32 pixel diamonds — same geometry as the world map.
///
/// Inner arena (GridW × GridH): fully interactive tiles with cover/LoS/elevation mechanics.
/// Outer ring (ExtR tiles in every direction): decorative terrain rendered behind the arena.
///   — same biome / same noise height field → terrain flows coherently across the border.
///   — darkened + alpha-faded so the arena "cutout" reads clearly.
///
/// Per-row child nodes give correct painter's-algorithm Z-ordering with units:
///   RowNode[r].ZIndex = r*2  →  units at row r get ZIndex = r*2+1  →  HighlightNode above all.
///   OuterNode.ZIndex = -50   →  always rendered behind every inner tile.
/// </summary>
public partial class CombatGrid : Node2D
{
	// ── Grid constants ────────────────────────────────────────────────────

	public const int   GridW  = 11;
	public const int   GridH  = 11;
	public const int   ElevPx = 22;
	public const float TileW  = 64f;
	public const float TileH  = 32f;

	/// <summary>Decorative tiles rendered outside the playable arena in each direction.</summary>
	public const int ExtR = 8;

	// ── Grid data ─────────────────────────────────────────────────────────

	/// <summary>Noise amplitude used for visual tile height (px). Shared by all draw passes.</summary>
	public const float VisNoisePx = 26f;

	private CombatTileData?[,] _tiles     = new CombatTileData?[GridW, GridH];
	private bool               _tilesReady = false;

	/// <summary>Full extended tile-type grid, including outer ring. Size = (GridW+2*ExtR, GridH+2*ExtR).</summary>
	private TileType[,] _allTiles = new TileType[GridW + ExtR * 2, GridH + ExtR * 2];

	/// <summary>Per-tile smooth noise 0..1. Same grid size as _allTiles. Used for coherent terrain height.</summary>
	private float[,] _noisePx = new float[GridW + ExtR * 2, GridH + ExtR * 2];

	public BiomeType CurrentBiome { get; private set; } = BiomeType.Grassland;

	// ── Render nodes (created in _Ready) ─────────────────────────────────

	private IsoTerrainMesh _terrain = null!;
	private HighlightNode  _hlNode  = null!;

	// ── Highlight state ───────────────────────────────────────────────────

	public HashSet<Vector2I> MoveHighlight    { get; } = new();
	public HashSet<Vector2I> AttackHighlight  { get; } = new();
	public HashSet<Vector2I> PreviewHighlight { get; } = new();
	public Vector2I?         HoveredCell      { get; set; }
	public Vector2I?         SelectedCell     { get; set; }

	// ── Lifecycle ─────────────────────────────────────────────────────────

	public override void _Ready()
	{
		// Standalone terrain mesh — rendered behind everything
		_terrain = new IsoTerrainMesh { ZIndex = -100 };
		AddChild(_terrain);

		// Highlight / selection overlay on top
		_hlNode = new HighlightNode(this) { ZIndex = GridH * 2 + 5 };
		AddChild(_hlNode);
	}

	public override void _Draw() { }

	public new void QueueRedraw() => _hlNode?.QueueRedraw();

	private void RedrawAll()
	{
		_tilesReady = true;
		_terrain?.QueueRedraw();
		_hlNode?.QueueRedraw();
	}

	// ── Public tile accessors ─────────────────────────────────────────────

	public CombatTileData GetTile(int col, int row) => _tiles[col, row]!;
	public CombatTileData GetTile(Vector2I cell)    => _tiles[cell.X, cell.Y]!;
	public bool InBounds(Vector2I cell) =>
		cell.X >= 0 && cell.X < GridW && cell.Y >= 0 && cell.Y < GridH;

	// ── Generation ────────────────────────────────────────────────────────

	public void GenerateFrom(ChunkData chunk, Vector2I centerTile,
	                         ChunkGenerator.PlanetParams? planet = null)
	{
		var rng = new Random(chunk.Coord.X * 73856093 ^ chunk.Coord.Y * 19349663);
		CurrentBiome = chunk.Biome;
		float alienFactor  = planet?.AlienFactor  ?? 0.2f;
		float ruinsDensity = planet?.RuinsDensity ?? 0.05f;

		int originCol = centerTile.X - GridW / 2;
		int originRow = centerTile.Y - GridH / 2;

		// Fill inner combat tiles
		for (int col = 0; col < GridW; col++)
		for (int row = 0; row < GridH; row++)
		{
			int chunkCol = originCol + col, chunkRow = originRow + row;
			TileType source = (chunkCol >= 0 && chunkCol < ChunkData.Size &&
			                   chunkRow >= 0 && chunkRow < ChunkData.Size)
				? chunk.Tiles[chunkCol, chunkRow]
				: TileType.Grassland;
			source = ApplyPlanetModifiers(source, chunk.Biome, alienFactor, ruinsDensity, rng);
			_tiles[col, row] = MapTileToCombat(source);
		}

		// Fill full extended tile array (inner + outer ring)
		TileType biomeFill = chunk.Biome switch
		{
			BiomeType.Ocean or BiomeType.DeepOcean          => TileType.Ocean,
			BiomeType.Mountain or BiomeType.Highland        => TileType.Mountain,
			BiomeType.Jungle                                => TileType.DenseForest,
			BiomeType.Desert or BiomeType.Savanna           => TileType.Desert,
			BiomeType.AlienWilds                            => TileType.AlienGrowth,
			_                                               => TileType.Grassland,
		};

		for (int col = -ExtR; col < GridW + ExtR; col++)
		for (int row = -ExtR; row < GridH + ExtR; row++)
		{
			int chunkCol = originCol + col, chunkRow = originRow + row;
			TileType t = (chunkCol >= 0 && chunkCol < ChunkData.Size &&
			              chunkRow >= 0 && chunkRow < ChunkData.Size)
				? chunk.Tiles[chunkCol, chunkRow]
				: biomeFill;
			_allTiles[col + ExtR, row + ExtR] = t;
		}

		// Generate coherent noise height field across the whole extended area
		int noiseSeed = chunk.Coord.X * 73856093 ^ chunk.Coord.Y * 19349663;
		GenerateHeightMap(noiseSeed);
		_terrain?.Generate(noiseSeed, (GridW + ExtR * 2) * 2, (GridH + ExtR * 2) * 2);
		RedrawAll();
	}

	public void GenerateDebug(int seed = 0)
	{
		var rng = new Random(seed);
		CurrentBiome = BiomeType.Grassland;

		for (int col = 0; col < GridW; col++)
		for (int row = 0; row < GridH; row++)
			_tiles[col, row] = MapTileToCombat(TileType.Grassland);

		PlaceCluster(rng, TileType.Forest,       2,          GridH / 2,     5);
		PlaceCluster(rng, TileType.Rocky,        GridW - 3,  GridH / 2,     5);
		PlaceCluster(rng, TileType.ShallowWater, GridW / 2,  GridH / 3,     4);
		PlaceCluster(rng, TileType.ShallowWater, GridW / 2 + 1, GridH * 2 / 3, 3);
		for (int i = 0; i < 4; i++)
		{
			int c = rng.Next(1, GridW - 1), r = rng.Next(1, GridH - 1);
			if (_tiles[c, r]?.SourceTile == TileType.Grassland)
				_tiles[c, r] = MapTileToCombat(TileType.Ruins);
		}

		// Fill _allTiles from inner + default grassland for outer
		for (int col = -ExtR; col < GridW + ExtR; col++)
		for (int row = -ExtR; row < GridH + ExtR; row++)
		{
			bool inner = col >= 0 && col < GridW && row >= 0 && row < GridH;
			_allTiles[col + ExtR, row + ExtR] = inner
				? (_tiles[col, row]?.SourceTile ?? TileType.Grassland)
				: TileType.Grassland;
		}

		GenerateHeightMap(seed);
		_terrain?.Generate(seed, (GridW + ExtR * 2) * 2, (GridH + ExtR * 2) * 2);
		RedrawAll();
	}

	private void PlaceCluster(Random rng, TileType t, int cx, int cy, int size)
	{
		for (int i = 0; i < size * 3; i++)
		{
			int c = cx + rng.Next(-size / 2, size / 2 + 1);
			int r = cy + rng.Next(-size / 2, size / 2 + 1);
			if (c >= 0 && c < GridW && r >= 0 && r < GridH)
				_tiles[c, r] = MapTileToCombat(t);
		}
	}

	// ── Height map ────────────────────────────────────────────────────────

	/// <summary>
	/// Fills _noisePx with smooth FBM values (0..1) across the extended grid.
	/// Low frequency → large smooth hills; the same field is used for both
	/// inner-tile jitter and outer-ring terrain height.
	/// </summary>
	private void GenerateHeightMap(int seed)
	{
		var noise = new FastNoiseLite();
		noise.Seed        = seed;
		noise.NoiseType   = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
		noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
		noise.FractalOctaves = 4;
		noise.Frequency   = 0.095f;  // low = big rolling hills

		int totalW = GridW + ExtR * 2;
		int totalH = GridH + ExtR * 2;

		for (int c = 0; c < totalW; c++)
		for (int r = 0; r < totalH; r++)
		{
			float n = noise.GetNoise2D(c, r);          // -1..1
			_noisePx[c, r] = (n + 1f) * 0.5f;         // 0..1
		}
	}

	// ── Visual height helpers ─────────────────────────────────────────────

	/// <summary>
	/// Base visual pixel height for a tile type.
	/// Used for outer-ring rendering (no gameplay elevation level available).
	/// For inner tiles the gameplay elevation is the primary driver;
	/// smooth noise adds a small modulation on top.
	/// </summary>
	public static float TileBaseVisualHeight(TileType t) => t switch
	{
		TileType.DeepOcean                           => 0f,
		TileType.Ocean                               => 0f,
		TileType.ShallowWater                        => 2f,
		TileType.Beach or TileType.MudFlat           => 4f,
		TileType.Desert or TileType.Savanna          => 7f,
		TileType.Ground or TileType.Grassland        => 9f,
		TileType.Forest                              => 18f,
		TileType.DenseForest or TileType.AlienGrowth => 22f,
		TileType.Ruins or TileType.Crystal           => 16f,
		TileType.Rocky                               => 32f,
		TileType.Mountain                            => 54f,
		TileType.Snow                                => 65f,
		_                                            => 9f,
	};

	/// <summary>
	/// Screen position of an inner tile's top face, derived from visual height (TileBaseVisualHeight
	/// + smooth noise). This is the canonical position for unit placement and highlight overlays.
	/// Decoupled from gameplay ElevationLevel so terrain looks natural rather than stepped.
	/// </summary>
	public Vector2 GetCellVisualPos(int col, int row)
	{
		float visH = TileBaseVisualHeight(_tiles[col, row]!.SourceTile)
		           + _noisePx[col + ExtR, row + ExtR] * VisNoisePx;
		return GridOrigin() + GridToScreen(col, row) - new Vector2(0, visH);
	}
	public Vector2 GetCellVisualPos(Vector2I cell) => GetCellVisualPos(cell.X, cell.Y);

	// ── Planet-modifier helpers ───────────────────────────────────────────

	private static TileType ApplyPlanetModifiers(TileType t, BiomeType biome,
	                                              float alienFactor, float ruinsDensity, Random rng)
	{
		if (biome == BiomeType.AlienWilds && t is TileType.Grassland or TileType.Ground or TileType.Forest)
			if (rng.NextDouble() < alienFactor * 0.4f) return TileType.AlienGrowth;
		if (t == TileType.Grassland && rng.NextDouble() < ruinsDensity * 0.3f)
			return TileType.Ruins;
		return t;
	}

	public static CombatTileData MapTileToCombat(TileType t) => t switch
	{
		TileType.DeepOcean    => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Impassable),
		TileType.Ocean        => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Impassable),
		TileType.ShallowWater => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Impassable),
		TileType.Beach        => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Open),
		TileType.MudFlat      => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Difficult),
		TileType.Desert       => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Open),
		TileType.Savanna      => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Open),
		TileType.Grassland    => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Open),
		TileType.Ground       => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Open),
		TileType.Forest       => new(t, CoverType.Half, ElevationLevel.Low,  TilePassability.Open),
		TileType.DenseForest  => new(t, CoverType.Full, ElevationLevel.Low,  TilePassability.Difficult),
		TileType.AlienGrowth  => new(t, CoverType.Half, ElevationLevel.Low,  TilePassability.Difficult),
		TileType.Rocky        => new(t, CoverType.Half, ElevationLevel.Mid,  TilePassability.Difficult),
		TileType.Mountain     => new(t, CoverType.Full, ElevationLevel.High, TilePassability.Difficult),
		TileType.Snow         => new(t, CoverType.None, ElevationLevel.High, TilePassability.Difficult),
		TileType.Crystal      => new(t, CoverType.Half, ElevationLevel.Mid,  TilePassability.Open),
		TileType.Ruins        => new(t, CoverType.Full, ElevationLevel.Low,  TilePassability.Open),
		_                     => new(t, CoverType.None, ElevationLevel.Low,  TilePassability.Open),
	};

	// ── Coordinate helpers ────────────────────────────────────────────────

	public Vector2 GridToScreen(int col, int row, ElevationLevel elev = ElevationLevel.Low)
	{
		float x = (col - row) * (TileW / 2f);
		float y = (col + row) * (TileH / 2f) - (int)elev * ElevPx;
		return new Vector2(x, y);
	}

	public Vector2 GridToScreen(Vector2I cell, ElevationLevel elev = ElevationLevel.Low)
		=> GridToScreen(cell.X, cell.Y, elev);

	public Vector2I ScreenToGrid(Vector2 pos)
	{
		float hw = TileW / 2f;
		float hh = TileH / 2f;
		float colF = (pos.X / hw + pos.Y / hh) / 2f;
		float rowF = (pos.Y / hh - pos.X / hw) / 2f;
		return new Vector2I(Mathf.RoundToInt(colF), Mathf.RoundToInt(rowF));
	}

	public Vector2 GridOrigin() => -GridToScreen(GridW / 2, GridH / 2);

	// ── LoS and cover ─────────────────────────────────────────────────────

	public bool HasLoS(Vector2I from, Vector2I to)
	{
		var fromElev   = _tiles[from.X, from.Y]!.Elevation;
		var toElev     = _tiles[to.X,   to.Y  ]!.Elevation;
		var maxEndElev = (ElevationLevel)Math.Max((int)fromElev, (int)toElev);

		foreach (var cell in BresenhamLine(from, to))
		{
			if (cell == from || cell == to) continue;
			if (!InBounds(cell)) return false;
			var tile = _tiles[cell.X, cell.Y]!;
			if (tile.Passability == TilePassability.Impassable) return false;
			if (tile.Elevation > maxEndElev) return false;
			if (tile.Cover == CoverType.Full) return false;
		}
		return true;
	}

	public CoverType GetCoverBetween(Vector2I attacker, Vector2I target)
	{
		var tile = _tiles[target.X, target.Y]!;
		if (tile.Cover == CoverType.None) return CoverType.None;
		Vector2I delta = attacker - target;
		bool flanking = Math.Abs(delta.X) > 0 && Math.Abs(delta.Y) > 0;
		return flanking ? CoverType.None : tile.Cover;
	}

	public static float CoverDodgeBonus(CoverType cover) => cover switch
	{
		CoverType.Half => 0.25f,
		CoverType.Full => 0.50f,
		_              => 0f,
	};

	public float ElevationHitBonus(Vector2I attacker, Vector2I target)
	{
		var aE = _tiles[attacker.X, attacker.Y]!.Elevation;
		var tE = _tiles[target.X,   target.Y  ]!.Elevation;
		return aE > tE ? 0.10f : aE < tE ? -0.10f : 0f;
	}

	// ── Reachability BFS ──────────────────────────────────────────────────

	public HashSet<Vector2I> GetReachable(Vector2I origin, int apBudget)
	{
		var reachable = new HashSet<Vector2I>(GridW * GridH);
		var queue     = new Queue<(Vector2I cell, int apLeft)>(GridW * GridH);
		var visited   = new Dictionary<Vector2I, int>(GridW * GridH);
		queue.Enqueue((origin, apBudget));
		visited[origin] = apBudget;

		while (queue.Count > 0)
		{
			var (cell, ap) = queue.Dequeue();
			foreach (var nb in Neighbours(cell))
			{
				if (!InBounds(nb)) continue;
				var tile = _tiles[nb.X, nb.Y]!;
				if (tile.Passability == TilePassability.Impassable) continue;
				int cost = tile.Passability == TilePassability.Difficult ? 2 : 1;
				int rem  = ap - cost;
				if (rem < 0) continue;
				if (visited.TryGetValue(nb, out int best) && best >= rem) continue;
				visited[nb] = rem;
				reachable.Add(nb);
				queue.Enqueue((nb, rem));
			}
		}
		return reachable;
	}

	public List<Vector2I> GetAttackTargets(Vector2I origin, WeaponDef weapon)
	{
		var targets = new List<Vector2I>(GridW * GridH);
		for (int col = 0; col < GridW; col++)
		for (int row = 0; row < GridH; row++)
		{
			var cell = new Vector2I(col, row);
			if (cell == origin) continue;
			int dist = HexDist(origin, cell);
			if (dist < weapon.RangeMin || dist > weapon.RangeMax) continue;
			if (!HasLoS(origin, cell)) continue;
			targets.Add(cell);
		}
		return targets;
	}

	// ── Shared static drawing helpers ─────────────────────────────────────

	private static Vector2[] DiamondCorners(Vector2 center)
	{
		float hw = TileW / 2f;
		float hh = TileH / 2f;
		return new Vector2[]
		{
			center + new Vector2(   0, -hh),
			center + new Vector2( hw,    0),
			center + new Vector2(   0,  hh),
			center + new Vector2(-hw,    0),
		};
	}

	private static (Color fill, Color sideL, Color sideR) TileColors(TileType t)
	{
		// Richer, more saturated top-face fills — matches low-poly diorama refs
		Color fill = t switch
		{
			TileType.DeepOcean    => new Color(0.02f, 0.06f, 0.28f),
			TileType.Ocean        => new Color(0.04f, 0.18f, 0.52f),
			TileType.ShallowWater => new Color(0.14f, 0.48f, 0.74f),
			TileType.Beach        => new Color(0.88f, 0.80f, 0.52f),
			TileType.MudFlat      => new Color(0.30f, 0.24f, 0.16f),
			TileType.Desert       => new Color(0.90f, 0.70f, 0.28f),
			TileType.Savanna      => new Color(0.70f, 0.62f, 0.22f),
			TileType.Grassland    => new Color(0.22f, 0.62f, 0.14f),
			TileType.Ground       => new Color(0.32f, 0.26f, 0.18f),
			TileType.Forest       => new Color(0.10f, 0.38f, 0.11f),
			TileType.DenseForest  => new Color(0.06f, 0.24f, 0.08f),
			TileType.AlienGrowth  => new Color(0.06f, 0.36f, 0.28f),
			TileType.Rocky        => new Color(0.50f, 0.42f, 0.32f),
			TileType.Mountain     => new Color(0.56f, 0.52f, 0.48f),
			TileType.Snow         => new Color(0.92f, 0.96f, 1.00f),
			TileType.Crystal      => new Color(0.58f, 0.10f, 0.92f),
			TileType.Ruins        => new Color(0.48f, 0.40f, 0.26f),
			_                     => new Color(0.22f, 0.62f, 0.14f),
		};

		// Unified earthy-brown slab — all tiles share the same base colour
		// (left face slightly lighter = catches ambient; right = in shadow)
		var sideL = new Color(0.32f, 0.20f, 0.10f);
		var sideR = new Color(0.20f, 0.13f, 0.06f);
		return (fill, sideL, sideR);
	}

	private static Color Brightened(Color c, float factor)
	{
		var o = new Color(
			Mathf.Clamp(c.R * factor, 0f, 1f),
			Mathf.Clamp(c.G * factor, 0f, 1f),
			Mathf.Clamp(c.B * factor, 0f, 1f));
		o.A = c.A;
		return o;
	}

	// ── Neighbour / distance helpers ──────────────────────────────────────

	private static IEnumerable<Vector2I> Neighbours(Vector2I cell)
	{
		int c = cell.X, r = cell.Y;
		yield return new Vector2I(c + 1, r    );
		yield return new Vector2I(c - 1, r    );
		yield return new Vector2I(c,     r + 1);
		yield return new Vector2I(c,     r - 1);
		yield return new Vector2I(c + 1, r + 1);
		yield return new Vector2I(c + 1, r - 1);
		yield return new Vector2I(c - 1, r + 1);
		yield return new Vector2I(c - 1, r - 1);
	}

	public static int HexDist(Vector2I a, Vector2I b) =>
		Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

	private static IEnumerable<Vector2I> BresenhamLine(Vector2I from, Vector2I to)
	{
		int x = from.X, y = from.Y;
		int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
		int sx = from.X < to.X ? 1 : -1, sy = from.Y < to.Y ? 1 : -1;
		int err = dx - dy;
		while (true)
		{
			yield return new Vector2I(x, y);
			if (x == to.X && y == to.Y) break;
			int e2 = 2 * err;
			if (e2 > -dy) { err -= dy; x += sx; }
			if (e2 <  dx) { err += dx; y += sy; }
		}
	}

	// ═══════════════════════════════════════════════════════════════════════
	// HighlightNode — hover, selection, highlights, and arena border
	// ═══════════════════════════════════════════════════════════════════════

	private sealed partial class HighlightNode : Node2D
	{
		private readonly CombatGrid _g;
		public HighlightNode(CombatGrid g) { _g = g; }

		// Reusable draw buffers — all drawing is on the main thread
		private static readonly Vector2[] _hlVerts    = new Vector2[4];
		private static readonly Color[]   _hlColors   = new Color[4];
		private static readonly Vector2[] _borderLine = new Vector2[5];

		public override void _Draw()
		{
			if (!_g._tilesReady) return;
			var origin = _g.GridOrigin();

			foreach (var cell in _g.MoveHighlight)
				DrawDiamond(_g.GetCellVisualPos(cell), new Color(0.3f, 0.6f, 1.0f, 0.35f));

			foreach (var cell in _g.AttackHighlight)
				DrawDiamond(_g.GetCellVisualPos(cell), new Color(1.0f, 0.25f, 0.15f, 0.35f));

			foreach (var cell in _g.PreviewHighlight)
				DrawDiamond(_g.GetCellVisualPos(cell), new Color(1.0f, 0.58f, 0.0f, 0.28f));

			if (_g.HoveredCell.HasValue && _g.InBounds(_g.HoveredCell.Value))
				DrawDiamond(_g.GetCellVisualPos(_g.HoveredCell.Value), new Color(1f, 1f, 1f, 0.18f));

			if (_g.SelectedCell.HasValue && _g.InBounds(_g.SelectedCell.Value))
			{
				FillDiamondVerts(_g.GetCellVisualPos(_g.SelectedCell.Value));
				_borderLine[0] = _hlVerts[0]; _borderLine[1] = _hlVerts[1];
				_borderLine[2] = _hlVerts[2]; _borderLine[3] = _hlVerts[3];
				_borderLine[4] = _hlVerts[0];
				DrawPolyline(_borderLine, new Color(1f, 0.9f, 0.1f, 0.9f), 2.0f);
			}

			// Arena perimeter border
			float hw = TileW / 2f, hh = TileH / 2f;
			_borderLine[0] = origin + _g.GridToScreen(0,         0        ) + new Vector2(   0, -hh);
			_borderLine[1] = origin + _g.GridToScreen(GridW - 1, 0        ) + new Vector2( hw,   0);
			_borderLine[2] = origin + _g.GridToScreen(GridW - 1, GridH - 1) + new Vector2(   0,  hh);
			_borderLine[3] = origin + _g.GridToScreen(0,         GridH - 1) + new Vector2(-hw,   0);
			_borderLine[4] = _borderLine[0];
			DrawPolyline(_borderLine, new Color(0.95f, 0.72f, 0.10f, 0.20f), 9f);
			DrawPolyline(_borderLine, new Color(0.98f, 0.82f, 0.20f, 0.90f), 2.5f);
		}

		private void DrawDiamond(Vector2 center, Color color)
		{
			FillDiamondVerts(center);
			_hlColors[0] = color; _hlColors[1] = color;
			_hlColors[2] = color; _hlColors[3] = color;
			DrawPolygon(_hlVerts, _hlColors);
		}

		private static void FillDiamondVerts(Vector2 center)
		{
			float hw = TileW / 2f, hh = TileH / 2f;
			_hlVerts[0] = center + new Vector2(   0, -hh);
			_hlVerts[1] = center + new Vector2( hw,    0);
			_hlVerts[2] = center + new Vector2(   0,  hh);
			_hlVerts[3] = center + new Vector2(-hw,    0);
		}
	}
}
