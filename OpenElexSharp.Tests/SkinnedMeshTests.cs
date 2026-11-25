using Microsoft.Xna.Framework;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class SkinnedMeshTests
{
	[Fact]
	public void NormalizeWeights_NormalizesAndKeepsIndices()
	{
		var weights = SkinnedMesh.NormalizeWeights(
			new Vector4(1f, 3f, 0f, 0f),
			new Vector4(1, 2, 0, 0),
			3,
			out var indices);

		Assert.Equal(0.25f, weights.X, precision: 4);
		Assert.Equal(0.75f, weights.Y, precision: 4);
		Assert.Equal(new Vector4(1, 2, 0, 0), indices);
	}

	[Fact]
	public void NormalizeWeights_ZeroWeights_FallBackToBoneZero()
	{
		var weights = SkinnedMesh.NormalizeWeights(
			Vector4.Zero,
			new Vector4(2, 1, 0, 0),
			3,
			out var indices);

		Assert.Equal(new Vector4(1f, 0f, 0f, 0f), weights);
		Assert.Equal(Vector4.Zero, indices);
	}

	[Fact]
	public void NormalizeWeights_ClampsOutOfRangeBones()
	{
		_ = SkinnedMesh.NormalizeWeights(
			new Vector4(1f, 0f, 0f, 0f),
			new Vector4(99, 255, 1, 2),
			3,
			out var indices);

		Assert.Equal(new Vector4(2, 2, 1, 2), indices);
	}
}
