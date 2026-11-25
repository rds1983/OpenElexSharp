using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace OpenElexSharp.Graphics;

/// <summary>
/// GPU skinned mesh built from an <c>skn</c> GMSH resource and its embedded skeleton. The bind pose
/// is rendered with identity bone transforms; the inverse bind matrices are kept for animation.
/// </summary>
public sealed class SkinnedMesh : IDisposable
{
	private readonly Matrix[] _inverseBindPose;
	private bool _disposed;

	private SkinnedMesh(
		DrMesh mesh,
		Skeleton skeleton,
		GmshMesh source,
		Matrix[] inverseBindPose)
	{
		Mesh = mesh;
		Skeleton = skeleton;
		Source = source;
		_inverseBindPose = inverseBindPose;
	}

	/// <summary>The GPU mesh parts; all parts share one skinned vertex buffer.</summary>
	public DrMesh Mesh { get; }

	/// <summary>The embedded bind-pose skeleton.</summary>
	public Skeleton Skeleton { get; }

	/// <summary>The parsed source geometry.</summary>
	public GmshMesh Source { get; }

	/// <summary>Number of bones expected by the skinned vertex buffer.</summary>
	public int BoneCount => Skeleton.BoneCount;

	/// <summary>Inverse bind-pose matrices in skeleton order, for animation skinning.</summary>
	public IReadOnlyList<Matrix> InverseBindPose => _inverseBindPose;

	/// <summary>
	/// Builds a skinned mesh. <paramref name="materialResolver"/> maps metadata material names to
	/// textures; missing materials fall back to the renderer checker texture.
	/// </summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="GmshFormatException">The source has no skinning data or usable geometry.</exception>
	public static SkinnedMesh Load(
		GraphicsDevice device,
		GmshMesh source,
		Skeleton skeleton,
		MeshMetadata? metadata = null,
		Func<string, ElexMaterial?>? materialResolver = null)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(skeleton);
		if (!source.HasSkinning)
		{
			throw new GmshFormatException($"Mesh '{source.Name}' has no skinning attributes.");
		}

		if (skeleton.BoneCount == 0 || skeleton.BoneCount > SkinnedEffect.MaxBones)
		{
			throw new GmshFormatException(
				$"Skeleton for '{source.Name}' has {skeleton.BoneCount} bones; " +
				$"SkinnedEffect supports 1-{SkinnedEffect.MaxBones}.");
		}

		if (!source.IsTriangleList || source.Indices.Count == 0)
		{
			throw new GmshFormatException($"Mesh '{source.Name}' has no usable triangle list.");
		}

		var positions = source.Positions;
		var normals = source.Normals.Count == positions.Count
			? source.Normals
			: MeshLoader.ComputeNormals(positions, source.Indices);
		var textureCoordinates = source.TextureCoordinates.Count == positions.Count
			? source.TextureCoordinates
			: null;
		var boneIndices = source.BoneIndices;
		var boneWeights = source.BoneWeights;
		var vertices = new VertexPositionNormalTextureSkinned[positions.Count];
		for (var index = 0; index < vertices.Length; index++)
		{
			var rawIndices = boneIndices[index];
			var rawWeights = boneWeights[index];
			var normalized = NormalizeWeights(
				rawWeights,
				rawIndices,
				skeleton.BoneCount,
				out var indices);
			vertices[index] = new VertexPositionNormalTextureSkinned(
				new Vector3(positions[index].X, positions[index].Y, positions[index].Z),
				new Vector3(normals[index].X, normals[index].Y, normals[index].Z),
				textureCoordinates is null
					? Vector2.Zero
					: new Vector2(textureCoordinates[index].X, textureCoordinates[index].Y),
				normalized,
				new Color(
					(byte)indices.X,
					(byte)indices.Y,
					(byte)indices.Z,
					(byte)indices.W));
		}

		var indicesArray = new int[source.Indices.Count];
		for (var index = 0; index < indicesArray.Length; index++)
		{
			indicesArray[index] = source.Indices[index];
		}

		var vertexBuffer = new VertexBuffer(
			device,
			VertexPositionNormalTextureSkinned.VertexDeclaration,
			vertices.Length,
			BufferUsage.WriteOnly);
		vertexBuffer.SetData(vertices);
		var indexBuffer = CreateIndexBuffer(device, indicesArray);
		var bounds = ComputeBounds(positions);
		var mesh = new DrMesh { Name = source.Name };
		foreach (var range in MeshLoader.ResolveSubmeshes(source, metadata))
		{
			var rangeBox = range.Bounds ?? bounds;
			var center = (rangeBox.Min + rangeBox.Max) * 0.5f;
			var sphere = new BoundingSphere(
				center,
				Math.Max(0.001f, Vector3.Distance(center, rangeBox.Max)));
			var material = materialResolver is null || string.IsNullOrEmpty(range.MaterialName)
				? null
				: materialResolver(range.MaterialName);
			var part = new DrMeshPart(
				vertexBuffer,
				indexBuffer,
				rangeBox,
				PrimitiveType.TriangleList,
				vertices.Length,
				range.PrimitiveCount,
				0,
				range.StartIndex)
			{
				Tag = new DrMeshPartTag(
					sphere,
					material,
					range.StartIndex,
					range.IndexCount,
					metadata?.LODRangeStart ?? 0f),
			};
			mesh.MeshParts.Add(part);
		}

		return new SkinnedMesh(mesh, skeleton, source, ComputeInverseBindPose(skeleton));
	}

	/// <summary>Returns identity transforms for rendering the mesh in its authored bind pose.</summary>
	public Matrix[] CreateBindPoseTransforms()
	{
		var transforms = new Matrix[BoneCount];
		Array.Fill(transforms, Matrix.Identity);
		return transforms;
	}

	internal static Vector4 NormalizeWeights(
		Vector4 rawWeights,
		Vector4 rawIndices,
		int boneCount,
		out Vector4 indices)
	{
		var maxIndex = Math.Max(0, boneCount - 1);
		indices = new Vector4(
			Math.Clamp((int)MathF.Round(rawIndices.X), 0, maxIndex),
			Math.Clamp((int)MathF.Round(rawIndices.Y), 0, maxIndex),
			Math.Clamp((int)MathF.Round(rawIndices.Z), 0, maxIndex),
			Math.Clamp((int)MathF.Round(rawIndices.W), 0, maxIndex));
		var weights = new Vector4(
			MathF.Max(0f, rawWeights.X),
			MathF.Max(0f, rawWeights.Y),
			MathF.Max(0f, rawWeights.Z),
			MathF.Max(0f, rawWeights.W));
		var sum = weights.X + weights.Y + weights.Z + weights.W;
		if (sum <= 1e-6f)
		{
			indices = Vector4.Zero;
			return new Vector4(1f, 0f, 0f, 0f);
		}

		weights /= sum;
		return weights;
	}

	private static Matrix[] ComputeInverseBindPose(Skeleton skeleton)
	{
		var count = skeleton.BoneCount;
		if (skeleton.Bones.Count == 0)
		{
			return [];
		}

		var positions = new NumericsVector3[count];
		var rotations = new NumericsQuaternion[count];
		var parents = new int[count];
		for (var index = 0; index < count; index++)
		{
			positions[index] = index < skeleton.Bones.Count
				? skeleton.Bones[index].Position
				: NumericsVector3.Zero;
			rotations[index] = index < skeleton.Bones.Count
				? skeleton.Bones[index].Rotation
				: NumericsQuaternion.Identity;
			if (index == 0)
			{
				parents[0] = -1;
			}
			else
			{
				var parent = index - 1 < skeleton.ParentIndices.Count
					? (int)skeleton.ParentIndices[index - 1]
					: index - 1;
				parents[index] = parent >= 0 && parent < count && parent != index ? parent : -1;
			}
		}

		var world = new SkeletonPose(positions, rotations, parents).ComputeWorldMatrices();
		var inverse = new Matrix[count];
		for (var index = 0; index < count; index++)
		{
			inverse[index] = Matrix.Invert(SceneTransforms.ToXna(world[index]));
		}

		return inverse;
	}

	private static IndexBuffer CreateIndexBuffer(GraphicsDevice device, int[] indices)
	{
		var use32Bit = indices.Any(index => index > ushort.MaxValue);
		var indexBuffer = use32Bit
			? new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly)
			: new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
		if (use32Bit)
		{
			indexBuffer.SetData(indices);
		}
		else
		{
			var shorts = new ushort[indices.Length];
			for (var index = 0; index < shorts.Length; index++)
			{
				shorts[index] = (ushort)indices[index];
			}

			indexBuffer.SetData(shorts);
		}

		return indexBuffer;
	}

	private static BoundingBox ComputeBounds(IReadOnlyList<NumericsVector3> positions)
	{
		var min = positions[0];
		var max = positions[0];
		foreach (var position in positions)
		{
			min = NumericsVector3.Min(min, position);
			max = NumericsVector3.Max(max, position);
		}

		return new BoundingBox(
			new Vector3(min.X, min.Y, min.Z),
			new Vector3(max.X, max.Y, max.Z));
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Mesh.Dispose();
	}
}
