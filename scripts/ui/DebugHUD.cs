using Godot;

/// <summary>
/// On-screen debug overlay. Toggle with F2.
/// Shows seed, planet type, chunk, biome, tile, position, FPS, and controls.
/// </summary>
public partial class DebugHUD : Label
{
	[Export] public NodePath ChunkManagerPath { get; set; }
	[Export] public NodePath PlayerPath       { get; set; }

	private ChunkManager _chunkManager;
	private Node2D        _player;
	private Button        _regenButton;

	private float _fpsSmooth  = 60f;

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
		}
	}

	public override void _Process(double delta)
	{
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
		       $"R = new seed";
	}
}
