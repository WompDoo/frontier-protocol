#nullable enable
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Root controller for a combat encounter. Owns the FSM, input handling,
/// and bridges all combat subsystems together.
///
/// Scene tree:
///   CombatScene (Node2D — this)
///   ├── CombatGrid (Node2D)      — tile rendering + spatial queries
///   ├── Camera2D                 — centered on grid, slight zoom
///   └── HUD (CanvasLayer 1)      — turn order, AP display, combat log
///
/// Data flow:
///   PartyManager → scouts → CombatUnit (player side)
///   ChunkManager.GetChunkAt(PartyManager.CurrentChunkCoord) → CombatGrid.GenerateFrom()
///   TurnQueue → drives FSM transitions
///   EnemyAI   → returns CombatAction list → CombatManager executes
///
/// Enter combat: call StartCombat(enemyGroup) from wherever encounter is triggered.
/// Exit combat:  emits CombatFinished(result) signal.
/// </summary>
public partial class CombatManager : Node2D
{
	[Signal] public delegate void CombatFinishedEventHandler(int result); // CombatResult cast to int

	// ── Scene refs (built in _Ready) ──────────────────────────────────────

	private CombatGrid  _grid    = null!;
	private Camera2D    _camera  = null!;
	private CanvasLayer _hud     = null!;

	// Top labels
	private Label _logLabel       = null!;
	private Label _phaseLabel     = null!;
	private Label _turnOrderLabel = null!;
	private Label _tooltip        = null!;   // floating label near cursor

	// Bottom bar
	private Panel       _bottomBar     = null!;
	private TextureRect _portrait      = null!;
	private ColorRect[] _apPips        = Array.Empty<ColorRect>();
	private Label       _unitNameLabel  = null!;
	private Label       _weaponLabel    = null!;
	private ColorRect   _hpBarFill      = null!;
	private Button      _btnAtk        = null!;
	private Button      _btnMove       = null!;
	private Button      _btnOw         = null!;
	private string?     _portraitUnitId;

	// Debug panel
	private Panel  _debugPanel = null!;
	private bool   _debugVisible = true;
	private CombatBgNode? _bgNode;
	private int    _debugResetSeed = 0;
	private static readonly WeaponDef[] DebugWeapons =
	{
		WeaponDB.Pistol, WeaponDB.Rifle, WeaponDB.Shotgun, WeaponDB.SniperRifle,
		WeaponDB.Fists, WeaponDB.Grenade,
	};

	// ── Combat state ──────────────────────────────────────────────────────

	private CombatPhase           _phase    = CombatPhase.Setup;
	private TurnQueue             _queue    = new();
	private readonly List<CombatUnit> _units = new();
	private CombatUnit?           _activeUnit;
	private Random                _rng     = new();

	// Occupied cells — kept in sync as units move
	private readonly HashSet<Vector2I> _occupied = new();

	// AI action pipeline (for enemy turns)
	private Queue<CombatAction> _pendingAiActions = new();
	private double _aiActionTimer = 0.0;
	private const double AiActionDelay = 0.5; // seconds between AI steps

	// ── Player input state ────────────────────────────────────────────────

	private enum PlayerState { SelectUnit, SelectMove, SelectAttack, SelectOverwatch }
	private PlayerState _playerState = PlayerState.SelectUnit;
	private CombatUnit? _selectedUnit;

	// ── Godot lifecycle ───────────────────────────────────────────────────

	public override void _Ready()
	{
		BuildScene();

		// Ensure the party has scouts (fallback for direct scene launch during dev)
		PartyManager.Instance?.DebugAddTestScouts(42);

		// Use enemies queued by WorldScene, or fall back to a default encounter
		var gm       = GameManager.Instance;
		var enemies  = gm != null && gm.PendingEnemies.Count > 0
			? gm.PendingEnemies
			: new List<(EnemyDef def, Vector2I cell)> { (EnemyDB.AlienWarrior, new Vector2I(9, 5)) };

		// Generate arena from the party's current chunk when possible
		ChunkData? chunk = TryGetCurrentChunk();
		StartCombat(enemies, chunk);

		// Clear pending enemies so they aren't reused on the next encounter
		if (gm != null) gm.PendingEnemies = new();

		CombatFinished += OnCombatFinished;
	}

	/// <summary>Regenerates the chunk the party is standing on using world-gen data from PartyManager.</summary>
	private static ChunkData? TryGetCurrentChunk()
	{
		var pm = PartyManager.Instance;
		if (pm == null || pm.WorldSeed == 0) return null;
		var planet = ChunkGenerator.DeriveParams(pm.WorldSeed);
		var coord  = pm.CurrentChunkCoord;
		return ChunkGenerator.Generate(
			new Vector2I(ChunkGenerator.NormX(coord.X), coord.Y),
			pm.WorldSeed, planet);
	}

	private void OnCombatFinished(int result)
	{
		PartyManager.Instance?.PostCombatRevive();
		// Phase stays at Iso — WorldScene will restore iso mode on re-entry
		if (GameManager.Instance != null)
			GameManager.Instance.Phase = GameManager.GamePhase.Iso;
		GetTree().ChangeSceneToFile("res://scenes/world.tscn");
	}

	private void BuildScene()
	{
		_grid = new CombatGrid();
		AddChild(_grid);

		_camera = new Camera2D { Zoom = new Vector2(1.8f, 1.8f), Position = Vector2.Zero };
		AddChild(_camera);

		_hud = new CanvasLayer { Layer = 1 };
		AddChild(_hud);

		_phaseLabel = MakeLabel("", new Vector2(8, 8), 14);
		_hud.AddChild(_phaseLabel);

		_turnOrderLabel = MakeLabel("", new Vector2(8, 30), 11);
		_hud.AddChild(_turnOrderLabel);

		// Floating tooltip near cursor — high Z so it renders above everything
		_tooltip = MakeLabel("", new Vector2(0, 0), 13);
		_tooltip.AddThemeColorOverride("font_color", new Color(1.0f, 0.95f, 0.6f));
		var ttStyle = new StyleBoxFlat { BgColor = new Color(0.04f, 0.04f, 0.10f, 0.92f) };
		ttStyle.SetContentMarginAll(6); ttStyle.SetCornerRadiusAll(4);
		_tooltip.AddThemeStyleboxOverride("normal", ttStyle);
		_tooltip.ZIndex  = 100;
		_tooltip.Visible = false;
		_hud.AddChild(_tooltip);

		// Log: above the compact bottom bar (114px), left-aligned, max 430px wide
		_logLabel = MakeLabel("", Vector2.Zero, 10);
		_logLabel.AnchorTop    = 1f; _logLabel.AnchorBottom = 1f;
		_logLabel.AnchorLeft   = 0f; _logLabel.AnchorRight  = 0f;
		_logLabel.OffsetTop    = -220f; _logLabel.OffsetBottom = -118f;
		_logLabel.OffsetLeft   = 8f;    _logLabel.OffsetRight  = 430f;
		_logLabel.AutowrapMode      = TextServer.AutowrapMode.WordSmart;
		_logLabel.CustomMinimumSize = new Vector2(420, 96);
		_hud.AddChild(_logLabel);

		BuildBackground();
		BuildBottomBar();
		BuildDebugPanel();
	}

	private void BuildBackground()
	{
		// Background layer — dark atmospheric sky, renders behind the 3D grid
		var bgLayer = new CanvasLayer { Layer = -1 };
		AddChild(bgLayer);
		_bgNode = new CombatBgNode();
		bgLayer.AddChild(_bgNode);

		// Vignette layer — dark edges to frame the view, renders above grid but below HUD
		var vigLayer = new CanvasLayer { Layer = 2 };
		AddChild(vigLayer);
		var vig = new CombatVignetteNode();
		vigLayer.AddChild(vig);
	}

	private void BuildBottomBar()
	{
		const float BarH = 114f;
		const float BarW = 430f;  // compact left panel — does NOT span full screen

		_bottomBar = new Panel();
		_bottomBar.AnchorLeft   = 0f;
		_bottomBar.AnchorRight  = 0f;   // right edge = left anchor + OffsetRight (fixed width)
		_bottomBar.AnchorTop    = 1f;
		_bottomBar.AnchorBottom = 1f;
		_bottomBar.OffsetLeft   = 0f;
		_bottomBar.OffsetRight  = BarW;
		_bottomBar.OffsetTop    = -BarH;
		_bottomBar.OffsetBottom = 0f;

		var bgStyle = new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, 0.96f) };
		bgStyle.BorderColor = new Color(0.28f, 0.30f, 0.38f, 1f);
		bgStyle.SetBorderWidthAll(1);
		bgStyle.CornerRadiusTopRight = 6;
		_bottomBar.AddThemeStyleboxOverride("panel", bgStyle);
		_hud.AddChild(_bottomBar);

		// ── Portrait (square with clipped corners) ──────────────────────
		var portraitBg = new Panel();
		portraitBg.Position = new Vector2(8f, 8f);
		portraitBg.Size     = new Vector2(90f, 90f);
		var pStyle = new StyleBoxFlat { BgColor = new Color(0.15f, 0.16f, 0.20f) };
		pStyle.BorderColor = new Color(0.40f, 0.42f, 0.55f, 1f);
		pStyle.SetBorderWidthAll(2);
		pStyle.SetCornerRadiusAll(6);
		portraitBg.AddThemeStyleboxOverride("panel", pStyle);
		_bottomBar.AddChild(portraitBg);

		_portrait = new TextureRect
		{
			Position    = new Vector2(2f, 2f),
			Size        = new Vector2(86f, 86f),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			ClipContents = true,
		};
		portraitBg.AddChild(_portrait);

		// ── AP pips (vertical stack beside portrait) ────────────────────
		_apPips = new ColorRect[2];
		for (int i = 0; i < 2; i++)
		{
			var pip = new ColorRect
			{
				Position = new Vector2(106f, 18f + i * 40f),
				Size     = new Vector2(22f, 22f),
				Color    = new Color(0.25f, 0.25f, 0.25f),
			};
			// Round pip background
			var pipStyle = new StyleBoxFlat { BgColor = new Color(0.25f, 0.25f, 0.25f) };
			pipStyle.SetCornerRadiusAll(11);
			pip.AddThemeStyleboxOverride("panel", pipStyle);
			_bottomBar.AddChild(pip);
			_apPips[i] = pip;
		}

		// ── Unit name / role ────────────────────────────────────────────
		_unitNameLabel = MakeLabel("", new Vector2(136f, 8f), 13);
		_unitNameLabel.AddThemeColorOverride("font_color", new Color(0.90f, 0.90f, 0.96f));
		_bottomBar.AddChild(_unitNameLabel);

		// ── Weapon line ─────────────────────────────────────────────────
		_weaponLabel = MakeLabel("", new Vector2(136f, 24f), 11);
		_weaponLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.80f, 0.65f));
		_bottomBar.AddChild(_weaponLabel);

		// ── Action buttons row ──────────────────────────────────────────
		const float BtnY = 42f, BtnW = 72f, BtnH = 40f, BtnGap = 5f;
		float bx = 136f;
		_btnAtk  = MakeActionButton("ATK",  new Vector2(bx, BtnY), new Vector2(BtnW, BtnH)); bx += BtnW + BtnGap;
		_btnMove = MakeActionButton("MOVE", new Vector2(bx, BtnY), new Vector2(BtnW, BtnH)); bx += BtnW + BtnGap;
		_btnOw   = MakeActionButton("OW",   new Vector2(bx, BtnY), new Vector2(BtnW, BtnH));
		// Overwatch tooltip so players understand what it does
		_btnOw.TooltipText = "Overwatch: spend 1 AP to react-fire at the first enemy that moves into line of sight on their turn.";
		_bottomBar.AddChild(_btnAtk);
		_bottomBar.AddChild(_btnMove);
		_bottomBar.AddChild(_btnOw);

		_btnAtk.Pressed += () =>
		{
			if (_selectedUnit != null && _phase == CombatPhase.PlayerTurn &&
			    _selectedUnit.CurrentAP >= _selectedUnit.Weapon.ApCost)
			{
				_playerState = PlayerState.SelectAttack;
				ShowAttackHighlight(_selectedUnit);
			}
		};
		_btnMove.Pressed += () =>
		{
			if (_selectedUnit != null && _phase == CombatPhase.PlayerTurn)
			{
				_playerState = PlayerState.SelectMove;
				ClearHoverInfo();
				ShowMoveHighlight(_selectedUnit);
			}
		};
		_btnOw.Pressed += () =>
		{
			if (_selectedUnit != null && _phase == CombatPhase.PlayerTurn && _selectedUnit.CurrentAP >= 1)
				ExecuteOverwatch(_selectedUnit);
		};

		// ── HP bar (spans panel width, bottom 10px) ─────────────────────
		var hpBg = new ColorRect { Color = new Color(0.15f, 0.05f, 0.05f) };
		hpBg.AnchorLeft   = 0f; hpBg.AnchorRight  = 1f;
		hpBg.AnchorTop    = 1f; hpBg.AnchorBottom = 1f;
		hpBg.OffsetTop    = -10f; hpBg.OffsetBottom = 0f;
		_bottomBar.AddChild(hpBg);

		_hpBarFill = new ColorRect { Color = new Color(0.80f, 0.15f, 0.12f) };
		_hpBarFill.AnchorLeft   = 0f; _hpBarFill.AnchorRight  = 1f;  // updated dynamically
		_hpBarFill.AnchorTop    = 1f; _hpBarFill.AnchorBottom = 1f;
		_hpBarFill.OffsetTop    = -10f; _hpBarFill.OffsetBottom = 0f;
		_bottomBar.AddChild(_hpBarFill);
	}

	private void BuildDebugPanel()
	{
		// Top-right debug overlay — toggle with F3
		_debugPanel = new Panel();
		_debugPanel.AnchorLeft   = 1f; _debugPanel.AnchorRight  = 1f;
		_debugPanel.AnchorTop    = 0f; _debugPanel.AnchorBottom = 0f;
		_debugPanel.OffsetLeft   = -260f; _debugPanel.OffsetRight = 0f;
		_debugPanel.OffsetTop    = 0f;   _debugPanel.OffsetBottom = 310f;

		var bgStyle = new StyleBoxFlat { BgColor = new Color(0.06f, 0.06f, 0.08f, 0.93f) };
		bgStyle.BorderColor = new Color(0.35f, 0.35f, 0.50f);
		bgStyle.SetBorderWidthAll(1);
		_debugPanel.AddThemeStyleboxOverride("panel", bgStyle);
		_hud.AddChild(_debugPanel);

		float y = 6f;
		var title = MakeLabel("[F3] DEBUG PANEL", new Vector2(8f, y), 10);
		title.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.9f));
		_debugPanel.AddChild(title);
		y += 20f;

		// ── Animation triggers ─────────────────────────────────────────
		var animLabel = MakeLabel("ANIMATIONS  (on active unit)", new Vector2(8f, y), 9);
		animLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.65f));
		_debugPanel.AddChild(animLabel);
		y += 16f;

		var animButtons = new (string label, CombatUnit.AnimState state)[]
		{
			("Idle",   CombatUnit.AnimState.Idle),
			("Walk",   CombatUnit.AnimState.Walk),
			("Run",    CombatUnit.AnimState.Run),
			("Shot 1", CombatUnit.AnimState.Shot1),
			("Shot 2", CombatUnit.AnimState.Shot2),
			("Attack", CombatUnit.AnimState.AttackMelee),
			("Hurt",   CombatUnit.AnimState.Hurt),
			("Dead",   CombatUnit.AnimState.Dead),
		};

		const float BW = 114f, BH = 26f, BG = 4f;
		int col = 0;
		foreach (var (lbl, state) in animButtons)
		{
			float bx = 8f + col * (BW + BG);
			var btn = MakeDebugButton(lbl, new Vector2(bx, y), new Vector2(BW, BH));
			var s   = state;
			btn.Pressed += () =>
			{
				var unit = _selectedUnit ?? _activeUnit;
				if (unit == null) return;
				if (s == CombatUnit.AnimState.Idle)
					unit.SetAnimation(CombatUnit.AnimState.Idle);
				else
					unit.PlayOnce(s, CombatUnit.AnimState.Idle);
			};
			_debugPanel.AddChild(btn);
			col++;
			if (col >= 2) { col = 0; y += BH + BG; }
		}
		if (col > 0) y += BH + BG;
		y += 4f;

		// ── Weapons ────────────────────────────────────────────────────
		var wLabel = MakeLabel("WEAPON  (next reset picks new)", new Vector2(8f, y), 9);
		wLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.65f));
		_debugPanel.AddChild(wLabel);
		y += 16f;

		foreach (var weapon in DebugWeapons)
		{
			var w   = weapon;
			var btn = MakeDebugButton(weapon.Name, new Vector2(8f, y), new Vector2(240f, BH));
			btn.Pressed += () =>
			{
				var unit = _selectedUnit ?? _activeUnit;
				if (unit != null) unit.Weapon = w;
				Log($"Weapon set to {w.Name}  (range {w.RangeMin}-{w.RangeMax}, base {w.BaseHitChance:P0})");
				ShowMoveHighlight(unit!);
			};
			_debugPanel.AddChild(btn);
			y += BH + BG;
		}
		y += 4f;

		// ── Reset ──────────────────────────────────────────────────────
		var resetBtn = MakeDebugButton("[R]  RESET COMBAT  (new weapon + terrain)", new Vector2(8f, y), new Vector2(240f, 30f));
		resetBtn.Pressed += () => ResetDebugCombat();
		_debugPanel.AddChild(resetBtn);

		// Resize panel to fit content
		_debugPanel.OffsetBottom = y + 36f;
	}

	private void ResetDebugCombat()
	{
		// Free all unit nodes before StartCombat clears the list
		foreach (var u in _units) u.QueueFree();
		_units.Clear();

		// Advance seed for new terrain; cycle weapon in order
		_debugResetSeed++;
		var newWeapon = DebugWeapons[_debugResetSeed % DebugWeapons.Length];

		_grid.GenerateDebug(_debugResetSeed * 7 + 13);
		_portraitUnitId = null;  // force portrait reload

		var party = GetNodeOrNull<PartyManager>("/root/PartyManager");
		party?.DebugAddTestScouts(42 + _debugResetSeed);

		StartCombat(new List<(EnemyDef, Vector2I)>
		{
			(EnemyDB.AlienWarrior, new Vector2I(9, 5)),
		});

		// Assign the cycled weapon to the player unit
		var player = _units.FirstOrDefault(u => u.Side == CombatSide.Player);
		if (player != null)
		{
			player.Weapon = newWeapon;
			Log($"Reset #{_debugResetSeed}  |  Weapon: {newWeapon.Name}  (range {newWeapon.RangeMin}-{newWeapon.RangeMax})");
		}
	}

	private static Button MakeDebugButton(string text, Vector2 pos, Vector2 size)
	{
		var btn = new Button { Text = text, Position = pos, Size = size };
		btn.AddThemeFontSizeOverride("font_size", 10);
		btn.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.92f));
		var normal = new StyleBoxFlat { BgColor = new Color(0.18f, 0.18f, 0.24f) };
		normal.SetCornerRadiusAll(3);
		btn.AddThemeStyleboxOverride("normal", normal);
		var hover = new StyleBoxFlat { BgColor = new Color(0.28f, 0.28f, 0.38f) };
		hover.SetCornerRadiusAll(3);
		btn.AddThemeStyleboxOverride("hover", hover);
		var pressed = new StyleBoxFlat { BgColor = new Color(0.38f, 0.38f, 0.55f) };
		pressed.SetCornerRadiusAll(3);
		btn.AddThemeStyleboxOverride("pressed", pressed);
		return btn;
	}

	private static Label MakeLabel(string text, Vector2 pos, int fontSize)
	{
		var l = new Label { Text = text, Position = pos };
		l.AddThemeColorOverride("font_color", Colors.White);
		l.AddThemeFontSizeOverride("font_size", fontSize);
		return l;
	}

	private static Button MakeActionButton(string text, Vector2 pos, Vector2 size)
	{
		var btn = new Button { Text = text, Position = pos, Size = size };
		btn.AddThemeFontSizeOverride("font_size", 14);
		btn.AddThemeColorOverride("font_color", new Color(0.08f, 0.06f, 0f));

		var normal = new StyleBoxFlat { BgColor = new Color(0.72f, 0.62f, 0.06f) };
		normal.SetCornerRadiusAll(4);
		btn.AddThemeStyleboxOverride("normal", normal);

		var hover = new StyleBoxFlat { BgColor = new Color(0.92f, 0.82f, 0.12f) };
		hover.SetCornerRadiusAll(4);
		btn.AddThemeStyleboxOverride("hover", hover);

		var pressed = new StyleBoxFlat { BgColor = new Color(0.50f, 0.42f, 0.02f) };
		pressed.SetCornerRadiusAll(4);
		btn.AddThemeStyleboxOverride("pressed", pressed);

		var disabled = new StyleBoxFlat { BgColor = new Color(0.28f, 0.28f, 0.28f) };
		disabled.SetCornerRadiusAll(4);
		btn.AddThemeStyleboxOverride("disabled", disabled);
		btn.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.5f, 0.5f));

		return btn;
	}

	private void UpdateBottomBar(CombatUnit? unit)
	{
		bool playerTurn = _phase == CombatPhase.PlayerTurn;
		_btnAtk.Disabled  = !playerTurn || unit == null || unit.CurrentAP < unit.Weapon.ApCost;
		_btnMove.Disabled = !playerTurn || unit == null || unit.CurrentAP < 1;
		_btnOw.Disabled   = !playerTurn || unit == null || unit.CurrentAP < 1 || unit.HasOverwatch;

		if (unit == null)
		{
			_unitNameLabel.Text    = "";
			_weaponLabel.Text      = "";
			_hpBarFill.AnchorRight = 0f;
			_portrait.Texture      = null;
			foreach (var pip in _apPips) pip.Color = new Color(0.25f, 0.25f, 0.25f);
			return;
		}

		// Portrait — only reload when unit changes
		if (unit.UnitId != _portraitUnitId)
		{
			_portraitUnitId = unit.UnitId;
			int    variant  = (Math.Abs(unit.UnitId.GetHashCode()) % 3) + 1;
			string path     = $"res://assets/sprites/scouts/Soldier_{variant}/Idle.png";
			if (ResourceLoader.Exists(path))
			{
				var atlas    = new AtlasTexture();
				atlas.Atlas  = GD.Load<Texture2D>(path);
				// Frame 0 = x:0–128, y:0–128. Crop the top portion to show the face/upper body.
				atlas.Region = new Rect2(0, 0, 128, 96);
				_portrait.Texture      = atlas;
				_portrait.SelfModulate = unit.Side == CombatSide.Enemy
					? new Color(1.0f, 0.55f, 0.55f) : Colors.White;
			}
		}

		// Name + role
		string role         = unit.EnemyDef != null ? unit.EnemyDef.Name : "Scout";
		string firstName    = unit.DisplayName.Split(' ')[0];
		_unitNameLabel.Text = $"{firstName}  —  {role}";

		// Weapon — name, damage range, reach, AP cost
		var w = unit.Weapon;
		_weaponLabel.Text = w.RangeMax == 0
			? $"{w.Name}  |  {w.DamageMin}–{w.DamageMax} dmg  |  melee  |  {w.ApCost} AP"
			: $"{w.Name}  |  {w.DamageMin}–{w.DamageMax} dmg  |  {w.RangeMin}–{w.RangeMax} tiles  |  {w.ApCost} AP";

		// AP pips — green = available, grey = spent
		for (int i = 0; i < _apPips.Length; i++)
			_apPips[i].Color = i < unit.CurrentAP
				? new Color(0.18f, 0.80f, 0.25f)
				: new Color(0.20f, 0.20f, 0.22f);

		// HP bar (AnchorRight = fraction of parent width)
		_hpBarFill.AnchorRight = unit.MaxHP > 0 ? (float)unit.CurrentHP / unit.MaxHP : 0f;
		_hpBarFill.Color = (float)unit.CurrentHP / unit.MaxHP > 0.5f
			? new Color(0.20f, 0.75f, 0.18f)
			: (float)unit.CurrentHP / unit.MaxHP > 0.25f
				? new Color(0.80f, 0.60f, 0.05f)
				: new Color(0.80f, 0.15f, 0.12f);
	}

	// ── Entry point ───────────────────────────────────────────────────────

	/// <summary>
	/// Begin a combat encounter.
	/// <paramref name="enemies"/> is a list of (EnemyDef, desired grid cell) pairs.
	/// Player scouts are taken from PartyManager.
	/// Arena is generated from the party's current chunk (or debug arena if no ChunkManager).
	/// </summary>
	public void StartCombat(List<(EnemyDef def, Vector2I cell)> enemies, ChunkData? chunk = null)
	{
		_phase = CombatPhase.Setup;
		_units.Clear();
		_occupied.Clear();
		_queue  = new TurnQueue();
		_rng    = new Random(System.Environment.TickCount);

		// Build arena
		if (chunk != null)
		{
			var planet = ChunkGenerator.DeriveParams(PartyManager.Instance?.WorldSeed ?? 0);
			_grid.GenerateFrom(chunk, new Vector2I(ChunkData.Size / 2, ChunkData.Size / 2), planet);
		}
		else
		{
			_grid.GenerateDebug(_rng.Next());
		}

		if (_bgNode != null)
		{
			_bgNode.Biome = _grid.CurrentBiome;
			_bgNode.QueueRedraw();
		}

		// Spawn player scouts
		var party = PartyManager.Instance;
		var playerCells = DefaultPlayerCells();
		int pi = 0;

		if (party != null)
		{
			foreach (var scout in party.Party)
			{
				if (!party.IsAlive(scout)) continue;
				if (pi >= playerCells.Count) break;

				var cell = playerCells[pi++];
				var unit = CombatUnit.FromScout(scout, party.GetHP(scout), cell);
				SpawnUnit(unit, cell);
			}
		}

		// If no party exists (debug), spawn 1 scout for clean 1v1 testing
		if (_units.Count == 0)
		{
			var scout = ScoutGenerator.Generate(42);
			var unit  = CombatUnit.FromScout(scout, scout.MaxHP, playerCells[0]);
			SpawnUnit(unit, playerCells[0]);
		}

		// Spawn enemies (skip cells already occupied)
		int eSeed = 0;
		foreach (var (def, preferredCell) in enemies)
		{
			var cell = FindFreeCell(preferredCell);
			var unit = CombatUnit.FromEnemy(def, cell, eSeed++);
			SpawnUnit(unit, cell);
		}

		// Build turn order
		_queue.Build(_units, _rng);

		Log($"Combat begins! Round 1. {_units.Count(u => u.Side == CombatSide.Player)} scouts vs {_units.Count(u => u.Side == CombatSide.Enemy)} enemies.");

		// Start first turn
		AdvanceTurn();
	}

	// ── Unit spawning ─────────────────────────────────────────────────────

	private void SpawnUnit(CombatUnit unit, Vector2I cell)
	{
		_units.Add(unit);
		_occupied.Add(cell);
		_grid.AddChild(unit);
		SnapUnitToGrid(unit);
	}

	private void SnapUnitToGrid(CombatUnit unit)
	{
		unit.Position = _grid.GetCellVisualPos(unit.GridCell);
		// ZIndex = row*2+1 so units sit between RowNode[row] (Z=row*2) and RowNode[row+1] (Z=row*2+2)
		unit.ZIndex = unit.GridCell.Y * 2 + 1;
	}

	// ── Turn flow ─────────────────────────────────────────────────────────

	private void AdvanceTurn()
	{
		foreach (var u in _units) u.IsActive = false;

		if (_queue.IsEmpty)
		{
			EndCombat(CombatResult.Victory);
			return;
		}

		var entry = _queue.Advance();

		// Find the unit for this entry
		_activeUnit = _units.FirstOrDefault(u => u.UnitId == entry.UnitId && u.IsAlive);
		if (_activeUnit == null)
		{
			// Unit died between queue build and now — skip
			AdvanceTurn();
			return;
		}

		int burnDmg = _activeUnit.BeginTurn();
		if (burnDmg > 0)
			Log($"{_activeUnit.DisplayName} takes {burnDmg} burn damage.");

		// Check death from burn
		if (!_activeUnit.IsAlive)
		{
			HandleUnitDeath(_activeUnit);
			AdvanceTurn();
			return;
		}

		if (entry.Side == CombatSide.Player)
		{
			BeginPlayerTurn(_activeUnit);
		}
		else
		{
			BeginEnemyTurn(_activeUnit);
		}

		UpdateHUD();
	}

	// ── Player turn ───────────────────────────────────────────────────────

	private void BeginPlayerTurn(CombatUnit unit)
	{
		_phase        = CombatPhase.PlayerTurn;
		_selectedUnit = unit;
		_playerState  = PlayerState.SelectMove;
		unit.IsActive = true;

		// Show movement range
		ShowMoveHighlight(unit);
		Log($"Your turn: {unit.DisplayName}  [{unit.CurrentAP} AP]");
	}

	private void ShowMoveHighlight(CombatUnit unit)
	{
		_grid.MoveHighlight.Clear();
		_grid.AttackHighlight.Clear();

		if (unit.CurrentAP >= 1)
		{
			var reachable = _grid.GetReachable(unit.GridCell, unit.MoveRange);
			foreach (var cell in reachable)
				if (!_occupied.Contains(cell) || cell == unit.GridCell)
					_grid.MoveHighlight.Add(cell);
		}
		_grid.QueueRedraw();
	}

	private void ShowAttackHighlight(CombatUnit unit)
	{
		_grid.MoveHighlight.Clear();
		_grid.AttackHighlight.Clear();
		_grid.PreviewHighlight.Clear();
		_tooltip.Visible = false;

		var targets = _grid.GetAttackTargets(unit.GridCell, unit.Weapon);
		foreach (var cell in targets)
			_grid.AttackHighlight.Add(cell);

		_grid.SelectedCell = unit.GridCell;
		_grid.QueueRedraw();
	}

	public override void _Input(InputEvent @event)
	{
		// Global keys — work regardless of phase
		if (@event is InputEventKey globalKey && globalKey.Pressed && !globalKey.Echo)
		{
			if (globalKey.Keycode == Key.F3)
			{
				_debugVisible = !_debugVisible;
				_debugPanel.Visible = _debugVisible;
				return;
			}
			if (globalKey.Keycode == Key.R)
			{
				ResetDebugCombat();
				return;
			}
		}

		if (_phase != CombatPhase.PlayerTurn || _selectedUnit == null) return;

		// Track hover every mouse move
		if (@event is InputEventMouseMotion)
		{
			var worldPos  = GetGlobalMousePosition();
			var localPos  = _grid.ToLocal(worldPos);
			var hovered   = _grid.ScreenToGrid(localPos - _grid.GridOrigin());
			if (_grid.InBounds(hovered) && hovered != _grid.HoveredCell)
			{
				_grid.HoveredCell = hovered;
				UpdateHoverInfo(hovered);
				_grid.QueueRedraw();
			}
		}

		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			var worldPos = GetGlobalMousePosition();
			var localPos = _grid.ToLocal(worldPos);
			var origin   = _grid.GridOrigin();
			var cell     = _grid.ScreenToGrid(localPos - origin);

			HandlePlayerClick(cell);
		}

		if (@event is InputEventKey key && key.Pressed)
		{
			switch (key.Keycode)
			{
				case Key.A:
					// Switch to attack mode
					if (_selectedUnit.CurrentAP >= _selectedUnit.Weapon.ApCost)
					{
						_playerState = PlayerState.SelectAttack;
						ShowAttackHighlight(_selectedUnit);
						Log("Attack mode. Click enemy to shoot. [A] to cancel.");
					}
					break;

				case Key.O:
					// Overwatch
					if (_selectedUnit.CurrentAP >= 1)
					{
						ExecuteOverwatch(_selectedUnit);
					}
					break;

				case Key.Space:
				case Key.Enter:
					// End turn
					EndPlayerTurn();
					break;

				case Key.Escape:
					_playerState = PlayerState.SelectMove;
					ClearHoverInfo();
					ShowMoveHighlight(_selectedUnit);
					break;
			}
		}
	}

	private void HandlePlayerClick(Vector2I cell)
	{
		if (!_grid.InBounds(cell) || _selectedUnit == null) return;

		switch (_playerState)
		{
			case PlayerState.SelectMove:
				if (_grid.MoveHighlight.Contains(cell) && _selectedUnit.CurrentAP >= 1)
				{
					ExecuteMove(_selectedUnit, cell);
					ShowMoveHighlight(_selectedUnit);
				}
				break;

			case PlayerState.SelectAttack:
				if (_grid.AttackHighlight.Contains(cell))
				{
					var target = _units.FirstOrDefault(u => u.GridCell == cell && u.IsAlive);
					if (target != null)
					{
						ExecuteAttack(_selectedUnit, target);
						_playerState = PlayerState.SelectMove;
						ShowMoveHighlight(_selectedUnit);
					}
				}
				else
				{
					// Click outside attack range — cancel attack mode
					_playerState = PlayerState.SelectMove;
					ShowMoveHighlight(_selectedUnit);
				}
				break;
		}

		if (_selectedUnit.CurrentAP == 0)
			EndPlayerTurn();
	}

	private void UpdateHoverInfo(Vector2I cell)
	{
		if (_selectedUnit == null) { _tooltip.Visible = false; return; }

		var mousePos = GetViewport().GetMousePosition();

		switch (_playerState)
		{
			case PlayerState.SelectMove:
			{
				_grid.PreviewHighlight.Clear();
				if (_grid.MoveHighlight.Contains(cell))
				{
					var reachableCells = _grid.GetAttackTargets(cell, _selectedUnit.Weapon);
					int enemyCount = _units.Count(u => u.Side == CombatSide.Enemy && u.IsAlive
					                                   && reachableCells.Contains(u.GridCell));
					foreach (var t in reachableCells)
						if (!_grid.MoveHighlight.Contains(t) && t != cell)
							_grid.PreviewHighlight.Add(t);
					_tooltip.Text = enemyCount > 0
						? $"Move here  →  {enemyCount} enem{(enemyCount == 1 ? "y" : "ies")} in range"
						: "Move here  —  no enemies in range";
					_tooltip.Position = mousePos + new Vector2(18, -30);
					_tooltip.Visible  = true;
				}
				else
				{
					_tooltip.Visible = false;
				}
				break;
			}
			case PlayerState.SelectAttack:
			{
				var target = _units.FirstOrDefault(u => u.GridCell == cell && u.IsAlive && u.Side == CombatSide.Enemy);
				if (target != null && _grid.AttackHighlight.Contains(cell))
				{
					int   range     = CombatGrid.HexDist(_selectedUnit.GridCell, cell);
					var   cover     = _grid.GetCoverBetween(_selectedUnit.GridCell, target.GridCell);
					var   elevBonus = _grid.ElevationHitBonus(_selectedUnit.GridCell, target.GridCell);
					float hit       = _selectedUnit.CalcHitChance(target, _selectedUnit.Weapon, cover, elevBonus, range);
					string coverStr = cover == CoverType.None ? "" : $"  [{cover} cover]";
					_tooltip.Text     = $"Hit: {hit:P0}{coverStr}  r{range}  —  {target.DisplayName}  HP {target.CurrentHP}/{target.MaxHP}";
					_tooltip.Position = mousePos + new Vector2(18, -30);
					_tooltip.Visible  = true;
				}
				else
				{
					_tooltip.Visible = false;
				}
				break;
			}
			default:
				_tooltip.Visible = false;
				break;
		}
	}

	private void ClearHoverInfo()
	{
		_grid.PreviewHighlight.Clear();
		_grid.HoveredCell = null;
		_tooltip.Visible  = false;
		_grid.QueueRedraw();
	}

	private void EndPlayerTurn()
	{
		_grid.MoveHighlight.Clear();
		_grid.AttackHighlight.Clear();
		_grid.PreviewHighlight.Clear();
		_grid.SelectedCell = null;
		_tooltip.Visible   = false;
		_grid.QueueRedraw();
		AdvanceTurn();
	}

	// ── Enemy turn ────────────────────────────────────────────────────────

	private void BeginEnemyTurn(CombatUnit unit)
	{
		_phase        = CombatPhase.EnemyTurn;
		unit.IsActive = true;
		var players   = _units.Where(u => u.Side == CombatSide.Player && u.IsAlive).ToList();
		var actions = EnemyAI.Evaluate(unit, players, _grid, _rng);

		_pendingAiActions = new Queue<CombatAction>(actions);
		_aiActionTimer    = AiActionDelay;
		Log($"Enemy turn: {unit.DisplayName}");
	}

	public override void _Process(double delta)
	{
		if (_phase != CombatPhase.EnemyTurn || _activeUnit == null) return;
		if (_pendingAiActions.Count == 0) return;

		_aiActionTimer -= delta;
		if (_aiActionTimer > 0) return;
		_aiActionTimer = AiActionDelay;

		var action = _pendingAiActions.Dequeue();

		switch (action.Kind)
		{
			case ActionKind.Move when action.TargetCell.HasValue:
				ExecuteMove(_activeUnit, action.TargetCell.Value);
				break;

			case ActionKind.Attack when action.TargetCell.HasValue:
				var tgt = _units.FirstOrDefault(u => u.GridCell == action.TargetCell.Value && u.IsAlive);
				if (tgt != null) ExecuteAttack(_activeUnit, tgt);
				break;

			case ActionKind.Overwatch:
				ExecuteOverwatch(_activeUnit);
				break;

			case ActionKind.EndTurn:
				_pendingAiActions.Clear();
				AdvanceTurn();
				break;
		}

		UpdateHUD();
	}

	// ── Actions ───────────────────────────────────────────────────────────

	private void ExecuteMove(CombatUnit unit, Vector2I destination)
	{
		if (!unit.SpendAP(1)) return;

		int dist    = CombatGrid.HexDist(unit.GridCell, destination);
		int startRow = unit.GridCell.Y;

		_occupied.Remove(unit.GridCell);
		unit.GridCell = destination;
		_occupied.Add(destination);

		var targetPos = _grid.GetCellVisualPos(destination);

		unit.FaceDirection(unit.Position, targetPos);

		// Walk ≤2 tiles, Run ≥3; 0.42s/tile so movement is readable
		var moveAnim = dist <= 2 ? CombatUnit.AnimState.Walk : CombatUnit.AnimState.Run;
		float duration = Mathf.Clamp(dist * 0.42f, 0.42f, 1.40f);
		// During movement stay in front of all tiles in the path (fixes downward-move clipping)
		unit.ZIndex = Math.Max(startRow, destination.Y) * 2 + 1;
		// Sync animation frame rate to tween duration so footsteps match travel speed
		unit.SetMoveAnimation(moveAnim, duration);

		var tween = unit.CreateTween();
		tween.TweenProperty(unit, "position", targetPos, duration)
		     .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut);
		tween.TweenCallback(Callable.From(() =>
		{
			unit.ZIndex = destination.Y * 2 + 1;
			if (!unit.IsAlive) return;  // don't clobber death animation
			unit.AnimFrameTime = 0.16f;
			unit.SetAnimation(CombatUnit.AnimState.Idle);
		}));

		Log($"{unit.DisplayName} moves to ({destination.X},{destination.Y}).");
		CheckOverwatchReactions(unit);
		UpdateHUD();
	}

	private void ExecuteAttack(CombatUnit attacker, CombatUnit target)
	{
		if (!attacker.SpendAP(attacker.Weapon.ApCost)) return;

		int   range     = CombatGrid.HexDist(attacker.GridCell, target.GridCell);
		var   cover     = _grid.GetCoverBetween(attacker.GridCell, target.GridCell);
		var   elevBonus = _grid.ElevationHitBonus(attacker.GridCell, target.GridCell);
		float hitChance = attacker.CalcHitChance(target, attacker.Weapon, cover, elevBonus, range);
		bool  hit       = (float)_rng.NextDouble() < hitChance;

		// Attack animation + projectile FX
		if (attacker.Weapon.Category is WeaponCategory.Melee or WeaponCategory.Unarmed)
			attacker.PlayOnce(CombatUnit.AnimState.AttackMelee);
		else if (hitChance >= 0.50f)
			attacker.PlayOnce(CombatUnit.AnimState.Shot2);
		else
			attacker.PlayOnce(CombatUnit.AnimState.Shot1);

		// Spawn projectile travelling from attacker to target (always visible regardless of hit/miss)
		CombatProjectile.FireForWeapon(_grid, attacker.Position, target.Position, attacker.Weapon);

		if (hit)
		{
			int dmg = _rng.Next(attacker.Weapon.DamageMin, attacker.Weapon.DamageMax + 1);

			bool crit = (float)_rng.NextDouble() > 0.80f;
			if (crit) dmg = (int)(dmg * 1.5f);

			bool ko = target.TakeDamage(dmg);
			Log($"{attacker.DisplayName} HITS {target.DisplayName} for {dmg}{(crit ? " (CRIT!)" : "")} dmg.{(ko ? " KO'd!" : "")}");

			// Hurt animation; Dead is triggered in HandleUnitDeath (covers all death paths)
			if (!ko)
				target.PlayOnce(CombatUnit.AnimState.Hurt);

			// Burning on alien spit
			if (attacker.Weapon.Id == "alien_spit" && _rng.NextDouble() < 0.30)
			{
				target.ApplyStatus(StatusEffect.Burning);
				Log($"{target.DisplayName} is Burning!");
			}
			// Stun on thorax slam
			if (attacker.Weapon.Id == "alien_thorax" && _rng.NextDouble() < 0.25)
			{
				target.ApplyStatus(StatusEffect.Stunned);
				Log($"{target.DisplayName} is Stunned!");
			}

			if (ko) HandleUnitDeath(target);
		}
		else
		{
			Log($"{attacker.DisplayName} misses {target.DisplayName}. ({hitChance:P0} hit, {cover} cover)");

			// Near miss: suppress target
			if (_rng.NextDouble() < 0.40)
			{
				target.ApplyStatus(StatusEffect.Suppressed);
				Log($"{target.DisplayName} is Suppressed!");
			}
		}

		UpdateHUD();
	}

	private void ExecuteOverwatch(CombatUnit unit)
	{
		if (!unit.SpendAP(1)) return;
		unit.ApplyStatus(StatusEffect.Overwatch);
		Log($"{unit.DisplayName} goes on Overwatch.");
	}

	private void CheckOverwatchReactions(CombatUnit movedUnit)
	{
		foreach (var watcher in _units.Where(u => u.HasOverwatch && u.Side != movedUnit.Side && u.IsAlive))
		{
			if (!_grid.HasLoS(watcher.GridCell, movedUnit.GridCell)) continue;
			int dist = CombatGrid.HexDist(watcher.GridCell, movedUnit.GridCell);
			if (dist < watcher.Weapon.RangeMin || dist > watcher.Weapon.RangeMax) continue;

			Log($"{watcher.DisplayName} reacts with Overwatch fire!");
			watcher.ClearStatus(StatusEffect.Overwatch);
			// Temporarily grant AP for the reaction shot
			watcher.CurrentAP++;
			ExecuteAttack(watcher, movedUnit);
		}
	}

	// ── Unit death ────────────────────────────────────────────────────────

	private void HandleUnitDeath(CombatUnit unit)
	{
		_queue.Remove(unit.UnitId);
		_occupied.Remove(unit.GridCell);
		// Ensure Dead animation plays (covers burn-death path where ExecuteAttack didn't fire)
		unit.PlayOnce(CombatUnit.AnimState.Dead, CombatUnit.AnimState.Dead);
		// Keep ZIndex at current grid position — setting -1 would render body UNDER the tile polygons
		// Living units at higher rows get higher ZIndex naturally, so they render on top of bodies

		if (unit.Side == CombatSide.Player)
		{
			// Write damage back to PartyManager
			var party = PartyManager.Instance;
			if (party != null)
			{
				foreach (var scout in party.Party)
				{
					if (scout.Id == unit.UnitId)
					{
						party.ApplyDamage(scout, unit.MaxHP); // ensure KO recorded
						break;
					}
				}
			}
		}

		CheckEndConditions();
	}

	private void CheckEndConditions()
	{
		bool playersAlive = _units.Any(u => u.Side == CombatSide.Player  && u.IsAlive);
		bool enemiesAlive  = _units.Any(u => u.Side == CombatSide.Enemy   && u.IsAlive);

		if (!enemiesAlive)  { EndCombat(CombatResult.Victory); return; }
		if (!playersAlive)  { EndCombat(CombatResult.Defeat);  return; }
	}

	private void EndCombat(CombatResult result)
	{
		_phase = result == CombatResult.Victory ? CombatPhase.Victory : CombatPhase.Defeat;
		_grid.MoveHighlight.Clear();
		_grid.AttackHighlight.Clear();
		_grid.SelectedCell = null;
		_grid.QueueRedraw();

		Log(result == CombatResult.Victory
			? "VICTORY! All enemies eliminated."
			: "DEFEAT. The squad is wiped out.");

		if (result == CombatResult.Victory)
			PartyManager.Instance?.PostCombatRevive();

		UpdateHUD();
		EmitSignal(SignalName.CombatFinished, (int)result);
	}

	// ── HUD ───────────────────────────────────────────────────────────────

	private void UpdateHUD()
	{
		_phaseLabel.Text = _phase switch
		{
			CombatPhase.PlayerTurn => $"Round {_queue.RoundNumber}  —  YOUR TURN",
			CombatPhase.EnemyTurn  => $"Round {_queue.RoundNumber}  —  ENEMY TURN",
			CombatPhase.Victory    => "VICTORY",
			CombatPhase.Defeat     => "DEFEAT",
			_                      => $"Round {_queue.RoundNumber}",
		};

		var orderLines = _queue.Order
			.Take(8)
			.Select(e =>
			{
				var u    = _units.FirstOrDefault(u => u.UnitId == e.UnitId);
				string tag  = u?.Side == CombatSide.Player ? "[P]" : "[E]";
				string name = u?.DisplayName.Split(' ')[0] ?? e.UnitId;
				return $"{tag} {name}";
			});
		_turnOrderLabel.Text = string.Join("  ›  ", orderLines);

		UpdateBottomBar(_activeUnit);
	}

	private readonly List<string> _logLines = new();
	private void Log(string message)
	{
		_logLines.Add(message);
		if (_logLines.Count > 6) _logLines.RemoveAt(0);
		_logLabel.Text = string.Join("\n", _logLines);
		GD.Print($"[Combat] {message}");
	}

	// ── Helpers ───────────────────────────────────────────────────────────

	/// <summary>Default player spawn cells — left side of 11×11 hex grid.</summary>
	private static List<Vector2I> DefaultPlayerCells() => new()
	{
		new Vector2I(1, 3),
		new Vector2I(1, 5),
		new Vector2I(1, 7),
		new Vector2I(2, 4),
		new Vector2I(2, 6),
	};

	private Vector2I FindFreeCell(Vector2I preferred)
	{
		if (!_occupied.Contains(preferred) && _grid.InBounds(preferred))
			return preferred;

		// Spiral outward to find a free cell
		for (int radius = 1; radius < 4; radius++)
			for (int dx = -radius; dx <= radius; dx++)
				for (int dy = -radius; dy <= radius; dy++)
				{
					var cell = preferred + new Vector2I(dx, dy);
					if (_grid.InBounds(cell) && !_occupied.Contains(cell) &&
					    _grid.GetTile(cell).Passability != TilePassability.Impassable)
						return cell;
				}

		return preferred; // fallback
	}

	// ── Atmospheric background ─────────────────────────────────────────────────

	/// <summary>
	/// Draws a biome-tinted sky gradient and a procedural horizon silhouette behind the combat grid.
	/// Lives in CanvasLayer -1 so it renders beneath everything.
	/// </summary>
	private sealed partial class CombatBgNode : Node2D
	{
		public BiomeType Biome { get; set; } = BiomeType.Grassland;

		public override void _Draw()
		{
			var vp   = GetViewport().GetVisibleRect();
			float vw = vp.Size.X, vh = vp.Size.Y;

			// Sky colours derived from biome
			var (skyTop, skyHorizon, groundDark) = BiomePalette(Biome);

			// Full screen sky gradient (top → horizon)
			float horizY = vh * 0.26f;
			DrawPolygon(
				new[] { new Vector2(0,0), new Vector2(vw,0), new Vector2(vw,horizY), new Vector2(0,horizY) },
				new[] { skyTop, skyTop, skyHorizon, skyHorizon });

			// Ground fill below horizon
			DrawRect(new Rect2(0, horizY, vw, vh - horizY), groundDark);

			// Horizon silhouette — wavy landform profile
			var silouette = new System.Collections.Generic.List<Vector2>();
			silouette.Add(new Vector2(0, vh));
			float amp1 = SilhouetteAmp(Biome, 0), amp2 = SilhouetteAmp(Biome, 1);
			float frq1 = SilhouetteFreq(Biome, 0), frq2 = SilhouetteFreq(Biome, 1);
			for (int x = 0; x <= (int)vw; x += 12)
			{
				float peak = MathF.Sin(x * frq1) * amp1 + MathF.Sin(x * frq2 + 1.3f) * amp2;
				silouette.Add(new Vector2(x, horizY + 20f - peak));
			}
			silouette.Add(new Vector2(vw, vh));
			var silPts   = silouette.ToArray();
			var silColor = new Color(groundDark.R * 0.5f, groundDark.G * 0.5f, groundDark.B * 0.5f, 1f);
			DrawPolygon(silPts, System.Linq.Enumerable.Repeat(silColor, silPts.Length).ToArray());

			// Subtle ground-to-bottom fog gradient
			DrawPolygon(
				new[] { new Vector2(0, horizY + 40f), new Vector2(vw, horizY + 40f),
				        new Vector2(vw, vh),           new Vector2(0, vh) },
				new[] { new Color(0,0,0,0), new Color(0,0,0,0),
				        new Color(0,0,0,0.55f), new Color(0,0,0,0.55f) });
		}

		private static (Color skyTop, Color skyHorizon, Color groundDark) BiomePalette(BiomeType b) => b switch
		{
			BiomeType.Desert    => (new Color(0.06f,0.05f,0.10f), new Color(0.28f,0.15f,0.06f), new Color(0.18f,0.10f,0.04f)),
			BiomeType.Savanna   => (new Color(0.05f,0.05f,0.08f), new Color(0.22f,0.14f,0.05f), new Color(0.14f,0.09f,0.03f)),
			BiomeType.Jungle    => (new Color(0.04f,0.08f,0.04f), new Color(0.08f,0.22f,0.06f), new Color(0.04f,0.14f,0.04f)),
			BiomeType.Forest    => (new Color(0.04f,0.07f,0.04f), new Color(0.08f,0.18f,0.06f), new Color(0.04f,0.12f,0.03f)),
			BiomeType.Mountain  => (new Color(0.05f,0.06f,0.10f), new Color(0.12f,0.14f,0.22f), new Color(0.08f,0.09f,0.15f)),
			BiomeType.Arctic    => (new Color(0.08f,0.10f,0.18f), new Color(0.18f,0.22f,0.32f), new Color(0.12f,0.14f,0.22f)),
			BiomeType.AlienWilds=> (new Color(0.06f,0.03f,0.10f), new Color(0.18f,0.06f,0.28f), new Color(0.10f,0.03f,0.18f)),
			BiomeType.Coastal   => (new Color(0.04f,0.06f,0.12f), new Color(0.06f,0.14f,0.28f), new Color(0.04f,0.10f,0.20f)),
			_                   => (new Color(0.04f,0.06f,0.08f), new Color(0.10f,0.18f,0.08f), new Color(0.06f,0.12f,0.05f)),
		};

		private static float SilhouetteAmp(BiomeType b, int layer) => b switch
		{
			BiomeType.Mountain  => layer == 0 ? 70f  : 45f,
			BiomeType.Forest    => layer == 0 ? 40f  : 25f,
			BiomeType.Jungle    => layer == 0 ? 50f  : 30f,
			BiomeType.Desert    => layer == 0 ? 18f  : 10f,
			BiomeType.Arctic    => layer == 0 ? 12f  : 8f,
			BiomeType.AlienWilds=> layer == 0 ? 55f  : 35f,
			_                   => layer == 0 ? 28f  : 16f,
		};

		private static float SilhouetteFreq(BiomeType b, int layer) => b switch
		{
			BiomeType.Mountain  => layer == 0 ? 0.005f : 0.012f,
			BiomeType.Forest    => layer == 0 ? 0.014f : 0.031f,
			BiomeType.Desert    => layer == 0 ? 0.006f : 0.018f,
			BiomeType.AlienWilds=> layer == 0 ? 0.009f : 0.022f,
			_                   => layer == 0 ? 0.010f : 0.025f,
		};
	}

	// ── Vignette / frame overlay ──────────────────────────────────────────────

	/// <summary>
	/// Dark gradient strips at each screen edge to frame the battlefield.
	/// Lives in CanvasLayer 2, above the grid but below the HUD.
	/// </summary>
	private sealed partial class CombatVignetteNode : Node2D
	{
		private const float Strength = 180f;  // px wide

		public override void _Draw()
		{
			var vp   = GetViewport().GetVisibleRect();
			float vw = vp.Size.X, vh = vp.Size.Y;
			var black0 = new Color(0, 0, 0, 0);
			var black7 = new Color(0, 0, 0, 0.72f);

			// Top strip
			DrawPolygon(new[] { new Vector2(0,0), new Vector2(vw,0),
			                    new Vector2(vw,Strength), new Vector2(0,Strength) },
			            new[] { black7, black7, black0, black0 });
			// Bottom strip
			DrawPolygon(new[] { new Vector2(0,vh-Strength), new Vector2(vw,vh-Strength),
			                    new Vector2(vw,vh), new Vector2(0,vh) },
			            new[] { black0, black0, black7, black7 });
			// Left strip
			DrawPolygon(new[] { new Vector2(0,0), new Vector2(Strength,0),
			                    new Vector2(Strength,vh), new Vector2(0,vh) },
			            new[] { black7, black0, black0, black7 });
			// Right strip
			DrawPolygon(new[] { new Vector2(vw-Strength,0), new Vector2(vw,0),
			                    new Vector2(vw,vh), new Vector2(vw-Strength,vh) },
			            new[] { black0, black7, black7, black0 });
		}
	}
}
