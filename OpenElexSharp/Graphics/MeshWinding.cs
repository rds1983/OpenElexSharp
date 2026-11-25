using NumericsVector3 = System.Numerics.Vector3;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Result of comparing each triangle's geometric normal with the decoded vertex normals.
/// </summary>
/// <param name="Agreeing">Triangles whose face normal points the same way as the vertices.</param>
/// <param name="Disagreeing">Triangles whose face normal points the opposite way.</param>
/// <param name="Degenerate">Triangles with zero area or an inconclusive dot product.</param>
public readonly record struct MeshWindingAgreement(
	int Agreeing,
	int Disagreeing,
	int Degenerate)
{
	/// <summary>Number of analyzed triangles.</summary>
	public int Total => Agreeing + Disagreeing + Degenerate;

	/// <summary>Share of non-degenerate triangles that agree with the vertex normals.</summary>
	public float AgreementRatio => Agreeing + Disagreeing == 0
		? 0f
		: Agreeing / (float)(Agreeing + Disagreeing);
}

/// <summary>
/// Determines triangle winding relative to the decoded vertex normals. A triangle is
/// <see cref="MeshWindingAgreement.Agreeing"/> when
/// <c>dot(cross(b - a, c - a), normal)</c> is positive, i.e. the index order is
/// counter-clockwise when viewed from the side the normal points to.
/// </summary>
public static class MeshWinding
{
	private const float AreaEpsilon = 1e-12f;

	/// <summary>Analyzes a mesh triangle list.</summary>
	/// <param name="positions">Per-vertex object-space positions.</param>
	/// <param name="normals">Per-vertex object-space normals.</param>
	/// <param name="indices">Triangle-list indices.</param>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// Position/normal counts differ, or an index is out of range.
	/// </exception>
	public static MeshWindingAgreement Analyze(
		IReadOnlyList<NumericsVector3> positions,
		IReadOnlyList<NumericsVector3> normals,
		IReadOnlyList<int> indices)
	{
		ArgumentNullException.ThrowIfNull(positions);
		ArgumentNullException.ThrowIfNull(normals);
		ArgumentNullException.ThrowIfNull(indices);
		if (positions.Count != normals.Count)
		{
			throw new ArgumentException("Positions and normals must have the same count.", nameof(normals));
		}

		var agreeing = 0;
		var disagreeing = 0;
		var degenerate = 0;
		for (var index = 0; index + 2 < indices.Count; index += 3)
		{
			var a = Index(positions, indices[index], nameof(indices));
			var b = Index(positions, indices[index + 1], nameof(indices));
			var c = Index(positions, indices[index + 2], nameof(indices));
			var face = NumericsVector3.Cross(b - a, c - a);
			if (face.LengthSquared() <= AreaEpsilon)
			{
				++degenerate;
				continue;
			}

			var normal =
				Index(normals, indices[index], nameof(indices)) +
				Index(normals, indices[index + 1], nameof(indices)) +
				Index(normals, indices[index + 2], nameof(indices));
			var dot = NumericsVector3.Dot(face, normal);
			if (dot > 0f)
			{
				++agreeing;
			}
			else if (dot < 0f)
			{
				++disagreeing;
			}
			else
			{
				++degenerate;
			}
		}

		return new MeshWindingAgreement(agreeing, disagreeing, degenerate);
	}

	private static NumericsVector3 Index(
		IReadOnlyList<NumericsVector3> values,
		int index,
		string parameterName)
	{
		if ((uint)index >= (uint)values.Count)
		{
			throw new ArgumentException($"Index {index} is outside the vertex list.", parameterName);
		}

		return values[index];
	}
}
