#nullable enable
using Godot;
using System;

/// <summary>
/// Continuous height-field terrain renderer for the combat arena.
/// Replaces the per-tile RowNode + OuterNode rendering — no tile grid edges visible.
///
/// Approach:
///   - Vertex grid: (TotalW+1) × (TotalH+1).  Each tile uses 4 shared corner vertices.
///   - Vertex heights = average of surrounding tile base-heights + FBM noise.
///   - Interior tiles: top face only (no side faces → no visible seams).
///   - Perimeter: SW slab face (front-left) + SE slab face (front-right).
///   - Slope shading: NW light source computed from each quad's face gradient.
///   - Props drawn in back-to-front pass after terrain geometry.
/// </summary>
public partial class CombatTerrain : Node2D
{
	// ── Mirror CombatGrid constants ────────────────────────────────────────
	private const int   GridW = CombatGrid.GridW;   // 11
	private const int   GridH = CombatGrid.GridH;   // 11
	private const int   ExtR  = CombatGrid.ExtR;    //  8
	private const float TileW = CombatGrid.TileW;   // 64
	private const float TileH = CombatGrid.TileH;   // 32

	private const int TotalW = GridW + ExtR * 2;    // 27
	private const int TotalH = GridH + ExtR * 2;    // 27
	private const int VW     = TotalW + 1;           // 28
	private const int VH     = TotalH + 1;           // 28

	// Noise-driven height range (pixels) — larger = hillier terrain
	private const float NoiseAmp  = 28f;
	// Extra depth of the outer earth slab below the lowest terrain vertex
	private const float SlabExtra = 55f;

	// ── Terrain data ──────────────────────────────────────────────────────

	private float[,]    _vH    = new float[VW, VH];
	private TileType[,] _tiles = new TileType[TotalW, TotalH];
	private float       _slabBot;   // absolute screen-Y of the slab base
	private bool        _ready;

	// ── Public API ────────────────────────────────────────────────────────

	/// <summary>
	/// Average vertex height at the centre of inner tile (gridCol, gridRow).
	/// Used by CombatGrid.GetCellVisualPos so unit positions align with terrain.
	/// </summary>
	public float GetTileVisualHeight(int gridCol, int gridRow)
	{
		int c = gridCol + ExtR, r = gridRow + ExtR;
		return (_vH[c, r] + _vH[c + 1, r] + _vH[c + 1, r + 1] + _vH[c, r + 1]) * 0.25f;
	}

	/// <summary>
	/// Build the height field from CombatGrid's tile array and noise map.
	/// Call this after every GenerateFrom / GenerateDebug.
	/// </summary>
	public void Build(TileType[,] allTiles, float[,] noisePx)
	{
		// Copy tile data
		for (int c = 0; c < TotalW; c++)
		for (int r = 0; r < TotalH; r++)
			_tiles[c, r] = allTiles[c, r];

		// Compute vertex heights: average surrounding tile base-heights + noise
		for (int u = 0; u <= TotalW; u++)
		for (int v = 0; v <= TotalH; v++)
		{
			float sum = 0f; int cnt = 0;
			for (int dc = -1; dc <= 0; dc++)
			for (int dr = -1; dr <= 0; dr++)
			{
				int tc = u + dc, tr = v + dr;
				if (tc >= 0 && tc < TotalW && tr >= 0 && tr < TotalH)
				{
					sum += TileBaseH(_tiles[tc, tr]);
					cnt++;
				}
			}
			float baseH = cnt > 0 ? sum / cnt : 0f;
			int ni = Mathf.Clamp(u, 0, TotalW - 1);
			int nj = Mathf.Clamp(v, 0, TotalH - 1);
			_vH[u, v] = baseH + noisePx[ni, nj] * NoiseAmp;
		}

		// Slab bottom = below the farthest (most screen-Y) terrain vertex
		float maxY = float.MinValue;
		for (int u = 0; u <= TotalW; u++)
		for (int v = 0; v <= TotalH; v++)
		{
			float y = VtxY(u, v);
			if (y > maxY) maxY = y;
		}
		_slabBot = maxY + SlabExtra;

		_ready = true;
		QueueRedraw();
	}

	// ── Rendering ─────────────────────────────────────────────────────────

	public override void _Draw()
	{
		if (!_ready) return;

		// Back-to-front: slab first, then terrain quads, then props
		DrawSlabPass();

		for (int sum = 0; sum <= TotalW + TotalH - 2; sum++)
		for (int c = 0; c < TotalW; c++)
		{
			int r = sum - c;
			if (r < 0 || r >= TotalH) continue;
			DrawTerrainQuad(c, r);
		}

		for (int sum = 0; sum <= TotalW + TotalH - 2; sum++)
		for (int c = 0; c < TotalW; c++)
		{
			int r = sum - c;
			if (r < 0 || r >= TotalH) continue;
			DrawProp(c, r);
		}
	}

	// ── Slab (outer earth wall) ────────────────────────────────────────────

	private void DrawSlabPass()
	{
		// Slab bottom line (flat dark strip — ties everything together)
		DrawBottomBase();

		// SW face: bottom row (r = TotalH-1) — left-facing slab side
		for (int c = 0; c < TotalW; c++)
		{
			var pW = Vtx(c,     TotalH);
			var pS = Vtx(c + 1, TotalH);
			DrawSlabFace(pW, pS, isLight: true);
		}

		// SE face: right column (c = TotalW-1) — right-facing slab side
		for (int r = 0; r < TotalH; r++)
		{
			var pS = Vtx(TotalW, r + 1);
			var pE = Vtx(TotalW, r);
			DrawSlabFace(pS, pE, isLight: false);
		}
	}

	private void DrawSlabFace(Vector2 ptA, Vector2 ptB, bool isLight)
	{
		float baseFactor = isLight ? 1.06f : 0.70f;
		float botFactor  = isLight ? 0.58f : 0.42f;
		var slab = new Color(0.28f, 0.18f, 0.09f);

		DrawPolygon(
			new[] { ptA, ptB,
			        new Vector2(ptB.X, _slabBot), new Vector2(ptA.X, _slabBot) },
			new[] {
				Bright(slab, baseFactor),
				Bright(slab, baseFactor),
				Bright(slab, botFactor),
				Bright(slab, botFactor),
			});
	}

	private void DrawBottomBase()
	{
		// Thin shadow strip under the whole slab
		var dark = new Color(0f, 0f, 0f, 0.45f);
		// SW bottom edge: row r=TotalH-1, W corners
		for (int c = 0; c < TotalW; c++)
		{
			var pW = Vtx(c, TotalH);
			var pS = Vtx(c + 1, TotalH);
			DrawPolyline(
				new[] { new Vector2(pW.X, _slabBot), new Vector2(pS.X, _slabBot) },
				dark, 1.5f);
		}
	}

	// ── Terrain quad (single tile top-face) ───────────────────────────────

	private void DrawTerrainQuad(int c, int r)
	{
		// 4 diamond corners from shared vertex grid
		var pN = Vtx(c,     r);
		var pE = Vtx(c + 1, r);
		var pS = Vtx(c + 1, r + 1);
		var pW = Vtx(c,     r + 1);

		// Outer ring: fade and darken proportionally to distance from inner arena
		int  gc   = c - ExtR, gr = r - ExtR;
		bool inner = gc >= 0 && gc < GridW && gr >= 0 && gr < GridH;
		float alpha  = 1.0f;
		float darken = 1.0f;
		if (!inner)
		{
			int dist = Math.Max(Math.Max(-gc, gc - (GridW - 1)),
			                    Math.Max(-gr, gr - (GridH - 1)));
			float ef = Mathf.Clamp(dist / (float)ExtR, 0f, 1f);
			alpha    = 1f - ef * 0.78f;
			darken   = 0.58f - ef * 0.14f;
		}

		// Slope-based shading: NW light source
		// Compute surface gradient from 4 vertex heights
		float hN = _vH[c, r], hE = _vH[c + 1, r],
		      hS = _vH[c + 1, r + 1], hW = _vH[c, r + 1];
		// Slope in iso U direction (NE)
		float slopeU = (hE + hS) * 0.5f - (hN + hW) * 0.5f;
		// Slope in iso V direction (NW)
		float slopeV = (hW + hS) * 0.5f - (hN + hE) * 0.5f;
		// Light from NW → faces slanting away from NW are darker
		float lightF = 1.0f + (-slopeU - slopeV) * 0.011f;
		lightF = Mathf.Clamp(lightF, 0.62f, 1.42f);

		// Per-tile stable hash variation (subtle, breaks uniformity)
		float hash = MathF.Abs(MathF.Sin(c * 127.1f + r * 311.7f));
		float vary = (hash - 0.5f) * 0.08f;

		TileType t = _tiles[c, r];
		var fill = TileFillColor(t);
		if (!inner) fill = Bright(fill, darken);
		var col = Bright(fill, lightF + vary) with { A = alpha };

		DrawPolygon(new[] { pN, pE, pS, pW },
		            new[] { col, col, col, col });
	}

	// ── Props ──────────────────────────────────────────────────────────────

	private void DrawProp(int c, int r)
	{
		int gc = c - ExtR, gr = r - ExtR;
		bool inner = gc >= 0 && gc < GridW && gr >= 0 && gr < GridH;

		// Fade-threshold: skip props too far into the outer ring
		if (!inner)
		{
			int dist = Math.Max(Math.Max(-gc, gc - (GridW - 1)),
			                    Math.Max(-gr, gr - (GridH - 1)));
			if (dist > ExtR * 0.70f) return;
		}

		var pN = Vtx(c,     r);
		var pE = Vtx(c + 1, r);
		var pS = Vtx(c + 1, r + 1);
		var pW = Vtx(c,     r + 1);
		// Visual top-face centre = average of 4 corners
		var centre = (pN + pE + pS + pW) * 0.25f;

		float scale = inner ? 0.90f : 0.72f;
		PropDraw.DrawProp(this, _tiles[c, r], centre, c * 37 + r * 13, scale);
	}

	// ── Vertex helpers ─────────────────────────────────────────────────────

	/// <summary>
	/// Screen position of vertex (u, v) at its stored height.
	/// Formula derivation:  gc = u-ExtR, gr = v-ExtR
	///   x = (gc-gr)*TileW/2
	///   y = GridOrigin.Y + (gc+gr)*TileH/2 - TileH/2 - H
	///     = (u+v)*16 - (2*ExtR+1)*16 - GridOriginY_neg - H
	/// For GridW=GridH=11, GridOrigin.Y = -80, so offset = 272-80 = 352 (precomputed below).
	/// </summary>
	private float VtxX(int u, int v) => (u - v) * (TileW * 0.5f);

	private float VtxY(int u, int v)
	{
		// GridOrigin().Y = -(GridW/2 + GridH/2)*TileH/2 = -(5+5)*16 = -80
		const float originY = -(GridW / 2 + GridH / 2) * TileH * 0.5f;
		return originY + (u + v - 2 * ExtR - 1) * (TileH * 0.5f) - _vH[u, v];
	}

	private Vector2 Vtx(int u, int v) => new(VtxX(u, v), VtxY(u, v));

	// ── Color helpers ──────────────────────────────────────────────────────

	private static Color TileFillColor(TileType t) => t switch
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

	/// <summary>
	/// Base pixel height for a tile type — drives vertex height averaging.
	/// Higher = more raised terrain for that biome.
	/// </summary>
	private static float TileBaseH(TileType t) => t switch
	{
		TileType.DeepOcean                           => 0f,
		TileType.Ocean                               => 0f,
		TileType.ShallowWater                        => 2f,
		TileType.Beach or TileType.MudFlat           => 4f,
		TileType.Desert or TileType.Savanna          => 7f,
		TileType.Ground or TileType.Grassland        => 10f,
		TileType.Forest                              => 18f,
		TileType.DenseForest or TileType.AlienGrowth => 24f,
		TileType.Ruins or TileType.Crystal           => 14f,
		TileType.Rocky                               => 30f,
		TileType.Mountain                            => 52f,
		TileType.Snow                                => 62f,
		_                                            => 10f,
	};

	private static Color Bright(Color c, float f) =>
		new(Mathf.Clamp(c.R * f, 0f, 1f),
		    Mathf.Clamp(c.G * f, 0f, 1f),
		    Mathf.Clamp(c.B * f, 0f, 1f), c.A);
}
