using Godot;

/// <summary>
/// Procedural parallax star field with a planet atmosphere glow.
/// Attach to a Node2D inside SpaceLayer (CanvasLayer at layer -10).
/// Stars wrap seamlessly and have 3 depth tiers with different parallax rates.
/// The planet atmosphere disc becomes visible when zoomed out far enough.
/// </summary>
public partial class StarBackground : Node2D
{
	private const int   StarCount    = 350;
	private const ulong StarSeed     = 0xDEADBEEFu;

	// Star data arrays (avoid allocations per frame)
	private readonly Vector2[] _pos      = new Vector2[StarCount];
	private readonly float[]   _size     = new float[StarCount];
	private readonly float[]   _alpha    = new float[StarCount];
	private readonly float[]   _parallax = new float[StarCount];

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
				// Far: tiny, dim, barely moves
				_size[i]     = 1.0f;
				_alpha[i]    = 0.20f + rng.Randf() * 0.15f;
				_parallax[i] = 0.004f;
			}
			else if (tier < 0.85f)
			{
				// Mid: medium, moderate movement
				_size[i]     = 1.4f;
				_alpha[i]    = 0.35f + rng.Randf() * 0.20f;
				_parallax[i] = 0.018f;
			}
			else
			{
				// Near: larger, more visible, noticeable parallax
				_size[i]     = 2.0f;
				_alpha[i]    = 0.55f + rng.Randf() * 0.25f;
				_parallax[i] = 0.055f;
			}
		}
	}

	public override void _Process(double delta) => QueueRedraw();

	public override void _Draw()
	{
		var     vp     = GetViewport();
		Vector2 size   = vp.GetVisibleRect().Size;
		var     cam    = vp.GetCamera2D();

		Vector2 camPos = cam?.GlobalPosition ?? Vector2.Zero;
		float   zoom   = cam?.Zoom.X ?? 1f;

		// ── Background fill ───────────────────────────────────────────────────
		DrawRect(new Rect2(Vector2.Zero, size), new Color(0.01f, 0.01f, 0.055f, 1f));

		// ── Star layers ───────────────────────────────────────────────────────
		for (int i = 0; i < StarCount; i++)
		{
			float px = _parallax[i];

			// Shift star position by camera, then tile/wrap within screen bounds
			float sx = ((_pos[i].X * size.X) - camPos.X * px) % size.X;
			float sy = ((_pos[i].Y * size.Y) - camPos.Y * px) % size.Y;
			if (sx < 0) sx += size.X;
			if (sy < 0) sy += size.Y;

			DrawCircle(new Vector2(sx, sy), _size[i],
			           new Color(1f, 1f, 1f, _alpha[i]));
		}

		// ── Planet atmosphere glow ────────────────────────────────────────────
		// World origin (0,0) maps to screen: viewport_centre + (-camPos * zoom)
		Vector2 planetScreen = size * 0.5f + (-camPos) * zoom;

		// In isometric space, PlanetRadius chunks in the ±X tile direction =
		// PlanetRadius × ChunkSize × TileWidth/2 screen pixels at zoom=1.
		// We approximate with the smaller (Y) isometric axis so the glow fits
		// the visible disc rather than the wide diamond.
		float planetR = ChunkGenerator.PlanetRadius * ChunkData.Size
		                * (Chunk.TileHeight / 2f) * zoom;

		// Outer glow rings (drawn large → small so inner rings paint over outer)
		float[] ringScale = { 1.55f, 1.38f, 1.22f, 1.10f, 1.02f };
		float[] ringAlpha = { 0.018f, 0.030f, 0.045f, 0.055f, 0.030f };
		var     atmColor  = new Color(0.15f, 0.40f, 0.80f);

		for (int r = 0; r < ringScale.Length; r++)
		{
			float radius = planetR * ringScale[r];

			// Skip rings that are absurdly large (zoomed in, fully on planet surface)
			// — Godot becomes slow drawing circles with radius > ~4000px
			if (radius > 3600f) continue;

			DrawCircle(planetScreen, radius,
			           atmColor with { A = ringAlpha[r] });
		}

		// Terminator shadow on the planet disc (subtle directional shading)
		if (planetR <= 3600f && planetR > 20f)
		{
			// Faint dark crescent on one side to break the flat look
			DrawCircle(planetScreen + new Vector2(planetR * 0.18f, planetR * 0.12f),
			           planetR * 0.92f,
			           new Color(0.00f, 0.00f, 0.02f, 0.06f));
		}
	}
}
