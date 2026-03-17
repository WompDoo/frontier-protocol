#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Autoload singleton. Owns all CellRecord save data for the current planet.
/// Keyed by globe grid index (gi 0..23, gj 0..11).
///
/// Usage:
///   CellDatabase.Instance.InitPlanet(worldSeed)           — call when a new planet is loaded
///   CellDatabase.Instance.GetOrCreate(gi, gj, biome)      — get/create a cell's record
///   CellDatabase.Instance.CheckUnlockNeighbours(gi, gj)   — call after a run extracts
///   CellDatabase.Instance.Save() / Load(worldSeed)        — persistence
/// </summary>
public partial class CellDatabase : Node
{
	public static CellDatabase Instance { get; private set; } = null!;

	private int _worldSeed;
	private readonly Dictionary<Vector2I, CellRecord> _cells = new();

	/// <summary>
	/// Globe biome lookup — set by WorldScene (same pattern as ChunkManager.BiomeLookup).
	/// Returns null if globe isn't ready yet.
	/// </summary>
	public Func<int, int, BiomeType?>? BiomeLookup { get; set; }

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready()    { Instance = this; }
	public override void _ExitTree() { if (Instance == this) Instance = null!; }

	// ── Planet init ───────────────────────────────────────────────────────────

	/// <summary>
	/// Set the active world seed. Clears all in-memory cell data.
	/// Call this before loading a save or starting a new game.
	/// </summary>
	public void InitPlanet(int worldSeed)
	{
		_worldSeed = worldSeed;
		_cells.Clear();
	}

	// ── Cell seed ─────────────────────────────────────────────────────────────

	/// <summary>Deterministic cell seed from world seed + globe grid position.</summary>
	public static int MakeCellSeed(int worldSeed, int gi, int gj)
		=> worldSeed ^ (gi * 73856093) ^ (gj * 19349663);

	// ── Record access ─────────────────────────────────────────────────────────

	/// <summary>
	/// Returns the existing CellRecord for this globe cell, or creates a fresh one.
	/// </summary>
	public CellRecord GetOrCreate(int gi, int gj, BiomeType biome)
	{
		var idx = new Vector2I(gi, gj);
		if (!_cells.TryGetValue(idx, out var rec))
		{
			rec = new CellRecord
			{
				GridIndex = idx,
				Biome     = biome,
				CellSeed  = MakeCellSeed(_worldSeed, gi, gj),
			};
			_cells[idx] = rec;
		}
		return rec;
	}

	/// <summary>Returns the record if it exists, null otherwise.</summary>
	public CellRecord? TryGet(int gi, int gj)
	{
		_cells.TryGetValue(new Vector2I(gi, gj), out var rec);
		return rec;
	}

	// ── Unlock logic ──────────────────────────────────────────────────────────

	/// <summary>
	/// If all 5 bars on (gi,gj) are ≥ 80%, unlock the 4 cardinal neighbours.
	/// Safe to call after every extract — does nothing if threshold not met.
	/// </summary>
	public void CheckUnlockNeighbours(int gi, int gj)
	{
		if (!_cells.TryGetValue(new Vector2I(gi, gj), out var rec)) return;
		var def = CellContentDef.Generate(rec.Biome, rec.CellSeed);
		if (!rec.AllBarsAtThreshold(def)) return;

		Span<(int dgi, int dgj)> dirs = stackalloc (int, int)[]
			{ (0, -1), (0, 1), (-1, 0), (1, 0) };

		foreach (var (dgi, dgj) in dirs)
		{
			int ngi = gi + dgi;
			int ngj = gj + dgj;
			if (ngi < 0 || ngi >= 24 || ngj < 0 || ngj >= 12) continue;

			BiomeType? biome = BiomeLookup?.Invoke(ngi, ngj);
			if (biome is null) continue;

			var neighbour = GetOrCreate(ngi, ngj, biome.Value);
			neighbour.IsUnlocked = true;
		}
	}

	// ── Save / Load ───────────────────────────────────────────────────────────

	private const string SaveDir = "user://saves/";

	private string SavePath(int worldSeed) => $"{SaveDir}planet_{worldSeed}.json";

	public void Save()
	{
		DirAccess.MakeDirRecursiveAbsolute(SaveDir);

		var root = new SaveRoot
		{
			WorldSeed = _worldSeed,
			Cells     = new List<CellDto>(_cells.Count),
		};

		foreach (var (idx, rec) in _cells)
		{
			var dto = new CellDto
			{
				Gi                 = idx.X,
				Gj                 = idx.Y,
				Biome              = (int)rec.Biome,
				CellSeed           = rec.CellSeed,
				IsUnlocked         = rec.IsUnlocked,
				IsHomeBase         = rec.IsHomeBase,
				RunCount           = rec.RunCount,
				ScannedFlora       = new List<int>(rec.ScannedFlora),
				CapturedFauna      = new List<int>(rec.CapturedFauna),
				TaggedResources    = new List<int>(rec.TaggedResources),
				ExtractedResources = new List<int>(rec.ExtractedResources),
				RevealedCamps      = new List<int>(rec.RevealedCamps),
				UltraRareMisses    = new Dictionary<int, int>(rec.UltraRareMisses),
				ExploredChunks     = new List<int[]>(),
			};
			foreach (var v in rec.ExploredChunks)
				dto.ExploredChunks.Add(new[] { v.X, v.Y });

			root.Cells.Add(dto);
		}

		using var file = FileAccess.Open(SavePath(_worldSeed), FileAccess.ModeFlags.Write);
		if (file is null)
		{
			GD.PushWarning($"CellDatabase.Save: cannot open {SavePath(_worldSeed)}");
			return;
		}
		file.StoreString(JsonSerializer.Serialize(root, _jsonOptions));
	}

	public void Load(int worldSeed)
	{
		InitPlanet(worldSeed);
		string path = SavePath(worldSeed);
		if (!FileAccess.FileExists(path)) return;

		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file is null) return;

		var root = JsonSerializer.Deserialize<SaveRoot>(file.GetAsText(), _jsonOptions);
		if (root is null) return;

		foreach (var dto in root.Cells)
		{
			var idx = new Vector2I(dto.Gi, dto.Gj);
			var rec = new CellRecord
			{
				GridIndex  = idx,
				Biome      = (BiomeType)dto.Biome,
				CellSeed   = dto.CellSeed,
				IsUnlocked = dto.IsUnlocked,
				IsHomeBase = dto.IsHomeBase,
				RunCount   = dto.RunCount,
			};
			foreach (var id in dto.ScannedFlora)       rec.ScannedFlora.Add(id);
			foreach (var id in dto.CapturedFauna)      rec.CapturedFauna.Add(id);
			foreach (var id in dto.TaggedResources)    rec.TaggedResources.Add(id);
			foreach (var id in dto.ExtractedResources) rec.ExtractedResources.Add(id);
			foreach (var id in dto.RevealedCamps)      rec.RevealedCamps.Add(id);
			foreach (var pair in dto.UltraRareMisses)  rec.UltraRareMisses[pair.Key] = pair.Value;
			foreach (var xy in dto.ExploredChunks)
				if (xy.Length >= 2) rec.ExploredChunks.Add(new Vector2I(xy[0], xy[1]));

			_cells[idx] = rec;
		}
	}

	// ── JSON helpers ──────────────────────────────────────────────────────────

	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented          = false,
		PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	// ── Save DTOs (private) ───────────────────────────────────────────────────

	private class SaveRoot
	{
		public int           WorldSeed { get; set; }
		public List<CellDto> Cells     { get; set; } = new();
	}

	private class CellDto
	{
		public int                    Gi                 { get; set; }
		public int                    Gj                 { get; set; }
		public int                    Biome              { get; set; }
		public int                    CellSeed           { get; set; }
		public bool                   IsUnlocked         { get; set; }
		public bool                   IsHomeBase         { get; set; }
		public int                    RunCount           { get; set; }
		public List<int[]>            ExploredChunks     { get; set; } = new();
		public List<int>              ScannedFlora       { get; set; } = new();
		public List<int>              CapturedFauna      { get; set; } = new();
		public List<int>              TaggedResources    { get; set; } = new();
		public List<int>              ExtractedResources { get; set; } = new();
		public List<int>              RevealedCamps      { get; set; } = new();
		public Dictionary<int, int>   UltraRareMisses    { get; set; } = new();
	}
}
