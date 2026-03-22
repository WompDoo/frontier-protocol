using Godot;

/// <summary>
/// On-screen debug overlay. Toggle with F2.
/// Shows seed, planet type, chunk, biome, tile, position, FPS, and controls.
/// M = toggle full biome overview map (world-space chunk grid, colored by biome).
/// </summary>
public partial class DebugHUD : Label
{
	[Export] public NodePath ChunkManagerPath { get; set; }
	[Export] public NodePath PlayerPath       { get; set; }

	private ChunkManager _chunkManager;
	private Node2D        _player;
	private Button        _regenButton;

	private float _fpsSmooth   = 60f;
	private bool  _showBiomeMap = false;

	// Biome map constants
	private const int   MapW    = 48;    // chunks wide
	private const int   MapH    = 32;    // chunks tall
	private const float TileW   = 18f;   // screen pixels per chunk (diamond width)
	private const float TileH   = 13.5f; // screen pixels per chunk (diamond height)

	public override void _Ready()
	{
		_chunkManager = GetNode<ChunkManager>(ChunkManagerPath);
		_player       = GetNode<Node2D>(PlayerPath);

		AnchorLeft   = 0; AnchorTop    = 0;
		AnchorRight  = 0; AnchorBottom = 0;
		Position     = new Vector2(8, 8);

		AddThemeColorOverride("font_color",        Colors.White);
		AddThemeColorOverride("font_shadow_color",  new Color(0, 0, 0, 0.8f));
		AddThemeConstantOverride("shadow_offset_x", 1);
		AddThemeConstantOverride("shadow_offset_y", 1);
		AddThemeFontSizeOverride("font_size", 13);

		_regenButton                   = new Button();
		_regenButton.Text              = "New Seed  [R]";
		_regenButton.Position          = new Vector2(0, 520);
		_regenButton.CustomMinimumSize = new Vector2(120, 0);
		_regenButton.Pressed          += () => _chunkManager?.Regenerate();
		AddChild(_regenButton);

		Visible = true; // Visible by default for dev
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.F2)
				Visible = !Visible;
			else if (key.Keycode == Key.R)
				_chunkManager?.Regenerate();
			else if (key.Keycode == Key.M)
			{
				_showBiomeMap = !_showBiomeMap;
				QueueRedraw();
			}
		}
	}

	public override void _Draw()
	{
		if (!_showBiomeMap || _chunkManager is null || _player is null) return;

		Vector2 vp      = GetViewport().GetVisibleRect().Size;
		Vector2 origin  = vp * 0.5f;   // centre of screen

		Vector2I playerChunk = ChunkManager.WorldToChunk(_player.Position);
		var      planet      = _chunkManager.Planet;
		int      seed        = _chunkManager.WorldSeed;

		// Background
		DrawRect(new Rect2(origin.X - (MapW * TileW * 0.5f) - 6,
		                   origin.Y - (MapH * TileH * 0.5f) - 6,
		                   MapW * TileW + 12, MapH * TileH + 12),
		         new Color(0, 0, 0, 0.72f));

		// Label
		DrawString(ThemeDB.FallbackFont,
		           new Vector2(origin.X - 100, origin.Y - MapH * TileH * 0.5f - 16),
		           $"BIOME MAP  [M to close]   ● = you  ({playerChunk.X},{playerChunk.Y})",
		           HorizontalAlignment.Left, -1, 12, Colors.White);

		for (int dc = -MapW / 2; dc < MapW / 2; dc++)
		for (int dr = -MapH / 2; dr < MapH / 2; dr++)
		{
			int cx = playerChunk.X + dc;
			int cy = playerChunk.Y + dr;

			BiomeType b = ChunkGenerator.GetBiome(new Vector2I(cx, cy), seed, planet);
			Color fill  = BiomeMapColor(b);

			// Isometric diamond in screen space
			float sx = origin.X + (dc - dr) * TileW * 0.5f;
			float sy = origin.Y + (dc + dr) * TileH * 0.5f;

			DrawPolygon(new[] {
				new Vector2(sx,              sy - TileH * 0.5f),
				new Vector2(sx + TileW * 0.5f, sy),
				new Vector2(sx,              sy + TileH * 0.5f),
				new Vector2(sx - TileW * 0.5f, sy),
			}, new[] { fill, fill, fill, fill });
		}

		// Player marker
		DrawCircle(origin, 3.5f, Colors.White);
	}

	private static Color BiomeMapColor(BiomeType b) => b switch
	{
		BiomeType.DeepOcean  => new Color(0.02f, 0.06f, 0.30f),
		BiomeType.Ocean      => new Color(0.04f, 0.20f, 0.54f),
		BiomeType.Coastal    => new Color(0.14f, 0.48f, 0.74f),
		BiomeType.Desert     => new Color(0.90f, 0.70f, 0.28f),
		BiomeType.Savanna    => new Color(0.70f, 0.62f, 0.22f),
		BiomeType.Grassland  => new Color(0.26f, 0.68f, 0.16f),
		BiomeType.Forest     => new Color(0.10f, 0.38f, 0.11f),
		BiomeType.Jungle     => new Color(0.05f, 0.28f, 0.08f),
		BiomeType.AlienWilds => new Color(0.06f, 0.36f, 0.28f),
		BiomeType.Highland   => new Color(0.48f, 0.40f, 0.28f),
		BiomeType.Mountain   => new Color(0.56f, 0.52f, 0.48f),
		BiomeType.Arctic     => new Color(0.88f, 0.92f, 1.00f),
		BiomeType.SafeZone   => new Color(1.00f, 1.00f, 0.40f),
		_                    => new Color(0.5f,  0.5f,  0.5f),
	};

	public override void _Process(double delta)
	{
		if (_showBiomeMap) QueueRedraw();
		if (!Visible || _chunkManager is null || _player is null) return;

		// Smooth FPS
		float fps = (float)Engine.GetFramesPerSecond();
		_fpsSmooth = Mathf.Lerp(_fpsSmooth, fps, 0.1f);

		Vector2I  chunk     = ChunkManager.WorldToChunk(_player.Position);
		var       planet    = _chunkManager.Planet;   // cached — no allocation each frame
		BiomeType biome     = ChunkGenerator.GetBiome(chunk, _chunkManager.WorldSeed, planet);
		string    pType     = ChunkGenerator.PlanetTypeLabel(planet);

		TileType? tile      = _chunkManager.GetTileAt(_player.Position);
		string    tileStr   = tile.HasValue ? tile.Value.ToString() : "—";

		Text = $"[DEBUG]  F2 = toggle\n" +
		       $"FPS:         {_fpsSmooth:F0}\n" +
		       $"Seed:        {_chunkManager.WorldSeed}\n" +
		       $"Planet:      {pType}\n" +
		       $"Water:       {_chunkManager.WaterFraction * 100f:F0}%\n" +
		       $"Chunk:       ({chunk.X}, {chunk.Y})\n" +
		       $"Biome:       {biome}\n" +
		       $"Tile:        {tileStr}\n" +
		       $"Pos:         ({_player.Position.X:F0}, {_player.Position.Y:F0})\n" +
		       $"\n" +
		       $"WASD = move\n" +
		       $"Shift+WASD = fast\n" +
		       $"RMB = teleport\n" +
		       $"Scroll = zoom\n" +
		       $"F1 = chunk borders\n" +
		       $"M  = biome map\n" +
		       $"R = new seed";
	}
}
