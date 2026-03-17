#nullable enable
using Godot;
using System;

/// <summary>
/// Procedural isometric prop renderer — shared by CombatGrid and Chunk.
/// All draw calls are issued on the provided <paramref name="canvas"/> Node2D
/// (so they appear in that node's _Draw pass).
///
/// Props are pure DrawPolygon geometry: no sprites, no textures.
/// Target aesthetic: low-poly diorama — flat-shaded triangles/polygons,
/// bold silhouettes, single directional light from top-left.
/// </summary>
public static class PropDraw
{
	// ── Public entry point ────────────────────────────────────────────────

	/// <summary>
	/// Draw a procedural prop for <paramref name="tile"/> at <paramref name="center"/>
	/// (the visual top-face position of the tile in canvas-local space).
	/// <paramref name="seed"/> drives stable per-tile variation.
	/// <paramref name="scale"/> tunes overall size (default 1.0).
	/// </summary>
	public static void DrawProp(Node2D canvas, TileType tile, Vector2 center,
	                            int seed, float scale = 1.0f)
	{
		// Sub-tile positional jitter so props don't all sit dead-centre
		float jx = MathF.Sin(seed * 1.618034f) * 4f * scale;
		float jy = MathF.Sin(seed * 2.718282f) * 2f * scale;
		var pos = center + new Vector2(jx, jy);

		switch (tile)
		{
			case TileType.Forest:
				PineTree(canvas, pos, scale, snow: false);
				break;

			case TileType.DenseForest:
				// Two trees — one slightly back-left, one front-right and smaller
				PineTree(canvas, pos + new Vector2(-5f * scale, -2f * scale), scale * 1.10f, snow: false);
				PineTree(canvas, pos + new Vector2( 5f * scale,  0f),         scale * 0.82f, snow: false);
				break;

			case TileType.Snow:
				if ((seed & 3) != 3)   // ~75 % of tiles get a tree
					PineTree(canvas, pos, scale * 0.88f, snow: true);
				break;

			case TileType.AlienGrowth:
				if ((seed % 5) != 0)   // 80 % density
					RoundTree(canvas, pos, scale, alien: true);
				break;

			case TileType.Grassland:
				if ((seed % 3) == 0)   // ~33 % density
					RoundTree(canvas, pos, scale * 0.78f, alien: false);
				break;

			case TileType.Rocky:
				RockPile(canvas, pos, scale, seed, rubble: false);
				break;

			case TileType.Mountain:
				RockPile(canvas, pos, scale * 1.28f, seed, rubble: false);
				break;

			case TileType.Desert:
				if ((seed % 4) == 0)   // scattered cacti
					Cactus(canvas, pos, scale);
				break;

			case TileType.Ruins:
				if ((seed % 2) == 0)
					RockPile(canvas, pos, scale * 0.68f, seed, rubble: true);
				break;
		}
	}

	// ═══════════════════════════════════════════════════════════════════════
	// Pine / conifer tree
	// ═══════════════════════════════════════════════════════════════════════

	private static void PineTree(Node2D cv, Vector2 c, float s, bool snow)
	{
		ShadowEllipse(cv, c, 12f * s, 5f * s);

		// Trunk
		var trunk = new Color(0.26f, 0.17f, 0.08f);
		cv.DrawPolygon(
			new[] {
				c + new Vector2(-2.5f * s,  0),
				c + new Vector2( 2.5f * s,  0),
				c + new Vector2( 2.0f * s, -8f * s),
				c + new Vector2(-2.0f * s, -8f * s),
			},
			new[] { trunk, trunk, trunk.Lightened(0.15f), trunk.Lightened(0.15f) }
		);

		// Three stacked triangle layers — bottom wide+dark, top narrow+light
		Color[] darks = snow
			? new[] { new Color(0.18f, 0.34f, 0.18f),
			          new Color(0.22f, 0.42f, 0.22f),
			          new Color(0.28f, 0.50f, 0.28f) }
			: new[] { new Color(0.07f, 0.26f, 0.07f),
			          new Color(0.11f, 0.36f, 0.11f),
			          new Color(0.15f, 0.46f, 0.15f) };

		float[] W  = { 14f, 10.5f, 7f   };
		float[] H  = { 14f, 12f,   11f  };
		float[] Y0 = { -6f, -15f,  -24f }; // layer base Y relative to c.Y

		for (int i = 0; i < 3; i++)
		{
			float w   = W[i] * s;
			float h   = H[i] * s;
			float y   = c.Y + Y0[i] * s;
			var   col = darks[i];
			cv.DrawPolygon(
				new[] {
					new Vector2(c.X - w, y),
					new Vector2(c.X + w, y),
					new Vector2(c.X,     y - h),
				},
				new[] { col, col, col.Lightened(0.30f) }
			);
		}

		// Snow caps on upper two layers
		if (snow)
		{
			for (int i = 1; i < 3; i++)
			{
				float w   = W[i] * s * 0.58f;
				float h   = H[i] * s;
				float y   = c.Y + Y0[i] * s;
				float mid = y - h * 0.38f;
				cv.DrawPolygon(
					new[] {
						new Vector2(c.X - w, mid),
						new Vector2(c.X + w, mid),
						new Vector2(c.X,     y - h),
					},
					new[] {
						new Color(0.85f, 0.91f, 0.97f),
						new Color(0.85f, 0.91f, 0.97f),
						new Color(0.96f, 0.98f, 1.00f),
					}
				);
			}
		}
	}

	// ═══════════════════════════════════════════════════════════════════════
	// Round / deciduous tree
	// ═══════════════════════════════════════════════════════════════════════

	private static void RoundTree(Node2D cv, Vector2 c, float s, bool alien)
	{
		ShadowEllipse(cv, c, 11f * s, 4.5f * s);

		// Trunk
		var trunk = new Color(0.28f, 0.18f, 0.09f);
		cv.DrawPolygon(
			new[] {
				c + new Vector2(-2.5f * s,  0),
				c + new Vector2( 2.5f * s,  0),
				c + new Vector2( 2.0f * s, -8f * s),
				c + new Vector2(-2.0f * s, -8f * s),
			},
			new[] { trunk, trunk, trunk.Lightened(0.22f), trunk.Lightened(0.22f) }
		);

		// Canopy — flattened 10-gon approximating an isometric sphere
		Color fill      = alien ? new Color(0.05f, 0.52f, 0.40f) : new Color(0.20f, 0.58f, 0.13f);
		Color fillLight = fill.Lightened(0.35f);
		Color fillDark  = fill.Darkened(0.22f);

		var   cc  = c + new Vector2(0, -19f * s);
		float rx  = 11f * s, ry = 9f * s;
		int   seg = 10;
		var pts  = new Vector2[seg];
		var cols = new Color [seg];

		for (int i = 0; i < seg; i++)
		{
			float a = i * Mathf.Tau / seg - MathF.PI * 0.5f; // start at top
			pts[i]  = cc + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
			// Vertices near the top of the sphere are lighter (catching the light)
			float t = (pts[i].Y - (cc.Y - ry)) / (ry * 2f); // 0 = top, 1 = bottom
			cols[i] = t < 0.35f ? fillLight : t > 0.70f ? fillDark : fill;
		}
		cv.DrawPolygon(pts, cols);

		// Bottom-shadow crescent — gives the sphere depth
		cv.DrawArc(cc + new Vector2(0, ry * 0.35f), rx * 0.75f,
		           0.25f, MathF.PI - 0.25f, 8,
		           fill.Darkened(0.35f) with { A = 0.55f }, 2.5f * s);
	}

	// ═══════════════════════════════════════════════════════════════════════
	// Rock pile  (also used for rubble / ruins)
	// ═══════════════════════════════════════════════════════════════════════

	private static void RockPile(Node2D cv, Vector2 c, float s, int seed, bool rubble)
	{
		ShadowEllipse(cv, c, 14f * s, 6f * s);

		Color dark  = rubble ? new Color(0.32f, 0.27f, 0.20f) : new Color(0.30f, 0.27f, 0.24f);
		Color mid   = rubble ? new Color(0.46f, 0.39f, 0.27f) : new Color(0.48f, 0.43f, 0.38f);
		Color light = rubble ? new Color(0.60f, 0.52f, 0.38f) : new Color(0.64f, 0.58f, 0.52f);

		// Rock A — left-back
		cv.DrawPolygon(new[] {
			c + new Vector2(-13f * s,   0),
			c + new Vector2( -5f * s,   0),
			c + new Vector2( -3f * s, -13f * s),
			c + new Vector2( -9f * s, -17f * s),
			c + new Vector2(-15f * s,  -9f * s),
		}, new[] { dark, dark, mid, light, dark });

		// Rock B — right-back
		cv.DrawPolygon(new[] {
			c + new Vector2(  3f * s,   0),
			c + new Vector2( 13f * s,   0),
			c + new Vector2( 14f * s,  -8f * s),
			c + new Vector2(  9f * s, -17f * s),
			c + new Vector2(  2f * s,  -9f * s),
		}, new[] { dark, dark, mid, light, mid });

		// Rock C — front-centre (overlaps A + B for depth)
		cv.DrawPolygon(new[] {
			c + new Vector2( -6f * s,  2f * s),
			c + new Vector2(  6f * s,  2f * s),
			c + new Vector2(  6f * s, -10f * s),
			c + new Vector2( -5f * s, -11f * s),
		}, new[] { mid, mid, light, light });
	}

	// ═══════════════════════════════════════════════════════════════════════
	// Cactus
	// ═══════════════════════════════════════════════════════════════════════

	private static void Cactus(Node2D cv, Vector2 c, float s)
	{
		ShadowEllipse(cv, c, 8f * s, 3.5f * s);

		var dark  = new Color(0.10f, 0.30f, 0.08f);
		var mid   = new Color(0.16f, 0.42f, 0.12f);
		var light = new Color(0.22f, 0.54f, 0.18f);

		float tw = 3.5f * s;
		float th = 28f  * s;

		// Main trunk
		cv.DrawPolygon(new[] {
			c + new Vector2(-tw,  0),
			c + new Vector2( tw,  0),
			c + new Vector2( tw, -th),
			c + new Vector2(-tw, -th),
		}, new[] { dark, mid, mid, light });

		// Left arm — horizontal then vertical
		float aw = 9f * s, as2 = 2.5f * s;
		cv.DrawPolygon(new[] {
			c + new Vector2(-tw,       -th * 0.42f),
			c + new Vector2(-tw - aw,  -th * 0.42f),
			c + new Vector2(-tw - aw,  -th * 0.52f),
			c + new Vector2(-tw,       -th * 0.52f),
		}, new[] { dark, dark, mid, mid });
		cv.DrawPolygon(new[] {
			c + new Vector2(-tw - aw + as2, -th * 0.52f),
			c + new Vector2(-tw - aw - as2, -th * 0.52f),
			c + new Vector2(-tw - aw - as2, -th * 0.80f),
			c + new Vector2(-tw - aw + as2, -th * 0.80f),
		}, new[] { mid, dark, mid, light });

		// Right arm — slightly higher
		cv.DrawPolygon(new[] {
			c + new Vector2( tw,       -th * 0.55f),
			c + new Vector2( tw + aw,  -th * 0.55f),
			c + new Vector2( tw + aw,  -th * 0.64f),
			c + new Vector2( tw,       -th * 0.64f),
		}, new[] { dark, dark, mid, mid });
		cv.DrawPolygon(new[] {
			c + new Vector2(tw + aw - as2, -th * 0.64f),
			c + new Vector2(tw + aw + as2, -th * 0.64f),
			c + new Vector2(tw + aw + as2, -th * 0.90f),
			c + new Vector2(tw + aw - as2, -th * 0.90f),
		}, new[] { mid, dark, light, mid });
	}

	// ── Drop shadow ellipse ───────────────────────────────────────────────

	private static void ShadowEllipse(Node2D cv, Vector2 center, float rx, float ry)
	{
		const int Segs = 12;
		var pts  = new Vector2[Segs];
		var cols = new Color [Segs];
		var col  = new Color(0f, 0f, 0f, 0.26f);
		for (int i = 0; i < Segs; i++)
		{
			float a = i * Mathf.Tau / Segs;
			pts [i] = center + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
			cols[i] = col;
		}
		cv.DrawPolygon(pts, cols);
	}
}
