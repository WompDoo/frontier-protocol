using Godot;
using System.Collections.Generic;

/// <summary>
/// Manages chunk loading/unloading around the player.
/// Generates a 5×5 window of chunks centred on the player's current chunk.
/// </summary>
public partial class ChunkManager : Node2D
{
	private const int LoadRadius = 2;

	[Export] public int      WorldSeed  { get; set; } = 0;
	[Export] public NodePath PlayerPath { get; set; }

	private Node2D                          _player;
	private ChunkGenerator.PlanetParams     _planet;
	private readonly Dictionary<Vector2I, Chunk> _chunks = new();
	private Vector2I _lastPlayerChunk = new(int.MinValue, int.MinValue);

	public int                          Seed          => WorldSeed;
	public ChunkGenerator.PlanetParams  Planet        => _planet;
	public float                        WaterFraction { get; set; }   // set by GlobeView after texture gen

	public override void _Ready()
	{
		_player = GetNode<Node2D>(PlayerPath);

		if (WorldSeed == 0)
			WorldSeed = (int)GD.Randi();

		_planet = ChunkGenerator.DeriveParams(WorldSeed);

		GD.Print($"[World] seed={WorldSeed}  {ChunkGenerator.PlanetTypeLabel(_planet)}");
		GD.Print($"[World] {ChunkGenerator.BiomeDistributionInfo(_planet)}");
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo
		    && key.Keycode == Key.F1)
		{
			Chunk.DebugBorders = !Chunk.DebugBorders;
			foreach (var chunk in _chunks.Values)
				chunk.QueueRedraw();
			GD.Print($"[Debug] Chunk borders: {(Chunk.DebugBorders ? "ON" : "OFF")}");
		}
	}

	public override void _Process(double delta)
	{
		if (_player is null) return;
		var chunkCoord = WorldToChunk(_player.Position);
		if (chunkCoord == _lastPlayerChunk) return;
		_lastPlayerChunk = chunkCoord;
		UpdateChunks(chunkCoord);
	}

	// ── Chunk lifecycle ───────────────────────────────────────────────────────

	public void Regenerate()
	{
		foreach (var chunk in _chunks.Values)
			chunk.QueueFree();
		_chunks.Clear();

		WorldSeed = (int)GD.Randi();
		_planet   = ChunkGenerator.DeriveParams(WorldSeed);

		GD.Print($"[World] REGEN seed={WorldSeed}  {ChunkGenerator.PlanetTypeLabel(_planet)}");
		GD.Print($"[World] {ChunkGenerator.BiomeDistributionInfo(_planet)}");

		_lastPlayerChunk = new(int.MinValue, int.MinValue); // force reload next tick
		if (_player is not null)
			UpdateChunks(WorldToChunk(_player.Position));
	}

	// ── Tile lookup ───────────────────────────────────────────────────────────

	/// <summary>Returns the tile type at a world position, or null if the chunk isn't loaded.</summary>
	public TileType? GetTileAt(Vector2 worldPos)
	{
		Vector2I chunkCoord = WorldToChunk(worldPos);
		if (!_chunks.TryGetValue(chunkCoord, out Chunk chunk) || chunk.Data == null)
			return null;

		Vector2 chunkOrigin = ChunkToWorld(chunkCoord);
		Vector2 localPos    = worldPos - chunkOrigin;

		// Inverse of TileToLocal: sx=(tx-ty)*TW/2,  sy=(tx+ty)*TH/2
		float tw = Chunk.TileWidth / 2f;
		float th = Chunk.TileHeight / 2f;
		float tx = (localPos.X / tw + localPos.Y / th) * 0.5f;
		float ty = (localPos.Y / th - localPos.X / tw) * 0.5f;

		int ix = Mathf.FloorToInt(tx);
		int iy = Mathf.FloorToInt(ty);

		if (ix < 0 || ix >= ChunkData.Size || iy < 0 || iy >= ChunkData.Size)
			return null;

		return chunk.Data.Tiles[ix, iy];
	}

	// ── Private chunk management ──────────────────────────────────────────────

	private void UpdateChunks(Vector2I centre)
	{
		var needed = new HashSet<Vector2I>();
		for (int dx = -LoadRadius; dx <= LoadRadius; dx++)
			for (int dy = -LoadRadius; dy <= LoadRadius; dy++)
			{
				// X is raw (unbounded) — longitude wraps via ChunkGenerator.NormX internally.
				// Y is clamped so we never load chunks beyond the poles.
				int rawY  = Mathf.Clamp(centre.Y + dy, ChunkGenerator.LatMin, ChunkGenerator.LatMax);
				needed.Add(new Vector2I(centre.X + dx, rawY));
			}

		var remove = new List<Vector2I>();
		foreach (var coord in _chunks.Keys)
			if (!needed.Contains(coord)) remove.Add(coord);

		foreach (var coord in remove)
		{
			_chunks[coord].QueueFree();
			_chunks.Remove(coord);
		}

		foreach (var coord in needed)
			if (!_chunks.ContainsKey(coord))
				LoadChunk(coord);
	}

	private void LoadChunk(Vector2I coord)
	{
		var data  = ChunkGenerator.Generate(coord, WorldSeed, _planet);
		var chunk = new Chunk();
		AddChild(chunk);
		chunk.Init(data, ChunkToWorld(coord));
		_chunks[coord] = chunk;
	}

	// ── Coordinate conversion ─────────────────────────────────────────────────

	public static Vector2I WorldToChunk(Vector2 pos)
	{
		float tx = (pos.X / (Chunk.TileWidth  / 2f) + pos.Y / (Chunk.TileHeight / 2f)) * 0.5f;
		float ty = (pos.Y / (Chunk.TileHeight / 2f) - pos.X / (Chunk.TileWidth  / 2f)) * 0.5f;

		return new Vector2I(Mathf.FloorToInt(tx / ChunkData.Size),
		                    Mathf.FloorToInt(ty / ChunkData.Size));
	}

	public static Vector2 ChunkToWorld(Vector2I coord)
	{
		int tileX = coord.X * ChunkData.Size;
		int tileY = coord.Y * ChunkData.Size;
		return new Vector2(
			(tileX - tileY) * (Chunk.TileWidth  / 2f),
			(tileX + tileY) * (Chunk.TileHeight / 2f)
		);
	}
}
