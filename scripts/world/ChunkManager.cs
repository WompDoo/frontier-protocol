using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Manages chunk loading/unloading around the player.
/// Generates a 5×5 window of chunks centred on the player's current chunk.
/// Chunk generation is spread across frames (LoadPerFrame per frame) to avoid
/// the deploy-time freeze: the player's own chunk loads immediately, neighbours stream in.
/// </summary>
public partial class ChunkManager : Node2D
{
	private const int LoadRadius   = 2;
	private const int LoadPerFrame = 3;  // max chunks generated per frame while streaming

	// ── Run area ──────────────────────────────────────────────────────────────
	// The run area is a 9×9 chunk grid centered on the deploy chunk.
	// Outer 2 rings (Chebyshev dist 3–4) generate as impassable barrier terrain.
	// Chunks beyond dist 4 are not loaded at all (black void).
	private const int RunHalfSize  = 4;  // ±4 → 9×9 total area
	private const int BarrierStart = 3;  // dist ≥ 3 from center = barrier ring (2 chunks wide)

	private Vector2I? _runCenter;  // null = no boundary (globe/debug mode)

	/// <summary>
	/// Set the run area center. All subsequent chunk loading will be bounded
	/// to a 9×9 area, with the outer 2-chunk ring generating as barrier terrain.
	/// </summary>
	public void SetRunArea(Vector2I centerChunk)
	{
		if (_runCenter == centerChunk) return;
		_runCenter = centerChunk;
		// Force a full reload so any previously loaded out-of-bounds chunks are cleared
		_lastPlayerChunk = new(int.MinValue, int.MinValue);
	}

	/// <summary>Clear run area bounds (use in globe/unlimited mode).</summary>
	public void ClearRunArea()
	{
		_runCenter = null;
		_lastPlayerChunk = new(int.MinValue, int.MinValue);
	}

	[Export] public int      WorldSeed  { get; set; } = 0;
	[Export] public NodePath PlayerPath { get; set; }

	private Node2D                               _player;
	private ChunkGenerator.PlanetParams          _planet;
	private readonly Dictionary<Vector2I, Chunk> _chunks    = new();
	private readonly Queue<Vector2I>             _loadQueue = new();
	private Vector2I _lastPlayerChunk = new(int.MinValue, int.MinValue);
	private OverworldRenderer?                   _overworld;

	public int                          Seed          => WorldSeed;
	public ChunkGenerator.PlanetParams  Planet        => _planet;
	public float                        WaterFraction { get; set; }   // set by GlobeView after texture gen

	/// <summary>
	/// Optional callback set by WorldScene. Given a chunk coordinate, returns the globe's
	/// dominant biome for that area — used to override ChunkGenerator's independent noise so
	/// the overworld matches what the globe view shows.
	/// </summary>
	public Func<Vector2I, BiomeType?>?    BiomeLookup  { get; set; }

	/// <summary>
	/// Optional callback set by WorldScene. Returns globe-traced river crossing data for
	/// a chunk, or null if no river passes through it.
	/// </summary>
	public Func<Vector2I, RiverCrossing?>? RiverLookup { get; set; }

	public override void _Ready()
	{
		_player = GetNode<Node2D>(PlayerPath);

		// Find optional OverworldRenderer sibling — enables 3D terrain mode
		_overworld = GetParent().GetNodeOrNull<OverworldRenderer>("OverworldRenderer");
		if (_overworld is not null)
		{
			Chunk.UseThreeDRenderer = true;
			GD.Print("[World] OverworldRenderer found — using 3D terrain mode");
		}

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
		if (chunkCoord != _lastPlayerChunk)
		{
			_lastPlayerChunk = chunkCoord;
			UpdateChunks(chunkCoord);
			ApplyAtmosphericPerspective(chunkCoord);
		}

		// Drain the load queue — spreads generation across frames to prevent freezes
		int loaded = 0;
		while (_loadQueue.Count > 0 && loaded < LoadPerFrame)
		{
			var coord = _loadQueue.Dequeue();
			if (!_chunks.ContainsKey(coord))
			{
				LoadChunk(coord);
				loaded++;
			}
		}
	}

	// ── Chunk lifecycle ───────────────────────────────────────────────────────

	public void Regenerate()
	{
		_loadQueue.Clear();
		foreach (var chunk in _chunks.Values)
			chunk.QueueFree();
		_chunks.Clear();
		_overworld?.ClearAll();

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
		var needed = new HashSet<Vector2I>(25);  // 5×5 window
		for (int dx = -LoadRadius; dx <= LoadRadius; dx++)
			for (int dy = -LoadRadius; dy <= LoadRadius; dy++)
			{
				var coord = new Vector2I(centre.X + dx,
				                        Mathf.Clamp(centre.Y + dy, ChunkGenerator.LatMin, ChunkGenerator.LatMax));

				// When a run area is active, only load chunks within the 9×9 boundary
				if (_runCenter.HasValue)
				{
					var d    = coord - _runCenter.Value;
					int dist = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
					if (dist > RunHalfSize) continue;
				}

				needed.Add(coord);
			}

		// Unload out-of-range chunks
		var remove = new List<Vector2I>(8);
		foreach (var coord in _chunks.Keys)
			if (!needed.Contains(coord)) remove.Add(coord);
		foreach (var coord in remove)
		{
			_chunks[coord].QueueFree();
			_chunks.Remove(coord);
			_overworld?.RemoveChunk(coord);
		}

		// Load player's own chunk immediately — no blank tile under their feet
		if (!_chunks.ContainsKey(centre))
			LoadChunk(centre);

		// Queue the rest for streaming across subsequent frames
		_loadQueue.Clear();
		foreach (var coord in needed)
			if (!_chunks.ContainsKey(coord))
				_loadQueue.Enqueue(coord);
	}

	/// <summary>
	/// Darkens chunks that are far from the player to simulate atmospheric perspective —
	/// terrain "falls away" toward the horizon like on a curved planet surface.
	/// Centre chunk = full brightness; edge chunks = 65% brightness.
	/// </summary>
	private void ApplyAtmosphericPerspective(Vector2I centre)
	{
		foreach (var (coord, chunk) in _chunks)
		{
			int dist = Mathf.Max(Mathf.Abs(coord.X - centre.X),
			                     Mathf.Abs(coord.Y - centre.Y));
			float t    = Mathf.Clamp(dist / (float)LoadRadius, 0f, 1f);
			float bright = Mathf.Lerp(1.0f, 0.55f, t * t);  // quadratic falloff
			chunk.Modulate = new Color(bright, bright, bright, 1f);
		}
	}

	private void LoadChunk(Vector2I coord)
	{
		BiomeType? globeBiome = BiomeLookup?.Invoke(coord);

		// Barrier ring: outer 2 chunks of the run area generate as impassable mountain terrain
		if (_runCenter.HasValue)
		{
			var d    = coord - _runCenter.Value;
			int dist = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
			if (dist >= BarrierStart)
				globeBiome = BiomeType.Mountain;
		}

		var data  = ChunkGenerator.Generate(coord, WorldSeed, _planet, globeBiome);
		RiverCrossing? river = RiverLookup?.Invoke(coord);
		var chunk = new Chunk();
		AddChild(chunk);
		chunk.Init(data, ChunkToWorld(coord), WorldSeed, river, _planet.SeaLevel);
		_chunks[coord] = chunk;
		_overworld?.AddChunk(coord, data, WorldSeed, _planet.SeaLevel, river);
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
