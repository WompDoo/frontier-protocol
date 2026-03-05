using Godot;

/// <summary>
/// Scout generation viewer. Toggle with Tab.
/// Shows a procedurally generated scout card — animated portrait, name,
/// background, stats, HP, traits (with clash detection if 2 traits known).
/// Press Reroll or R to generate a new scout from a fresh seed.
///
/// Layout:
///   Left column  — animated Idle portrait (Soldier 1/2/3 chosen from seed)
///   Right column — archetype, name, background, hook, HP, stats
///   Full width   — traits block, hint bar
/// </summary>
public partial class ScoutView : CanvasLayer
{
	[Export] public NodePath ChunkManagerPath { get; set; }

	private ChunkManager _chunkManager;

	// ── State ─────────────────────────────────────────────────────────────

	private Scout _scout;
	private int   _rollSeed;

	// ── UI nodes ──────────────────────────────────────────────────────────

	private Panel            _card;
	private AnimatedSprite2D _portrait;
	private Label            _nameLabel;
	private Label            _archetypeLabel;
	private Label            _backgroundLabel;
	private Label            _hookLabel;
	private Label            _statsLabel;
	private Label            _hpLabel;
	private Label            _traitLabel;
	private Label            _hintLabel;
	private Button           _rerollBtn;

	// ── Layout constants ───────────────────────────────────────────────────

	private const int CardW  = 660;
	private const int CardH  = 560;
	private const int PortX  = 20;
	private const int PortY  = 50;  // relative to card top (below header)
	private const int PortW  = 158;
	private const int PortH  = 200;
	private const int TextX  = 196; // left edge of right text column

	// ── Colours ───────────────────────────────────────────────────────────

	private static readonly Color ColBackground = new(0.07f, 0.08f, 0.12f, 0.94f);
	private static readonly Color ColBorder     = new(0.25f, 0.30f, 0.40f, 1.00f);
	private static readonly Color ColPortBg     = new(0.04f, 0.05f, 0.08f, 1.00f);
	private static readonly Color ColTitle      = new(0.95f, 0.90f, 0.75f, 1.00f);
	private static readonly Color ColSubtle     = new(0.55f, 0.58f, 0.65f, 1.00f);
	private static readonly Color ColStat       = new(0.70f, 0.85f, 1.00f, 1.00f);
	private static readonly Color ColHp         = new(0.95f, 0.40f, 0.40f, 1.00f);
	private static readonly Color ColArchetype  = new(0.80f, 0.60f, 1.00f, 1.00f);

	public override void _Ready()
	{
		_chunkManager = GetNode<ChunkManager>(ChunkManagerPath);

		Layer   = 7;
		Visible = false;

		BuildUI();
		_rollSeed = (int)Time.GetTicksMsec();
		RollScout();
	}

	// ── Input ─────────────────────────────────────────────────────────────

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Tab)
			{
				Visible = !Visible;
				GetViewport().SetInputAsHandled();
			}
			else if (key.Keycode == Key.R && Visible)
			{
				RollScout();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	// ── Scout generation ──────────────────────────────────────────────────

	private void RollScout()
	{
		_rollSeed = unchecked(_rollSeed * 1664525 + 1013904223); // LCG shuffle
		_scout    = ScoutGenerator.Generate(_rollSeed);

		int variant = (System.Math.Abs(_rollSeed) % 3) + 1; // 1, 2, or 3
		SetPortraitSprite(variant);
		RefreshCard();
	}

	// ── UI builder ────────────────────────────────────────────────────────

	private void BuildUI()
	{
		var   vp    = GetViewport().GetVisibleRect().Size;
		float cardX = (vp.X - CardW) / 2f;
		float cardY = (vp.Y - CardH) / 2f;

		// Dim overlay
		var overlay = new ColorRect { Size = vp, Color = new Color(0f, 0f, 0f, 0.55f) };
		AddChild(overlay);

		// Card panel
		_card          = new Panel();
		_card.Position = new Vector2(cardX, cardY);
		_card.Size     = new Vector2(CardW, CardH);

		var style = new StyleBoxFlat
		{
			BgColor                = ColBackground,
			BorderColor            = ColBorder,
			BorderWidthLeft        = 2, BorderWidthRight   = 2,
			BorderWidthTop         = 2, BorderWidthBottom  = 2,
			CornerRadiusTopLeft    = 6, CornerRadiusTopRight    = 6,
			CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
		};
		_card.AddThemeStyleboxOverride("panel", style);
		AddChild(_card);

		// ── Header ────────────────────────────────────────────────────────
		var header = MakeLabel("◆  FRONTIER PROTOCOL  —  SCOUT DOSSIER  ◆", 20, 22, 12, ColSubtle);
		header.Size                = new Vector2(CardW - 40, 20);
		header.HorizontalAlignment = HorizontalAlignment.Center;
		_card.AddChild(header);

		// ── Portrait panel (dark bg + border) ─────────────────────────────
		_card.AddChild(new ColorRect
		{
			Position = new Vector2(PortX, PortY),
			Size     = new Vector2(PortW, PortH),
			Color    = ColPortBg,
		});

		var portBorderStyle = new StyleBoxFlat
		{
			BgColor            = Colors.Transparent,
			BorderColor        = ColBorder,
			BorderWidthLeft    = 1, BorderWidthRight  = 1,
			BorderWidthTop     = 1, BorderWidthBottom = 1,
		};
		var portBorder = new Panel { Position = new Vector2(PortX, PortY), Size = new Vector2(PortW, PortH) };
		portBorder.AddThemeStyleboxOverride("panel", portBorderStyle);
		_card.AddChild(portBorder);

		// AnimatedSprite2D lives in CanvasLayer space (not inside Panel), centred on portrait box
		_portrait          = new AnimatedSprite2D();
		_portrait.Position = new Vector2(cardX + PortX + PortW / 2f,
		                                 cardY + PortY + PortH / 2f + 16f);
		_portrait.Scale    = new Vector2(1.35f, 1.35f);
		AddChild(_portrait);

		// ── Right text column (aligned with portrait top) ──────────────────
		int tpy = PortY;
		int tw  = CardW - TextX - 20;

		_archetypeLabel = MakeLabel("", TextX, tpy, 11, ColArchetype);
		_card.AddChild(_archetypeLabel);
		tpy += 20;

		_nameLabel = MakeLabel("", TextX, tpy, 20, ColTitle);
		_card.AddChild(_nameLabel);
		tpy += 32;

		_backgroundLabel = MakeLabel("", TextX, tpy, 12, ColSubtle);
		_card.AddChild(_backgroundLabel);
		tpy += 18;

		_hookLabel = MakeLabel("", TextX, tpy, 11, new Color(0.75f, 0.75f, 0.75f));
		_hookLabel.Size         = new Vector2(tw, 66);
		_hookLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		_card.AddChild(_hookLabel);
		tpy += 72;

		AddDivider(tpy, TextX, tw); tpy += 14;

		_hpLabel = MakeLabel("", TextX, tpy, 14, ColHp);
		_card.AddChild(_hpLabel);
		tpy += 24;

		_statsLabel = MakeLabel("", TextX, tpy, 12, ColStat);
		_card.AddChild(_statsLabel);

		// ── Full-width traits section — below portrait ─────────────────────
		int fullY = PortY + PortH + 12;

		AddDivider(fullY); fullY += 14;

		_card.AddChild(MakeLabel("TRAITS", 24, fullY, 11, ColSubtle));
		fullY += 18;

		_traitLabel             = MakeLabel("", 24, fullY, 12, Colors.White);
		_traitLabel.Size        = new Vector2(CardW - 48, 188);
		_traitLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		_card.AddChild(_traitLabel);

		// ── Hint + Reroll ──────────────────────────────────────────────────
		_hintLabel = MakeLabel("Tab = close   R = reroll", 24, CardH - 38, 11, ColSubtle);
		_hintLabel.Size                = new Vector2(CardW - 48, 20);
		_hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_card.AddChild(_hintLabel);

		_rerollBtn = new Button
		{
			Text              = "Reroll Scout",
			Position          = new Vector2(CardW - 140, CardH - 44),
			CustomMinimumSize = new Vector2(120, 28),
		};
		_rerollBtn.Pressed += RollScout;
		_card.AddChild(_rerollBtn);
	}

	// ── Portrait animation ─────────────────────────────────────────────────

	private void SetPortraitSprite(int variant)
	{
		if (_portrait is null) return;

		string path = $"res://assets/sprites/scouts/Soldier_{variant}/Idle.png";
		var    tex  = ResourceLoader.Load<Texture2D>(path);
		if (tex is null) return;

		const int FrameH  = 128;
		const int FrameW  = 128;
		int frameCount    = tex.GetWidth() / FrameW;

		var frames = new SpriteFrames();
		frames.AddAnimation("idle");
		frames.SetAnimationSpeed("idle", 8f);
		frames.SetAnimationLoop("idle", true);

		for (int i = 0; i < frameCount; i++)
			frames.AddFrame("idle", new AtlasTexture
			{
				Atlas  = tex,
				Region = new Rect2(i * FrameW, 0, FrameW, FrameH),
			});

		_portrait.SpriteFrames = frames;
		_portrait.Animation    = "idle";
		_portrait.Play();
	}

	// ── Card refresh ──────────────────────────────────────────────────────

	private void RefreshCard()
	{
		if (_scout is null) return;

		var s = _scout;

		_archetypeLabel.Text  = s.Archetype.ToUpper();
		_nameLabel.Text       = s.FullName;
		_backgroundLabel.Text = $"{s.Background.Name}  ·  {s.Sponsor}";
		_hookLabel.Text       = $"\"{s.NarrativeHook}\"";
		_hpLabel.Text         = $"HP  {s.MaxHP}";

		_statsLabel.Text =
			$"END {s.BaseStats.END:D2}   " +
			$"PRC {s.BaseStats.PRC:D2}   " +
			$"ING {s.BaseStats.ING:D2}   " +
			$"AWR {s.BaseStats.AWR:D2}   " +
			$"RES {s.BaseStats.RES:D2}   " +
			$"STR {s.BaseStats.STR:D2}";

		if (s.Traits.Count == 0)
		{
			_traitLabel.AddThemeColorOverride("font_color", ColSubtle);
			_traitLabel.Text = "No traits.";
		}
		else
		{
			var lines = new System.Text.StringBuilder();
			foreach (var t in s.Traits)
			{
				if (t.Type == TraitType.Positive)
					lines.AppendLine($"✦  {t.Name}\n     {t.Description}");
				else
					lines.AppendLine($"⚡  {t.Name}\n     ↑ {t.Pro}\n     ↓ {t.Con}");
				lines.AppendLine();
			}
			if (!string.IsNullOrEmpty(s.Background.MechanicalLean))
				lines.Append($"◌  Background lean: {s.Background.MechanicalLean}");

			_traitLabel.Text = lines.ToString().TrimEnd();
			_traitLabel.AddThemeColorOverride("font_color", Colors.White);
		}
	}

	// ── Helpers ───────────────────────────────────────────────────────────

	private static Label MakeLabel(string text, float x, float y, int size, Color color)
	{
		var lbl = new Label { Text = text, Position = new Vector2(x, y) };
		lbl.AddThemeFontSizeOverride("font_size", size);
		lbl.AddThemeColorOverride("font_color", color);
		lbl.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
		lbl.AddThemeConstantOverride("shadow_offset_x", 1);
		lbl.AddThemeConstantOverride("shadow_offset_y", 1);
		return lbl;
	}

	// Full-width card divider
	private void AddDivider(float y) => AddDivider(y, 20f, CardW - 40f);

	// Partial-width divider
	private void AddDivider(float y, float x, float w)
	{
		_card.AddChild(new ColorRect
		{
			Position = new Vector2(x, y),
			Size     = new Vector2(w, 1),
			Color    = ColBorder,
		});
	}
}
