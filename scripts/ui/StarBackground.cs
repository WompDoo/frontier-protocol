using Godot;

/// <summary>
/// Procedural parallax star field with a planet atmosphere glow.
/// Attach to a Node2D inside SpaceLayer (CanvasLayer at layer -10).
/// In Globe mode: dark space + stars + atmosphere rings.
/// In Iso mode: alien sky horizon gradient + stars visible through thin atmosphere.
/// </summary>
public partial class StarBackground : Node2D
{
	private const int   StarCount = 350;
	private const ulong StarSeed  = 0xDEADBEEFu;

	// Star data arrays (avoid allocations per frame)
	private readonly Vector2[] _pos      = new Vector2[StarCount];
	private readonly float[]   _size     = new float[StarCount];
	private readonly float[]   _alpha    = new float[StarCount];
	private readonly float[]   _parallax = new float[StarCount];

	// Reusable polygon buffers for DrawIsoSky — allocated once, filled each frame
	private readonly Vector2[] _isoPolyVerts  = new Vector2[4];
	private readonly Color[]   _isoPolyColors = new Color[4];
	private readonly Vector2[] _auroraVerts   = new Vector2[4];
	private readonly Color[]   _auroraColors  = new Color[4];

	// Atmosphere ring constants — never change at runtime
	private static readonly float[] AtmRingScale = { 1.55f, 1.38f, 1.22f, 1.10f, 1.02f };
	private static readonly float[] AtmRingAlpha = { 0.018f, 0.030f, 0.045f, 0.055f, 0.030f };

	// Sky palette cache (derived from world seed + biome via GameManager/PartyManager)
	private Color _skyTop    = new(0.02f, 0.04f, 0.10f);
	private Color _skyHorizon= new(0.08f, 0.16f, 0.32f);
	private Color _skyGlow   = new(0.20f, 0.35f, 0.60f);
	private int   _lastSeed  = -1;

	public override void _Ready()
	{
		var rng = new RandomNumberGenerator();
		rng.Seed = StarSeed;

		for (int i = 0; i < StarCount; i++)
		{
			_pos[i] = new Vector2(rng.Randf(), rng.Randf()); // normalized 0-1

			float tier = rng.Randf();
			if (tier < 0.55f)
			{
				_size[i]     = 1.0f;
				_alpha[i]    = 0.20f + rng.Randf() * 0.15f;
				_parallax[i] = 0.004f;
			}
			else if (tier < 0.85f)
			{
				_size[i]     = 1.4f;
				_alpha[i]    = 0.35f + rng.Randf() * 0.20f;
				_parallax[i] = 0.018f;
			}
			else
			{
				_size[i]     = 2.0f;
				_alpha[i]    = 0.55f + rng.Randf() * 0.25f;
				_parallax[i] = 0.055f;
			}
		}
	}

	public override void _Process(double delta) => QueueRedraw();

	public override void _Draw()
	{
		var     vp   = GetViewport();
		Vector2 size = vp.GetVisibleRect().Size;
		var     cam  = vp.GetCamera2D();

		Vector2 camPos = cam?.GlobalPosition ?? Vector2.Zero;
		float   zoom   = cam?.Zoom.X ?? 1f;

		bool isoMode = GameManager.Instance?.Phase == GameManager.GamePhase.Iso;

		if (isoMode)
			DrawIsoSky(size, camPos, zoom);
		else
			DrawSpaceSky(size, camPos, zoom);
	}

	// ── Globe / space background ──────────────────────────────────────────────

	private void DrawSpaceSky(Vector2 size, Vector2 camPos, float zoom)
	{
		DrawRect(new Rect2(Vector2.Zero, size), new Color(0.01f, 0.01f, 0.055f, 1f));
		DrawStars(size, camPos, starAlpha: 1.0f);
		DrawAtmosphereRings(size, camPos, zoom);
	}

	// ── Isometric alien sky ───────────────────────────────────────────────────

	private void DrawIsoSky(Vector2 size, Vector2 camPos, float zoom)
	{
		// Refresh sky palette when world seed changes
		int seed = PartyManager.Instance?.WorldSeed ?? 0;
		if (seed != _lastSeed)
		{
			_lastSeed = seed;
			DeriveSkyPalette(seed);
		}

		float horizonY = size.Y * 0.62f;

		DrawRect(new Rect2(0, 0, size.X, horizonY), _skyTop with { A = 1f });

		// Atmosphere gradient band — fill reusable buffers instead of allocating
		_isoPolyVerts[0] = new Vector2(0f,     horizonY);
		_isoPolyVerts[1] = new Vector2(size.X, horizonY);
		_isoPolyVerts[2] = new Vector2(size.X, size.Y);
		_isoPolyVerts[3] = new Vector2(0f,     size.Y);
		_isoPolyColors[0] = _skyHorizon with { A = 1f };
		_isoPolyColors[1] = _isoPolyColors[0];
		_isoPolyColors[2] = _skyGlow    with { A = 1f };
		_isoPolyColors[3] = _isoPolyColors[2];
		DrawPolygon(_isoPolyVerts, _isoPolyColors);

		// Subtle aureole at the horizon line
		float auroraH    = size.Y * 0.06f;
		var   auroraColor = new Color(
			Mathf.Min(_skyHorizon.R * 1.5f, 1f),
			Mathf.Min(_skyHorizon.G * 1.5f, 1f),
			Mathf.Min(_skyHorizon.B * 1.5f, 1f));
		_auroraVerts[0] = new Vector2(0f,     horizonY - auroraH * 0.5f);
		_auroraVerts[1] = new Vector2(size.X, horizonY - auroraH * 0.5f);
		_auroraVerts[2] = new Vector2(size.X, horizonY + auroraH);
		_auroraVerts[3] = new Vector2(0f,     horizonY + auroraH);
		_auroraColors[0] = auroraColor with { A = 0f };
		_auroraColors[1] = _auroraColors[0];
		_auroraColors[2] = auroraColor with { A = 0.22f };
		_auroraColors[3] = _auroraColors[2];
		DrawPolygon(_auroraVerts, _auroraColors);

		// Stars visible through atmosphere (dimmer near horizon)
		DrawStars_Iso(size, camPos, horizonY);
	}

	private void DeriveSkyPalette(int seed)
	{
		// Deterministic sky color from world seed — each planet has a unique sky
		var rng = new System.Random(seed ^ 0x5A3C9F1B);

		// Hue families: blue, teal, purple, orange-red, green — weighted toward cool
		float hue = (float)(rng.NextDouble() * 360.0);
		float sat  = 0.35f + (float)rng.NextDouble() * 0.30f;

		// Deep space top: very dark version of the hue
		Color.FromHsv(hue, sat * 0.4f, 0.04f + (float)rng.NextDouble() * 0.04f).ToHsv(
			out float h, out float s, out float v);
		_skyTop = Color.FromHsv(h, s, v);

		// Horizon: medium brightness
		_skyHorizon = Color.FromHsv(hue, sat * 0.6f, 0.12f + (float)rng.NextDouble() * 0.10f);

		// Ground glow: slightly warmer / lighter
		float hueShift = (float)(rng.NextDouble() * 20.0 - 10.0);
		_skyGlow = Color.FromHsv((hue + hueShift + 360f) % 360f, sat * 0.5f,
		                         0.22f + (float)rng.NextDouble() * 0.10f);
	}

	// ── Star helpers ──────────────────────────────────────────────────────────

	private void DrawStars(Vector2 size, Vector2 camPos, float starAlpha)
	{
		for (int i = 0; i < StarCount; i++)
		{
			float px = _parallax[i];
			float sx = ((_pos[i].X * size.X) - camPos.X * px) % size.X;
			float sy = ((_pos[i].Y * size.Y) - camPos.Y * px) % size.Y;
			if (sx < 0) sx += size.X;
			if (sy < 0) sy += size.Y;

			DrawCircle(new Vector2(sx, sy), _size[i],
			           new Color(1f, 1f, 1f, _alpha[i] * starAlpha));
		}
	}

	private void DrawStars_Iso(Vector2 size, Vector2 camPos, float horizonY)
	{
		for (int i = 0; i < StarCount; i++)
		{
			float px = _parallax[i];
			float sx = ((_pos[i].X * size.X) - camPos.X * px) % size.X;
			float sy = ((_pos[i].Y * size.Y) - camPos.Y * px) % size.Y;
			if (sx < 0) sx += size.X;
			if (sy < 0) sy += size.Y;

			// Stars fade out toward and below the horizon
			float fadeStart = horizonY * 0.5f;
			float alpha = _alpha[i];
			if (sy > fadeStart)
				alpha *= 1f - Mathf.Clamp((sy - fadeStart) / (horizonY - fadeStart), 0f, 1f);
			if (alpha < 0.01f) continue;

			DrawCircle(new Vector2(sx, sy), _size[i], new Color(1f, 1f, 1f, alpha));
		}
	}

	private void DrawAtmosphereRings(Vector2 size, Vector2 camPos, float zoom)
	{
		Vector2 planetScreen = size * 0.5f + (-camPos) * zoom;

		float planetR = ChunkGenerator.PlanetRadius * ChunkData.Size
		                * (Chunk.TileHeight / 2f) * zoom;

		var atmColor = new Color(0.15f, 0.40f, 0.80f);

		for (int r = 0; r < AtmRingScale.Length; r++)
		{
			float radius = planetR * AtmRingScale[r];
			if (radius > 3600f) continue;
			DrawCircle(planetScreen, radius, atmColor with { A = AtmRingAlpha[r] });
		}

		if (planetR <= 3600f && planetR > 20f)
		{
			DrawCircle(planetScreen + new Vector2(planetR * 0.18f, planetR * 0.12f),
			           planetR * 0.92f,
			           new Color(0.00f, 0.00f, 0.02f, 0.06f));
		}
	}
}
