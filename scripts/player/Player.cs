using Godot;

public partial class Player : CharacterBody2D
{
	private const float Speed      = 120f;
	private const float ShiftMult  = 10f;   // hold Shift for 10x speed
	private const float ZoomMin    = 0.05f;
	private const float ZoomMax    = 6.0f;
	private const float ZoomStep   = 0.25f;
	private const float ZoomSmooth = 12f;   // lerp speed toward target zoom

	private Camera2D _camera;
	private float    _zoomTarget = 3.0f;    // starting zoom — 3× gives ~10-tile viewport width

	public override void _Ready()
	{
		_camera     = GetNode<Camera2D>("Camera2D");
		_camera.Zoom = Vector2.One * _zoomTarget;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.ButtonIndex == MouseButton.WheelUp)
				AdjustZoom(ZoomStep);
			else if (mb.ButtonIndex == MouseButton.WheelDown)
				AdjustZoom(-ZoomStep);
			else if (mb.ButtonIndex == MouseButton.Right)
				GlobalPosition = GetGlobalMousePosition();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		var direction = Vector2.Zero;

		if (Input.IsKeyPressed(Key.W) || Input.IsActionPressed("ui_up"))
			direction += Vector2.Up;
		if (Input.IsKeyPressed(Key.S) || Input.IsActionPressed("ui_down"))
			direction += Vector2.Down;
		if (Input.IsKeyPressed(Key.A) || Input.IsActionPressed("ui_left"))
			direction += Vector2.Left;
		if (Input.IsKeyPressed(Key.D) || Input.IsActionPressed("ui_right"))
			direction += Vector2.Right;

		if (direction != Vector2.Zero)
			direction = direction.Normalized();

		float speed = Input.IsKeyPressed(Key.Shift) ? Speed * ShiftMult : Speed;
		Velocity = direction * speed;
		MoveAndSlide();
	}

	public override void _Process(double delta)
	{
		float current = _camera.Zoom.X;
		if (!Mathf.IsEqualApprox(current, _zoomTarget, 0.001f))
			_camera.Zoom = Vector2.One * Mathf.Lerp(current, _zoomTarget, (float)delta * ZoomSmooth);
	}

	private void AdjustZoom(float step)
	{
		_zoomTarget = Mathf.Clamp(_zoomTarget + step, ZoomMin, ZoomMax);
	}
}
