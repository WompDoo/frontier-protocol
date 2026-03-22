#nullable enable
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Root script for world.tscn. Orchestrates Globe ↔ Isometric mode switching
/// and triggers combat encounters when the scout party enters hostile territory.
///
/// Globe mode  — GlobeLayer visible, ChunkManager + Player hidden.
/// Iso mode    — ChunkManager + Player visible, GlobeLayer hidden.
/// Press Escape in Iso to return to Globe.
///
/// Combat is launched by changing to combat.tscn; returning sets Phase=Iso so
/// this script restores Iso mode automatically on re-entry.
/// </summary>
public partial class WorldScene : Node2D
{
	// ── Encounter tuning ──────────────────────────────────────────────────

	/// <summary>Set false to suppress all random encounters (useful during exploration testing).</summary>
	public static bool EncountersEnabled = false;

	/// <summary>Base probability of an encounter when crossing a chunk boundary.</summary>
	private const float BaseEncounterChance = 0.20f;

	// ── Node refs ─────────────────────────────────────────────────────────

	private CanvasLayer       _globeLayer    = null!;
	private GlobeView         _globeView     = null!;
	private ChunkManager      _chunkManager  = null!;
	private Node2D            _ySort         = null!;
	private Player            _player        = null!;
	private OverworldRenderer? _overworld    = null;

	// ── Encounter state ───────────────────────────────────────────────────

	private Vector2I _lastChunk = new(int.MinValue, int.MinValue);
	private Random   _rng       = new();
	private bool     _combatPending = false;

	// ── Lifecycle ─────────────────────────────────────────────────────────

	public override void _Ready()
	{
		_globeLayer   = GetNode<CanvasLayer>("GlobeLayer");
		_globeView    = GetNode<GlobeView>("GlobeLayer/GlobeView");
		_chunkManager = GetNode<ChunkManager>("ChunkManager");
		_ySort        = GetNode<Node2D>("YSort");
		_player       = GetNode<Player>("YSort/Player");

		_overworld = GetNodeOrNull<OverworldRenderer>("OverworldRenderer");

		_globeView.DeployRequested    += OnDeployRequested;
		_globeView.LandingSiteChosen  += OnLandingSiteChosen;
		_chunkManager.BiomeLookup      = coord => _globeView.GetBiomeForChunk(coord.X, coord.Y);
		_chunkManager.RiverLookup      = coord => _globeView.GetRiverCrossing(coord.X, coord.Y);

		// Globe grid → chunk coord: cell centre = gj*10 + LatMin + 5 = gj*10 - 55
		// LatMin=-60, 12 cells × 10 chunks each → gj=0 centre Y=-55, gj=6 centre Y=5, gj=11 centre Y=55
		CellDatabase.Instance.BiomeLookup = (gi, gj) =>
			_globeView.GetBiomeForChunk(gi * 10 + 5, Math.Clamp(gj * 10 - 55, -60, 59));

		CellDatabase.Instance.InitPlanet(_chunkManager.WorldSeed);

		// Restore correct mode after a scene change (e.g. returning from combat)
		if (GameManager.Instance.Phase == GameManager.GamePhase.Iso)
		{
			EnterIso(GameManager.Instance.DeployChunkCoord, teleport: true);
		}
		else
		{
			EnterGlobe();
			// First launch: no home base selected → show landing site picker
			if (!GameManager.Instance.HomeBaseSelected)
				_globeView.StartLandingSiteSelection();
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo
		    && key.Keycode == Key.Escape
		    && GameManager.Instance.Phase == GameManager.GamePhase.Iso)
		{
			EnterGlobe();
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Process(double delta)
	{
		if (GameManager.Instance.Phase != GameManager.GamePhase.Iso || _combatPending) return;

		// Update PartyManager with current position
		var chunkCoord = ChunkManager.WorldToChunk(_player.GlobalPosition);
		PartyManager.Instance.CurrentChunkCoord = chunkCoord;

		// Encounter roll on chunk boundary cross
		if (chunkCoord != _lastChunk)
		{
			bool firstEntry = _lastChunk.X == int.MinValue;
			_lastChunk = chunkCoord;
			if (!firstEntry) CheckEncounter(chunkCoord);
		}
	}

	// ── Mode switching ────────────────────────────────────────────────────

	private void EnterGlobe()
	{
		GameManager.Instance.Phase = GameManager.GamePhase.Globe;
		_globeLayer.Visible   = true;
		_chunkManager.Visible = false;
		_ySort.Visible        = false;
		if (_overworld != null) _overworld.SetActive(false);
		_chunkManager.ClearRunArea();
		_lastChunk = new(int.MinValue, int.MinValue);
	}

	private void EnterIso(Vector2I chunkCoord, bool teleport = false)
	{
		GameManager.Instance.Phase              = GameManager.GamePhase.Iso;
		GameManager.Instance.DeployChunkCoord   = chunkCoord;
		PartyManager.Instance.WorldSeed         = _chunkManager.WorldSeed;
		PartyManager.Instance.CurrentChunkCoord = chunkCoord;

		_globeLayer.Visible   = false;
		_chunkManager.Visible = true;
		_ySort.Visible        = true;
		if (_overworld != null) _overworld.SetActive(true);

		_chunkManager.SetRunArea(chunkCoord);

		if (teleport)
			_player.GlobalPosition = ChunkManager.ChunkToWorld(chunkCoord);

		_lastChunk     = new(int.MinValue, int.MinValue);  // force encounter-state reset
		_combatPending = false;
	}

	private void OnLandingSiteChosen(int lonIdx, int latIdx)
	{
		// Mark cell as the home base in CellDatabase (sample biome at cell centre)
		BiomeType biome = _globeView.GetBiomeForChunk(lonIdx * 10 + 5,
		                      Math.Clamp(latIdx * 10 - 55, -60, 59))
		                  ?? BiomeType.Grassland;

		var rec = CellDatabase.Instance.GetOrCreate(lonIdx, latIdx, biome);
		rec.IsHomeBase = true;
		rec.IsUnlocked = true;
		GameManager.Instance.HomeBaseSelected = true;

		// Deploy immediately — this is the tutorial/first run
		OnDeployRequested(lonIdx, latIdx);
	}

	private void OnDeployRequested(int lonIdx, int latIdx)
	{
		// Globe grid: 24 lon × 12 lat cells; world: 240 × 120 chunks → 10 chunks per cell.
		// Cell centre: X = gi*10+5, Y = gj*10-45  (not -50 which is the northern edge)
		// gj=0→Y=-45 (near north pole), gj=5→Y=5 (equator+), gj=11→Y=65→clamped 59
		int chunkX = lonIdx * 10 + 5;
		int chunkY = Math.Clamp(latIdx * 10 - 55, -60, 59);
		EnterIso(new Vector2I(chunkX, chunkY), teleport: true);
	}

	// ── Encounter logic ───────────────────────────────────────────────────

	private void CheckEncounter(Vector2I chunkCoord)
	{
		if (!EncountersEnabled) return;

		// Generate chunk data to know biome and scale danger
		var planet     = ChunkGenerator.DeriveParams(_chunkManager.WorldSeed);
		var chunkData  = ChunkGenerator.Generate(chunkCoord, _chunkManager.WorldSeed, planet);
		float danger   = BiomeDanger(chunkData.Biome);

		if (_rng.NextDouble() > BaseEncounterChance * danger) return;

		// Select enemies for this biome
		var enemies = BuildEnemyGroup(chunkData.Biome);
		TriggerCombat(enemies, chunkCoord);
	}

	private static float BiomeDanger(BiomeType biome) => biome switch
	{
		BiomeType.AlienWilds  => 2.0f,
		BiomeType.Mountain    => 1.5f,
		BiomeType.Jungle      => 1.4f,
		BiomeType.Desert      => 1.2f,
		BiomeType.Ocean       => 0.0f,  // no encounters at sea
		BiomeType.DeepOcean   => 0.0f,
		_                     => 1.0f,
	};

	private static List<(EnemyDef def, Vector2I cell)> BuildEnemyGroup(BiomeType biome)
	{
		// Pick enemy archetype based on biome
		EnemyDef primary = biome switch
		{
			BiomeType.Desert or BiomeType.Savanna => EnemyDB.AlienScout,
			BiomeType.Mountain                    => EnemyDB.AlienWarrior,
			BiomeType.AlienWilds                  => EnemyDB.AlienLurker,
			_                                     => EnemyDB.AlienWarrior,
		};
		EnemyDef secondary = biome switch
		{
			BiomeType.AlienWilds => EnemyDB.AlienWarrior,
			_                    => EnemyDB.AlienScout,
		};

		return new List<(EnemyDef, Vector2I)>
		{
			(primary,   new Vector2I(9, 3)),
			(secondary, new Vector2I(9, 7)),
		};
	}

	private void TriggerCombat(List<(EnemyDef def, Vector2I cell)> enemies, Vector2I chunkCoord)
	{
		_combatPending = true;
		GameManager.Instance.Phase          = GameManager.GamePhase.Combat;
		GameManager.Instance.PendingEnemies = enemies;
		PartyManager.Instance.CurrentChunkCoord = chunkCoord;

		// Brief pause so the player sees the tile they're on before cut to combat
		GetTree().CreateTimer(0.3f).Timeout += () =>
			GetTree().ChangeSceneToFile("res://scenes/combat.tscn");
	}
}
