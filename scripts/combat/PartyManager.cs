using Godot;
using System.Collections.Generic;

/// <summary>
/// Autoload singleton. Owns the active scouting party across all scenes.
///
/// Responsibilities:
///   - Tracks which scouts are in the current expedition party (max 4)
///   - Stores persistent HP so damage survives scene transitions
///   - Tracks KO history for permanent-death logic (2nd KO = dead)
///   - Knows the party's current chunk position so combat arena gen can pull ChunkData
///   - Provides DebugAddTestScouts() for quick iteration
///
/// Data cascade position: below PlanetParams/ChunkData, above CombatGrid.
/// </summary>
public partial class PartyManager : Node
{
	public static PartyManager Instance { get; private set; } = null!;

	public const int MaxPartySize = 4;

	// ── Party roster ──────────────────────────────────────────────────────

	private readonly List<Scout> _party = new();
	public IReadOnlyList<Scout> Party => _party;

	// ── Persistent HP ─────────────────────────────────────────────────────
	// Keyed by Scout.Id. Survives scene changes.

	private readonly Dictionary<string, int> _hp = new();

	// ── KO tracking ───────────────────────────────────────────────────────
	// Scouts that have been KO'd at least once this run.
	// A second KO within the same run = permanent death.

	private readonly HashSet<string> _knockedOutOnce = new();

	// ── World position ────────────────────────────────────────────────────
	// Updated by Player.cs / ChunkManager each frame. Combat gen reads this.

	/// <summary>Current chunk the party stands on (chunk grid coordinates).</summary>
	public Vector2I CurrentChunkCoord { get; set; } = Vector2I.Zero;

	/// <summary>World seed — needed to regenerate ChunkData for arena generation.</summary>
	public int WorldSeed { get; set; } = 0;

	// ── Lifecycle ─────────────────────────────────────────────────────────

	public override void _Ready()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null!;
	}

	// ── Party management ──────────────────────────────────────────────────

	public bool AddScout(Scout scout)
	{
		if (_party.Count >= MaxPartySize || _party.Contains(scout)) return false;
		_party.Add(scout);
		if (!_hp.ContainsKey(scout.Id))
			_hp[scout.Id] = scout.MaxHP;
		return true;
	}

	public void RemoveScout(Scout scout) => _party.Remove(scout);

	public void ClearParty()
	{
		_party.Clear();
		_hp.Clear();
		_knockedOutOnce.Clear();
	}

	// ── HP management ─────────────────────────────────────────────────────

	public int GetHP(Scout scout)  => _hp.TryGetValue(scout.Id, out var h) ? h : scout.MaxHP;
	public int GetHP(string id)    => _hp.TryGetValue(id, out var h) ? h : 0;

	public void SetHP(Scout scout, int value)
	{
		_hp[scout.Id] = Mathf.Clamp(value, 0, scout.MaxHP);
	}

	/// <summary>
	/// Apply <paramref name="damage"/> to a scout.
	/// Returns <c>true</c> if the scout is knocked out (HP reached 0).
	/// Second KO within a run = status set to Dead.
	/// </summary>
	public bool ApplyDamage(Scout scout, int damage)
	{
		int current = Mathf.Max(0, GetHP(scout) - damage);
		_hp[scout.Id] = current;

		if (current > 0) return false;

		// Scout is down.
		if (_knockedOutOnce.Contains(scout.Id))
		{
			scout.Status = ScoutStatus.Dead;   // second time: permanent
		}
		else
		{
			_knockedOutOnce.Add(scout.Id);
			// Stays in roster as Injured — needs rest between runs
		}
		return true;
	}

	/// <summary>Restore HP, clamped to MaxHP.</summary>
	public void HealScout(Scout scout, int amount)
	{
		_hp[scout.Id] = Mathf.Min(GetHP(scout) + amount, scout.MaxHP);
	}

	/// <summary>
	/// Called after combat ends. KO'd scouts that aren't permanently dead
	/// are revived at 1 HP — they need a full rest before next fight.
	/// </summary>
	public void PostCombatRevive()
	{
		foreach (var scout in _party)
		{
			if (scout.Status == ScoutStatus.Dead) continue;
			if (GetHP(scout) <= 0)
				_hp[scout.Id] = 1;
		}
	}

	/// <summary>Reset KO history for a new run (e.g. back at colony).</summary>
	public void ResetRunState()
	{
		_knockedOutOnce.Clear();
		// HP persists — scouts heal at colony between runs (to be implemented)
	}

	// ── Query helpers ──────────────────────────────────────────────────────

	public bool IsAlive(Scout scout)     => scout.Status != ScoutStatus.Dead && GetHP(scout) > 0;
	public bool IsKnockedOut(Scout scout) => GetHP(scout) <= 0 && scout.Status != ScoutStatus.Dead;

	/// <summary>True if every scout in the party is KO'd or dead — combat defeat condition.</summary>
	public bool IsPartyWiped() => _party.Count == 0 || _party.TrueForAll(s => !IsAlive(s));

	// ── Debug ──────────────────────────────────────────────────────────────

	/// <summary>
	/// Populate the party with 3 generated scouts for quick testing.
	/// No-op if party already has members.
	/// </summary>
	public void DebugAddTestScouts(int seed = 42)
	{
		if (_party.Count > 0) return;
		for (int i = 0; i < 3; i++)
		{
			var scout = ScoutGenerator.Generate(seed + i * 1000);
			AddScout(scout);
		}
	}
}
