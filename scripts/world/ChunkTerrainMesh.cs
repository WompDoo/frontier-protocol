#nullable enable
using Godot;
using System;

/// <summary>
/// Continuous height-field terrain renderer for a single world chunk.
///
/// Replaces Chunk.cs's ArrayMesh tile-grid with the IsoTerrainMesh approach:
///   - Shared corner vertices between tiles → no seam artefacts within a chunk
///   - World-space noise sampling → seamless across adjacent chunk borders
///   - TileType base heights encode globe-generated biome data as terrain elevation
///   - Height-based colour gradient (same stops as IsoTerrainMesh)
///   - Cliff face quads on steep height drops
///   - Props via PropDraw (world-consistent prop system)
///
/// Scale:
///   VRes = 2 subdivides each 16×16 world tile chunk into a 32×32 hi-res mesh.
///   Tile pixel size halved (32×16 instead of 64×32) → same visual footprint, 4× polygons.
///   Vertex (0,0) aligns with the N corner of old Chunk tile (0,0) — world positions preserved.
/// </summary>
public partial class ChunkTerrainMesh : Node2D
{
	// ── Tile geometry ──────────────────────────────────────────────────────
	private const int   ChunkTiles = ChunkData.Size;         // 16 world tiles per axis
	private const int   VRes       = 1;                      // subdivision factor
	private const int   NTiles     = ChunkTiles * VRes;      // 32 hi-res tiles per axis
	private const int   NVerts     = NTiles + 1;             // 33 vertices per axis

	private const float TW  = 64f;   // tile pixel width  (matches Chunk.TileWidth)
	private const float TH  = 48f;   // tile pixel height (matches Chunk.TileHeight)
	private const float HTW = TW * 0.5f;   // 32
	private const float HTH = TH * 0.5f;   // 24

	// ── Height parameters ──────────────────────────────────────────────────
	// NormAmp is the denominator for HeightColor normalisation.
	// TileBaseH max (Snow=72) + NoiseAmp (20) + GlobeMacroAmp (30) = 122 → 122 so Snow at
	// high elevation still maps to ~0.82, and extreme peaks can reach 1.0.
	private const float NormAmp       = 130f;
	private const float NoiseAmp      = 28f;   // FBM noise added on top of TileBaseH
	private const float GlobeMacroAmp = 30f;   // globe elevation macro bias amplitude

	// Water constants
	private const float WaterThreshold = 9.5f;
	private const float WaterLevel     = 2.0f;
	private const float ShoreH         = 13.0f;

	// Minimum average-height drop between adjacent tiles to draw a cliff wall
	private const float CliffDrop = 14f;

	// ── Internal data ──────────────────────────────────────────────────────
	private float[,]    _vH    = new float[1, 1];  // vertex heights [NVerts, NVerts]
	private float[,]    _fDen  = new float[1, 1];  // per-tile forest density noise
	private TileType[,] _wTile = new TileType[1, 1]; // world tile type for each hi-res tile
	private bool         _ready;
	private TerrainProfile _profile;

	// ── Public API ─────────────────────────────────────────────────────────

	/// <summary>
	/// Generates the height-field from ChunkData.
	/// worldSeed must be the same global seed for all chunks — ensures seamless borders.
	/// seaLevel is the planet's effective sea level (from PlanetParams) used to bias
	/// vertex heights so the overworld macro-elevation matches the globe view.
	/// </summary>
	public void GenerateFromChunk(ChunkData data, int worldSeed, float seaLevel = 0.52f, RiverCrossing? river = null)
	{
		_profile = ProfileForBiome(data.Biome);
		_vH    = new float[NVerts, NVerts];
		_fDen  = new float[NTiles, NTiles];
		_wTile = new TileType[NTiles, NTiles];

		int cx = data.Coord.X, cy = data.Coord.Y;

		// ── Build hi-res tile-type lookup (2×2 nearest-neighbor from 16×16 world) ──
		for (int c = 0; c < NTiles; c++)
		for (int r = 0; r < NTiles; r++)
		{
			int tc = Mathf.Clamp(c / VRes, 0, ChunkTiles - 1);
			int tr = Mathf.Clamp(r / VRes, 0, ChunkTiles - 1);
			_wTile[c, r] = data.Tiles[tc, tr];
		}

		// ── Height field ────────────────────────────────────────────────────
		// Noise objects are cached per worldSeed — rebuilt only when seed changes.
		EnsureNoise(worldSeed);

		float amp   = NoiseAmp * _profile.HeightScale;
		float eBias = _profile.ElevationBias;

		// Cylindrical radius — matches ChunkGenerator.SampleElev exactly
		float CylR = ChunkGenerator.LonWidth / (2f * MathF.PI);

		for (int u = 0; u < NVerts; u++)
		for (int v = 0; v < NVerts; v++)
		{
			// World-space coordinates → seamless cross-chunk noise
			float wx = cx * NTiles + u;
			float wy = cy * NTiles + v;

			// Base height from surrounding world tiles (biome data from globe)
			float baseH = AvgBaseH(u, v, data);

			// FBM noise variation on top
			float n = _nLarge.GetNoise2D(wx, wy) * 0.60f
			        + _nMedium.GetNoise2D(wx, wy) * 0.28f
			        + _nDetail.GetNoise2D(wx, wy) * 0.12f;
			n = (n + 1f) * 0.5f;
			n = Mathf.Clamp(n + eBias * (n - 0.40f), 0f, 1f);

			// Globe macro elevation bias — cylindrical 3D noise, same seed/params as
			// ChunkGenerator.SampleElev, so high/low terrain matches the globe view.
			float gCx     = cx + u / (float)NTiles;
			float gCy     = cy + v / (float)NTiles;
			float normGx  = ((gCx % ChunkGenerator.LonWidth) + ChunkGenerator.LonWidth) % ChunkGenerator.LonWidth;
			float lonRad   = normGx / ChunkGenerator.LonWidth * 2f * MathF.PI;
			float globeElev = (_nGlobe.GetNoise3D(CylR * MathF.Cos(lonRad), CylR * MathF.Sin(lonRad), gCy) + 1f) * 0.5f;
			float globeBias = (globeElev - seaLevel) * GlobeMacroAmp;

			float h = baseH + n * amp + globeBias;

			// Clamp water-tile vertices: noise must not lift ocean above water line
			if (baseH < WaterThreshold)
				h = Mathf.Min(h, WaterLevel + 1.5f);

			_vH[u, v] = h;
		}

		// ── Optional water features (land biomes only) ───────────────────
		if (!IsWaterBiome(data.Biome))
		{
			if (river.HasValue)
			{
				// Globe-traced river: position source/sink on the correct edges
				CarveRiverDirected(river.Value.EntrySide, river.Value.ExitSide);
			}
			else
			{
				var rng = new Random(cx * 73856093 ^ cy * 19349663 ^ worldSeed);
				if (rng.NextDouble() < _profile.WaterChance)
					CarveWaterFeature(rng);
			}
		}

		// ── Forest density noise (world-space for cross-chunk coherence) ──
		for (int c = 0; c < NTiles; c++)
		for (int r = 0; r < NTiles; r++)
		{
			float wx = cx * NTiles + c, wy = cy * NTiles + r;
			_fDen[c, r] = (_nForest.GetNoise2D(wx, wy) + 1f) * 0.5f;
		}

		_ready = true;
		QueueRedraw();
	}

	// ── Base height from tile data ─────────────────────────────────────────

	/// Average TileBaseH of world tiles surrounding vertex (u, v).
	private float AvgBaseH(int u, int v, ChunkData data)
	{
		float sum = 0f; int cnt = 0;
		for (int du = -1; du <= 0; du++)
		for (int dv = -1; dv <= 0; dv++)
		{
			int wc = Mathf.Clamp(u / VRes + du, 0, ChunkTiles - 1);
			int wr = Mathf.Clamp(v / VRes + dv, 0, ChunkTiles - 1);
			sum += TileBaseH(data.Tiles[wc, wr]);
			cnt++;
		}
		return sum / cnt;
	}

	/// Pixel height associated with each tile type — drives the HeightColor gradient.
	/// Tuned so values map cleanly to HeightColor stops (NormAmp = 92).
	private static float TileBaseH(TileType t) => t switch
	{
		TileType.DeepOcean                           => 0f,
		TileType.Ocean                               => 2f,
		TileType.ShallowWater                        => 5f,
		TileType.Beach                               => 8f,
		TileType.MudFlat                             => 6f,
		TileType.Desert or TileType.Savanna          => 14f,
		TileType.Ground or TileType.Grassland        => 18f,
		TileType.Forest                              => 28f,
		TileType.DenseForest or TileType.AlienGrowth => 34f,
		TileType.Ruins                               => 22f,
		TileType.Crystal                             => 25f,
		TileType.Rocky                               => 50f,
		TileType.Mountain                            => 62f,
		TileType.Snow                                => 72f,
		_                                            => 18f,
	};

	// ── Biome → TerrainProfile ─────────────────────────────────────────────

	private static TerrainProfile ProfileForBiome(BiomeType b) => b switch
	{
		// Water biomes — flat, no land water features
		BiomeType.DeepOcean => TerrainProfile.Coastal with { ElevationBias = -0.5f, WaterChance = 0f },
		BiomeType.Ocean     => TerrainProfile.Coastal with { WaterChance = 0f },
		BiomeType.Coastal   => TerrainProfile.Coastal with { WaterChance = 0.15f },

		// Arid — rare water, lots of rocky variation
		BiomeType.Desert  => TerrainProfile.Desert  with { WaterChance = 0.04f },
		BiomeType.Savanna => TerrainProfile.Desert  with { WaterChance = 0.06f, ForestDensity = 0.22f, ElevationBias = 0.08f },

		// Temperate — occasional ponds/rivers, not every chunk
		BiomeType.Grassland => TerrainProfile.Forest with { ForestDensity = 0.30f, WaterChance = 0.08f, ElevationBias = 0.05f },
		BiomeType.Forest    => TerrainProfile.Forest with { WaterChance = 0.10f },
		BiomeType.Jungle    => TerrainProfile.Forest with { WaterChance = 0.16f, ForestDensity = 0.78f, ElevationBias = 0.12f },

		// Alien — swampy but not flooded
		BiomeType.AlienWilds => TerrainProfile.Swamp with { ForestDensity = 0.72f, WaterChance = 0.18f },

		// Elevated — dramatic height variation, minimal water
		BiomeType.Highland => TerrainProfile.Mountain with { WaterChance = 0.08f, HeightScale = 1.20f },
		BiomeType.Mountain => TerrainProfile.Mountain with { WaterChance = 0.06f },

		// Polar — glacial, sparse features
		BiomeType.Arctic => TerrainProfile.Arctic with { WaterChance = 0.10f },

		BiomeType.SafeZone => TerrainProfile.Forest with { WaterChance = 0.06f, ForestDensity = 0.28f },

		_ => TerrainProfile.Forest with { WaterChance = 0.10f },
	};

	private static bool IsWaterBiome(BiomeType b) =>
		b is BiomeType.DeepOcean or BiomeType.Ocean;

	// ── Rendering ─────────────────────────────────────────────────────────

	public override void _Draw()
	{
		if (!_ready) return;

		int maxSum = NTiles + NTiles - 2;
		for (int sum = 0; sum <= maxSum; sum++)
		for (int c = 0; c < NTiles; c++)
		{
			int r = sum - c;
			if (r < 0 || r >= NTiles) continue;
			DrawQuad(c, r);
			DrawCliffFaces(c, r);
		}

		// Props pass — separate so all terrain is rendered beneath them
		for (int sum = 0; sum <= maxSum; sum++)
		for (int c = 0; c < NTiles; c++)
		{
			int r = sum - c;
			if (r < 0 || r >= NTiles) continue;
			DrawProps(c, r);
		}
	}

	// Reusable draw buffers — all drawing is single-threaded on the main thread.
	private static readonly Vector2[] _quadVerts  = new Vector2[4];
	private static readonly Color[]   _quadColors = new Color[4];
	private static readonly Vector2[] _faceVerts  = new Vector2[4];
	private static readonly Color[]   _faceColors = new Color[4];

	// ── Terrain quad ──────────────────────────────────────────────────────

	private void DrawQuad(int c, int r)
	{
		_quadVerts[0] = Vtx(c,     r);
		_quadVerts[1] = Vtx(c + 1, r);
		_quadVerts[2] = Vtx(c + 1, r + 1);
		_quadVerts[3] = Vtx(c,     r + 1);

		float hN = _vH[c, r], hE = _vH[c + 1, r],
		      hS = _vH[c + 1, r + 1], hW = _vH[c, r + 1];
		float avgH = (hN + hE + hS + hW) * 0.25f;

		bool isWater = avgH < WaterThreshold;
		Color fill = isWater ? HeightColor(avgH / NormAmp) : TileFillColor(_wTile[c, r]);
		Color col;

		if (isWater)
		{
			float depthT = Mathf.Clamp(avgH / WaterThreshold, 0f, 1f);
			float hsh    = MathF.Abs(MathF.Sin(c * 127.1f + r * 311.7f));
			float shim   = Mathf.Lerp(0.78f, 1.08f, depthT) + hsh * 0.06f;
			col = Bright(fill, shim);
		}
		else
		{
			float slopeU = (hE + hS) * 0.5f - (hN + hW) * 0.5f;
			float slopeV = (hW + hS) * 0.5f - (hN + hE) * 0.5f;
			float lf     = Mathf.Clamp(1.0f + (-slopeU - slopeV) * 0.011f, 0.62f, 1.42f);
			float hsh    = MathF.Abs(MathF.Sin(c * 127.1f + r * 311.7f));
			float vary   = (hsh - 0.5f) * 0.08f;
			col = Bright(fill, lf + vary);
		}

		_quadColors[0] = col; _quadColors[1] = col;
		_quadColors[2] = col; _quadColors[3] = col;
		DrawPolygon(_quadVerts, _quadColors);
	}

	// ── Cliff faces ───────────────────────────────────────────────────────

	private float CenterH(int c, int r) =>
		(_vH[c, r] + _vH[c + 1, r] + _vH[c + 1, r + 1] + _vH[c, r + 1]) * 0.25f;

	private void DrawCliffFaces(int c, int r)
	{
		var slabL = new Color(0.28f, 0.19f, 0.10f); // SW face — lighter
		var slabR = new Color(0.18f, 0.12f, 0.07f); // SE face — darker
		float myH = CenterH(c, r);

		// SW face: toward tile (c, r+1)
		if (r < NTiles - 1)
		{
			float drop = myH - CenterH(c, r + 1);
			if (drop > CliffDrop)
			{
				_faceVerts[0] = Vtx(c,     r + 1);
				_faceVerts[1] = Vtx(c + 1, r + 1);
				float fh = drop * 0.90f;
				_faceVerts[3] = new Vector2(_faceVerts[0].X, _faceVerts[0].Y + fh);
				_faceVerts[2] = new Vector2(_faceVerts[1].X, _faceVerts[1].Y + fh);
				float t  = Mathf.Clamp(drop / 40f, 0f, 1f);
				_faceColors[0] = Bright(slabL, 1.05f + t * 0.15f);
				_faceColors[1] = _faceColors[0];
				_faceColors[2] = Bright(slabL, 0.48f);
				_faceColors[3] = _faceColors[2];
				DrawPolygon(_faceVerts, _faceColors);
			}
		}

		// SE face: toward tile (c+1, r)
		if (c < NTiles - 1)
		{
			float drop = myH - CenterH(c + 1, r);
			if (drop > CliffDrop)
			{
				_faceVerts[0] = Vtx(c + 1, r);
				_faceVerts[1] = Vtx(c + 1, r + 1);
				float fh = drop * 0.90f;
				_faceVerts[3] = new Vector2(_faceVerts[0].X, _faceVerts[0].Y + fh);
				_faceVerts[2] = new Vector2(_faceVerts[1].X, _faceVerts[1].Y + fh);
				float t  = Mathf.Clamp(drop / 40f, 0f, 1f);
				_faceColors[0] = Bright(slabR, 0.80f + t * 0.10f);
				_faceColors[1] = _faceColors[0];
				_faceColors[2] = Bright(slabR, 0.38f);
				_faceColors[3] = _faceColors[2];
				DrawPolygon(_faceVerts, _faceColors);
			}
		}
	}

	// ── Props ──────────────────────────────────────────────────────────────

	private void DrawProps(int c, int r)
	{
		float avgH = CenterH(c, r);
		if (avgH < 6f) return;  // water — no props

		var pN = Vtx(c,     r);
		var pE = Vtx(c + 1, r);
		var pS = Vtx(c + 1, r + 1);
		var pW = Vtx(c,     r + 1);
		var centre = (pN + pE + pS + pW) * 0.25f;

		TileType wt      = _wTile[c, r];
		int      pSeed   = c * 37 + r * 13;
		float    dBase   = 0.70f - _profile.ForestDensity * 0.65f;
		float    den     = _fDen[c, r];

		// Beach / shore: sparse rocks only
		if (avgH < 14f)
		{
			if ((pSeed & 15) == 0)
				PropDraw.DrawProp(this, TileType.Rocky, centre, pSeed, 0.50f);
			return;
		}

		// Land: PropDraw driven by world tile type + density noise
		if (den > dBase - 0.05f)
			PropDraw.DrawProp(this, wt, centre, pSeed, 0.90f);
	}

	// ── Water feature carving ──────────────────────────────────────────────

	private void CarveWaterFeature(Random rng)
	{
		float roll = (float)rng.NextDouble();
		if (roll < 0.35f) CarveRiver(rng);
		else              CarvePond(rng, small: roll < 0.70f);
	}

	private void CarvePond(Random rng, bool small)
	{
		float radius = small
			? 4.0f + (float)rng.NextDouble() * 3.0f
			: 6.5f + (float)rng.NextDouble() * 5.5f;
		float margin = radius + 4f;
		float cx = margin + (float)rng.NextDouble() * (NTiles - 2 * margin);
		float cy = margin + (float)rng.NextDouble() * (NTiles - 2 * margin);

		for (int u = 0; u < NVerts; u++)
		for (int v = 0; v < NVerts; v++)
		{
			float d = MathF.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));
			if (d < radius)
			{
				float t = d / radius;
				_vH[u, v] = Mathf.Lerp(WaterLevel * 0.7f, WaterLevel, t * t);
			}
			else if (d < radius * 1.25f)
			{
				float t = (d - radius) / (radius * 0.25f);
				float target = Mathf.Lerp(6.0f, 9.0f, t);
				_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * t * 0.35f);
			}
			else if (d < radius * 2.2f)
			{
				float t = (d - radius * 1.25f) / (radius * 0.95f);
				float target = Mathf.Lerp(ShoreH, _vH[u, v], t);
				if (_vH[u, v] > target)
					_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * t * 0.7f);
			}
		}
	}

	/// <summary>
	/// Carves a river with entry/exit determined by globe-level routing.
	/// EntrySide = -1 means the river springs from the highest point in the chunk.
	/// Sides: 0=North, 1=East, 2=South, 3=West.
	/// </summary>
	private void CarveRiverDirected(int entrySide, int exitSide)
	{
		float mid  = NTiles * 0.5f;
		Vector2 source = entrySide < 0 ? FindPeak(border: 3) : EdgeMidpoint(entrySide, mid);
		Vector2 sink   = EdgeMidpoint(exitSide, mid);

		// Deterministic meander jitter — no external rng needed
		var rng = new Random((int)(source.X * 137 + sink.X * 71 + sink.Y * 43));
		float jx = ((float)rng.NextDouble() - 0.5f) * NTiles * 0.18f;
		float jy = ((float)rng.NextDouble() - 0.5f) * NTiles * 0.18f;
		var cp1 = new Vector2(source.X + (sink.X - source.X) * 0.33f + jx,
		                      source.Y + (sink.Y - source.Y) * 0.33f + jy);
		var cp2 = new Vector2(source.X + (sink.X - source.X) * 0.67f - jx * 0.5f,
		                      source.Y + (sink.Y - source.Y) * 0.67f - jy * 0.5f);

		var pts = new Vector2[] { source, cp1, cp2, sink };

		// Slightly wider than a terrain-derived river — this is a real river, not a stream
		float rwNarrow = entrySide < 0 ? 1.8f : 2.5f;
		float rwWide   = 3.5f + (float)rng.NextDouble() * 1.5f;

		for (int u = 0; u < NVerts; u++)
		for (int v = 0; v < NVerts; v++)
		{
			float minDist = float.MaxValue;
			float tBest   = 0f;
			for (int i = 0; i < pts.Length - 1; i++)
			{
				float d = SegDistT(new Vector2(u, v), pts[i], pts[i + 1], out float segT);
				if (d < minDist) { minDist = d; tBest = (i + segT) / (pts.Length - 1f); }
			}
			float rw = Mathf.Lerp(rwNarrow, rwWide, tBest);
			if (minDist < rw)
			{
				float blend = minDist / rw;
				_vH[u, v] = Mathf.Lerp(WaterLevel, _vH[u, v], blend * blend);
			}
			else if (minDist < rw * 2.4f)
			{
				float t = (minDist - rw) / (rw * 1.4f);
				float target = Mathf.Lerp(WaterLevel + 2.0f, ShoreH, t);
				if (_vH[u, v] > target)
					_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * 0.7f);
			}
		}
	}

	private static Vector2 EdgeMidpoint(int side, float mid) => side switch
	{
		0 => new Vector2(mid,    0f),      // North
		1 => new Vector2(NTiles, mid),     // East
		2 => new Vector2(mid,    NTiles),  // South
		3 => new Vector2(0f,     mid),     // West
		_ => new Vector2(mid,    mid),     // fallback: center
	};

	private void CarveRiver(Random rng)
	{
		// Source: highest terrain point in the chunk interior — spring or glacial origin.
		// Sink: lowest terrain point on any chunk edge — where the river exits downhill.
		var source = FindPeak(border: 3);
		var sink   = FindLowestEdge(source);

		float sourceH = _vH[(int)source.X, (int)source.Y];
		float sinkH   = _vH[(int)sink.X,   (int)sink.Y];

		// Flat terrain — a pond fits better than a river
		if (sourceH - sinkH < 4f)
		{
			CarvePond(rng, small: true);
			return;
		}

		// Two control points add natural meander while keeping the flow downhill overall
		float jx = ((float)rng.NextDouble() - 0.5f) * NTiles * 0.22f;
		float jy = ((float)rng.NextDouble() - 0.5f) * NTiles * 0.22f;
		var cp1 = new Vector2(source.X + (sink.X - source.X) * 0.33f + jx,
		                      source.Y + (sink.Y - source.Y) * 0.33f + jy);
		var cp2 = new Vector2(source.X + (sink.X - source.X) * 0.67f - jx * 0.5f,
		                      source.Y + (sink.Y - source.Y) * 0.67f - jy * 0.5f);

		var pts = new Vector2[] { source, cp1, cp2, sink };

		// Rivers widen from source (narrow spring) toward mouth (broader stream)
		float rwNarrow = 1.5f;
		float rwWide   = 2.8f + (float)rng.NextDouble() * 1.5f;

		for (int u = 0; u < NVerts; u++)
		for (int v = 0; v < NVerts; v++)
		{
			float minDist = float.MaxValue;
			float tBest   = 0f;
			for (int i = 0; i < pts.Length - 1; i++)
			{
				float d = SegDistT(new Vector2(u, v), pts[i], pts[i + 1], out float segT);
				if (d < minDist)
				{
					minDist = d;
					tBest   = (i + segT) / (pts.Length - 1f);
				}
			}

			float rw = Mathf.Lerp(rwNarrow, rwWide, tBest);

			if (minDist < rw)
			{
				float blend = minDist / rw;
				_vH[u, v] = Mathf.Lerp(WaterLevel, _vH[u, v], blend * blend);
			}
			else if (minDist < rw * 2.4f)
			{
				float t = (minDist - rw) / (rw * 1.4f);
				float target = Mathf.Lerp(WaterLevel + 2.0f, ShoreH, t);
				if (_vH[u, v] > target)
					_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * 0.7f);
			}
		}
	}

	/// <summary>Highest-height interior vertex, staying <paramref name="border"/> vertices from all edges.</summary>
	private Vector2 FindPeak(int border)
	{
		float maxH = float.MinValue;
		int bu = NTiles / 2, bv = NTiles / 2;
		for (int u = border; u <= NTiles - border; u++)
		for (int v = border; v <= NTiles - border; v++)
			if (_vH[u, v] > maxH) { maxH = _vH[u, v]; bu = u; bv = v; }
		return new Vector2(bu, bv);
	}

	/// <summary>Lowest-height vertex on any chunk edge, at least 35% of chunk-width from <paramref name="source"/>.</summary>
	private Vector2 FindLowestEdge(Vector2 source)
	{
		float minH    = float.MaxValue;
		float minDist = NTiles * 0.35f;
		int   bu = NTiles / 2, bv = NTiles;

		void Check(int u, int v)
		{
			float dx = u - source.X, dy = v - source.Y;
			if (MathF.Sqrt(dx * dx + dy * dy) < minDist) return;
			float h = _vH[u, v];
			if (h >= minH) return;
			minH = h; bu = u; bv = v;
		}

		for (int u = 0; u <= NTiles; u++) { Check(u, 0);      Check(u, NTiles); }
		for (int v = 1; v  <  NTiles; v++) { Check(0, v); Check(NTiles, v); }

		return new Vector2(bu, bv);
	}

	private static float SegDistT(Vector2 p, Vector2 a, Vector2 b, out float t)
	{
		Vector2 ab = b - a, ap = p - a;
		float lsq = ab.LengthSquared();
		t = lsq < 1e-6f ? 0f : Mathf.Clamp(ap.Dot(ab) / lsq, 0f, 1f);
		return (p - (a + ab * t)).Length();
	}

	// ── Vertex helpers ─────────────────────────────────────────────────────
	// With VRes=1, vertex (u,v) maps directly to world tile (u,v).
	// VtxX/Y compute isometric screen position including height displacement.

	private float VtxX(int u, int v) => (u - v) * HTW;

	// TH subtracted so vertex(0,0) aligns with the N tip of old chunk tile(0,0).
	private float VtxY(int u, int v) => (u + v) * HTH - TH - _vH[u, v];

	private Vector2 Vtx(int u, int v) => new(VtxX(u, v), VtxY(u, v));

	// ── Colour helpers ─────────────────────────────────────────────────────

	// Per-tile flat colour — same palette as CombatTerrain for visual consistency.
	// Water tiles still use the height gradient (depth-based), but all land tiles
	// get their biome colour directly from TileType so colours are vibrant + unambiguous.
	private static Color TileFillColor(TileType t) => t switch
	{
		TileType.DeepOcean                    => new Color(0.02f, 0.06f, 0.28f),
		TileType.Ocean                        => new Color(0.04f, 0.18f, 0.52f),
		TileType.ShallowWater                 => new Color(0.14f, 0.48f, 0.74f),
		TileType.Beach                        => new Color(0.88f, 0.80f, 0.52f),
		TileType.MudFlat                      => new Color(0.30f, 0.24f, 0.16f),
		TileType.Desert                       => new Color(0.90f, 0.70f, 0.28f),
		TileType.Savanna                      => new Color(0.70f, 0.62f, 0.22f),
		TileType.Grassland or TileType.Ground => new Color(0.22f, 0.62f, 0.14f),
		TileType.Forest                       => new Color(0.10f, 0.38f, 0.11f),
		TileType.DenseForest                  => new Color(0.06f, 0.24f, 0.08f),
		TileType.AlienGrowth                  => new Color(0.06f, 0.36f, 0.28f),
		TileType.Rocky                        => new Color(0.50f, 0.42f, 0.32f),
		TileType.Mountain                     => new Color(0.56f, 0.52f, 0.48f),
		TileType.Snow                         => new Color(0.92f, 0.96f, 1.00f),
		TileType.Crystal                      => new Color(0.58f, 0.10f, 0.92f),
		TileType.Ruins                        => new Color(0.48f, 0.40f, 0.26f),
		_                                     => new Color(0.22f, 0.62f, 0.14f),
	};

	// 8-stop height-colour gradient cached as static readonly — zero allocation per call.
	private static readonly float[] HeightColorStops =
		{ 0.00f, 0.06f, 0.13f, 0.24f, 0.46f, 0.64f, 0.80f, 1.00f };
	private static readonly Color[] HeightColorMap =
	{
		new(0.04f, 0.16f, 0.58f),  // 0.00 — deep water
		new(0.08f, 0.38f, 0.74f),  // 0.06 — shallow water
		new(0.80f, 0.73f, 0.46f),  // 0.13 — beach / sand
		new(0.34f, 0.55f, 0.20f),  // 0.24 — muted grassland
		new(0.13f, 0.40f, 0.11f),  // 0.46 — forest green
		new(0.42f, 0.34f, 0.22f),  // 0.64 — rocky / earthy
		new(0.56f, 0.52f, 0.48f),  // 0.80 — mountain grey
		new(0.88f, 0.93f, 1.00f),  // 1.00 — snow
	};

	private static Color HeightColor(float h01)
	{
		h01 = Mathf.Clamp(h01, 0f, 1f);
		for (int i = 0; i < HeightColorStops.Length - 1; i++)
		{
			if (h01 <= HeightColorStops[i + 1])
			{
				float t = (h01 - HeightColorStops[i]) / (HeightColorStops[i + 1] - HeightColorStops[i]);
				return HeightColorMap[i].Lerp(HeightColorMap[i + 1], t);
			}
		}
		return HeightColorMap[HeightColorMap.Length - 1];
	}

	private static Color Bright(Color c, float f) =>
		new(Mathf.Clamp(c.R * f, 0f, 1f),
		    Mathf.Clamp(c.G * f, 0f, 1f),
		    Mathf.Clamp(c.B * f, 0f, 1f), c.A);

	// ── Noise factory + cache ─────────────────────────────────────────────

	// Noise objects are stateless for reading — safe to share across all chunks
	// of the same worldSeed. Rebuilt only when the seed changes (e.g. Regenerate).
	private static int            _cachedSeed = int.MinValue;
	private static FastNoiseLite  _nLarge  = null!;
	private static FastNoiseLite  _nMedium = null!;
	private static FastNoiseLite  _nDetail = null!;
	private static FastNoiseLite  _nForest = null!;
	private static FastNoiseLite  _nGlobe  = null!;  // matches ChunkGenerator.SampleElev

	private static void EnsureNoise(int worldSeed)
	{
		if (_cachedSeed == worldSeed) return;
		_cachedSeed = worldSeed;
		_nLarge  = Noise(worldSeed,      0.006f, 4);
		_nMedium = Noise(worldSeed + 17, 0.016f, 3);
		_nDetail = Noise(worldSeed + 37, 0.040f, 2);
		_nForest = Noise(worldSeed + 73, 0.022f, 2);

		// Globe elevation noise — Perlin, same seed XOR as ChunkGenerator.SampleElev
		_nGlobe = new FastNoiseLite();
		_nGlobe.Seed           = worldSeed ^ 0x1A2B3C4D;
		_nGlobe.NoiseType      = FastNoiseLite.NoiseTypeEnum.Perlin;
		_nGlobe.FractalType    = FastNoiseLite.FractalTypeEnum.Fbm;
		_nGlobe.FractalOctaves = 5;
		_nGlobe.Frequency      = 0.09f;
	}

	private static FastNoiseLite Noise(int seed, float freq, int octaves)
	{
		var n = new FastNoiseLite();
		n.Seed           = seed;
		n.NoiseType      = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
		n.FractalType    = FastNoiseLite.FractalTypeEnum.Fbm;
		n.FractalOctaves = octaves;
		n.Frequency      = freq;
		return n;
	}
}
