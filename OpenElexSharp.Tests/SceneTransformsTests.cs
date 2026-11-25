using Microsoft.Xna.Framework;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class SceneTransformsTests
{
	[Fact]
	public void ToXna_PreservesRowMajorElements()
	{
		var source = new System.Numerics.Matrix4x4(
			1, 2, 3, 4,
			5, 6, 7, 8,
			9, 10, 11, 12,
			13, 14, 15, 16);

		var result = SceneTransforms.ToXna(source);

		Assert.Equal(new Matrix(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16), result);
	}

	[Fact]
	public void ToXna_PreservesTranslation()
	{
		var source = System.Numerics.Matrix4x4.CreateTranslation(10, 20, 30);

		var result = SceneTransforms.ToXna(source);

		Assert.Equal(new Vector3(10, 20, 30), result.Translation);
	}
}
