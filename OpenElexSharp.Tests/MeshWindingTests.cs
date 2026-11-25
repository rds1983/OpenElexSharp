using System.Numerics;
using OpenElexSharp.Graphics;

namespace OpenElexSharp.Tests;

public class MeshWindingTests
{
	private static readonly Vector3[] Triangle =
	[
		new(0, 0, 0),
		new(1, 0, 0),
		new(0, 1, 0),
	];

	private static readonly Vector3[] UpNormals =
	[
		Vector3.UnitZ,
		Vector3.UnitZ,
		Vector3.UnitZ,
	];

	[Fact]
	public void Analyze_CounterClockwiseTriangle_Agrees()
	{
		var result = MeshWinding.Analyze(Triangle, UpNormals, [0, 1, 2]);

		Assert.Equal(1, result.Agreeing);
		Assert.Equal(0, result.Disagreeing);
		Assert.Equal(1f, result.AgreementRatio);
	}

	[Fact]
	public void Analyze_ClockwiseTriangle_Disagrees()
	{
		var result = MeshWinding.Analyze(Triangle, UpNormals, [0, 2, 1]);

		Assert.Equal(0, result.Agreeing);
		Assert.Equal(1, result.Disagreeing);
		Assert.Equal(0f, result.AgreementRatio);
	}

	[Fact]
	public void Analyze_DegenerateTriangle_CountsSeparately()
	{
		var positions = new[]
		{
			new Vector3(0, 0, 0),
			new Vector3(1, 0, 0),
			new Vector3(2, 0, 0),
		};

		var result = MeshWinding.Analyze(positions, UpNormals, [0, 1, 2]);

		Assert.Equal(1, result.Degenerate);
		Assert.Equal(0, result.Agreeing);
		Assert.Equal(0, result.AgreementRatio);
	}

	[Fact]
	public void Analyze_MixedTriangles_ReportsCounts()
	{
		var positions = new Vector3[6];
		Triangle.CopyTo(positions, 0);
		Triangle.CopyTo(positions, 3);
		var normals = new Vector3[6];
		UpNormals.CopyTo(normals, 0);
		UpNormals.CopyTo(normals, 3);

		var result = MeshWinding.Analyze(positions, normals, [0, 1, 2, 3, 5, 4]);

		Assert.Equal(1, result.Agreeing);
		Assert.Equal(1, result.Disagreeing);
		Assert.Equal(2, result.Total);
		Assert.Equal(0.5f, result.AgreementRatio);
	}

	[Fact]
	public void Analyze_RejectsMismatchedCounts()
	{
		Assert.Throws<ArgumentException>(
			() => MeshWinding.Analyze(Triangle, [Vector3.UnitZ], [0, 1, 2]));
	}

	[Fact]
	public void Analyze_RejectsOutOfRangeIndex()
	{
		Assert.Throws<ArgumentException>(
			() => MeshWinding.Analyze(Triangle, UpNormals, [0, 1, 3]));
	}

	[Fact]
	public void Analyze_NullArguments_Throw()
	{
		Assert.Throws<ArgumentNullException>(
			() => MeshWinding.Analyze(null!, UpNormals, [0, 1, 2]));
		Assert.Throws<ArgumentNullException>(
			() => MeshWinding.Analyze(Triangle, null!, [0, 1, 2]));
		Assert.Throws<ArgumentNullException>(
			() => MeshWinding.Analyze(Triangle, UpNormals, null!));
	}
}
