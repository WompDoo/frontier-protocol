#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Autoload singleton. Tracks the top-level game phase and data that must survive
/// scene transitions (Globe ↔ Isometric ↔ Combat).
/// </summary>
public partial class GameManager : Node
{
	public static GameManager Instance { get; private set; } = null!;

	public enum GamePhase { Globe, Iso, Combat }

	/// <summary>Current phase — read by WorldScene._Ready to restore correct mode after a scene change.</summary>
	public GamePhase Phase { get; set; } = GamePhase.Globe;

	/// <summary>Chunk coord the player deployed to. Set when Deploy is pressed in the globe cell panel.</summary>
	public Vector2I DeployChunkCoord { get; set; } = Vector2I.Zero;

	/// <summary>Enemies queued for the next combat encounter. Consumed by CombatManager._Ready.</summary>
	public List<(EnemyDef def, Vector2I cell)> PendingEnemies { get; set; } = new();

	/// <summary>Set to true after the player picks a home base cell on first launch.</summary>
	public bool HomeBaseSelected { get; set; } = false;

	public override void _Ready()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null!;
	}
}
