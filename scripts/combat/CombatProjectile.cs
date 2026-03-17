#nullable enable
using Godot;

/// <summary>
/// Spawns a one-shot projectile/blast FX sprite that travels from attacker to target,
/// then frees itself.  All sprite sheets point UPWARD (-Y) in their first frame, so
/// rotation = direction.Angle() + π/2 aligns them to any screen-space direction.
///
/// Frame-layout constants (matching the exhaust_0x sheets at their actual pixel sizes):
///   Exhaust_01  896×384  → 7 cols × 3 rows = 21 frames  128×128 px each  (small bullet)
///   Exhaust_02 1152×600  → 8 cols × 3 rows = 24 frames  144×200 px each  (elongated slug)
///   Exhaust_03 1024×600  → 8 cols × 3 rows = 24 frames  128×200 px each  (big blast)
/// </summary>
public partial class CombatProjectile : Node2D
{
	// ── Cached textures (loaded once, reused for every projectile) ────────

	private static Texture2D? _texEx1;  // Exhaust_01 — small bullet / rifle burst
	private static Texture2D? _texEx2;  // Exhaust_02 — sniper slug
	private static Texture2D? _texEx3;  // Exhaust_03 — blast / shotgun / grenade

	private static void EnsureTextures()
	{
		_texEx1 ??= TryLoad("res://assets/sprites/fx/Exhaust_01/exhaust_01_spritesheet.png");
		_texEx2 ??= TryLoad("res://assets/sprites/fx/Exhaust_02/exhaust02_spritesheet.png");
		_texEx3 ??= TryLoad("res://assets/sprites/fx/Exhaust_03/exhaust_03_spritesheet.png");
	}

	private static Texture2D? TryLoad(string path) =>
		ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

	// ── Public entry point ────────────────────────────────────────────────

	/// <summary>
	/// Spawns the appropriate projectile effect(s) for a given weapon.
	/// <paramref name="parent"/> is the node to attach projectiles to (typically CombatGrid).
	/// </summary>
	public static void FireForWeapon(Node parent, Vector2 from, Vector2 to, WeaponDef weapon)
	{
		EnsureTextures();
		switch (weapon.Category)
		{
			case WeaponCategory.Pistol:
				// Single small bullet
				if (_texEx1 != null)
					SpawnTravelling(parent, from, to, _texEx1, 7, 3, speed: 500f, scale: 0.26f);
				break;

			case WeaponCategory.Rifle:
				// Burst: 3 bullets fired sequentially, 90 ms apart
				if (_texEx1 != null)
				{
					SpawnTravelling(parent, from, to, _texEx1, 7, 3, speed: 490f, scale: 0.22f);
					var t2 = parent.GetTree().CreateTimer(0.09f);
					t2.Timeout += () => { if (GodotObject.IsInstanceValid((GodotObject)parent)) SpawnTravelling(parent, from, to, _texEx1, 7, 3, speed: 490f, scale: 0.22f); };
					var t3 = parent.GetTree().CreateTimer(0.18f);
					t3.Timeout += () => { if (GodotObject.IsInstanceValid((GodotObject)parent)) SpawnTravelling(parent, from, to, _texEx1, 7, 3, speed: 490f, scale: 0.22f); };
				}
				break;

			case WeaponCategory.SniperRifle:
				// Faster, slightly larger version of the pistol bullet
				if (_texEx1 != null)
					SpawnTravelling(parent, from, to, _texEx1, 7, 3, speed: 880f, scale: 0.34f);
				break;

			case WeaponCategory.Shotgun:
				// Blast spawned partway to target — short travel then explode
				if (_texEx3 != null)
				{
					var midpoint = from.Lerp(to, 0.60f);
					SpawnTravelling(parent, from, midpoint, _texEx3, 8, 3, speed: 420f, scale: 0.50f);
				}
				break;

			case WeaponCategory.Explosive:
				// Grenade arcs to target, blast at impact
				if (_texEx3 != null)
					SpawnTravelling(parent, from, to, _texEx3, 8, 3, speed: 340f, scale: 0.65f);
				break;

			// Melee / Unarmed — no projectile
		}
	}

	// ── Internal spawning helpers ─────────────────────────────────────────

	/// <summary>
	/// Spawns a sprite that moves from <paramref name="from"/> to <paramref name="to"/>
	/// while animating through all frames.  The sprite is oriented so frame 0 (pointing up)
	/// rotates to face the direction of travel.
	/// </summary>
	private static void SpawnTravelling(Node parent, Vector2 from, Vector2 to,
	                                    Texture2D tex, int hframes, int vframes,
	                                    float speed, float scale, float extraAngle = 0f)
	{
		float dist = from.DistanceTo(to);
		if (dist < 0.5f) return;

		var proj = new CombatProjectile();
		parent.AddChild(proj);
		proj.ZIndex = 200;   // above all units
		proj.Position = from;

		var dir    = (to - from).Normalized();
		float rot  = dir.Angle() + Mathf.Pi / 2f + extraAngle;

		int   totalFrames = hframes * vframes;
		float duration    = dist / speed;
		float frameTime   = Mathf.Max(0.016f, duration / totalFrames);

		var sprite = new Sprite2D
		{
			Texture  = tex,
			Hframes  = hframes,
			Vframes  = vframes,
			Frame    = 0,
			Scale    = new Vector2(scale, scale),
			Rotation = rot,
		};
		proj._sprite     = sprite;
		proj._totalFrames = totalFrames;
		proj._frameTime  = frameTime;
		proj.AddChild(sprite);

		// Travel toward target (or spread point if extraAngle != 0)
		var destination = extraAngle == 0f ? to : from + dir.Rotated(extraAngle) * dist;
		var tween = proj.CreateTween();
		tween.TweenProperty(proj, "position", destination, duration)
		     .SetTrans(Tween.TransitionType.Linear);
		tween.TweenCallback(Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(proj)) proj.QueueFree();
		}));
	}

	// ── Animation ─────────────────────────────────────────────────────────

	private Sprite2D? _sprite;
	private int       _totalFrames;
	private float     _frameTime;
	private float     _animTimer;
	private int       _frame;

	public override void _Process(double delta)
	{
		if (_sprite == null) return;
		_animTimer += (float)delta;
		if (_animTimer >= _frameTime)
		{
			_animTimer = 0f;
			_frame     = (_frame + 1) % _totalFrames;
			_sprite.Frame = _frame;
		}
	}
}
