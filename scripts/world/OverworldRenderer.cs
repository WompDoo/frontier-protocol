using Godot;
using System.Collections.Generic;

/// <summary>
/// 3D terrain renderer displayed on CanvasLayer(-5), beneath all 2D content.
/// A SubViewport holds the 3D scene; a TextureRect fills the screen with the result.
///
/// Camera math: the Camera3D at RotationDegrees(-45,0,0) produces an orthographic
/// projection that is pixel-identical to the 2D isometric system:
///   screen_x = x3d = (u-v)*HTW
///   screen_y = (z3d - y3d) * √2/2 = (u+v)*HTH - h
///
/// Camera position for 2D cam at (camX, camY):
///   3D position = (camX, CamHeight, camY*√2 + CamHeight)
///   Camera3D.Size = viewportHeight / Camera2D.Zoom.Y
/// </summary>
public partial class OverworldRenderer : CanvasLayer
{
	private const float Sqrt2     = 1.41421356f;
	private const float CamHeight = 800f;

	[Export] public NodePath PlayerCameraPath { get; set; }

	private Camera2D    _camera2D;
	private SubViewport _viewport;
	private Camera3D    _camera3D;
	private TextureRect _textureRect;
	private Node3D      _terrainRoot;

	private readonly Dictionary<Vector2I, TerrainChunk3D> _chunks = new();

	public override void _Ready()
	{
		Layer   = -5;
		Visible = false;  // hidden by default; WorldScene calls SetActive(true) on EnterIso

		// SubViewport — renders the 3D scene
		// TransparentBg=true lets the 2D star background show through water/void tiles.
		_viewport = new SubViewport();
		_viewport.TransparentBg           = true;
		_viewport.RenderTargetUpdateMode  = SubViewport.UpdateMode.Disabled;  // enabled in SetActive
		_viewport.RenderTargetClearMode   = SubViewport.ClearMode.Always;
		AddChild(_viewport);

		// TextureRect — fills the screen with the SubViewport texture
		_textureRect                    = new TextureRect();
		_textureRect.Texture            = _viewport.GetTexture();
		_textureRect.StretchMode        = TextureRect.StretchModeEnum.Scale;
		_textureRect.AnchorRight        = 1f;
		_textureRect.AnchorBottom       = 1f;
		_textureRect.MouseFilter        = Control.MouseFilterEnum.Ignore;
		AddChild(_textureRect);

		// Root node for all terrain chunks
		_terrainRoot = new Node3D();
		_viewport.AddChild(_terrainRoot);

		// Directional light (sun-like, angled from north-west)
		var sun = new DirectionalLight3D();
		sun.LightEnergy   = 1.2f;
		sun.RotationDegrees = new Vector3(-55f, 30f, 0f);
		_viewport.AddChild(sun);

		// World environment — transparent bg so the 2D star layer shows through
		var worldEnv = new WorldEnvironment();
		var env      = new Environment();
		env.BackgroundMode     = Environment.BGMode.Color;
		env.BackgroundColor    = new Color(0f, 0f, 0f, 0f);
		env.AmbientLightSource = Environment.AmbientSource.Color;
		env.AmbientLightColor  = Colors.White;
		env.AmbientLightEnergy = 0.45f;
		worldEnv.Environment   = env;
		_viewport.AddChild(worldEnv);

		// Camera
		_camera3D            = new Camera3D();
		_camera3D.Projection = Camera3D.ProjectionType.Orthogonal;
		_camera3D.RotationDegrees = new Vector3(-45f, 0f, 0f);
		_camera3D.Size       = 100f;  // overwritten each frame
		_camera3D.Near       = 0.1f;
		_camera3D.Far        = 8000f;
		_viewport.AddChild(_camera3D);

		if (PlayerCameraPath != null)
			_camera2D = GetNode<Camera2D>(PlayerCameraPath);
	}

	public override void _Process(double delta)
	{
		if (_camera2D is null || _viewport is null) return;
		SyncCamera();
	}

	private void SyncCamera()
	{
		var vpSize = GetViewport().GetVisibleRect().Size;

		// Keep SubViewport same size as the screen
		var vpSizeI = new Vector2I((int)vpSize.X, (int)vpSize.Y);
		if (_viewport.Size != vpSizeI)
		{
			_viewport.Size = vpSizeI;
			_textureRect.SetSize(vpSize);
		}

		// 2D camera position in world space (global position of Camera2D node)
		Vector2 cam2d = _camera2D.GlobalPosition;
		float   zoom  = _camera2D.Zoom.Y;

		// 3D camera sits above the 2D center; looking at (cam2d.X, 0, cam2d.Y*√2)
		_camera3D.Position = new Vector3(cam2d.X, CamHeight, cam2d.Y * Sqrt2 + CamHeight);

		// Ortho size = visible world height (in screen-pixel-equivalent units)
		_camera3D.Size = vpSize.Y / zoom;
	}

	// ── Visibility / activity ─────────────────────────────────────────────

	/// <summary>
	/// Called by WorldScene when switching between globe and iso mode.
	/// Pauses SubViewport rendering in globe mode to avoid wasted GPU work.
	/// </summary>
	public void SetActive(bool active)
	{
		Visible = active;
		_viewport.RenderTargetUpdateMode = active
			? SubViewport.UpdateMode.Always
			: SubViewport.UpdateMode.Disabled;
	}

	// ── Public chunk management ────────────────────────────────────────────

	public void AddChunk(Vector2I coord, ChunkData data, int worldSeed, float seaLevel, RiverCrossing? river)
	{
		if (_chunks.ContainsKey(coord)) return;

		Vector2 worldPos2D = ChunkManager.ChunkToWorld(coord);
		var tc = new TerrainChunk3D();
		_terrainRoot.AddChild(tc);
		// Chunk position in 3D: same XZ as 2D world position; Y=0 is the terrain base plane
		tc.Position = new Vector3(worldPos2D.X, 0f, worldPos2D.Y * Sqrt2);
		tc.GenerateFromChunk(data, worldSeed, seaLevel, river);
		_chunks[coord] = tc;
	}

	public void RemoveChunk(Vector2I coord)
	{
		if (!_chunks.TryGetValue(coord, out var tc)) return;
		tc.QueueFree();
		_chunks.Remove(coord);
	}

	public void ClearAll()
	{
		foreach (var tc in _chunks.Values)
			tc.QueueFree();
		_chunks.Clear();
	}
}
