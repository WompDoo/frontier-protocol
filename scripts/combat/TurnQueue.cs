#nullable enable
using System;
using System.Collections.Generic;

/// <summary>
/// Pure-C# turn ordering system. No Godot dependency.
///
/// Initiative formula:
///   Scouts  → AWR×2 + ING + rand(0, PRC)
///   Enemies → BaseInitiative + rand(0, 5)
///
/// The queue is built once at combat start, then cycled round-by-round.
/// Dead/KO'd units are removed via Remove(). When the last unit in a round
/// is popped, Advance() wraps and starts a new round (incrementing RoundNumber).
/// </summary>
public class TurnQueue
{
	// ── Entry ─────────────────────────────────────────────────────────────

	public record Entry(string UnitId, CombatSide Side, int Initiative)
	{
		public override string ToString() =>
			$"[{Side}] {UnitId}  init={Initiative}";
	}

	// ── State ─────────────────────────────────────────────────────────────

	/// <summary>Full sorted order for this combat (rebuilt each round if units die).</summary>
	private List<Entry> _order = new();

	/// <summary>Remaining units still to act in the current round.</summary>
	private Queue<Entry> _remaining = new();

	public int RoundNumber { get; private set; } = 1;

	public Entry? Current { get; private set; }

	/// <summary>Snapshot of the full turn order (for HUD display).</summary>
	public IReadOnlyList<Entry> Order => _order;

	// ── Build ─────────────────────────────────────────────────────────────

	/// <summary>
	/// Build the initiative order from a list of units.
	/// Call this once at combat start after all units have been created.
	/// </summary>
	public void Build(IEnumerable<CombatUnit> units, Random rng)
	{
		_order.Clear();
		foreach (var u in units)
			_order.Add(new Entry(u.UnitId, u.Side, u.RollInitiative(rng)));
		_order.Sort((a, b) => b.Initiative.CompareTo(a.Initiative));
		StartNewRound();
	}

	// ── Advance ───────────────────────────────────────────────────────────

	/// <summary>
	/// Move to the next unit in the queue.
	/// Returns the entry whose turn is now active.
	/// When the round is exhausted, a new round begins automatically.
	/// </summary>
	public Entry Advance()
	{
		if (_remaining.Count == 0)
		{
			RoundNumber++;
			StartNewRound();
		}

		Current = _remaining.Dequeue();
		return Current;
	}

	/// <summary>Peek at the next unit without advancing.</summary>
	public Entry? Peek() => _remaining.Count > 0 ? _remaining.Peek() : null;

	// ── Remove ────────────────────────────────────────────────────────────

	/// <summary>
	/// Remove a unit (KO'd / dead). Affects both the full order and the remaining queue.
	/// </summary>
	public void Remove(string unitId)
	{
		_order.RemoveAll(e => e.UnitId == unitId);

		// Rebuild remaining queue without the removed unit
		int count = _remaining.Count;
		for (int i = 0; i < count; i++)
		{
			var e = _remaining.Dequeue();
			if (e.UnitId != unitId) _remaining.Enqueue(e);
		}
	}

	// ── Query ─────────────────────────────────────────────────────────────

	public bool IsEmpty => _order.Count == 0;

	public bool HasUnitsOfSide(CombatSide side)
	{
		foreach (var e in _order)
			if (e.Side == side) return true;
		return false;
	}

	/// <summary>How many units still need to act in the current round.</summary>
	public int RemainingThisRound => _remaining.Count;

	// ── Internal ──────────────────────────────────────────────────────────

	private void StartNewRound()
	{
		_remaining = new Queue<Entry>(_order);
	}
}
