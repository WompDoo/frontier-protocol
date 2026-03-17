#nullable enable
using Godot;
using System;

/// <summary>
/// Data-driven parameters passed in from the overworld when hooking up.
/// Call Generate(seed, w, h, profile) to apply.  All fields have sensible
/// defaults — Forest is the baseline biome used during standalone testing.
/// </summary>
public struct TerrainProfile
{
	/// Probability [0,1] that a water feature spawns (river / pond / lake).
	public float WaterChance;

	/// Height-field bias: -1 = very flat, 0 = default, +1 = very hilly.
	/// Applied as a sigmoid-style contrast on the raw noise output.
	public float ElevationBias;

	/// Forest cluster density [0,1].  0.5 = default noise-driven.
	/// Lower threshold = more tiles grow trees.
	public float ForestDensity;

	/// Overall height amplitude scale (1.0 = default 58 px).
	/// Mountain biomes use 1.4; coastal 0.75.
	public float HeightScale;

	// ── Biome presets ────────────────────────────────────────────────────

	public static TerrainProfile Forest   => new() { WaterChance = 0.45f, ElevationBias =  0.10f, ForestDensity = 0.60f, HeightScale = 1.00f };
	public static TerrainProfile Desert   => new() { WaterChance = 0.06f, ElevationBias =  0.05f, ForestDensity = 0.08f, HeightScale = 0.90f };
	public static TerrainProfile Mountain => new() { WaterChance = 0.20f, ElevationBias =  0.65f, ForestDensity = 0.25f, HeightScale = 1.45f };
	public static TerrainProfile Arctic   => new() { WaterChance = 0.30f, ElevationBias =  0.45f, ForestDensity = 0.12f, HeightScale = 1.30f };
	public static TerrainProfile Coastal  => new() { WaterChance = 0.85f, ElevationBias = -0.15f, ForestDensity = 0.40f, HeightScale = 0.80f };
	public static TerrainProfile Swamp    => new() { WaterChance = 0.75f, ElevationBias = -0.35f, ForestDensity = 0.65f, HeightScale = 0.70f };
}

/// <summary>
/// Standalone procedural isometric terrain renderer.
/// Targets the low-poly diorama aesthetic from the reference images:
///   - Smooth height-field mesh (no tile-grid seams)
///   - Height-based colour gradient (water → sand → grass → forest → rock → snow)
///   - Dense mixed forest placement: 2–3 trees per tile in forest zones
///   - Slope shading from NW light source
///   - Clean earthy slab base
///
/// No dependencies on combat or world systems — drop in anywhere with Generate(seed).
/// </summary>
public partial class IsoTerrainMesh : Node2D
{
	// ── Tile dimensions ───────────────────────────────────────────────────
	// Halved vs the combat grid (32×16 instead of 64×32) so we can use 2×
	// the tile count in the same visual footprint → 4× polygon density.
	private const float TW  = 32f;
	private const float TH  = 16f;
	private const float HTW = TW * 0.5f;   // 16
	private const float HTH = TH * 0.5f;   //  8

	// ── Terrain size (tiles) ──────────────────────────────────────────────
	private int _w = 54;   // width  (2 × CombatGrid extended grid)
	private int _h = 54;   // height

	// ── Height parameters ─────────────────────────────────────────────────
	private const float HeightAmp = 58f;    // max terrain height in pixels
	private const float SlabExtra = 60f;    // depth below lowest vertex

	// ── Internal data ─────────────────────────────────────────────────────
	private float[,]    _vH     = new float[1, 1]; // vertex heights [vw, vh]
	private float[,]    _fDen   = new float[1, 1]; // per-tile forest-density noise
	private float        _slabBot;
	private bool         _ready;
	private TerrainProfile _profile = TerrainProfile.Forest;

	// ── Public API ────────────────────────────────────────────────────────

	/// <summary>
	/// Backward-compatible overload — uses Forest profile with the given waterChance.
	/// </summary>
	public void Generate(int seed, int w = 54, int h = 54, float waterChance = 0.45f)
	{
		var p = TerrainProfile.Forest;
		p.WaterChance = waterChance;
		Generate(seed, w, h, p);
	}

	/// <summary>
	/// Full API — drives generation from an overworld-supplied TerrainProfile.
	/// Call after adding the node to the scene tree.
	/// </summary>
	public void Generate(int seed, int w, int h, TerrainProfile profile)
	{
		_profile = profile;
		_w = w; _h = h;
		int vw = _w + 1, vh = _h + 1;

		_vH   = new float[vw, vh];
		_fDen = new float[_w, _h];

		// ── Height field: 3 overlapping noise layers ───────────────────
		var nLarge  = Noise(seed,      0.025f, 4); // large rolling hills
		var nMedium = Noise(seed + 17, 0.065f, 3); // medium undulation
		var nDetail = Noise(seed + 37, 0.160f, 2); // fine surface texture

		float amp = HeightAmp * profile.HeightScale;

		for (int u = 0; u < vw; u++)
		for (int v = 0; v < vh; v++)
		{
			float n = nLarge .GetNoise2D(u, v) * 0.60f
			        + nMedium.GetNoise2D(u, v) * 0.28f
			        + nDetail.GetNoise2D(u, v) * 0.12f;
			n = (n + 1f) * 0.5f; // 0..1
			// Base bias — grassland/forest dominate by default
			n = Mathf.Clamp(n * 1.22f - 0.08f, 0f, 1f);
			// ElevationBias: positive = push values away from midpoint (hillier)
			//               negative = pull toward midpoint (flatter)
			float bias = profile.ElevationBias;
			n = Mathf.Clamp(n + bias * (n - 0.40f), 0f, 1f);
			_vH[u, v] = n * amp;
		}

		// ── Water feature: carve pond / lake / river into height field ──
		ApplyWaterFeature(seed, profile.WaterChance);

		// ── Forest density: separate noise for organic forest clusters ──
		var nForest = Noise(seed + 73, 0.088f, 2);
		for (int c = 0; c < _w; c++)
		for (int r = 0; r < _h; r++)
			_fDen[c, r] = (nForest.GetNoise2D(c, r) + 1f) * 0.5f;

		// ── Slab bottom ────────────────────────────────────────────────
		float maxY = float.MinValue;
		for (int u = 0; u < vw; u++)
		for (int v = 0; v < vh; v++)
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

		// Back-to-front painter's order
		int maxSum = _w + _h - 2;
		for (int sum = 0; sum <= maxSum; sum++)
		for (int c = 0; c < _w; c++)
		{
			int r = sum - c;
			if (r < 0 || r >= _h) continue;

			// Slab faces for front-edge tiles (draw before top face)
			if (r == _h - 1) DrawSlabSW(c, r);
			if (c == _w - 1) DrawSlabSE(c, r);

			DrawQuad(c, r);
		// Cliff faces after the quad so adjacent tiles naturally overlap their top edge
		DrawCliffFaces(c, r);
		}

		// Props pass (separate so all terrain is drawn first)
		for (int sum = 0; sum <= maxSum; sum++)
		for (int c = 0; c < _w; c++)
		{
			int r = sum - c;
			if (r < 0 || r >= _h) continue;
			DrawProps(c, r);
		}
	}

	// ── Slab ──────────────────────────────────────────────────────────────

	private static readonly Color _slabBase = new(0.26f, 0.17f, 0.08f);

	private void DrawSlabSW(int c, int r)
	{
		_slabVerts[0] = Vtx(c,     _h);
		_slabVerts[1] = Vtx(c + 1, _h);
		_slabVerts[2] = new Vector2(_slabVerts[1].X, _slabBot);
		_slabVerts[3] = new Vector2(_slabVerts[0].X, _slabBot);
		_slabColors[0] = Bright(_slabBase, 1.12f); _slabColors[1] = Bright(_slabBase, 1.05f);
		_slabColors[2] = Bright(_slabBase, 0.54f); _slabColors[3] = Bright(_slabBase, 0.58f);
		DrawPolygon(_slabVerts, _slabColors);
	}

	private void DrawSlabSE(int c, int r)
	{
		_slabVerts[0] = Vtx(_w, r + 1);
		_slabVerts[1] = Vtx(_w, r);
		_slabVerts[2] = new Vector2(_slabVerts[1].X, _slabBot);
		_slabVerts[3] = new Vector2(_slabVerts[0].X, _slabBot);
		_slabColors[0] = Bright(_slabBase, 0.72f); _slabColors[1] = Bright(_slabBase, 0.78f);
		_slabColors[2] = Bright(_slabBase, 0.38f); _slabColors[3] = Bright(_slabBase, 0.36f);
		DrawPolygon(_slabVerts, _slabColors);
	}

	// Reusable draw buffers — all drawing is single-threaded on the main thread.
	private static readonly Vector2[] _quadVerts  = new Vector2[4];
	private static readonly Color[]   _quadColors = new Color[4];
	private static readonly Vector2[] _faceVerts  = new Vector2[4];
	private static readonly Color[]   _faceColors = new Color[4];
	private static readonly Vector2[] _slabVerts  = new Vector2[4];
	private static readonly Color[]   _slabColors = new Color[4];

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

		Color fill = HeightColor(avgH / HeightAmp);
		Color col;

		bool isWater = avgH < WaterThreshold;
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
			float lf     = Mathf.Clamp(1.0f + (-slopeU - slopeV) * 0.014f, 0.58f, 1.48f);
			float hsh    = MathF.Abs(MathF.Sin(c * 127.1f + r * 311.7f));
			float vary   = (hsh - 0.5f) * 0.06f;
			col = Bright(fill, lf + vary);
		}

		_quadColors[0] = col; _quadColors[1] = col;
		_quadColors[2] = col; _quadColors[3] = col;
		DrawPolygon(_quadVerts, _quadColors);
	}

	// ── Props ──────────────────────────────────────────────────────────────

	private void DrawProps(int c, int r)
	{
		float avgH = (_vH[c, r] + _vH[c + 1, r] + _vH[c + 1, r + 1] + _vH[c, r + 1]) * 0.25f;

		// Terrain-surface centre of this tile
		var pN = Vtx(c, r); var pE = Vtx(c + 1, r);
		var pS = Vtx(c + 1, r + 1); var pW = Vtx(c, r + 1);
		var centre = (pN + pE + pS + pW) * 0.25f;

		int seed = c * 73856093 ^ r * 19349663;

		// ── Water: no props ───────────────────────────────────────────
		if (avgH < 6f) return;

		// ── Beach: occasional small rocks ────────────────────────────
		if (avgH < 13f)
		{
			if ((seed & 7) == 0)
				RockPile(centre, seed, 0.55f, rubble: false);
			return;
		}

		// Density thresholds scaled by profile.ForestDensity [0,1].
		// ForestDensity=0.5 → threshold 0.62 (default noise-driven)
		// ForestDensity=1.0 → threshold 0.37 (very dense)
		// ForestDensity=0.0 → threshold 0.87 (sparse)
		float dBase = 0.87f - _profile.ForestDensity * 0.50f;

		// ── Grassland: scattered round trees ─────────────────────────
		if (avgH < 28f)
		{
			float den = _fDen[c, r];
			if (den > dBase + 0.08f)
			{
				int s2 = seed ^ 0xABCD;
				var off = new Vector2(MathF.Sin(s2 * 1.6f) * 8f, MathF.Sin(s2 * 2.9f) * 4f);
				RoundTree(centre + off, seed, 0.88f, autumn: false);
			}
			return;
		}

		// ── Forest: 1–3 dense trees per tile ─────────────────────────
		if (avgH < 46f)
		{
			float den    = _fDen[c, r];
			int   count  = den > dBase - 0.12f ? 3 : den > dBase ? 2 : 1;
			bool  isPine = (seed & 3) != 0; // 75 % pine, 25 % round

			for (int i = 0; i < count; i++)
			{
				int   si  = seed ^ (i * 0xDEAD + 0xBEEF);
				float ox  = MathF.Sin(si * 1.618f) * (count > 1 ? 13f : 6f);
				float oy  = MathF.Sin(si * 2.718f) * (count > 1 ?  6f : 3f);
				float sc  = 0.90f + MathF.Abs(MathF.Sin(si * 3.14f)) * 0.22f;
				var   pos = centre + new Vector2(ox, oy);

				if (isPine)
					PineTree(pos, si, sc, snow: false);
				else
				{
					bool autumn = (si & 15) == 0; // ~6 % autumn
					RoundTree(pos, si, sc * 0.95f, autumn);
				}
			}
			return;
		}

		// ── Rocky: rock piles + occasional pine ───────────────────────
		if (avgH < 58f)
		{
			RockPile(centre, seed, 1.10f, rubble: false);
			if ((seed & 3) == 0)
			{
				var off = new Vector2(MathF.Sin(seed * 2.2f) * 7f, -4f);
				PineTree(centre + off, seed ^ 0xFF, 0.72f, snow: false);
			}
			return;
		}

		// ── Mountain / snow ───────────────────────────────────────────
		RockPile(centre, seed, 1.30f, rubble: false);
		if ((seed & 1) == 0)
			PineTree(centre + new Vector2(MathF.Sin(seed * 1.4f) * 6f, -3f),
			         seed, 0.80f, snow: true);
	}

	// ── Inline prop draw calls ─────────────────────────────────────────────
	// (copies of PropDraw internals so IsoTerrainMesh controls colors/size fully)

	private void PineTree(Vector2 c, int seed, float s, bool snow)
	{
		ShadowEllipse(c, 12f * s, 5f * s);

		var trunk = new Color(0.24f, 0.15f, 0.07f);
		DrawPolygon(
			new[] { c + new Vector2(-2.5f * s,  0),
			        c + new Vector2( 2.5f * s,  0),
			        c + new Vector2( 2.0f * s, -8f * s),
			        c + new Vector2(-2.0f * s, -8f * s) },
			new[] { trunk, trunk, trunk.Lightened(0.18f), trunk.Lightened(0.18f) });

		Color[] layers = snow
			? new[] { new Color(0.18f, 0.34f, 0.18f), new Color(0.22f, 0.42f, 0.22f), new Color(0.28f, 0.50f, 0.28f) }
			: new[] { new Color(0.06f, 0.24f, 0.07f), new Color(0.10f, 0.34f, 0.10f), new Color(0.14f, 0.44f, 0.14f) };
		float[] W  = { 15f, 11f, 7.5f };
		float[] H  = { 15f, 13f, 11f  };
		float[] Y0 = { -5f, -15f, -25f };

		for (int i = 0; i < 3; i++)
		{
			float w = W[i] * s, h = H[i] * s, y = c.Y + Y0[i] * s;
			var col = layers[i];
			DrawPolygon(
				new[] { new Vector2(c.X - w, y), new Vector2(c.X + w, y), new Vector2(c.X, y - h) },
				new[] { col, col, col.Lightened(0.28f) });
		}

		if (snow)
		{
			for (int i = 1; i < 3; i++)
			{
				float w = W[i] * s * 0.58f, h = H[i] * s, y = c.Y + Y0[i] * s;
				float mid = y - h * 0.38f;
				DrawPolygon(
					new[] { new Vector2(c.X - w, mid), new Vector2(c.X + w, mid), new Vector2(c.X, y - h) },
					new[] { new Color(0.84f, 0.90f, 0.97f), new Color(0.84f, 0.90f, 0.97f), new Color(0.96f, 0.98f, 1f) });
			}
		}
	}

	private void RoundTree(Vector2 c, int seed, float s, bool autumn)
	{
		ShadowEllipse(c, 11f * s, 4.5f * s);

		var trunk = new Color(0.27f, 0.17f, 0.08f);
		DrawPolygon(
			new[] { c + new Vector2(-2.5f * s,  0),
			        c + new Vector2( 2.5f * s,  0),
			        c + new Vector2( 2.0f * s, -8f * s),
			        c + new Vector2(-2.0f * s, -8f * s) },
			new[] { trunk, trunk, trunk.Lightened(0.20f), trunk.Lightened(0.20f) });

		// Canopy color: spring green OR autumn (red/orange/yellow)
		Color fill, fillLight, fillDark;
		if (autumn)
		{
			// Mix of warm autumn tones based on seed
			int av = seed & 3;
			fill = av == 0 ? new Color(0.82f, 0.22f, 0.05f)   // deep red
			     : av == 1 ? new Color(0.90f, 0.52f, 0.06f)   // orange
			     : av == 2 ? new Color(0.88f, 0.72f, 0.08f)   // golden yellow
			     :           new Color(0.74f, 0.28f, 0.06f);  // brick red
		}
		else
		{
			// Spring/summer green — slightly varied
			int gv = seed & 3;
			fill = gv == 0 ? new Color(0.22f, 0.64f, 0.12f)
			     : gv == 1 ? new Color(0.28f, 0.70f, 0.10f)
			     : gv == 2 ? new Color(0.18f, 0.56f, 0.08f)
			     :           new Color(0.34f, 0.62f, 0.14f);
		}
		fillLight = fill.Lightened(0.32f);
		fillDark  = fill.Darkened(0.20f);

		var cc  = c + new Vector2(0, -18f * s);
		float rx = 11f * s, ry = 9f * s;
		const int Seg = 10;
		var pts  = new Vector2[Seg];
		var cols = new Color[Seg];
		for (int i = 0; i < Seg; i++)
		{
			float a = i * Mathf.Tau / Seg - MathF.PI * 0.5f;
			pts[i]  = cc + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
			float t = (pts[i].Y - (cc.Y - ry)) / (ry * 2f);
			cols[i] = t < 0.35f ? fillLight : t > 0.70f ? fillDark : fill;
		}
		DrawPolygon(pts, cols);
		DrawArc(cc + new Vector2(0, ry * 0.35f), rx * 0.75f,
		        0.25f, MathF.PI - 0.25f, 8, fill.Darkened(0.35f) with { A = 0.50f }, 2.4f * s);
	}

	private void RockPile(Vector2 c, int seed, float s, bool rubble)
	{
		ShadowEllipse(c, 14f * s, 6f * s);

		Color dark  = rubble ? new Color(0.30f, 0.25f, 0.18f) : new Color(0.28f, 0.25f, 0.22f);
		Color mid   = rubble ? new Color(0.44f, 0.37f, 0.25f) : new Color(0.46f, 0.41f, 0.36f);
		Color light = rubble ? new Color(0.58f, 0.50f, 0.36f) : new Color(0.62f, 0.56f, 0.50f);

		DrawPolygon(new[] {
			c + new Vector2(-13f*s,    0), c + new Vector2( -5f*s,    0),
			c + new Vector2( -3f*s, -13f*s), c + new Vector2( -9f*s, -17f*s),
			c + new Vector2(-15f*s,  -9f*s),
		}, new[] { dark, dark, mid, light, dark });

		DrawPolygon(new[] {
			c + new Vector2(  3f*s,    0), c + new Vector2( 13f*s,    0),
			c + new Vector2( 14f*s,  -8f*s), c + new Vector2(  9f*s, -17f*s),
			c + new Vector2(  2f*s,  -9f*s),
		}, new[] { dark, dark, mid, light, mid });

		DrawPolygon(new[] {
			c + new Vector2(-6f*s,  2f*s), c + new Vector2( 6f*s,  2f*s),
			c + new Vector2( 6f*s, -10f*s), c + new Vector2(-5f*s, -11f*s),
		}, new[] { mid, mid, light, light });
	}

	private void ShadowEllipse(Vector2 center, float rx, float ry)
	{
		const int Segs = 12;
		var pts  = new Vector2[Segs];
		var cols = new Color[Segs];
		var col  = new Color(0f, 0f, 0f, 0.22f);
		for (int i = 0; i < Segs; i++)
		{
			float a = i * Mathf.Tau / Segs;
			pts[i]  = center + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
			cols[i] = col;
		}
		DrawPolygon(pts, cols);
	}

	// ── Vertex helpers ────────────────────────────────────────────────────

	// GridOrigin: position the grid centred on this node's local origin.
	// Tile (_w/2, _h/2) centres at screen (0, 0).
	private Vector2 GridOrigin() =>
		new(-(_w * 0.5f - _h * 0.5f) * HTW,
		    -(_w * 0.5f + _h * 0.5f) * HTH);

	private float VtxX(int u, int v) => GridOrigin().X + (u - v) * HTW;

	private float VtxY(int u, int v)
	{
		var o = GridOrigin();
		return o.Y + (u + v) * HTH - HTH - _vH[u, v];
	}

	private Vector2 Vtx(int u, int v) => new(VtxX(u, v), VtxY(u, v));

	// ── Water feature ────────────────────────────────────────────────────

	/// Height below which a quad is treated as water (flat shading, no props).
	private const float WaterThreshold = 8.5f;
	private const float WaterLevel     = 2.8f;  // surface height carved to
	private const float ShoreH         = 11.0f; // shore-transition target height

	/// <summary>
	/// Optionally carve a pond, lake, or river into the height field.
	/// 0–20% roll = river; 20–35% = pond; 35–45% = lake; 45–100% = no water.
	/// </summary>
	private void ApplyWaterFeature(int seed, float chance)
	{
		var rng  = new Random(seed ^ 0x4A3B2C1D);
		float r0 = (float)rng.NextDouble();
		if (r0 >= chance) return;

		float roll = r0 / chance; // normalise to [0, 1] within the "water" portion
		if      (roll < 0.30f) Carve_River(rng);
		else if (roll < 0.65f) Carve_Pond (rng, small: true);
		else                   Carve_Pond (rng, small: false);
	}

	private void Carve_Pond(Random rng, bool small)
	{
		float radius = small
			? 3.6f + (float)rng.NextDouble() * 3.2f   // 3.6 – 6.8 tiles
			: 6.0f + (float)rng.NextDouble() * 5.6f;  // 6.0 – 11.6 tiles

		// Avoid placing too close to the perimeter
		float margin = radius + 4f;
		float cx = margin + (float)rng.NextDouble() * (_w - 2 * margin);
		float cy = margin + (float)rng.NextDouble() * (_h - 2 * margin);

		int vw = _w + 1, vh = _h + 1;
		for (int u = 0; u < vw; u++)
		for (int v = 0; v < vh; v++)
		{
			float d = MathF.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy));

			if (d < radius)
			{
				// Water basin: smooth bowl, deepest at centre
				float t = d / radius;
				_vH[u, v] = Mathf.Lerp(WaterLevel * 0.7f, WaterLevel, t * t);
			}
			else if (d < radius * 1.22f)
			{
				// Sandy beach band: heights pulled into 5–8 px range
				// HeightColor hits the sandy stop at h01 ≈ 0.09 → 5.2 px
				float t      = (d - radius) / (radius * 0.22f);
				float target = Mathf.Lerp(6.0f, 8.5f, t);
				_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * t * 0.35f);
			}
			else if (d < radius * 2.2f)
			{
				// Shore ramp back to terrain
				float t      = (d - radius * 1.22f) / (radius * 0.98f);
				float target = Mathf.Lerp(ShoreH, _vH[u, v], t);
				if (_vH[u, v] > target)
					_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * t * 0.7f);
			}
		}
	}

	private void Carve_River(Random rng)
	{
		// 4 waypoints: enter one edge, exit another, winding through the middle
		var pts = new Vector2[4];
		float wf = _w, hf = _h, m = 2f;
		int dir = rng.Next(4);

		switch (dir)
		{
			case 0: // top → bottom
				pts[0] = new(m + (float)rng.NextDouble() * (wf - 2 * m), 0);
				pts[1] = new(wf * 0.20f + (float)rng.NextDouble() * wf * 0.60f, hf * 0.33f);
				pts[2] = new(wf * 0.20f + (float)rng.NextDouble() * wf * 0.60f, hf * 0.66f);
				pts[3] = new(m + (float)rng.NextDouble() * (wf - 2 * m), hf);
				break;
			case 1: // left → right
				pts[0] = new(0,  m + (float)rng.NextDouble() * (hf - 2 * m));
				pts[1] = new(wf * 0.33f, hf * 0.20f + (float)rng.NextDouble() * hf * 0.60f);
				pts[2] = new(wf * 0.66f, hf * 0.20f + (float)rng.NextDouble() * hf * 0.60f);
				pts[3] = new(wf, m + (float)rng.NextDouble() * (hf - 2 * m));
				break;
			case 2: // top-left corner → bottom-right
				pts[0] = new(m + (float)rng.NextDouble() * wf * 0.35f, 0);
				pts[1] = new(wf * 0.25f + (float)rng.NextDouble() * wf * 0.15f, hf * 0.33f);
				pts[2] = new(wf * 0.55f + (float)rng.NextDouble() * wf * 0.15f, hf * 0.66f);
				pts[3] = new(wf, m + (float)rng.NextDouble() * hf * 0.60f);
				break;
			default: // top → right
				pts[0] = new(m + (float)rng.NextDouble() * (wf - 2 * m), 0);
				pts[1] = new(wf * 0.40f + (float)rng.NextDouble() * wf * 0.20f, hf * 0.28f);
				pts[2] = new(wf * 0.65f + (float)rng.NextDouble() * wf * 0.20f, hf * 0.42f);
				pts[3] = new(wf, m + (float)rng.NextDouble() * hf * 0.45f);
				break;
		}

		float rw = 2.6f + (float)rng.NextDouble() * 2.4f; // river half-width in tiles (doubled for 54-grid)

		int vw = _w + 1, vh = _h + 1;
		for (int u = 0; u < vw; u++)
		for (int v = 0; v < vh; v++)
		{
			float minDist = float.MaxValue;
			for (int i = 0; i < pts.Length - 1; i++)
			{
				float d = SegDist(new Vector2(u, v), pts[i], pts[i + 1]);
				if (d < minDist) minDist = d;
			}

			if (minDist < rw)
			{
				// Inside channel: carve to water level (smooth trough)
				float blend = minDist / rw;
				_vH[u, v] = Mathf.Lerp(WaterLevel, _vH[u, v], blend * blend);
			}
			else if (minDist < rw * 2.4f)
			{
				// Bank: gentle rise back to terrain
				float t      = (minDist - rw) / (rw * 1.4f);
				float target = Mathf.Lerp(WaterLevel + 2.0f, ShoreH, t);
				if (_vH[u, v] > target)
					_vH[u, v] = Mathf.Lerp(target, _vH[u, v], t * 0.7f);
			}
		}
	}

	private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
	{
		Vector2 ab  = b - a, ap = p - a;
		float   lsq = ab.LengthSquared();
		float   t   = lsq < 1e-6f ? 0f : Mathf.Clamp(ap.Dot(ab) / lsq, 0f, 1f);
		return (p - (a + ab * t)).Length();
	}

	// ── Cliff faces ───────────────────────────────────────────────────────

	/// Average height of tile (c, r) — shared with DrawCliffFaces.
	private float CenterH(int c, int r) =>
		(_vH[c, r] + _vH[c + 1, r] + _vH[c + 1, r + 1] + _vH[c, r + 1]) * 0.25f;

	/// Minimum height drop between adjacent tile centres to draw a cliff face.
	private const float CliffDrop = 12f;

	/// <summary>
	/// Draws SW and SE cliff side-faces when the current tile is significantly
	/// higher than its painted-after neighbours.  The adjacent tile's quad is
	/// drawn later in painter's order and naturally covers the face's top edge,
	/// leaving only the visible cliff wall below.
	/// </summary>
	private void DrawCliffFaces(int c, int r)
	{
		var slabL = new Color(0.28f, 0.19f, 0.10f); // SW face (lighter / more lit)
		var slabR = new Color(0.18f, 0.12f, 0.07f); // SE face (darker / shadowed)
		float myH = CenterH(c, r);

		// ── SW face: toward tile (c, r+1) ─────────────────────────────
		if (r < _h - 1)
		{
			float drop = myH - CenterH(c, r + 1);
			if (drop > CliffDrop)
			{
				_faceVerts[0] = Vtx(c,     r + 1);
				_faceVerts[1] = Vtx(c + 1, r + 1);
				float fh = drop * 0.90f;
				_faceVerts[3] = new Vector2(_faceVerts[0].X, _faceVerts[0].Y + fh);
				_faceVerts[2] = new Vector2(_faceVerts[1].X, _faceVerts[1].Y + fh);
				float t = Mathf.Clamp(drop / 40f, 0f, 1f);
				_faceColors[0] = Bright(slabL, 1.05f + t * 0.15f);
				_faceColors[1] = _faceColors[0];
				_faceColors[2] = Bright(slabL, 0.48f);
				_faceColors[3] = _faceColors[2];
				DrawPolygon(_faceVerts, _faceColors);
			}
		}

		// ── SE face: toward tile (c+1, r) ─────────────────────────────
		if (c < _w - 1)
		{
			float drop = myH - CenterH(c + 1, r);
			if (drop > CliffDrop)
			{
				_faceVerts[0] = Vtx(c + 1, r);
				_faceVerts[1] = Vtx(c + 1, r + 1);
				float fh = drop * 0.90f;
				_faceVerts[3] = new Vector2(_faceVerts[0].X, _faceVerts[0].Y + fh);
				_faceVerts[2] = new Vector2(_faceVerts[1].X, _faceVerts[1].Y + fh);
				float t = Mathf.Clamp(drop / 40f, 0f, 1f);
				_faceColors[0] = Bright(slabR, 0.80f + t * 0.10f);
				_faceColors[1] = _faceColors[0];
				_faceColors[2] = Bright(slabR, 0.38f);
				_faceColors[3] = _faceColors[2];
				DrawPolygon(_faceVerts, _faceColors);
			}
		}
	}

	// ── Colour helpers ────────────────────────────────────────────────────

	// Height-colour gradient cached as static readonly — zero allocation per call.
	private static readonly float[] HeightColorStops =
		{ 0.00f, 0.09f, 0.20f, 0.42f, 0.62f, 0.80f, 1.00f };
	private static readonly Color[] HeightColorMap =
	{
		new(0.06f, 0.30f, 0.76f),  // 0.00 — deep water
		new(0.86f, 0.80f, 0.50f),  // 0.09 — sandy beach
		new(0.30f, 0.70f, 0.16f),  // 0.20 — bright grassland
		new(0.12f, 0.42f, 0.10f),  // 0.42 — forest green
		new(0.40f, 0.33f, 0.22f),  // 0.62 — rocky/earthy
		new(0.55f, 0.51f, 0.47f),  // 0.80 — mountain grey
		new(0.90f, 0.94f, 1.00f),  // 1.00 — snow
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

	// ── Noise factory ────────────────────────────────────────────────────

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
