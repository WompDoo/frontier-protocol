#nullable enable
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Stateless enemy AI. Given the current state of the grid + units, returns
/// a list of CombatActions for the active enemy unit to execute this turn.
///
/// Priority order:
///   1. If target is in attack range + LoS → Attack.
///   2. If not in range → move toward the closest player unit.
///      Lurkers prefer to approach from flanking angles (orthogonal axis).
///   3. If can't reach attack range this turn → move as close as possible,
///      then Overwatch if weapon has range ≥ 2.
///   4. EndTurn.
///
/// This is a pure-logic class (returns actions; doesn't mutate state itself).
/// CombatManager applies the actions and handles animation.
/// </summary>
public static class EnemyAI
{
	/// <summary>
	/// Evaluate what the enemy should do this turn and return an ordered action list.
	/// CombatManager will execute these in sequence until AP runs out or EndTurn is hit.
	/// </summary>
	public static List<CombatAction> Evaluate(
		CombatUnit        self,
		List<CombatUnit>  playerUnits,
		CombatGrid        grid,
		Random            rng)
	{
		var actions = new List<CombatAction>(6);
		if (!self.CanAct()) return actions;

		// Build alive list without LINQ — avoids closure + ToList allocation
		var aliveTargets = new List<CombatUnit>(playerUnits.Count);
		foreach (var u in playerUnits)
			if (u.IsAlive) aliveTargets.Add(u);
		if (aliveTargets.Count == 0)
		{
			actions.Add(new CombatAction(ActionKind.EndTurn));
			return actions;
		}

		// Find the closest player unit by Chebyshev distance
		var target = PickTarget(self, aliveTargets, grid);
		int apLeft = self.CurrentAP;

		while (apLeft > 0)
		{
			// Check if target is in attack range right now
			bool inRange = IsInAttackRange(self.GridCell, target.GridCell, self.Weapon, grid);

			if (inRange && self.Weapon.ApCost <= apLeft)
			{
				// Attack!
				actions.Add(new CombatAction(ActionKind.Attack, target.GridCell));
				apLeft -= self.Weapon.ApCost;
			}
			else if (!inRange && apLeft >= 1)
			{
				// Move toward target (or flank for Lurkers)
				var destination = PickMoveDestination(self, target, aliveTargets, grid, rng);
				if (destination.HasValue && destination.Value != self.GridCell)
				{
					actions.Add(new CombatAction(ActionKind.Move, destination.Value));
					apLeft -= 1;
				}
				else
				{
					// Stuck — can't get closer. Overwatch if ranged weapon.
					if (self.Weapon.RangeMax >= 2 && apLeft >= 1)
					{
						actions.Add(new CombatAction(ActionKind.Overwatch));
						apLeft -= 1;
					}
					break;
				}
			}
			else
			{
				break;
			}
		}

		actions.Add(new CombatAction(ActionKind.EndTurn));
		return actions;
	}

	// ── Target selection ──────────────────────────────────────────────────

	private static CombatUnit PickTarget(CombatUnit self, List<CombatUnit> targets, CombatGrid grid)
	{
		// Single pass: track lowest-HP in-range target AND closest overall target
		CombatUnit? bestInRange = null;
		int         bestHP      = int.MaxValue;
		CombatUnit? closest     = null;
		int         closestDist = int.MaxValue;

		foreach (var t in targets)
		{
			int dist = ChebyshevDist(self.GridCell, t.GridCell);
			if (dist < closestDist) { closestDist = dist; closest = t; }
			if (IsInAttackRange(self.GridCell, t.GridCell, self.Weapon, grid) && t.CurrentHP < bestHP)
				{ bestHP = t.CurrentHP; bestInRange = t; }
		}

		return bestInRange ?? closest ?? targets[0];
	}

	// ── Movement ──────────────────────────────────────────────────────────

	private static Vector2I? PickMoveDestination(
		CombatUnit       self,
		CombatUnit       target,
		List<CombatUnit> allPlayers,
		CombatGrid       grid,
		Random           rng)
	{
		var reachable = grid.GetReachable(self.GridCell, self.MoveRange);
		if (reachable.Count == 0) return null;

		// Occupied cells (by other units) are off-limits
		var occupied = new HashSet<Vector2I>(allPlayers.Count);
		foreach (var u in allPlayers) occupied.Add(u.GridCell);

		// Lurkers try to flank — prefer cells that approach the target
		// from a different axis than the target's current exposure direction.
		bool isLurker = self.EnemyDef?.Id == "alien_lurker";

		Vector2I? best = null;
		float bestScore = float.MinValue;

		foreach (var cell in reachable)
		{
			if (occupied.Contains(cell)) continue;
			if (grid.GetTile(cell).Passability == TilePassability.Impassable) continue;

			float score = ScoreMoveCell(cell, self.GridCell, target.GridCell, self.Weapon, isLurker, grid);
			// Tiny random tiebreaker so identical-score cells aren't always the same
			score += (float)rng.NextDouble() * 0.01f;

			if (score > bestScore)
			{
				bestScore = score;
				best = cell;
			}
		}

		return best;
	}

	private static float ScoreMoveCell(
		Vector2I moveCell, Vector2I selfCell, Vector2I targetCell,
		WeaponDef weapon, bool isLurker, CombatGrid grid)
	{
		int dist = ChebyshevDist(moveCell, targetCell);

		// Prefer to end up at max range of weapon (snipers hang back, melee gets close)
		int idealDist = weapon.Category == WeaponCategory.Melee ? 1 : weapon.RangeMax - 1;
		float rangePenalty = Mathf.Abs(dist - idealDist);

		// Prefer cells with LoS to target
		float losBonus = grid.HasLoS(moveCell, targetCell) ? 2.0f : 0f;

		// Prefer cover tiles
		float coverBonus = grid.GetTile(moveCell).Cover switch
		{
			CoverType.Full => 1.5f,
			CoverType.Half => 0.8f,
			_              => 0f,
		};

		// Lurker: prefer diagonal approach (flanking angle)
		float flankBonus = 0f;
		if (isLurker)
		{
			var delta = targetCell - moveCell;
			bool diagonal = delta.X != 0 && delta.Y != 0;
			flankBonus = diagonal ? 1.5f : 0f;
		}

		return losBonus + coverBonus + flankBonus - rangePenalty;
	}

	// ── Helpers ───────────────────────────────────────────────────────────

	private static bool IsInAttackRange(Vector2I from, Vector2I to, WeaponDef weapon, CombatGrid grid)
	{
		int dist = ChebyshevDist(from, to);
		if (dist < weapon.RangeMin || dist > weapon.RangeMax) return false;
		return grid.HasLoS(from, to);
	}

	private static int ChebyshevDist(Vector2I a, Vector2I b) =>
		Mathf.Max(Mathf.Abs(a.X - b.X), Mathf.Abs(a.Y - b.Y));
}
