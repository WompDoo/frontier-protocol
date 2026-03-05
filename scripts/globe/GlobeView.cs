using Godot;
using System.Collections.Generic;

/// <summary>
/// Renders the planet as a 3D sphere in a transparent SubViewport.
/// Globe is always visible. Scroll=zoom, drag=spin, F1=grid overlay.
///
/// Grid is rendered on a separate MeshInstance3D sphere (not baked into terrain texture).
/// F1: toggle grid sphere. Click a grid cell (while F1 is active) to open a region survey panel.
/// </summary>
public partial class GlobeView : Node2D
{
	[Export] public NodePath ChunkManagerPath { get; set; }
	[Export] public NodePath PlayerCameraPath  { get; set; }

	private const int TexW = 2048;
	private const int TexH = 1024;

	private const int GridDivLon  = 24;   // 15° per cell east-west
	private const int GridDivLat  = 12;   // 15° per cell north-south
	private const int MaxBiomeIdx = 16;

	private SubViewport    _subViewport;
	private TextureRect    _display;
	private MeshInstance3D _sphere;
	private MeshInstance3D _cloudSphere;        // outer cloud layer  (r=1.10) — storms + cyclones
	private MeshInstance3D _cloudSphereInner;   // inner cloud layer  (r=1.06) — base stratocumulus
	private MeshInstance3D _gridSphere;   // separate layer — not baked into terrain
	private Node3D         _globeRoot;
	private StarField      _starField;
	private Camera3D       _camera3D;

	private ChunkManager _chunkManager;
	private Camera2D     _playerCamera;

#nullable enable
	private Image? _grassPal;
	private Image? _shorePal;
	private Image? _winterPal;

	private const float AutoRotSpeed  = 0.05f;
	private const float CloudDriftRel = 0.025f;
	private const float GlobeZoomMin  = 1.4f;
	private const float GlobeZoomMax  = 3.5f;
	private const float GlobeZoomStep = 0.18f;

	private float _dispScale  = 0.14f;
	private float _waterDepth = 0.85f;
	private float _satBoost   = 1.05f;
	private CanvasLayer? _sliderLayer;

	private bool  _isDragging    = false;
	private bool  _clickWasDrag  = false;
	private Vector2 _clickStart  = Vector2.Zero;
	private bool  _debugGrid     = false;
	private int   _lastSeed      = -1;
	private float _rotVelocity   = AutoRotSpeed;
	private float _dragVelocity  = 0f;

	// ── Cell selection / snap-to ─────────────────────────────────────────────
	private int   _selCellGi    = -1;
	private int   _selCellGj    = -1;
	private bool  _snapToTarget = false;
	private float _targetYRot   = 0f;
	private float _zoomTarget   = GlobeZoomMax;

	// ── Grid cell data ─────────────────────────────────────────────────────────

	public record struct GridCellData(
		BiomeType DominantBiome,
		float     AvgElevation,
		float     AvgMoisture,
		float     AvgTemp,
		bool      HasOcean,
		bool      HasMountains
	);

	private GridCellData[,] _gridData = new GridCellData[GridDivLon, GridDivLat];

	public GridCellData? GetGridCell(int lonIdx, int latIdx)
	{
		if (lonIdx < 0 || lonIdx >= GridDivLon || latIdx < 0 || latIdx >= GridDivLat) return null;
		return _gridData[lonIdx, latIdx];
	}

	// ── Cell info panel ────────────────────────────────────────────────────────

	private CanvasLayer? _cellLayer;
	private Panel?       _cellCard;
	private Label?       _cellTitle;
	private Label?       _cellBiome;
	private Label?       _cellElev;
	private Label?       _cellHumid;
	private Label?       _cellTemp;
	private Label?       _cellTerrain;
	private Label?       _cellFauna;
	private Label?       _cellHazard;

	private static readonly Color CellBg      = new(0.07f, 0.08f, 0.12f, 0.94f);
	private static readonly Color CellBorder  = new(0.25f, 0.30f, 0.40f, 1.00f);
	private static readonly Color CellTitle   = new(0.95f, 0.90f, 0.75f, 1.00f);
	private static readonly Color CellSubtle  = new(0.55f, 0.58f, 0.65f, 1.00f);
	private static readonly Color CellStat    = new(0.70f, 0.85f, 1.00f, 1.00f);
	private static readonly Color CellDanger  = new(0.95f, 0.40f, 0.40f, 1.00f);
	private static readonly Color CellGreen   = new(0.55f, 0.90f, 0.55f, 1.00f);

	// ── Lifecycle ─────────────────────────────────────────────────────────────

	public override void _Ready()
	{
		_chunkManager = GetNode<ChunkManager>(ChunkManagerPath);
		_playerCamera = GetNode<Camera2D>(PlayerCameraPath);

		BuildScene();
		BuildCellPanel();
		LoadPalettes();
		BuildDebugSliders();
		GenerateGlobeTexture();
		_lastSeed = _chunkManager.WorldSeed;

		Visible                             = true;
		_subViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		_chunkManager.Visible               = false;
	}

	// ── Scene construction ────────────────────────────────────────────────────

	private void BuildScene()
	{
		var vpSize = GetViewport().GetVisibleRect().Size;

		_subViewport = new SubViewport
		{
			Size                   = new Vector2I((int)vpSize.X, (int)vpSize.Y),
			TransparentBg          = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		AddChild(_subViewport);

		_camera3D = new Camera3D { Position = new Vector3(0f, 0f, 2.6f) };
		_subViewport.AddChild(_camera3D);

		var env = new Environment();
		env.BackgroundMode     = Godot.Environment.BGMode.Color;
		env.BackgroundColor    = new Color(0f, 0f, 0f, 0f);
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		env.AmbientLightColor  = new Color(0.06f, 0.07f, 0.10f);
		env.AmbientLightEnergy = 0.6f;
		env.GlowEnabled        = true;
		env.GlowIntensity      = 0.8f;
		env.GlowBloom          = 0.12f;
		env.GlowStrength       = 1.4f;
		_subViewport.AddChild(new WorldEnvironment { Environment = env });

		_subViewport.AddChild(new DirectionalLight3D
		{
			LightEnergy     = 1.8f,
			LightColor      = new Color(1.0f, 0.96f, 0.88f),
			RotationDegrees = new Vector3(-35f, -45f, 0f),
		});

		_globeRoot = new Node3D();
		_subViewport.AddChild(_globeRoot);

		_sphere      = new MeshInstance3D();
		_sphere.Mesh = new SphereMesh { Radius = 1.0f, Height = 2.0f, RadialSegments = 512, Rings = 256 };
		_sphere.SetSurfaceOverrideMaterial(0, new ShaderMaterial { Shader = new Shader { Code = PlanetShaderCode } });
		_globeRoot.AddChild(_sphere);

		// Atmosphere halo — subtler than before (avoid the thick opaque ring)
		var atmSphere = new MeshInstance3D();
		atmSphere.Mesh = new SphereMesh { Radius = 1.25f, Height = 2.50f, RadialSegments = 64, Rings = 32 };
		atmSphere.SetSurfaceOverrideMaterial(0, new ShaderMaterial { Shader = new Shader { Code = AtmosphereShaderCode } });
		_globeRoot.AddChild(atmSphere);

		// Outer cloud shell — cyclone storms + bright tops
		_cloudSphere      = new MeshInstance3D();
		_cloudSphere.Mesh = new SphereMesh { Radius = 1.10f, Height = 2.20f, RadialSegments = 64, Rings = 32 };
		_cloudSphere.SetSurfaceOverrideMaterial(0, new ShaderMaterial { Shader = new Shader { Code = CloudShaderCode } });
		_globeRoot.AddChild(_cloudSphere);

		// Inner cloud shell — base stratocumulus layer, denser / slightly darker undersides
		_cloudSphereInner      = new MeshInstance3D();
		_cloudSphereInner.Mesh = new SphereMesh { Radius = 1.06f, Height = 2.12f, RadialSegments = 64, Rings = 32 };
		_cloudSphereInner.SetSurfaceOverrideMaterial(0, new ShaderMaterial { Shader = new Shader { Code = CloudInnerShaderCode } });
		_globeRoot.AddChild(_cloudSphereInner);

		// Grid sphere — additive overlay, separate from terrain, toggled by F1
		_gridSphere         = new MeshInstance3D();
		_gridSphere.Mesh    = new SphereMesh { Radius = 1.005f, Height = 2.01f, RadialSegments = 256, Rings = 128 };
		_gridSphere.Visible = false;
		_gridSphere.SetSurfaceOverrideMaterial(0, new ShaderMaterial { Shader = new Shader { Code = GridShaderCode } });
		_globeRoot.AddChild(_gridSphere);

		_starField = new StarField();
		_starField.Init();
		AddChild(_starField);

		_display = new TextureRect
		{
			Position    = Vector2.Zero,
			Size        = vpSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			Texture     = _subViewport.GetTexture(),
		};
		AddChild(_display);
	}

	// ── Grid texture (UV lines only, seed-independent) ─────────────────────────



	// ── Input ─────────────────────────────────────────────────────────────────

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.F1)
			{
				_debugGrid          = !_debugGrid;
				_gridSphere.Visible = _debugGrid;
				if (!_debugGrid) HideCellPanel();
			}
			if (key.Keycode == Key.F3 && _sliderLayer is not null)
				_sliderLayer.Visible = !_sliderLayer.Visible;
			if (key.Keycode == Key.Escape)
				HideCellPanel();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
				AdjustGlobeZoom(-GlobeZoomStep);
			else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
				AdjustGlobeZoom(+GlobeZoomStep);
			else if (mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed)
				{
					_isDragging   = true;
					_clickWasDrag = false;
					_clickStart   = mb.Position;
				}
				else
				{
					_isDragging = false;
					if (!_clickWasDrag && _debugGrid)
						TryClickGridCell(mb.Position);
					_clickWasDrag = false;
				}
			}
		}
		else if (@event is InputEventMouseMotion mm && _isDragging)
		{
			_dragVelocity += mm.Relative.X * 0.008f;
			if (mm.Position.DistanceTo(_clickStart) > 6f)
				_clickWasDrag = true;
		}
	}

	private void AdjustGlobeZoom(float delta)
	{
		if (_camera3D is null) return;
		float z = Mathf.Clamp(_camera3D.Position.Z + delta, GlobeZoomMin, GlobeZoomMax);
		_camera3D.Position = new Vector3(0f, 0f, z);
	}

	// ── Update ────────────────────────────────────────────────────────────────

	public override void _Process(double delta)
	{
		if (_chunkManager.WorldSeed != _lastSeed)
		{
			_lastSeed = _chunkManager.WorldSeed;
			GenerateGlobeTexture();
		}

		if (_snapToTarget)
		{
			// Smoothly rotate globe to face selected cell and zoom in
			float diff = Mathf.Wrap(_targetYRot - _globeRoot.Rotation.Y, -Mathf.Pi, Mathf.Pi);
			_globeRoot.Rotation = new Vector3(0f, _globeRoot.Rotation.Y + diff * (float)delta * 3.0f, 0f);
			float cz = Mathf.Lerp(_camera3D.Position.Z, _zoomTarget, (float)delta * 2.5f);
			_camera3D.Position = new Vector3(0f, 0f, cz);
			_rotVelocity  = 0f;
			_dragVelocity = 0f;
		}
		else if (_isDragging)
		{
			_rotVelocity  = Mathf.Clamp(_dragVelocity / Mathf.Max((float)delta, 0.001f), -20f, 20f);
			_dragVelocity = 0f;
		}
		else
			_rotVelocity = Mathf.Lerp(_rotVelocity, AutoRotSpeed, (float)delta * 1.5f);

		if (!_snapToTarget)
		{
			_globeRoot.RotateY(_rotVelocity * (float)delta);
			_cloudSphere.RotateY(CloudDriftRel * (float)delta);
		}

		// Clouds stay fully visible until Z=2.0, then fade over the last stretch to min zoom
		float zFrac2      = Mathf.Clamp(Mathf.InverseLerp(2.0f, GlobeZoomMin, _camera3D.Position.Z), 0f, 1f);
		float cloudAlpha2 = 1f - zFrac2 * zFrac2;   // quadratic: gentle fade start, quick at close
		if (_cloudSphere.GetActiveMaterial(0) is ShaderMaterial cloudMat)
			cloudMat.SetShaderParameter("cloud_alpha_scale", cloudAlpha2);
		if (_cloudSphereInner.GetActiveMaterial(0) is ShaderMaterial cloudMatI)
		{
			_cloudSphereInner.RotateY(CloudDriftRel * 1.35f * (float)delta);   // inner layer drifts faster
			cloudMatI.SetShaderParameter("cloud_alpha_scale", cloudAlpha2 * 0.60f);
		}
	}

	// ── Grid cell click — ray-sphere intersection ─────────────────────────────

	/// <summary>
	/// Maps a screen-space click to a grid cell via ray-sphere intersection in
	/// the globe root's local space (accounting for current planet rotation).
	/// </summary>
	private void TryClickGridCell(Vector2 screenPos)
	{
		if (_camera3D is null || _globeRoot is null) return;

		// Project mouse to 3D ray in the SubViewport's world space
		var rayOrigin = _camera3D.ProjectRayOrigin(screenPos);
		var rayDir    = _camera3D.ProjectRayNormal(screenPos);

		// Transform ray to globe root's local space (undoes current planet rotation)
		var g2l       = _globeRoot.GlobalTransform.AffineInverse();
		var localO    = g2l * rayOrigin;
		var localD    = (g2l.Basis * rayDir).Normalized();

		// Intersect with unit sphere: |O + t*D|² = 1
		float b    = localO.Dot(localD);
		float c    = localO.Dot(localO) - 1f;
		float disc = b * b - c;
		if (disc < 0f) return; // ray missed the sphere

		float t   = -b - Mathf.Sqrt(disc);
		if (t < 0f) return;   // hit was behind camera

		var hit = localO + localD * t;

		// Convert hit point to UV — must match Godot SphereMesh UV exactly.
		// SphereMesh vertex: x=sin(phi)*cos(lat), z=cos(phi)*cos(lat) → phi=atan2(x, z)
		// UV.x = phi/(2π) with seam at phi=0 (-Z), wrapping from 0→1.
		float lat = Mathf.Asin(Mathf.Clamp(hit.Y, -1f, 1f));
		float phi = Mathf.Atan2(hit.X, hit.Z); // Godot sphere phi: atan2(sin(phi), cos(phi))

		float u = Mathf.Wrap(phi, 0f, Mathf.Tau) / Mathf.Tau;
		float v = 0.5f - lat / Mathf.Pi;

		int gi = Mathf.Clamp((int)(u * GridDivLon), 0, GridDivLon - 1);
		int gj = Mathf.Clamp((int)(v * GridDivLat), 0, GridDivLat - 1);

		// Stop rotation, zoom in to show the selected region.
		// phi_world = phi_local + Rotation.Y → to face camera (phi_world=0): Rotation.Y = -phi_local
		_selCellGi    = gi;
		_selCellGj    = gj;
		_snapToTarget = true;
		_targetYRot   = -phi;
		_zoomTarget   = 2.0f;
		UpdateGridSelection();
		ShowCellPanel(gi, gj);
	}

	// ── Cell info panel ────────────────────────────────────────────────────────

	private void BuildCellPanel()
	{
		_cellLayer         = new CanvasLayer();
		_cellLayer.Layer   = 8;
		_cellLayer.Visible = false;
		AddChild(_cellLayer);

		var vp    = GetViewport().GetVisibleRect().Size;
		int cardW = 480, cardH = 520;

		_cellCard          = new Panel();
		_cellCard.Position = new Vector2(vp.X - cardW - 20f, (vp.Y - cardH) / 2f);
		_cellCard.Size     = new Vector2(cardW, cardH);
		var style = new StyleBoxFlat
		{
			BgColor                = CellBg,
			BorderColor            = CellBorder,
			BorderWidthLeft        = 2, BorderWidthRight   = 2,
			BorderWidthTop         = 2, BorderWidthBottom  = 2,
			CornerRadiusTopLeft    = 6, CornerRadiusTopRight    = 6,
			CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
		};
		_cellCard.AddThemeStyleboxOverride("panel", style);
		_cellLayer.AddChild(_cellCard);

		int px = 22, py = 18;

		// Header
		var header = CLabel("◆  FRONTIER PROTOCOL  —  REGION SURVEY  ◆", px, py, 11, CellSubtle);
		header.Size = new Vector2(cardW - px * 2, 20);
		header.HorizontalAlignment = HorizontalAlignment.Center;
		_cellCard.AddChild(header);
		py += 26;

		// Title (cell coordinates + dominant biome)
		_cellTitle = CLabel("", px, py, 18, CellTitle);
		_cellCard.AddChild(_cellTitle);
		py += 30;

		AddCardDivider(cardW, py); py += 14;

		// Stats section
		_cellCard.AddChild(CLabel("TERRAIN ANALYSIS", px, py, 11, CellSubtle));
		py += 18;

		_cellBiome = CLabel("", px, py, 13, CellStat);
		_cellCard.AddChild(_cellBiome);
		py += 20;

		_cellElev = CLabel("", px, py, 12, Colors.White);
		_cellCard.AddChild(_cellElev);
		py += 18;

		_cellHumid = CLabel("", px, py, 12, Colors.White);
		_cellCard.AddChild(_cellHumid);
		py += 18;

		_cellTemp = CLabel("", px, py, 12, Colors.White);
		_cellCard.AddChild(_cellTemp);
		py += 22;

		// Terrain description
		_cellTerrain = CLabel("", px, py, 11, new Color(0.75f, 0.75f, 0.75f));
		_cellTerrain.Size         = new Vector2(cardW - px * 2, 44);
		_cellTerrain.AutowrapMode = TextServer.AutowrapMode.Word;
		_cellCard.AddChild(_cellTerrain);
		py += 52;

		AddCardDivider(cardW, py); py += 14;

		_cellCard.AddChild(CLabel("ESTIMATED FAUNA", px, py, 11, CellSubtle));
		py += 18;

		_cellFauna = CLabel("", px, py, 12, Colors.White);
		_cellFauna.Size         = new Vector2(cardW - px * 2, 150);
		_cellFauna.AutowrapMode = TextServer.AutowrapMode.Word;
		_cellCard.AddChild(_cellFauna);
		py += 158;

		AddCardDivider(cardW, py); py += 14;

		_cellHazard              = CLabel("", px, py, 13, CellDanger);
		_cellHazard.Size         = new Vector2(cardW - px * 2, 30);
		_cellHazard.AutowrapMode = TextServer.AutowrapMode.Word;
		_cellCard.AddChild(_cellHazard);
		py += 36;

		// Close button
		var closeBtn = new Button
		{
			Text              = "Close  [Esc]",
			Position          = new Vector2(cardW - 130, cardH - 44),
			CustomMinimumSize = new Vector2(110, 28),
		};
		closeBtn.Pressed += HideCellPanel;
		_cellCard.AddChild(closeBtn);

		// Hint
		var hint = CLabel("Click grid line or press Esc to close", px, cardH - 38, 11, CellSubtle);
		hint.Size = new Vector2(cardW - 150, 20);
		_cellCard.AddChild(hint);
	}

	private void ShowCellPanel(int gi, int gj)
	{
		if (_cellLayer is null || _cellCard is null) return;

		var cell = _gridData[gi, gj];

		// Heading
		float lonDeg = (gi + 0.5f) / GridDivLon * 360f - 180f;
		float latDeg = 90f - (gj + 0.5f) / GridDivLat * 180f;
		string ns = latDeg >= 0 ? "N" : "S";
		string ew = lonDeg >= 0 ? "E" : "W";
		if (_cellTitle != null)
			_cellTitle.Text = $"{BiomeName(cell.DominantBiome)}  —  {Mathf.Abs(latDeg):F0}°{ns}  {Mathf.Abs(lonDeg):F0}°{ew}";

		if (_cellBiome != null)
			_cellBiome.Text = $"Dominant Biome     {BiomeName(cell.DominantBiome)}"
			                + (cell.HasOcean ? "  (coastal access)" : "")
			                + (cell.HasMountains ? "  (mountain ranges)" : "");

		if (_cellElev != null)
			_cellElev.Text = $"Elevation          {Bar(cell.AvgElevation)}  {ElevLabel(cell.AvgElevation)}";

		if (_cellHumid != null)
			_cellHumid.Text = $"Humidity           {Bar(cell.AvgMoisture)}  {HumidLabel(cell.AvgMoisture)}";

		if (_cellTemp != null)
			_cellTemp.Text = $"Temperature        {Bar(cell.AvgTemp)}  {TempLabel(cell.AvgTemp)}";

		var (fauna, hazard, terrain) = DeriveRegionInfo(cell);

		if (_cellTerrain != null)
			_cellTerrain.Text = terrain;

		if (_cellFauna != null)
		{
			var sb = new System.Text.StringBuilder();
			foreach (var f in fauna)
				sb.AppendLine($"⚡  {f}");
			_cellFauna.Text = sb.ToString().TrimEnd();
		}

		if (_cellHazard != null)
		{
			string stars  = new string('★', hazard) + new string('☆', 5 - hazard);
			string rating = hazard switch { 1 => "LOW", 2 => "MODERATE", 3 => "ELEVATED", 4 => "HIGH", _ => "EXTREME" };
			int    squad  = hazard <= 2 ? 1 : hazard <= 3 ? 2 : hazard <= 4 ? 3 : 4;
			_cellHazard.Text = $"HAZARD  {stars}  {rating}   ·   Min. {squad} scouts";
			_cellHazard.AddThemeColorOverride("font_color",
				hazard >= 5 ? CellDanger : hazard >= 4 ? new Color(1f, 0.6f, 0.2f) : CellGreen);
		}

		_cellLayer.Visible = true;
	}

	private void HideCellPanel()
	{
		if (_cellLayer is not null) _cellLayer.Visible = false;
		_snapToTarget = false;
		_selCellGi    = -1;
		_selCellGj    = -1;
		UpdateGridSelection();
	}

	private void UpdateGridSelection()
	{
		if (_gridSphere?.GetActiveMaterial(0) is ShaderMaterial gMat)
		{
			gMat.SetShaderParameter("selected_gi", _selCellGi);
			gMat.SetShaderParameter("selected_gj", _selCellGj);
		}
		// Also drive planet shader dimming — sel_gi = -1 means "no selection, full colour"
		if (_sphere?.GetActiveMaterial(0) is ShaderMaterial pMat)
		{
			pMat.SetShaderParameter("sel_gi", _selCellGi);
			pMat.SetShaderParameter("sel_gj", _selCellGj);
		}
	}

	// ── Region data derivation ────────────────────────────────────────────────

	private static (string[] fauna, int hazard, string terrain) DeriveRegionInfo(GridCellData cell)
	{
		var fauna  = new List<string>();
		int hazard = 2;
		string terrain;

		switch (cell.DominantBiome)
		{
			case BiomeType.AlienWilds:
				fauna.Add("Unknown apex organisms — non-terrestrial biology");
				fauna.Add("Bioluminescent colony swarms");
				fauna.Add("Mycelium network — potential sentience");
				hazard  = 5;
				terrain = "Extreme alien growth. Unclassified hazards. Scout survival data limited.";
				break;
			case BiomeType.Jungle:
				fauna.Add("Dense apex predator packs");
				fauna.Add("Megafauna territorial zones");
				fauna.Add("Spore organism bloom — active");
				hazard  = 4;
				terrain = "Impenetrable canopy, restricted sight-lines. Rich in resources.";
				break;
			case BiomeType.Forest:
				fauna.Add("Ambush predators — vine-integrated");
				fauna.Add("Swarm species — high density");
				hazard  = 3;
				terrain = "Dense understory, variable visibility. Moderate resource yield.";
				break;
			case BiomeType.Grassland:
			case BiomeType.Savanna:
				fauna.Add("Herd megafauna — migratory");
				fauna.Add("Open-ground pursuit predators");
				hazard  = 2;
				terrain = "Wide sight-lines, moderate natural cover. Exposed to weather events.";
				break;
			case BiomeType.Desert:
				fauna.Add("Heat-adapted solitary predators");
				fauna.Add("Subterranean colony species");
				hazard  = 3;
				terrain = "Extreme heat exposure. Resource-scarce. Night operations recommended.";
				break;
			case BiomeType.Highland:
			case BiomeType.Mountain:
				fauna.Add("Minimal fauna — elevation-limited");
				fauna.Add("Territorial megafauna at lower ridges");
				hazard  = 4;
				terrain = "Treacherous terrain, exposure risk. Exceptional long-range visibility.";
				break;
			case BiomeType.Arctic:
				fauna.Add("Cold-adapted predators — rare, lethal");
				fauna.Add("Sub-glacial species — unconfirmed contact");
				hazard  = 4;
				terrain = "Severe cold exposure. Oxygen drain elevated. Low visibility in weather.";
				break;
			case BiomeType.DeepOcean:
			case BiomeType.Ocean:
			case BiomeType.Coastal:
				fauna.Add("Aquatic megafauna — unclassified");
				fauna.Add("Coastal territorial species");
				hazard  = 3;
				terrain = "Coastal landing zones. Tide-dependent access. Aquatic hazards uncharted.";
				break;
			case BiomeType.SafeZone:
				fauna.Add("Low-threat indigenous species");
				hazard  = 1;
				terrain = "Designated safe perimeter near drop point. Cleared and surveyed.";
				break;
			default:
				fauna.Add("Survey data unavailable");
				hazard  = 2;
				terrain = "Unclassified terrain. First expedition will gather baseline data.";
				break;
		}

		if (cell.HasMountains)
		{
			fauna.Add("Ridge nesting species detected");
			hazard = Mathf.Min(5, hazard + 1);
		}
		if (cell.AvgMoisture > 0.78f) fauna.Add("High spore concentration — respiratory risk");
		if (cell.AvgTemp < 0.30f)     hazard = Mathf.Min(5, hazard + 1);
		if (cell.AvgElevation > 0.75f) fauna.Add("Thin atmosphere — oxygen supplementation required");

		return (fauna.ToArray(), hazard, terrain);
	}

	// ── Texture generation ────────────────────────────────────────────────────

	private void GenerateGlobeTexture()
	{
		int seed   = _chunkManager.WorldSeed;
		var planet = ChunkGenerator.DeriveParams(seed);

		float contFreq = planet.Type switch
		{
			PlanetType.ArchipelagoWorld => 0.52f,  // higher freq → smaller, more numerous islands
			PlanetType.ContinentalWorld => 0.36f,  // was 0.24 — less blob-like, more varied coastlines
			PlanetType.JungleWorld      => 0.44f,  // mid freq with good internal detail
			_                           => 0.42f,
		};
		float warpAmp     = 0.55f + planet.AlienFactor * 0.20f;   // enough for organic coastlines, not sphere-destroying
		float mtnBoostStr = 0.28f + (1f - planet.SeaLevel) * 0.10f;

		var contNoise = new FastNoiseLite
		{
			Seed                = seed ^ 0x1A2B3C4D,
			NoiseType           = FastNoiseLite.NoiseTypeEnum.Perlin,
			Frequency           = contFreq,
			FractalType         = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves      = 8,
			DomainWarpEnabled   = true,
			DomainWarpType      = FastNoiseLite.DomainWarpTypeEnum.SimplexReduced,
			DomainWarpAmplitude = warpAmp,
			DomainWarpFrequency = 0.25f,
		};
		// Second layer at 2× frequency — adds peninsula detail & sub-continent variation
		var contNoise2 = new FastNoiseLite
		{
			Seed                = seed ^ 0x2B3C4D5E,
			NoiseType           = FastNoiseLite.NoiseTypeEnum.Perlin,
			Frequency           = contFreq * 2.0f,
			FractalType         = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves      = 5,
			DomainWarpEnabled   = true,
			DomainWarpType      = FastNoiseLite.DomainWarpTypeEnum.SimplexReduced,
			DomainWarpAmplitude = warpAmp * 0.5f,
			DomainWarpFrequency = 0.45f,
		};
		var ridgeNoise = new FastNoiseLite
		{
			Seed           = seed ^ unchecked((int)0x9A8B7C6D),
			NoiseType      = FastNoiseLite.NoiseTypeEnum.Perlin,
			Frequency      = 0.55f,
			FractalType    = FastNoiseLite.FractalTypeEnum.Ridged,
			FractalOctaves = 5,
		};
		var moistNoise  = MakeNoise3D(seed ^ 0x5E6F7A8B, contFreq * 0.95f, 4);  // track contFreq → coherent biome zones at all scales
		var detailNoise = MakeNoise3D(seed ^ unchecked((int)0xC3D4E5F6), 3.50f, 3);
		var riverNoise  = new FastNoiseLite
		{
			Seed               = seed ^ 0x52495645,
			NoiseType          = FastNoiseLite.NoiseTypeEnum.Cellular,
			Frequency          = 1.8f,
			CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance2Div,
			CellularJitter     = 0.9f,
		};

		// Pre-allocated byte arrays avoid per-pixel managed Image.SetPixel overhead
		var colorBytes  = new byte[TexW * TexH * 3];
		var heightBytes = new byte[TexW * TexH];

		int[,,]  biomeVotes = new int[GridDivLon, GridDivLat, MaxBiomeIdx];
		float[,] elevSum    = new float[GridDivLon, GridDivLat];
		float[,] moistSum   = new float[GridDivLon, GridDivLat];
		float[,] tempSum    = new float[GridDivLon, GridDivLat];
		int[,]   pixCount   = new int[GridDivLon, GridDivLat];
		// ── Pre-pass: find the cont threshold that gives exactly targetWater coverage ──
		// Builds an area-weighted histogram (cos-lat) to account for equirectangular distortion.
		const int   HistBins  = 512;
		const float MinWater  = 0.50f;
		const float MaxWater  = 0.80f;
		float targetFraction  = Mathf.Clamp(planet.SeaLevel, MinWater, MaxWater);
		var   hist            = new float[HistBins];
		float histTotal       = 0f;
		for (int py = 0; py < TexH; py++)
		for (int px = 0; px < TexW; px++)
		{
			float v2  = (py + 0.5f) / TexH;
			float lat2 = (0.5f - v2) * Mathf.Pi;
			float nz2  = Mathf.Sin(lat2);
			float nx2  = Mathf.Cos(lat2) * Mathf.Cos((((px + 0.5f) / TexW) * 2f - 1f) * Mathf.Pi);
			float ny2  = Mathf.Cos(lat2) * Mathf.Sin((((px + 0.5f) / TexW) * 2f - 1f) * Mathf.Pi);
			float c    = Norm(contNoise.GetNoise3D(nx2, ny2, nz2)) * 0.75f
			           + Norm(contNoise2.GetNoise3D(nx2, ny2, nz2)) * 0.25f;
			float pf   = 1f - Mathf.Abs(nz2) * 0.38f;
			c = Mathf.Clamp(c * pf + (1f - pf) * 0.5f, 0f, 1f);
			float w    = Mathf.Cos(lat2);
			hist[Mathf.Min((int)(c * HistBins), HistBins - 1)] += w;
			histTotal += w;
		}
		float effectiveSeaLevel = 0.5f;
		float cumulative        = 0f;
		for (int i = 0; i < HistBins; i++)
		{
			cumulative += hist[i] / histTotal;
			if (cumulative >= targetFraction) { effectiveSeaLevel = (i + 0.5f) / HistBins; break; }
		}
		// ─────────────────────────────────────────────────────────────────────────────

		float waterWeight = 0f, totalWeight = 0f;

		for (int py = 0; py < TexH; py++)
		for (int px = 0; px < TexW; px++)
		{
			float u   = (px + 0.5f) / TexW;
			float v   = (py + 0.5f) / TexH;
			float lon = (u * 2f - 1f) * Mathf.Pi;
			float lat = (0.5f - v)    * Mathf.Pi;

			float nx = Mathf.Cos(lat) * Mathf.Cos(lon);
			float ny = Mathf.Cos(lat) * Mathf.Sin(lon);
			float nz = Mathf.Sin(lat);

			float cont   = Norm(contNoise.GetNoise3D(nx, ny, nz)) * 0.75f
			           + Norm(contNoise2.GetNoise3D(nx, ny, nz)) * 0.25f;
			float poleFade = 1f - Mathf.Abs(nz) * 0.38f;
			cont = Mathf.Clamp(cont * poleFade + (1f - poleFade) * 0.5f, 0f, 1f);
			float ridge  = Norm(ridgeNoise.GetNoise3D(nx, ny, nz));
			float moist  = Norm(moistNoise.GetNoise3D(nx, ny, nz));
			float detail = Norm(detailNoise.GetNoise3D(nx, ny, nz));

			float aboveSea  = cont - effectiveSeaLevel;
			float coastProx = aboveSea > 0f
			    ? Mathf.Clamp(1f - Mathf.Abs(aboveSea - 0.045f) / 0.055f, 0f, 1f)
			    : 0f;
			float mtnBoost  = ridge * coastProx * mtnBoostStr;
			float elev      = Mathf.Clamp(cont + mtnBoost, 0f, 1f);

			float temp  = Mathf.Clamp(1f - Mathf.Abs(nz) * 0.90f + planet.GlobalTempMod, 0f, 1f);
			moist       = Mathf.Clamp(moist * (0.6f + planet.VegBias * 0.8f), 0f, 1f);

			float jit   = (detail - 0.5f) * 0.12f;
			var biome   = ChunkGenerator.Classify(
				Mathf.Clamp(elev  + jit,        0f, 1f),
				Mathf.Clamp(moist + jit,        0f, 1f),
				Mathf.Clamp(temp  + jit * 0.4f, 0f, 1f),
				planet with { SeaLevel = effectiveSeaLevel });

			Color c = BiomeToColor(biome, detail, elev, planet);

			// Rivers and lakes
			bool isWater = biome == BiomeType.DeepOcean || biome == BiomeType.Ocean || biome == BiomeType.Coastal;
			float areaW = Mathf.Cos(lat);   // cos(lat) = actual sphere area per pixel
			totalWeight += areaW;
			if (isWater) waterWeight += areaW;
			if (!isWater)
			{
				float rv      = Norm(riverNoise.GetNoise3D(nx, ny, nz));
				bool  isLake  = elev < effectiveSeaLevel + 0.07f && moist > 0.62f;
				bool  isRiver = rv > 0.920f && elev < effectiveSeaLevel + 0.32f && moist > 0.42f;
				if (isLake || isRiver)
					// Freshwater: brighter cyan-blue, distinct from deep-navy salt water
					c = new Color(0.06f, 0.38f, 0.62f).Lerp(new Color(0.04f, 0.30f, 0.54f), detail * 0.4f);
			}

			int idx = (py * TexW + px) * 3;
			colorBytes[idx]     = (byte)(c.R8);
			colorBytes[idx + 1] = (byte)(c.G8);
			colorBytes[idx + 2] = (byte)(c.B8);
			heightBytes[py * TexW + px] = (byte)(elev * 255f);

			int gi = Mathf.Min((int)(u * GridDivLon), GridDivLon - 1);
			int gj = Mathf.Min((int)(v * GridDivLat), GridDivLat - 1);
			int bi = Mathf.Clamp((int)biome, 0, MaxBiomeIdx - 1);
			biomeVotes[gi, gj, bi]++;
			elevSum[gi, gj]  += elev;
			moistSum[gi, gj] += moist;
			tempSum[gi, gj]  += temp;
			pixCount[gi, gj]++;
		}

		_chunkManager.WaterFraction = totalWeight > 0f ? waterWeight / totalWeight : 0f;

		// Build grid data
		_gridData = new GridCellData[GridDivLon, GridDivLat];
		for (int gi = 0; gi < GridDivLon; gi++)
		for (int gj = 0; gj < GridDivLat; gj++)
		{
			int cnt = pixCount[gi, gj];
			if (cnt == 0) continue;
			int maxV = 0, maxB = 0;
			for (int b = 0; b < MaxBiomeIdx; b++)
				if (biomeVotes[gi, gj, b] > maxV) { maxV = biomeVotes[gi, gj, b]; maxB = b; }

			_gridData[gi, gj] = new GridCellData(
				(BiomeType)maxB,
				elevSum[gi, gj]  / cnt,
				moistSum[gi, gj] / cnt,
				tempSum[gi, gj]  / cnt,
				biomeVotes[gi, gj, (int)BiomeType.DeepOcean] + biomeVotes[gi, gj, (int)BiomeType.Ocean] > cnt / 4,
				biomeVotes[gi, gj, (int)BiomeType.Mountain]  + biomeVotes[gi, gj, (int)BiomeType.Highland] > cnt / 10
			);
		}

		var image     = Image.CreateFromData(TexW, TexH, false, Image.Format.Rgb8,  colorBytes);
		var heightImg = Image.CreateFromData(TexW, TexH, false, Image.Format.L8,    heightBytes);
		var colorTex  = ImageTexture.CreateFromImage(image);
		var heightTex = ImageTexture.CreateFromImage(heightImg);

		if (_sphere.GetActiveMaterial(0) is ShaderMaterial mat)
		{
			mat.SetShaderParameter("albedo_texture",     colorTex);
			mat.SetShaderParameter("height_texture",     heightTex);
			mat.SetShaderParameter("sea_level",          effectiveSeaLevel);
			mat.SetShaderParameter("displacement_scale", _dispScale);
			mat.SetShaderParameter("water_depth_blend",  _waterDepth);
			mat.SetShaderParameter("sat_boost",          _satBoost);
		}
		if (_cloudSphere.GetActiveMaterial(0) is ShaderMaterial cMat)
			cMat.SetShaderParameter("seed_offset", (float)(seed % 1000) / 10f);
		if (_cloudSphereInner.GetActiveMaterial(0) is ShaderMaterial cMatI)
			cMatI.SetShaderParameter("seed_offset", (float)((seed ^ 0xABCD) % 1000) / 10f);
	}

	// ── Planet shader ─────────────────────────────────────────────────────────

	private const string PlanetShaderCode = @"
shader_type spatial;
render_mode cull_back;

uniform sampler2D albedo_texture  : source_color, hint_default_white;
uniform sampler2D height_texture  : hint_default_black;
uniform float displacement_scale  : hint_range(0.0, 0.30) = 0.14;
uniform float sea_level           : hint_range(0.0, 1.0)  = 0.40;
uniform float water_depth_blend   : hint_range(0.0, 1.0)  = 0.85;
uniform float sat_boost           : hint_range(1.0, 2.5)  = 1.10;
uniform int   sel_gi = -1;
uniform int   sel_gj = -1;

varying float v_height;
varying vec3  sphere_pos;

void vertex() {
	float h    = texture(height_texture, UV).r;
	v_height   = h;
	sphere_pos = VERTEX;

	// Power curve: flat plains barely rise, mountain peaks spike dramatically
	float land      = max(0.0, h - sea_level) / max(0.001, 1.0 - sea_level);
	land            = pow(land, 2.2);
	float pole_damp = 1.0 - smoothstep(0.65, 0.92, abs(VERTEX.y));
	VERTEX += NORMAL * land * displacement_scale * pole_damp;
}

void fragment() {
	vec3 col = texture(albedo_texture, UV).rgb;

	float luma = dot(col, vec3(0.299, 0.587, 0.114));
	col        = clamp(mix(vec3(luma), col, sat_boost), 0.0, 1.0);

	float wat = 1.0 - smoothstep(sea_level - 0.001, sea_level + 0.003, v_height);

	// 4-zone depth water colour
	// sqrt() compresses the shallow zone: most open ocean becomes dark navy,
	// only pixels genuinely close to sea_level get the bright coastal teal.
	float raw_depth = clamp((sea_level - v_height) / max(0.001, sea_level), 0.0, 1.0);
	float depth     = sqrt(raw_depth);
	vec3  coastal_c = vec3(0.08, 0.55, 0.70);  // bright teal — only at narrow shoreline
	vec3  shallow_c = vec3(0.03, 0.18, 0.44);  // medium blue
	vec3  ocean_c   = vec3(0.01, 0.05, 0.22);  // dark blue-navy open ocean
	vec3  deep_c    = vec3(0.00, 0.01, 0.12);  // deep indigo — ocean trenches
	vec3  waterCol;
	if (depth < 0.18)
		waterCol = mix(coastal_c, shallow_c, depth / 0.18);
	else if (depth < 0.50)
		waterCol = mix(shallow_c, ocean_c, (depth - 0.18) / 0.32);
	else
		waterCol = mix(ocean_c, deep_c, clamp((depth - 0.50) / 0.50, 0.0, 1.0));
	col = mix(col, waterCol, wat * water_depth_blend);

	// Ocean surface — 4 crossing wave trains: slow large swells + fast small chop
	float w1 = sin(TIME * 0.40 + sphere_pos.x * 22.0 + sphere_pos.z * 14.0) * 0.5 + 0.5;
	float w2 = sin(TIME * 0.68 + sphere_pos.z * 18.0 - sphere_pos.x *  9.0 + 1.57) * 0.5 + 0.5;
	float w3 = sin(TIME * 0.28 - sphere_pos.x * 13.0 + sphere_pos.y * 20.0 + 2.39) * 0.5 + 0.5;
	float w4 = sin(TIME * 0.95 + sphere_pos.y * 10.0 + sphere_pos.z * 26.0 + 0.78) * 0.5 + 0.5;
	float swell = w1 * 0.55 + w3 * 0.45;   // slow rolling swells
	float chop  = w2 * 0.50 + w4 * 0.50;   // fast surface chop
	// Crest brightening: peaks reflect more sky light
	col += wat * (swell * swell) * 0.07 * vec3(0.50, 0.78, 1.00);
	// Whitecaps: brief bright flash where swell and chop crests coincide
	float whitecap = pow(clamp(swell * chop * 1.6 - 0.78, 0.0, 1.0), 2.5);
	col = mix(col, vec3(0.90, 0.95, 1.00), wat * whitecap * 0.22);
	// Sun glint — tight specular on wave crest tips
	col += wat * pow(w1 * w2, 7.0) * 0.14 * vec3(0.82, 0.93, 1.00);

	// Mountain slope shadow (all terrain)
	float slope      = length(vec2(dFdx(v_height), dFdy(v_height)));
	float mtn_shadow = smoothstep(0.003, 0.012, slope) * (1.0 - wat);
	col = mix(col, col * 0.45, mtn_shadow * 0.60);

	// Alpine snow — only on genuine peaks
	float alpine    = smoothstep(0.88, 0.96, v_height) * (1.0 - wat);
	// Polar ice — near poles, lower elevation threshold
	float polar_t   = smoothstep(0.38, 0.62, abs(sphere_pos.y));
	float polar_ice = smoothstep(0.68, 0.80, v_height) * polar_t * (1.0 - wat);
	col = mix(col, vec3(0.92, 0.96, 1.00), max(alpine, polar_ice) * 0.80);

	// Selection: brighten selected cell, dim everything else
	int  ci     = int(UV.x * 24.0);
	int  cj     = int(UV.y * 12.0);
	bool in_sel = (sel_gi >= 0) && (ci == sel_gi) && (cj == sel_gj);
	if (sel_gi >= 0) {
		if (in_sel) {
			float luma2 = dot(col, vec3(0.299, 0.587, 0.114));
			col = clamp(mix(vec3(luma2), col, 1.35) * 1.18, 0.0, 1.0);
		} else {
			col *= 0.30;
		}
	}

	ALBEDO    = col;
	ROUGHNESS = mix(0.88, 0.04, wat);
	METALLIC  = mix(0.0,  0.10, wat);
	SPECULAR  = mix(0.05, 0.72, wat);
}
";

	// ── Atmosphere shader ─────────────────────────────────────────────────────

	private const string AtmosphereShaderCode = @"
shader_type spatial;
render_mode cull_front, blend_add, unshaded, depth_draw_never;

void fragment() {
	float rim   = 1.0 - clamp(dot(normalize(VIEW), NORMAL), 0.0, 1.0);
	float inner = pow(rim, 9.0);   // tight bright limb line only
	float outer = pow(rim, 5.5);   // very narrow halo — no bubble

	vec3 limb = vec3(0.50, 0.84, 0.92);
	vec3 halo = vec3(0.08, 0.35, 0.52);

	ALBEDO   = mix(halo, limb, inner);
	ALPHA    = outer * 0.10;        // barely visible — just a subtle rim
	EMISSION = limb * inner * 0.40;
}
";

	// ── Cloud shader ──────────────────────────────────────────────────────────

	private const string CloudShaderCode = @"
shader_type spatial;
render_mode cull_back, blend_mix;

uniform float seed_offset       = 0.0;
uniform float cloud_alpha_scale : hint_range(0.0, 1.0) = 1.0;

varying vec3 sphere_pos;

void vertex() { sphere_pos = VERTEX; }

vec3 ghash(vec3 p) {
	vec3 h = fract(sin(vec3(dot(p,vec3(127.1,311.7,74.7)),
	                        dot(p,vec3(269.5,183.3,246.1)),
	                        dot(p,vec3(113.5,271.9,124.6)))) * 43758.5453);
	return normalize(h * 2.0 - 1.0);
}
float gnoise(vec3 p) {
	vec3 i=floor(p); vec3 f=fract(p);
	vec3 u=f*f*f*(f*(f*6.0-15.0)+10.0);
	return 2.0*mix(mix(mix(dot(ghash(i),f),dot(ghash(i+vec3(1,0,0)),f-vec3(1,0,0)),u.x),
	                   mix(dot(ghash(i+vec3(0,1,0)),f-vec3(0,1,0)),dot(ghash(i+vec3(1,1,0)),f-vec3(1,1,0)),u.x),u.y),
	               mix(mix(dot(ghash(i+vec3(0,0,1)),f-vec3(0,0,1)),dot(ghash(i+vec3(1,0,1)),f-vec3(1,0,1)),u.x),
	                   mix(dot(ghash(i+vec3(0,1,1)),f-vec3(0,1,1)),dot(ghash(i+vec3(1,1,1)),f-vec3(1,1,1)),u.x),u.y),u.z);
}
float fbm(vec3 p) {
	float v=0.0,a=0.5;
	for(int i=0;i<5;i++){v+=a*gnoise(p);p*=2.0;a*=0.5;}
	return v*0.5+0.5;
}

// Vortex warp: rotates the FBM sampling coordinate around a storm centre.
// Differential rotation (more twist at inner radii) bends existing cloud bands
// into natural spiral arms — no fake shapes painted on top.
// spin_dir: +1 = CCW (N hemisphere), -1 = CW (S hemisphere)
vec2 vortexWarp(float lat, float lon, vec2 ctr, float t_spin, float radius, float spin_dir) {
	float dlat = lat - ctr.y;
	float dlon  = lon - ctr.x;
	dlon = mod(dlon + 3.14159, 6.28318) - 3.14159;
	float r = sqrt(dlat * dlat + dlon * dlon);
	if (r >= radius) return vec2(0.0);

	float nf      = r / radius;
	// Eye is calm; twist peaks around nf=0.25, fades at outer rim
	float eyeFade = smoothstep(0.0, 0.15, nf);
	float angle   = spin_dir * eyeFade * (1.0 - nf * nf) * 3.8 + t_spin;

	float cosA = cos(angle);
	float sinA = sin(angle);
	vec2 rotated = vec2(dlat * cosA - dlon * sinA,
	                    dlat * sinA + dlon * cosA);

	float edgeFade = smoothstep(1.0, 0.5, nf);
	return (rotated - vec2(dlat, dlon)) * edgeFade;
}

void fragment() {
	float t   = TIME * 0.012 + seed_offset;
	float lat = asin(clamp(normalize(sphere_pos).y, -0.9999, 0.9999));
	float lon = atan(sphere_pos.x, sphere_pos.z);

	// Storm activity — each storm independently pulses in/out on a slow sine cycle
	// Different periods so they rarely peak at the same time
	float sd = seed_offset;
	float a1 = smoothstep(0.4, 0.85, sin(TIME * 0.025 + sd * 1.00));  // ~4 min cycle
	float a2 = smoothstep(0.4, 0.85, sin(TIME * 0.020 + sd * 2.10));  // ~5 min cycle
	float a3 = smoothstep(0.4, 0.85, sin(TIME * 0.032 + sd * 3.70));  // ~3 min cycle

	// Storm centre positions — seeded, tropical/subtropical bands, slowly drifting westward
	vec2 c1 = vec2(mod(1.20 + sd * 0.11 - t * 0.003, 6.283) - 3.14159,  0.38 + sin(sd)        * 0.12);
	vec2 c2 = vec2(mod(-0.80 + sd * 0.17 - t * 0.002, 6.283) - 3.14159, -0.30 + cos(sd * 1.3) * 0.10);
	vec2 c3 = vec2(mod(3.50 + sd * 0.13 - t * 0.004, 6.283) - 3.14159,  0.26 + sin(sd * 0.7)  * 0.14);

	// Accumulate vortex warps scaled by activity; S hemisphere storm spins reversed
	vec2 cw = vortexWarp(lat, lon, c1,  t * 3.0, 0.42,  1.0) * a1
	        + vortexWarp(lat, lon, c2, -t * 2.5, 0.38, -1.0) * a2
	        + vortexWarp(lat, lon, c3,  t * 2.0, 0.35,  1.0) * a3;

	// Reconstruct warped sphere position for FBM sampling
	float wLat = clamp(lat + cw.x, -1.57, 1.57);
	float wLon = lon + cw.y;
	vec3  wp   = vec3(cos(wLat) * sin(wLon), sin(wLat), cos(wLat) * cos(wLon));

	// Domain warp for global atmospheric shapes
	vec3 sp = wp * 1.55;
	vec3 domWarp = vec3(
		gnoise(sp + vec3(t * 0.40, 0.0,    t * 0.25)),
		gnoise(sp + vec3(0.0,    t * 0.35, t * 0.50)),
		gnoise(sp + vec3(t * 0.20, t * 0.45, 0.0))
	);
	float big  = fbm(wp * 1.20 + domWarp * 0.28 + vec3(t * 0.5, t * 0.2, t * 0.3));
	float fine = fbm(wp * 3.50 + vec3(t * 0.9, t * 0.6, t * 1.1));
	float n    = big * 0.70 + fine * 0.30;

	// Calm eye: small clear disc at the centre of each active storm
	float r1 = length(vec2(mod(lon - c1.x + 3.14159, 6.28318) - 3.14159, lat - c1.y));
	float r2 = length(vec2(mod(lon - c2.x + 3.14159, 6.28318) - 3.14159, lat - c2.y));
	float r3 = length(vec2(mod(lon - c3.x + 3.14159, 6.28318) - 3.14159, lat - c3.y));
	// Tiny eye — eyewall cloud density comes naturally from spiral arms converging
	float eye1 = mix(1.0, smoothstep(0.0, 0.022, r1), a1);
	float eye2 = mix(1.0, smoothstep(0.0, 0.018, r2), a2);
	float eye3 = mix(1.0, smoothstep(0.0, 0.015, r3), a3);
	n = clamp(n * eye1 * eye2 * eye3, 0.0, 1.0);

	// Sharp cloud edge profile — more defined, less wispy
	float nSharp = smoothstep(0.46, 0.82, n);
	float alpha  = nSharp * 0.88 * cloud_alpha_scale;

	// Limb fade — clouds go transparent toward the horizon; prevents dark edge ring
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	alpha *= smoothstep(0.0, 0.25, facing);

	// Puffiness pass 1: top-lit centres, darker cloud edges
	float topLight  = mix(0.74, 1.00, facing * facing);
	float edgeShade = smoothstep(0.46, 0.78, n);
	float brightness = mix(0.80, topLight, edgeShade);

	// Puffiness pass 2: high-frequency cauliflower texture on cloud tops
	// Samples noise at 7× the base frequency to create surface knobble texture
	float cauli    = fbm(sphere_pos * 7.0 + vec3(t * 1.2, t * 0.7, t * 1.0)) * 0.5 + 0.5;
	float cauliFac = edgeShade * facing * 0.30;   // only on dense-top visible areas
	brightness     = brightness * (1.0 - cauliFac) + (brightness * mix(0.82, 1.08, cauli)) * cauliFac;

	ALBEDO    = vec3(clamp(brightness, 0.0, 1.0), clamp(brightness, 0.0, 1.0), clamp(brightness * 0.97, 0.0, 1.0));
	ALPHA     = alpha;
	ROUGHNESS = 1.0;
	METALLIC  = 0.0;
}
";

	// ── Inner cloud layer — base stratocumulus, no cyclones, slightly grey undersides ──

	private const string CloudInnerShaderCode = @"
shader_type spatial;
render_mode cull_back, blend_mix;

uniform float seed_offset       = 0.0;
uniform float cloud_alpha_scale : hint_range(0.0, 1.0) = 1.0;

varying vec3 sphere_pos;

void vertex() { sphere_pos = VERTEX; }

vec3 ghash(vec3 p) {
	vec3 h = fract(sin(vec3(dot(p,vec3(127.1,311.7,74.7)),
	                        dot(p,vec3(269.5,183.3,246.1)),
	                        dot(p,vec3(113.5,271.9,124.6)))) * 43758.5453);
	return normalize(h * 2.0 - 1.0);
}
float gnoise(vec3 p) {
	vec3 i=floor(p); vec3 f=fract(p);
	vec3 u=f*f*f*(f*(f*6.0-15.0)+10.0);
	return 2.0*mix(mix(mix(dot(ghash(i),f),dot(ghash(i+vec3(1,0,0)),f-vec3(1,0,0)),u.x),
	                   mix(dot(ghash(i+vec3(0,1,0)),f-vec3(0,1,0)),dot(ghash(i+vec3(1,1,0)),f-vec3(1,1,0)),u.x),u.y),
	               mix(mix(dot(ghash(i+vec3(0,0,1)),f-vec3(0,0,1)),dot(ghash(i+vec3(1,0,1)),f-vec3(1,0,1)),u.x),
	                   mix(dot(ghash(i+vec3(0,1,1)),f-vec3(0,1,1)),dot(ghash(i+vec3(1,1,1)),f-vec3(1,1,1)),u.x),u.y),u.z);
}
float fbm(vec3 p) {
	float v=0.0,a=0.5;
	for(int i=0;i<5;i++){v+=a*gnoise(p);p*=2.0;a*=0.5;}
	return v*0.5+0.5;
}

void fragment() {
	float t = TIME * 0.016 + seed_offset;
	float n = fbm(sphere_pos * 1.65 + vec3(t * 0.7, t * 0.4, t * 0.6));

	float alpha = clamp((n - 0.56) * 4.0, 0.0, 0.50) * cloud_alpha_scale;

	// Limb fade — kills the dark edge ring
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	alpha *= smoothstep(0.0, 0.28, facing);

	// Undersides are slightly grey (outer layer above blocks direct light)
	float shade      = mix(0.70, 0.92, facing);
	float density    = smoothstep(0.56, 0.78, n);
	float brightness = mix(shade, 0.93, density);

	ALBEDO    = vec3(brightness, brightness * 0.99, brightness * 0.97);
	ALPHA     = alpha;
	ROUGHNESS = 1.0;
	METALLIC  = 0.0;
}
";

	// ── Grid sphere shader (pure UV math — no texture, always on top) ──────────

	private const string GridShaderCode = @"
shader_type spatial;
render_mode cull_back, blend_add, unshaded, depth_draw_never, depth_test_disabled;

uniform int selected_gi = -1;
uniform int selected_gj = -1;

void fragment() {
	float fu = fract(UV.x * 24.0);
	float fv = fract(UV.y * 12.0);
	int   ci = int(UV.x * 24.0);
	int   cj = int(UV.y * 12.0);

	bool in_sel = (selected_gi >= 0) && (ci == selected_gi) && (cj == selected_gj);

	// Regular grid lines — suppressed on the selected cell
	float line = 0.0;
	if (!in_sel) {
		line = clamp(
			step(fu, 0.018) + step(0.982, fu) +
			step(fv, 0.025) + step(0.975, fv), 0.0, 1.0);
	}

	// Selected cell: soft cyan glow band — wider band with falloff, matches slab rim
	float glow = 0.0;
	if (in_sel) {
		float bw  = 0.12;
		float eu  = min(fu, 1.0 - fu) / bw;
		float ev  = min(fv, 1.0 - fv) / bw;
		float rim = 1.0 - clamp(min(eu, ev), 0.0, 1.0);  // 1 at edge, 0 inside
		float pulse = 0.80 + sin(TIME * 2.2) * 0.20;
		glow = pow(rim, 1.5) * pulse;
	}

	EMISSION = vec3(0.85, 0.90, 0.95) * line + vec3(0.10, 0.70, 1.00) * glow;
	ALPHA    = max(line * 0.85, glow * 0.80);
}
";

	// ── Star field ────────────────────────────────────────────────────────────

	private sealed partial class StarField : Node2D
	{
		private const int Count = 300;
		private readonly Vector2[] _pos   = new Vector2[Count];
		private readonly float[]   _size  = new float[Count];
		private readonly float[]   _alpha = new float[Count];

		public void Init()
		{
			var rng = new RandomNumberGenerator();
			rng.Seed = 0x53544152u;
			for (int i = 0; i < Count; i++)
			{
				_pos[i] = new Vector2(rng.Randf(), rng.Randf());
				float t = rng.Randf();
				if      (t < 0.55f) { _size[i] = 1.0f; _alpha[i] = 0.20f + rng.Randf() * 0.15f; }
				else if (t < 0.85f) { _size[i] = 1.4f; _alpha[i] = 0.35f + rng.Randf() * 0.20f; }
				else                { _size[i] = 2.0f; _alpha[i] = 0.55f + rng.Randf() * 0.25f; }
			}
			QueueRedraw();
		}

		public override void _Draw()
		{
			var size = GetViewport().GetVisibleRect().Size;
			DrawRect(new Rect2(Vector2.Zero, size), new Color(0.01f, 0.01f, 0.055f, 1f));
			for (int i = 0; i < Count; i++)
				DrawCircle(new Vector2(_pos[i].X * size.X, _pos[i].Y * size.Y),
				           _size[i], new Color(1f, 1f, 1f, _alpha[i]));
		}
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static FastNoiseLite MakeNoise3D(int seed, float frequency, int octaves)
	{
		var n = new FastNoiseLite();
		n.Seed = seed; n.Frequency = frequency;
		n.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
		n.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
		n.FractalOctaves = octaves;
		return n;
	}

	private static float Norm(float v) => (v + 1f) * 0.5f;

	private static string Bar(float v, int w = 7)
	{
		int filled = Mathf.Clamp(Mathf.RoundToInt(v * w), 0, w);
		return new string('█', filled) + new string('░', w - filled);
	}

	private static string ElevLabel(float v) =>
		v < 0.30f ? "Deep basin" : v < 0.50f ? "Lowland" : v < 0.65f ? "Upland" : v < 0.80f ? "High" : "Extreme";

	private static string HumidLabel(float v) =>
		v < 0.25f ? "Arid" : v < 0.45f ? "Dry" : v < 0.60f ? "Moderate" : v < 0.75f ? "Moist" : "Saturated";

	private static string TempLabel(float v) =>
		v < 0.20f ? "Glacial" : v < 0.35f ? "Cold" : v < 0.55f ? "Temperate" : v < 0.70f ? "Warm" : "Hot";

	private static string BiomeName(BiomeType b) => b switch
	{
		BiomeType.DeepOcean  => "Deep Ocean",
		BiomeType.Ocean      => "Open Ocean",
		BiomeType.Coastal    => "Coastal",
		BiomeType.Desert     => "Alien Desert",
		BiomeType.Savanna    => "Savanna",
		BiomeType.Grassland  => "Grassland",
		BiomeType.Forest     => "Forest",
		BiomeType.Jungle     => "Jungle",
		BiomeType.AlienWilds => "Alien Wilds",
		BiomeType.Highland   => "Highland",
		BiomeType.Mountain   => "Mountain",
		BiomeType.Arctic     => "Polar Frost",
		BiomeType.SafeZone   => "Safe Zone",
		_                    => "Unknown",
	};

	// ── Palette loading ───────────────────────────────────────────────────────

	private void LoadPalettes()
	{
		_grassPal  = TryLoadImage("res://assets/sprites/tiles/Ground/Grass.png");
		_shorePal  = TryLoadImage("res://assets/sprites/tiles/Ground/Shore.png");
		_winterPal = TryLoadImage("res://assets/sprites/tiles/Ground/Winter.png");
	}

	private static Image? TryLoadImage(string path)
	{
		try { return Image.LoadFromFile(path); } catch { return null; }
	}

	// ── Debug sliders ─────────────────────────────────────────────────────────

	private void BuildDebugSliders()
	{
		_sliderLayer         = new CanvasLayer();
		_sliderLayer.Layer   = 10;
		_sliderLayer.Visible = false;
		AddChild(_sliderLayer);

		var panel = new PanelContainer { Position = new Vector2(20f, 20f) };
		_sliderLayer.AddChild(panel);
		var vbox = new VBoxContainer();
		vbox.CustomMinimumSize = new Vector2(300f, 0f);
		panel.AddChild(vbox);
		vbox.AddChild(new Label { Text = "Debug  [F3 to close]" });

		AddSliderRow(vbox, "Mountains",   0f, 0.30f, _dispScale,  v => { _dispScale  = v; UpdateShaderParams(); });
		AddSliderRow(vbox, "Water Depth", 0f, 1.0f,  _waterDepth, v => { _waterDepth = v; UpdateShaderParams(); });
		AddSliderRow(vbox, "Saturation",  0.8f, 2.0f, _satBoost,  v => { _satBoost   = v; UpdateShaderParams(); });
	}

	private static void AddSliderRow(VBoxContainer p, string label, float min, float max, float init, System.Action<float> cb)
	{
		var hbox = new HBoxContainer(); p.AddChild(hbox);
		hbox.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(100f, 0f) });
		var s = new HSlider { MinValue = min, MaxValue = max, Value = init, Step = 0.01f, CustomMinimumSize = new Vector2(150f, 24f) };
		hbox.AddChild(s);
		var lbl = new Label { Text = init.ToString("F2"), CustomMinimumSize = new Vector2(40f, 0f) };
		hbox.AddChild(lbl);
		s.ValueChanged += v => { lbl.Text = ((float)v).ToString("F2"); cb((float)v); };
	}

	private void UpdateShaderParams()
	{
		if (_sphere.GetActiveMaterial(0) is ShaderMaterial mat)
		{
			mat.SetShaderParameter("displacement_scale", _dispScale);
			mat.SetShaderParameter("water_depth_blend",  _waterDepth);
			mat.SetShaderParameter("sat_boost",          _satBoost);
		}
	}

	private static Color Swatch(Image? pal, int i, Color fallback) =>
		pal is null ? fallback : pal.GetPixel(i * 16 + 8, Mathf.Min(8, pal.GetHeight() - 1));

	// ── Biome colours ─────────────────────────────────────────────────────────

	private Color BiomeToColor(BiomeType b, float d, float elev, ChunkGenerator.PlanetParams planet)
	{
		const float W = 0.52f;
		float hiShade = Mathf.Clamp((elev - 0.52f) / 0.28f, 0f, 1f);
		float loShade = Mathf.Clamp((0.58f - elev) / 0.28f, 0f, 1f);

		return b switch
		{
			BiomeType.DeepOcean =>
				new Color(0.00f, 0.01f, 0.08f).Lerp(new Color(0.00f, 0.03f, 0.12f), d * W),
			BiomeType.Ocean =>
				new Color(0.01f, 0.05f, 0.20f).Lerp(new Color(0.01f, 0.08f, 0.26f), d * W),
			BiomeType.Coastal =>
				new Color(0.10f, 0.42f, 0.66f).Lerp(new Color(0.14f, 0.52f, 0.74f), d * W),
			BiomeType.Grassland =>
				new Color(0.26f, 0.38f, 0.22f).Lerp(new Color(0.34f, 0.46f, 0.26f), d * W).Lightened(hiShade * 0.14f),
			BiomeType.Forest =>
				new Color(0.18f, 0.28f, 0.12f).Lerp(new Color(0.22f, 0.34f, 0.14f), d * W).Darkened(loShade * 0.16f),
			BiomeType.Jungle =>
				new Color(0.10f, 0.18f, 0.07f).Darkened(0.04f + d * 0.10f + loShade * 0.10f),
			BiomeType.AlienWilds =>
				new Color(0.08f + d * 0.06f, 0.20f + d * 0.08f, 0.12f + hiShade * 0.12f).Darkened(0.08f),
			BiomeType.Desert =>
				new Color(0.54f, 0.42f, 0.20f).Lerp(new Color(0.64f, 0.52f, 0.26f), d * W)
				                               .Lerp(new Color(0.70f, 0.58f, 0.30f), hiShade * 0.30f),
			BiomeType.Savanna =>
				new Color(0.48f, 0.44f, 0.18f).Lerp(new Color(0.56f, 0.50f, 0.22f), d * W).Lightened(hiShade * 0.10f),
			BiomeType.Highland =>
				new Color(0.34f, 0.28f, 0.22f).Lerp(new Color(0.42f, 0.34f, 0.26f), d * W).Lightened(hiShade * 0.18f),
			BiomeType.Mountain =>
				new Color(0.30f, 0.30f, 0.34f).Lerp(new Color(0.70f, 0.72f, 0.76f), d * W + hiShade * 0.60f),
			BiomeType.Arctic =>
				new Color(0.70f, 0.80f, 0.86f).Lerp(new Color(0.88f, 0.93f, 0.97f), d * W + hiShade * 0.14f),
			BiomeType.SafeZone =>
				new Color(0.20f, 0.52f, 0.26f),
			_ => new Color(0.16f, 0.18f, 0.14f),
		};
	}

	// ── Cell panel helpers ────────────────────────────────────────────────────

	private static Label CLabel(string text, float x, float y, int size, Color color)
	{
		var lbl = new Label { Text = text, Position = new Vector2(x, y) };
		lbl.AddThemeFontSizeOverride("font_size", size);
		lbl.AddThemeColorOverride("font_color", color);
		lbl.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
		lbl.AddThemeConstantOverride("shadow_offset_x", 1);
		lbl.AddThemeConstantOverride("shadow_offset_y", 1);
		return lbl;
	}

	private void AddCardDivider(int cardW, float y)
	{
		_cellCard!.AddChild(new ColorRect
		{
			Position = new Vector2(20, y),
			Size     = new Vector2(cardW - 40, 1),
			Color    = CellBorder,
		});
	}
}
