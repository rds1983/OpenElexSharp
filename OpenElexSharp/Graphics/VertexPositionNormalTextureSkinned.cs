using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Skinned vertex with four bone influences. The bone indices are stored as four bytes and the
/// weights as four floats, matching <see cref="SkinnedEffect"/> with
/// <c>WeightsPerVertex = 4</c>.
/// </summary>
public struct VertexPositionNormalTextureSkinned : IVertexType
{
	/// <summary>Object-space position.</summary>
	public Vector3 Position;

	/// <summary>Object-space normal.</summary>
	public Vector3 Normal;

	/// <summary>Diffuse texture coordinate.</summary>
	public Vector2 TextureCoordinate;

	/// <summary>Four bone weights, usually normalized to sum to one.</summary>
	public Vector4 BlendWeights;

	/// <summary>Four bone indices, one byte each.</summary>
	public Color BlendIndices;

	/// <summary>Vertex layout used by <see cref="SkinnedEffect"/>.</summary>
	public static readonly VertexDeclaration VertexDeclaration = new(
		new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
		new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
		new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
		new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.BlendWeight, 0),
		new VertexElement(48, VertexElementFormat.Byte4, VertexElementUsage.BlendIndices, 0));

	VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

	/// <summary>Creates a skinned vertex.</summary>
	public VertexPositionNormalTextureSkinned(
		Vector3 position,
		Vector3 normal,
		Vector2 textureCoordinate,
		Vector4 blendWeights,
		Color blendIndices)
	{
		Position = position;
		Normal = normal;
		TextureCoordinate = textureCoordinate;
		BlendWeights = blendWeights;
		BlendIndices = blendIndices;
	}
}
