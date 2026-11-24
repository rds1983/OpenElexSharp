using System.Numerics;

namespace OpenElexSharp.Assets;

/// <summary>One sparse vertex delta for a blend-shape target.</summary>
/// <param name="VertexIndex">Base-mesh vertex index.</param>
/// <param name="Position">Quantized position delta in model units.</param>
/// <param name="Normal">Quantized normal delta.</param>
public sealed record ElexBlendShapeDelta(int VertexIndex, Vector3 Position, Vector3 Normal);

/// <summary>A named blend-shape target with its non-zero vertex deltas.</summary>
public sealed class ElexBlendShapeTarget
{
	/// <summary>Creates a target.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	public ElexBlendShapeTarget(string name, IReadOnlyList<ElexBlendShapeDelta> deltas)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(deltas);
		Name = name;
		Deltas = deltas;
	}

	/// <summary>Target name, for example <c>blendShape1.jaw_open</c>.</summary>
	public string Name { get; }

	/// <summary>Non-zero deltas in vertex order.</summary>
	public IReadOnlyList<ElexBlendShapeDelta> Deltas { get; }
}
