using Microsoft.Xna.Framework;

namespace OpenElexSharp.Graphics;

/// <summary>Converts Genome scene transforms into MonoGame matrices.</summary>
public static class SceneTransforms
{
	/// <summary>
	/// Converts a row-major <see cref="System.Numerics.Matrix4x4"/> to an XNA matrix with the same
	/// row-major element order.
	/// </summary>
	public static Matrix ToXna(System.Numerics.Matrix4x4 value) =>
		new(
			value.M11, value.M12, value.M13, value.M14,
			value.M21, value.M22, value.M23, value.M24,
			value.M31, value.M32, value.M33, value.M34,
			value.M41, value.M42, value.M43, value.M44);
}
