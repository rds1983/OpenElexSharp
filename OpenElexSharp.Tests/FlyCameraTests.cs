using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class FlyCameraTests
{
	[Fact]
	public void Forward_DefaultLookDirection_IsPositiveZ()
	{
		var camera = new FlyCamera();

		Assert.Equal(new Vector3(0, 0, 1), camera.Forward, new VectorComparer(1e-6f));
		Assert.Equal(new Vector3(1, 0, 0), camera.Right, new VectorComparer(1e-6f));
	}

	[Fact]
	public void Forward_YawNinetyDegrees_LooksAlongPositiveX()
	{
		var camera = new FlyCamera { Yaw = MathHelper.PiOver2 };

		Assert.Equal(new Vector3(1, 0, 0), camera.Forward, new VectorComparer(1e-6f));
	}

	[Fact]
	public void Look_ClampsPitch()
	{
		var camera = new FlyCamera();

		camera.Look(0, -100_000);

		Assert.InRange(camera.Pitch, 1.553f, 1.554f);
		Assert.True(camera.Forward.Y > 0);
	}

	[Fact]
	public void Look_TurnsYawWithPointerDelta()
	{
		var camera = new FlyCamera { LookSensitivity = 0.5f };

		camera.Look(2, 0);

		Assert.Equal(1f, camera.Yaw, 1e-6f);
	}

	[Fact]
	public void UpdateLook_ArrowKeysTurnAndClamp()
	{
		var camera = new FlyCamera();

		camera.UpdateLook(new KeyboardState(Keys.Right), 1f);
		Assert.True(camera.Yaw > 0);

		camera.UpdateLook(new KeyboardState(Keys.Up), 100f);
		Assert.InRange(camera.Pitch, 1.553f, 1.554f);
	}

	[Fact]
	public void UpdateMovement_ForwardMovesAtBaseSpeed()
	{
		var camera = new FlyCamera { MoveSpeed = 100f };

		camera.UpdateMovement(new KeyboardState(Keys.W), 1f);

		Assert.Equal(new Vector3(0, 0, 100), camera.Position, new VectorComparer(1e-4f));
	}

	[Fact]
	public void UpdateMovement_ShiftUsesFastMultiplier()
	{
		var camera = new FlyCamera { MoveSpeed = 100f, FastMultiplier = 5f };

		camera.UpdateMovement(new KeyboardState(Keys.W, Keys.LeftShift), 1f);

		Assert.Equal(new Vector3(0, 0, 500), camera.Position, new VectorComparer(1e-4f));
	}

	[Fact]
	public void View_LooksFromPositionTowardForward()
	{
		var camera = new FlyCamera { Position = new Vector3(3, 4, 5) };

		var inverted = Matrix.Invert(camera.View);

		Assert.Equal(camera.Position, inverted.Translation, new VectorComparer(1e-4f));
	}

	private sealed class VectorComparer(float tolerance) : IEqualityComparer<Vector3>
	{
		public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;

		public int GetHashCode(Vector3 obj) => obj.GetHashCode();
	}
}
