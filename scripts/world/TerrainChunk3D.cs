#nullable enable
using Godot;
using System;

/// <summary>
/// Generates a 3D heightfield mesh for one world chunk.
/// Vertex formula: x3d=(u-v)*HTW, y3d=h*√2, z3d=(u+v)*HTH*√2
/// This maps identically to the 2D isometric projection when the camera is at -45° X rotation.
/// Placed as a child of OverworldRenderer's terrain root; position set externally.
/// </summary>
public partial class TerrainChunk3D : Node3D
{
	private const float Sqrt2 = 1.41421356f;

	private const int   ChunkTiles = ChunkData.Size;   // 16
	private const int   VRes       = 1;
	private const int   NTiles     = ChunkTiles * VRes; // 16
	private const int   NVerts     = NTiles + 1;        // 17

	private const float HTW = Chunk.TileWidth  * 0.5f; // 32
	private const float HTH = Chunk.TileHeight * 0.5f; // 24

	private const float NormAmp        = 130f;
	private const float NoiseAmp       = 28f;
	private const float GlobeMacroAmp  = 30f;
	private const float WaterThreshold = 9.5f;
	private const float WaterLevel     = 2.0f;
	private const float ShoreH         = 13.0f;

	private float[,]    _vH    = new float[1, 1];
	private TileType[,] _wTile = new TileType[1, 1];

	// ── Public API ─────────────────────────────────────────────────────────

	public void GenerateFromChunk(ChunkData data, int worldSeed, float seaLevel = 0.52f, RiverCrossing? river = null)
	{
		var profile = ProfileForBiome(data.Biome);
		_vH    = new float[NVerts, NVerts];
		_wTile = new TileType[NTiles, NTiles];

		int cx = data.Coord.X, cy = data.Coord.Y;

		for (int c = 0; c < NTiles; c++)
		for (int r = 0; r < NTiles; r++)
		{
			int tc = Mathf.Clamp(c / VRes, 0, ChunkTiles - 1);
			int tr = Mathf.Clamp(r / VRes, 0, ChunkTiles - 1);
			_wTile[c, r] = data.Tiles[tc, tr];
		}

		EnsureNoise(worldSeed);
		float amp   = NoiseAmp * profile.HeightScale;
		float eBias = profile.ElevationBias;
		float CylR  = ChunkGenerator.LonWidth / (2f * MathF.PI);

		for (int u = 0; u < NVerts; u++)
		for (int v = 0; v < NVerts; v++)
		{
			float wx      = cx * NTiles + u;
			float wy      = cy * NTiles + v;
			float baseH   = AvgBaseH(u, v, data);

			float n = _nLarge.GetNoise2D(wx, wy)  * 0.60f
			        + _nMedium.GetNoise2D(wx, wy) * 0.28f
			        + _nDetail.GetNoise2D(wx, wy) * 0.12f;
			n = (n + 1f) * 0.5f;
			n = Mathf.Clamp(n + eBias * (n - 0.40f), 0f, 1f);

			float gCx     = cx + u / (float)NTiles;
			float gCy     = cy + v / (float)NTiles;
			float normGx  = ((gCx % ChunkGenerator.LonWidth) + ChunkGenerator.LonWidth) % ChunkGenerator.LonWidth;
			float lonRad   = normGx / ChunkGenerator.LonWidth * 2f * MathF.PI;
			float globeElev = (_nGlobe.GetNoise3D(CylR * MathF.Cos(lonRad), CylR * MathF.Sin(lonRad), gCy) + 1f) * 0.5f;
			float globeBias = (globeElev - seaLevel) * GlobeMacroAmp;

			float h = baseH + n * amp + globeBias;
			if (baseH < WaterThreshold)
				h = Mathf.Min(h, WaterLevel + 1.5f);  // water tiles don't rise above water
			else
				h = Mathf.Max(h, ShoreH);              // land tiles don't sink below shore
			_vH[u, v] = h;
		}

		if (!IsWaterBiome(data.Biome))
		{
			if (river.HasValue)
				CarveRiverDirected(river.Value.EntrySide, river.Value.ExitSide);
			else
			{
				var rng = new Random(cx * 73856093 ^ cy * 19349663 ^ worldSeed);
				if (rng.NextDouble() < profile.WaterChance)
					CarveWaterFeature(rng);
			}
		}

		BuildMesh();
	}

	// ── Mesh construction ──────────────────────────────────────────────────

	private void BuildMesh()
	{
		var mat = new StandardMaterial3D();
		mat.VertexColorUseAsAlbedo = true;
		mat.ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded;

		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);

		for (int c = 0; c < NTiles; c++)
		for (int r = 0; r < NTiles; r++)
		{
			float hN = _vH[c, r],     hE = _vH[c + 1, r],
			      hS = _vH[c + 1, r + 1], hW = _vH[c, r + 1];
			float avgH = (hN + hE + hS + hW) * 0.25f;

			Color col = GetTileColor(c, r, avgH);

			var v0 = Vtx3D(c,     r,     hN);
			var v1 = Vtx3D(c + 1, r,     hE);
			var v2 = Vtx3D(c + 1, r + 1, hS);
			var v3 = Vtx3D(c,     r + 1, hW);

			// Triangle 1
			st.SetColor(col); st.AddVertex(v0);
			st.SetColor(col); st.AddVertex(v2);
			st.SetColor(col); st.AddVertex(v1);

			// Triangle 2
			st.SetColor(col); st.AddVertex(v0);
			st.SetColor(col); st.AddVertex(v3);
			st.SetColor(col); st.AddVertex(v2);
		}

		var mesh = st.Commit();
		var mi   = new MeshInstance3D();
		mi.Mesh             = mesh;
		mi.MaterialOverride = mat;
		AddChild(mi);
	}

	// ── 3D vertex position ─────────────────────────────────────────────────
	// Maps (u,v,h) to the same screen pixel as the 2D isometric Vtx(u,v)
	// when the Camera3D has RotationDegrees=(-45,0,0) and matching ortho size.

	private static Vector3 Vtx3D(int u, int v, float h) =>
		new((u - v) * HTW, h * Sqrt2, (u + v) * HTH * Sqrt2);

	// ── Colour ─────────────────────────────────────────────────────────────

	private Color GetTileColor(int c, int r, float avgH)
	{
		// Use tile TYPE (not height) to decide water vs land.
		// Height-based water detection would incorrectly colour land tiles as ocean
		// when the globe macro elevation bias pushes heights below WaterThreshold.
		bool isWaterTile = _wTile[c, r] is TileType.DeepOcean
		                                 or TileType.Ocean
		                                 or TileType.ShallowWater;

		if (isWaterTile)
		{
			float depthT = Mathf.Clamp(avgH / WaterThreshold, 0f, 1f);
			var   fill   = HeightColor(avgH / NormAmp);
			float shim   = Mathf.Lerp(0.78f, 1.08f, depthT);
			return Bright(fill, shim);
		}

		var   baseCol = TileFillColor(_wTile[c, r]);
		float hsh     = MathF.Abs(MathF.Sin(c * 127.1f + r * 311.7f));
		float vary    = (hsh - 0.5f) * 0.08f;
		return Bright(baseCol, 1.0f + vary);
	}

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

	private static readonly float[] HeightColorStops =
		{ 0.00f, 0.06f, 0.13f, 0.24f, 0.46f, 0.64f, 0.80f, 1.00f };
	private static readonly Color[] HeightColorMap =
	{
		new(0.04f, 0.16f, 0.58f),
		new(0.08f, 0.38f, 0.74f),
		new(0.80f, 0.73f, 0.46f),
		new(0.34f, 0.55f, 0.20f),
		new(0.13f, 0.40f, 0.11f),
		new(0.42f, 0.34f, 0.22f),
		new(0.56f, 0.52f, 0.48f),
		new(0.88f, 0.93f, 1.00f),
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
		return HeightColorMap[^1];
	}

	private static Color Bright(Color c, float f) =>
		new(Mathf.Clamp(c.R * f, 0f, 1f),
		    Mathf.Clamp(c.G * f, 0f, 1f),
		    Mathf.Clamp(c.B * f, 0f, 1f), c.A);

	// ── Height helpers ─────────────────────────────────────────────────────

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

	// ── Biome profile ──────────────────────────────────────────────────────

	private static TerrainProfile ProfileForBiome(BiomeType b) => b switch
	{
		BiomeType.DeepOcean  => TerrainProfile.Coastal with { ElevationBias = -0.5f, WaterChance = 0f },
		BiomeType.Ocean      => TerrainProfile.Coastal with { WaterChance = 0f },
		BiomeType.Coastal    => TerrainProfile.Coastal with { WaterChance = 0.15f },
		BiomeType.Desert     => TerrainProfile.Desert  with { WaterChance = 0.04f },
		BiomeType.Savanna    => TerrainProfile.Desert  with { WaterChance = 0.06f, ForestDensity = 0.22f, ElevationBias = 0.08f },
		BiomeType.Grassland  => TerrainProfile.Forest  with { ForestDensity = 0.30f, WaterChance = 0.08f, ElevationBias = 0.05f },
		BiomeType.Forest     => TerrainProfile.Forest  with { WaterChance = 0.10f },
		BiomeType.Jungle     => TerrainProfile.Forest  with { WaterChance = 0.16f, ForestDensity = 0.78f, ElevationBias = 0.12f },
		BiomeType.AlienWilds => TerrainProfile.Swamp   with { ForestDensity = 0.72f, WaterChance = 0.18f },
		BiomeType.Highland   => TerrainProfile.Mountain with { WaterChance = 0.08f, HeightScale = 1.20f },
		BiomeType.Mountain   => TerrainProfile.Mountain with { WaterChance = 0.06f },
		BiomeType.Arctic     => TerrainProfile.Arctic  with { WaterChance = 0.10f },
		BiomeType.SafeZone   => TerrainProfile.Forest  with { WaterChance = 0.06f, ForestDensity = 0.28f },
		_                    => TerrainProfile.Forest  with { WaterChance = 0.10f },
	};

	private static bool IsWaterBiome(BiomeType b) =>
		b is BiomeType.DeepOcean or BiomeType.Ocean;

	// ── Water features (same logic as ChunkTerrainMesh) ───────────────────

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
		float px = margin + (float)rng.NextDouble() * (NTiles - 2 * margin);
		float py = margin + (float)rng.NextDouble() * (NTiles - 2 * margin);

		for (int u = 0; u < NVerts; u++)
		for (int v = 0; v < NVerts; v++)
		{
			float d = MathF.Sqrt((u - px) * (u - px) + (v - py) * (v - py));
			if (d < radius)
			{
				float t = d / radius;
				_vH[u, v] = Mathf.Lerp(WaterLevel * 0.7f, WaterLevel, t * t);
			}
			else if (d < radius * 1.25f)
			{
				float t      = (d - radius) / (radius * 0.25f);
				float target = Mathf.Lerp(6.0f, 9.0f, t);
				_vH[u, v]   = Mathf.Lerp(target, _vH[u, v], t * t * 0.35f);
			}
			else if (d < radius * 2.2f)
			{
				float t      = (d - radius * 1.25f) / (radius * 0.95f);
				float target = Mathf.Lerp(ShoreH, _vH[u, v], t);
				if (_vH[u, v] > target)
					_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * t * 0.7f);
			}
		}
	}

	private void CarveRiverDirected(int entrySide, int exitSide)
	{
		float mid    = NTiles * 0.5f;
		Vector2 src  = entrySide < 0 ? FindPeak(border: 3) : EdgeMidpoint(entrySide, mid);
		Vector2 sink = EdgeMidpoint(exitSide, mid);

		var rng = new Random((int)(src.X * 137 + sink.X * 71 + sink.Y * 43));
		float jx = ((float)rng.NextDouble() - 0.5f) * NTiles * 0.18f;
		float jy = ((float)rng.NextDouble() - 0.5f) * NTiles * 0.18f;
		var cp1 = new Vector2(src.X + (sink.X - src.X) * 0.33f + jx,
		                      src.Y + (sink.Y - src.Y) * 0.33f + jy);
		var cp2 = new Vector2(src.X + (sink.X - src.X) * 0.67f - jx * 0.5f,
		                      src.Y + (sink.Y - src.Y) * 0.67f - jy * 0.5f);

		var pts = new Vector2[] { src, cp1, cp2, sink };
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
				_vH[u, v] = Mathf.Lerp(WaterLevel, _vH[u, v], (minDist / rw) * (minDist / rw));
			else if (minDist < rw * 2.4f)
			{
				float t = (minDist - rw) / (rw * 1.4f);
				float target = Mathf.Lerp(WaterLevel + 2.0f, ShoreH, t);
				if (_vH[u, v] > target)
					_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * 0.7f);
			}
		}
	}

	private void CarveRiver(Random rng)
	{
		var source = FindPeak(border: 3);
		var sink   = FindLowestEdge(source);
		if (_vH[(int)source.X, (int)source.Y] - _vH[(int)sink.X, (int)sink.Y] < 4f)
		{
			CarvePond(rng, small: true);
			return;
		}
		float jx = ((float)rng.NextDouble() - 0.5f) * NTiles * 0.22f;
		float jy = ((float)rng.NextDouble() - 0.5f) * NTiles * 0.22f;
		var cp1 = new Vector2(source.X + (sink.X - source.X) * 0.33f + jx,
		                      source.Y + (sink.Y - source.Y) * 0.33f + jy);
		var cp2 = new Vector2(source.X + (sink.X - source.X) * 0.67f - jx * 0.5f,
		                      source.Y + (sink.Y - source.Y) * 0.67f - jy * 0.5f);

		var pts     = new Vector2[] { source, cp1, cp2, sink };
		float rwN   = 1.5f;
		float rwW   = 2.8f + (float)rng.NextDouble() * 1.5f;

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
			float rw = Mathf.Lerp(rwN, rwW, tBest);
			if (minDist < rw)
				_vH[u, v] = Mathf.Lerp(WaterLevel, _vH[u, v], (minDist / rw) * (minDist / rw));
			else if (minDist < rw * 2.4f)
			{
				float t = (minDist - rw) / (rw * 1.4f);
				float target = Mathf.Lerp(WaterLevel + 2.0f, ShoreH, t);
				if (_vH[u, v] > target)
					_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * 0.7f);
			}
		}
	}

	private Vector2 FindPeak(int border)
	{
		float maxH = float.MinValue;
		int bu = NTiles / 2, bv = NTiles / 2;
		for (int u = border; u <= NTiles - border; u++)
		for (int v = border; v <= NTiles - border; v++)
			if (_vH[u, v] > maxH) { maxH = _vH[u, v]; bu = u; bv = v; }
		return new Vector2(bu, bv);
	}

	private Vector2 FindLowestEdge(Vector2 source)
	{
		float minH    = float.MaxValue;
		float minDist = NTiles * 0.35f;
		int   bu = NTiles / 2, bv = NTiles;

		void Check(int u, int v)
		{
			float dx = u - source.X, dy = v - source.Y;
			if (MathF.Sqrt(dx * dx + dy * dy) < minDist) return;
			if (_vH[u, v] >= minH) return;
			minH = _vH[u, v]; bu = u; bv = v;
		}

		for (int u = 0; u <= NTiles; u++) { Check(u, 0); Check(u, NTiles); }
		for (int v = 1; v <  NTiles; v++) { Check(0, v); Check(NTiles, v); }
		return new Vector2(bu, bv);
	}

	private static Vector2 EdgeMidpoint(int side, float mid) => side switch
	{
		0 => new Vector2(mid,    0f),
		1 => new Vector2(NTiles, mid),
		2 => new Vector2(mid,    NTiles),
		3 => new Vector2(0f,     mid),
		_ => new Vector2(mid,    mid),
	};

	private static float SegDistT(Vector2 p, Vector2 a, Vector2 b, out float t)
	{
		Vector2 ab = b - a, ap = p - a;
		float   lsq = ab.LengthSquared();
		t = lsq < 1e-6f ? 0f : Mathf.Clamp(ap.Dot(ab) / lsq, 0f, 1f);
		return (p - (a + ab * t)).Length();
	}

	// ── Noise cache (mirrors ChunkTerrainMesh, same seed XOR) ─────────────
	// Sharing the same static cache means no redundant noise construction
	// when both ChunkTerrainMesh and TerrainChunk3D coexist (future proofing).
	// In 3D-only mode (UseThreeDRenderer=true), only this class constructs the noise.

	private static int           _cachedSeed = int.MinValue;
	private static FastNoiseLite _nLarge  = null!;
	private static FastNoiseLite _nMedium = null!;
	private static FastNoiseLite _nDetail = null!;
	private static FastNoiseLite _nGlobe  = null!;

	private static void EnsureNoise(int worldSeed)
	{
		if (_cachedSeed == worldSeed) return;
		_cachedSeed = worldSeed;
		_nLarge  = Noise(worldSeed,      0.006f, 4);
		_nMedium = Noise(worldSeed + 17, 0.016f, 3);
		_nDetail = Noise(worldSeed + 37, 0.040f, 2);

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
