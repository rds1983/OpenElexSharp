using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Free-fly camera with yaw/pitch look, WASD/QE movement, Shift acceleration and arrow-key look.
/// Mouse look is applied by the host from pointer deltas so the camera stays testable without a
/// device.
/// </summary>
public sealed class FlyCamera
{
	private const float MaximumPitch = 1.553343f; // 89 degrees
	private const float ArrowLookSpeed = 1.5f;

	/// <summary>World-space eye position.</summary>
	public Vector3 Position { get; set; }

	/// <summary>Yaw in radians; zero looks toward +Z, positive turns right (+X).</summary>
	public float Yaw { get; set; }

	/// <summary>Pitch in radians; positive looks up, clamped to ±89°.</summary>
	public float Pitch { get; set; }

	/// <summary>Vertical field of view in radians.</summary>
	public float FieldOfView { get; set; } = MathHelper.PiOver4;

	/// <summary>Near clip distance.</summary>
	public float NearPlane { get; set; } = 1f;

	/// <summary>Far clip distance.</summary>
	public float FarPlane { get; set; } = 200_000f;

	/// <summary>Base movement speed in units per second.</summary>
	public float MoveSpeed { get; set; } = 2_000f;

	/// <summary>Multiplier applied while Shift is held.</summary>
	public float FastMultiplier { get; set; } = 5f;

	/// <summary>Mouse-look sensitivity in radians per pointer pixel.</summary>
	public float LookSensitivity { get; set; } = 0.0025f;

	/// <summary>Unit forward vector derived from yaw and pitch.</summary>
	public Vector3 Forward
	{
		get
		{
			var cosPitch = MathF.Cos(Pitch);
			return new Vector3(
				MathF.Sin(Yaw) * cosPitch,
				MathF.Sin(Pitch),
				MathF.Cos(Yaw) * cosPitch);
		}
	}

	/// <summary>Unit right vector on the horizontal plane.</summary>
	public Vector3 Right => Vector3.Normalize(Vector3.Cross(Vector3.Up, Forward));

	/// <summary>View matrix for the current pose.</summary>
	public Matrix View => Matrix.CreateLookAt(Position, Position + Forward, Vector3.Up);

	/// <summary>Projection matrix for the given viewport aspect ratio.</summary>
	public Matrix Projection(float aspectRatio) =>
		Matrix.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);

	/// <summary>Applies a mouse/pointer delta in pixels. Downward movement looks down.</summary>
	public void Look(float deltaX, float deltaY)
	{
		Yaw += deltaX * LookSensitivity;
		Pitch = Math.Clamp(Pitch - deltaY * LookSensitivity, -MaximumPitch, MaximumPitch);
	}

	/// <summary>Applies arrow-key look for one frame.</summary>
	public void UpdateLook(KeyboardState keyboard, float seconds)
	{
		var yaw = 0f;
		var pitch = 0f;
		if (keyboard.IsKeyDown(Keys.Left))
		{
			yaw -= ArrowLookSpeed;
		}

		if (keyboard.IsKeyDown(Keys.Right))
		{
			yaw += ArrowLookSpeed;
		}

		if (keyboard.IsKeyDown(Keys.Up))
		{
			pitch += ArrowLookSpeed;
		}

		if (keyboard.IsKeyDown(Keys.Down))
		{
			pitch -= ArrowLookSpeed;
		}

		Yaw += yaw * seconds;
		Pitch = Math.Clamp(Pitch + pitch * seconds, -MaximumPitch, MaximumPitch);
	}

	/// <summary>Applies WASD/QE movement and Shift acceleration for one frame.</summary>
	public void UpdateMovement(KeyboardState keyboard, float seconds)
	{
		var direction = Vector3.Zero;
		if (keyboard.IsKeyDown(Keys.W))
		{
			direction += Forward;
		}

		if (keyboard.IsKeyDown(Keys.S))
		{
			direction -= Forward;
		}

		if (keyboard.IsKeyDown(Keys.D))
		{
			direction += Right;
		}

		if (keyboard.IsKeyDown(Keys.A))
		{
			direction -= Right;
		}

		if (keyboard.IsKeyDown(Keys.E))
		{
			direction += Vector3.UnitY;
		}

		if (keyboard.IsKeyDown(Keys.Q))
		{
			direction -= Vector3.UnitY;
		}

		if (direction.LengthSquared() <= 1e-8f)
		{
			return;
		}

		var speed = MoveSpeed;
		if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
		{
			speed *= FastMultiplier;
		}

		Position += Vector3.Normalize(direction) * speed * seconds;
	}
}
