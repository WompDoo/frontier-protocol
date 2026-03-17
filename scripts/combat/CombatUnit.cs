#nullable enable
using Godot;
using System;
using System.Collections.Generic;

public partial class CombatUnit : Node2D
{
	// ── Sprite animation ──────────────────────────────────────────────────

	public enum AnimState { Idle, Walk, Run, Shot1, Shot2, AttackMelee, Hurt, Dead }

	private Sprite2D?  _sprite;
	private Texture2D? _texIdle, _texWalk, _texRun, _texShot1, _texShot2;
	private Texture2D? _texAttack, _texHurt, _texDead;
	private int _idleFrames=7, _walkFrames=8, _runFrames=8;
	private int _shot1Frames=4, _shot2Frames=4, _attackFrames=6;
	private int _hurtFrames=4, _deadFrames=6;

	private AnimState  _animState     = AnimState.Idle;
	private float      _animTimer     = 0f;
	private bool       _oneShot       = false;
	private AnimState  _oneShotReturn = AnimState.Idle;
	private float      _frameTime     = 0.16f;   // ~6 fps default; synced to move tween

	/// <summary>Set animation frame duration (seconds). Clamped to ≥40ms.</summary>
	public float AnimFrameTime { set => _frameTime = Mathf.Max(0.04f, value); }

	/// <summary>Gold border drawn around this unit when it is its turn.</summary>
	public bool IsActive { get; set; }

	public override void _Ready()
	{
		int    variant = (Math.Abs(UnitId.GetHashCode()) % 3) + 1;
		string dir     = $"res://assets/sprites/scouts/Soldier_{variant}/";

		LoadTex(dir + "Idle.png",   ref _texIdle,   ref _idleFrames);
		LoadTex(dir + "Walk.png",   ref _texWalk,   ref _walkFrames);
		LoadTex(dir + "Run.png",    ref _texRun,    ref _runFrames);
		LoadTex(dir + "Shot_1.png", ref _texShot1,  ref _shot1Frames);
		LoadTex(dir + "Shot_2.png", ref _texShot2,  ref _shot2Frames);
		LoadTex(dir + "Hurt.png",   ref _texHurt,   ref _hurtFrames);
		LoadTex(dir + "Dead.png",   ref _texDead,   ref _deadFrames);

		// Soldier_3 has a typo in the filename
		string atkPath = dir + "Attack.png";
		if (!ResourceLoader.Exists(atkPath)) atkPath = dir + "Attacck.png";
		LoadTex(atkPath, ref _texAttack, ref _attackFrames);

		if (_texIdle == null) return;

		// Scale 0.44 → sprite height ≈ 56.3px; feet are ~86% down the 128px frame.
		// Feet offset from Sprite2D centre = +20.3px.  To land feet at y=0 (tile centre):
		//   Position.Y = -(feet offset) = -20.
		_sprite = new Sprite2D
		{
			Texture  = _texIdle,
			Hframes  = _idleFrames,
			Frame    = 0,
			Scale    = new Vector2(0.44f, 0.44f),
			Position = new Vector2(0f, -20f),
		};

		if (Side == CombatSide.Enemy)
		{
			_sprite.FlipH        = true;
			_sprite.SelfModulate = new Color(1.0f, 0.45f, 0.45f);
		}

		AddChild(_sprite);
	}

	private void LoadTex(string path, ref Texture2D? tex, ref int frames)
	{
		if (!ResourceLoader.Exists(path)) return;
		tex    = GD.Load<Texture2D>(path);
		frames = Math.Max(1, tex.GetWidth() / 128);
	}

	public override void _Process(double delta)
	{
		if (_sprite == null) return;
		// Dead units freeze on last frame — no further animation
		if (!IsAlive && _animState == AnimState.Dead && !_oneShot) return;

		_animTimer += (float)delta;
		if (_animTimer < _frameTime) return;
		_animTimer = 0f;

		int count = FrameCount();
		if (_oneShot)
		{
			if (_sprite.Frame >= count - 1)
			{
				_oneShot = false;
				if (_animState == AnimState.Dead)
					_sprite.Frame = count - 1;  // freeze on last frame forever
				else
					SetAnimation(_oneShotReturn);
				return;
			}
			_sprite.Frame++;
		}
		else
		{
			_sprite.Frame = (_sprite.Frame + 1) % count;
		}
	}

	private int FrameCount() => _animState switch
	{
		AnimState.Walk        => _walkFrames,
		AnimState.Run         => _runFrames,
		AnimState.Shot1       => _shot1Frames,
		AnimState.Shot2       => _shot2Frames,
		AnimState.AttackMelee => _attackFrames,
		AnimState.Hurt        => _hurtFrames,
		AnimState.Dead        => _deadFrames,
		_                     => _idleFrames,
	};

	private Texture2D? TexForState(AnimState s) => s switch
	{
		AnimState.Walk        => _texWalk   ?? _texIdle,
		AnimState.Run         => _texRun    ?? _texWalk ?? _texIdle,
		AnimState.Shot1       => _texShot1  ?? _texIdle,
		AnimState.Shot2       => _texShot2  ?? _texIdle,
		AnimState.AttackMelee => _texAttack ?? _texIdle,
		AnimState.Hurt        => _texHurt   ?? _texIdle,
		AnimState.Dead        => _texDead   ?? _texIdle,
		_                     => _texIdle,
	};

	public void SetAnimation(AnimState state)
	{
		if (_sprite == null) return;
		// Once a dead unit is playing or has played its death anim, never override it
		if (!IsAlive && _animState == AnimState.Dead) return;
		if (_animState == state && !_oneShot) return;
		_oneShot   = false;
		_animState = state;
		_animTimer = 0f;
		_sprite.Frame = 0;
		var tex = TexForState(state);
		if (tex != null)
		{
			_sprite.Texture = tex;
			_sprite.Hframes = FrameCount();
		}
	}

	/// <summary>Play an animation once, then return to <paramref name="returnTo"/>.</summary>
	public void PlayOnce(AnimState state, AnimState returnTo = AnimState.Idle)
	{
		if (_sprite == null) return;
		_oneShotReturn = returnTo;
		_animState     = state == _animState ? AnimState.Idle : _animState; // force reset
		SetAnimation(state);
		_oneShot = true;
	}

	/// <summary>Flip sprite to face the direction of movement.</summary>
	public void FaceDirection(Vector2 from, Vector2 to)
	{
		if (_sprite == null || Mathf.Abs(to.X - from.X) < 1f) return;
		bool movingLeft = to.X < from.X;
		_sprite.FlipH = movingLeft;  // FlipH=true → faces left; same logic for both sides
	}

	/// <summary>
	/// Start a move animation with frame-time synced to the tween duration
	/// so the sprite footsteps match the actual travel speed.
	/// </summary>
	public void SetMoveAnimation(AnimState state, float tweenDuration)
	{
		int frames = state == AnimState.Run ? _runFrames : _walkFrames;
		_frameTime = Mathf.Max(0.04f, tweenDuration / frames);
		SetAnimation(state);
	}

	// ── Static factory ────────────────────────────────────────────────────

	public static CombatUnit FromScout(Scout scout, int currentHP, Vector2I gridCell)
	{
		var unit = new CombatUnit
		{
			UnitId       = scout.Id,
			DisplayName  = scout.FullName,
			Side         = CombatSide.Player,
			MaxHP        = scout.MaxHP,
			CurrentHP    = currentHP,
			MaxAP        = 2,
			MoveRange    = 3,                  // 3 tiles per Move action
			Weapon       = WeaponDB.Rifle,
			_scoutStats  = scout.BaseStats,
			_scoutTraits = scout.Traits,
		};
		unit.GridCell = gridCell;
		return unit;
	}

	public static CombatUnit FromEnemy(EnemyDef def, Vector2I gridCell, int seed = 0)
	{
		var unit = new CombatUnit
		{
			UnitId      = $"{def.Id}_{seed}",
			DisplayName = def.Name,
			Side        = CombatSide.Enemy,
			MaxHP       = def.MaxHP,
			CurrentHP   = def.MaxHP,
			MaxAP       = 2,
			MoveRange   = def.MoveRange,
			Weapon      = def.Weapon,
			_enemyDef   = def,
		};
		unit.GridCell = gridCell;
		return unit;
	}

	// ── Identity ──────────────────────────────────────────────────────────

	public string     UnitId      { get; private set; } = "";
	public string     DisplayName { get; private set; } = "";
	public CombatSide Side        { get; private set; }

	// ── Stats ─────────────────────────────────────────────────────────────

	public int MaxHP     { get; private set; }
	public int CurrentHP { get; set; }
	public int MaxAP     { get; private set; }
	public int CurrentAP { get; set; }
	public int MoveRange { get; private set; }

	public WeaponDef Weapon   { get; set; } = WeaponDB.Fists;
	public int       AmmoLeft { get; set; } = -1;

	// ── Status ────────────────────────────────────────────────────────────

	public StatusEffect ActiveStatus { get; private set; } = StatusEffect.None;

	public bool IsAlive      => CurrentHP > 0;
	public bool HasOverwatch => (ActiveStatus & StatusEffect.Overwatch) != 0;

	// ── Ability cooldowns ─────────────────────────────────────────────────

	private readonly Dictionary<string, int> _cooldowns = new();

	// ── Grid position ─────────────────────────────────────────────────────

	private Vector2I _gridCell;
	public Vector2I GridCell { get => _gridCell; set => _gridCell = value; }

	// ── Source data ───────────────────────────────────────────────────────

	private Stats?          _scoutStats;
	private List<TraitDef>? _scoutTraits;
	private EnemyDef?       _enemyDef;

	public Stats?          ScoutStats  => _scoutStats;
	public List<TraitDef>? ScoutTraits => _scoutTraits;
	public EnemyDef?       EnemyDef    => _enemyDef;

	// ── Turn lifecycle ────────────────────────────────────────────────────

	public int BeginTurn()
	{
		int burnDamage = 0;
		if ((ActiveStatus & StatusEffect.Burning) != 0)
		{
			burnDamage = 3;
			CurrentHP  = Mathf.Max(0, CurrentHP - burnDamage);
		}

		int ap = MaxAP;
		if ((ActiveStatus & StatusEffect.Suppressed) != 0)
			ap = Mathf.Max(0, ap - 1);
		CurrentAP = ap;

		ActiveStatus &= ~StatusEffect.Overwatch;
		ActiveStatus &= ~StatusEffect.Suppressed;

		if ((ActiveStatus & StatusEffect.Stunned) != 0)
		{
			CurrentAP = 0;
			ActiveStatus &= ~StatusEffect.Stunned;
		}

		var keys = new List<string>(_cooldowns.Keys);
		foreach (var k in keys)
		{
			_cooldowns[k] = Mathf.Max(0, _cooldowns[k] - 1);
			if (_cooldowns[k] == 0) _cooldowns.Remove(k);
		}

		QueueRedraw();
		return burnDamage;
	}

	// ── Action economy ────────────────────────────────────────────────────

	public bool CanAct() => IsAlive && CurrentAP > 0;
	public bool SpendAP(int amount)
	{
		if (CurrentAP < amount) return false;
		CurrentAP -= amount;
		QueueRedraw();
		return true;
	}

	// ── Status management ─────────────────────────────────────────────────

	public void ApplyStatus(StatusEffect effect) { ActiveStatus |= effect;  QueueRedraw(); }
	public void ClearStatus(StatusEffect effect)  { ActiveStatus &= ~effect; QueueRedraw(); }

	// ── Damage / healing ──────────────────────────────────────────────────

	public bool TakeDamage(int amount)
	{
		CurrentHP = Mathf.Max(0, CurrentHP - amount);
		QueueRedraw();
		return CurrentHP <= 0;
	}

	public void Heal(int amount)
	{
		CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
		QueueRedraw();
	}

	// ── Initiative ────────────────────────────────────────────────────────

	public int RollInitiative(Random rng)
	{
		if (_scoutStats is { } stats)
			return stats.AWR * 2 + stats.ING + rng.Next(0, stats.PRC + 1);
		if (_enemyDef is { } def)
			return def.BaseInitiative + rng.Next(0, 6);
		return rng.Next(0, 10);
	}

	// ── Hit-chance calculation ─────────────────────────────────────────────

	/// <summary>
	/// Hit chance accounting for weapon base, range sweetspot, PRC, cover, elevation, traits.
	/// </summary>
	public float CalcHitChance(CombatUnit target, WeaponDef weapon,
	                           CoverType targetCover, float elevBonus, int range)
	{
		float chance = weapon.BaseHitChance;

		// Range sweetspot
		chance += WeaponDB.GetRangeMod(weapon, range);

		// Scout PRC modifier
		if (_scoutStats is { } stats)
			chance += (stats.PRC - 10) * 0.01f;

		// Elevation
		chance += elevBonus;

		// Cover dodge
		chance -= CombatGrid.CoverDodgeBonus(targetCover);

		// Trait modifiers
		if (_scoutTraits is { } traits)
		{
			foreach (var trait in traits)
			{
				chance += trait.Id switch
				{
					"Sharpshooter" => 0.15f,
					"Reckless"     => -0.10f,
					"Cautious"     => 0.05f,
					_              => 0f,
				};
			}
		}

		// Sniper ignores half cover
		if (weapon.Category == WeaponCategory.SniperRifle && targetCover == CoverType.Half)
			chance += CombatGrid.CoverDodgeBonus(CoverType.Half);

		return Mathf.Clamp(chance, 0.05f, 0.95f);
	}

	// ── Rendering ─────────────────────────────────────────────────────────

	public override void _Draw()
	{
		// Dead: show faint shadow under the persistent body sprite
		if (!IsAlive)
		{
			if (_sprite != null)
				DrawArc(Vector2.Zero, 16f, 0, Mathf.Tau, 16, new Color(0.1f, 0.05f, 0.05f, 0.55f), 3f);
			return;
		}

		// Gold outline rectangle for active unit (no pulsing)
		if (IsActive)
			DrawRect(new Rect2(-25f, -25f, 50f, 55f), new Color(1f, 0.85f, 0.05f, 0.92f), false, 2.5f);

		var bodyColor = Side == CombatSide.Player
			? new Color(0.25f, 0.50f, 1.0f)
			: new Color(0.90f, 0.20f, 0.15f);
		if (CurrentAP == 0) bodyColor = bodyColor.Darkened(0.35f);

		if (_sprite != null)
			DrawArc(Vector2.Zero, 20f, 0, Mathf.Tau, 32, bodyColor with { A = 0.70f }, 3f);
		else
		{
			DrawCircle(Vector2.Zero, 14f, bodyColor);
			DrawArc(Vector2.Zero, 14f, 0, Mathf.Tau, 24, new Color(1, 1, 1, 0.5f), 1f);
			string lbl = DisplayName.Length > 0 ? DisplayName[0].ToString() : "?";
			DrawString(ThemeDB.FallbackFont, new Vector2(-5, 5), lbl,
			           HorizontalAlignment.Left, -1, 12, Colors.White);
		}

		// HP bar
		float hpFrac = MaxHP > 0 ? (float)CurrentHP / MaxHP : 0f;
		DrawRect(new Rect2(-20, -34, 40, 4), new Color(0.12f, 0.12f, 0.12f));
		DrawRect(new Rect2(-20, -34, 40 * hpFrac, 4),
		         hpFrac > 0.5f  ? new Color(0.2f, 0.85f, 0.2f)
		       : hpFrac > 0.25f ? new Color(0.9f, 0.7f, 0.1f)
		       :                   new Color(0.9f, 0.15f, 0.1f));

		// AP pips
		for (int i = 0; i < MaxAP; i++)
		{
			var pip = i < CurrentAP ? new Color(1f, 0.9f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);
			DrawCircle(new Vector2(-6 + i * 12, 34f), 4f, pip);
		}

		// Status icons
		float sx = -14f;
		if ((ActiveStatus & StatusEffect.Burning) != 0)
		{
			DrawString(ThemeDB.FallbackFont, new Vector2(sx, -34), "B",
			           HorizontalAlignment.Left, -1, 9, new Color(1f, 0.4f, 0f));
			sx += 10;
		}
		if ((ActiveStatus & StatusEffect.Stunned) != 0)
		{
			DrawString(ThemeDB.FallbackFont, new Vector2(sx, -34), "S",
			           HorizontalAlignment.Left, -1, 9, new Color(0.8f, 0.8f, 0f));
			sx += 10;
		}
		if ((ActiveStatus & StatusEffect.Suppressed) != 0)
		{
			DrawString(ThemeDB.FallbackFont, new Vector2(sx, -34), "P",
			           HorizontalAlignment.Left, -1, 9, new Color(0.5f, 0.7f, 1f));
		}
		if ((ActiveStatus & StatusEffect.Overwatch) != 0)
		{
			// Dashed gold ring + "WATCH" label so it's unmissable
			DrawArc(Vector2.Zero, 22f, 0, Mathf.Tau, 32, new Color(1f, 0.85f, 0.1f, 0.90f), 2.5f);
			DrawString(ThemeDB.FallbackFont, new Vector2(-16f, -42f), "WATCH",
			           HorizontalAlignment.Left, -1, 8, new Color(1f, 0.85f, 0.1f));
		}
	}
}
