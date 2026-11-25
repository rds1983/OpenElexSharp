using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Builds the DigitalRiseModel scene model for an ELEX <c>skn</c> resource and its embedded
/// skeleton. The result is an <see cref="ElexModel"/>: a model instance, a skinned mesh and the
/// per-part draw materials.
/// </summary>
public static class ElexModelLoader
{
	/// <summary>Builds a model from a parsed skn resource.</summary>
	/// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
	/// <exception cref="GmshFormatException">The mesh has no skinning data or the skeleton is empty.</exception>
	public static ElexModel Load(
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

		var boneCount = skeleton.Names.Count;
		if (boneCount == 0)
		{
			throw new GmshFormatException($"Skeleton for '{source.Name}' has no bones.");
		}

		var skeletonBones = new DrModelBone[boneCount];
		var parents = new int[boneCount];
		for (var index = 0; index < boneCount; index++)
		{
			var local = index < skeleton.Bones.Count
				? skeleton.Bones[index]
				: new SkeletonBone(index, NumericsVector3.Zero, NumericsQuaternion.Identity);
			skeletonBones[index] = new DrModelBone(skeleton.Names[index], null)
			{
				DefaultPose = new SrtTransform(
					new Vector3(local.Position.X, local.Position.Y, local.Position.Z),
					new Quaternion(local.Rotation.X, local.Rotation.Y, local.Rotation.Z, local.Rotation.W),
					Vector3.One),
			};
			parents[index] = index == 0
				? -1
				: index - 1 < skeleton.ParentIndices.Count
					? (int)skeleton.ParentIndices[index - 1]
					: index - 1;
		}

		var roots = new List<DrModelBone>();
		for (var index = 0; index < boneCount; index++)
		{
			var parent = parents[index];
			if (parent < 0 || parent >= boneCount || parent == index)
			{
				roots.Add(skeletonBones[index]);
				continue;
			}

			var siblings = skeletonBones[parent].Children?.ToList() ?? [];
			siblings.Add(skeletonBones[index]);
			skeletonBones[parent].Children = siblings.ToArray();
		}

		var mesh = BuildMesh(device, source, metadata, materialResolver, skeleton);
		var root = new DrModelBone("_ROOT");
		var children = new List<DrModelBone>(roots) { new DrModelBone("_MESH", mesh) };
		root.Children = children.ToArray();
		var model = new DrModel(root);

		// Inverse bind transforms come from DrModel's own absolute bind pose, so the skin uses
		// exactly the same hierarchy and default poses as the animation controller.
		var absolute = new Matrix[model.Bones.Length];
		model.CopyAbsoluteBoneTransformsTo(absolute);
		var joints = new DrSkinJoint[boneCount];
		for (var index = 0; index < boneCount; index++)
		{
			var absoluteIndex = skeletonBones[index].Index;
			joints[index] = new DrSkinJoint(
				skeletonBones[index],
				Matrix.Invert(absolute[absoluteIndex]));
		}

		// The skin must be attached before the instance is created: the instance discovers
		// skins from the model's mesh parts in its constructor.
		var skin = new DrSkin(0, joints);
		foreach (var part in mesh.MeshParts)
		{
			part.Skin = skin;
		}

		var instance = new DrModelInstance(model);
		instance.ResetTransforms();
		return new ElexModel { Instance = instance };
	}

	private static DrMesh BuildMesh(
		GraphicsDevice device,
		GmshMesh source,
		MeshMetadata? metadata,
		Func<string, ElexMaterial?>? materialResolver,
		Skeleton skeleton)
	{
		var positions = source.Positions;
		var normals = source.Normals.Count == positions.Count
			? source.Normals
			: MeshLoader.ComputeNormals(positions, source.Indices);
		var textureCoordinates = source.TextureCoordinates.Count == positions.Count
			? source.TextureCoordinates
			: null;
		var boneOffsets = BuildBoneOffsets(metadata, positions.Count);
		var vertices = new VertexPositionNormalTextureSkinned[positions.Count];
		for (var index = 0; index < vertices.Length; index++)
		{
			var rawIndices = source.BoneIndices[index] + new Vector4(boneOffsets[index]);
			var rawWeights = source.BoneWeights[index];
			var weights = NormalizeWeights(
				rawWeights,
				rawIndices,
				skeleton.Names.Count,
				out var indices);
			vertices[index] = new VertexPositionNormalTextureSkinned(
				new Vector3(positions[index].X, positions[index].Y, positions[index].Z),
				new Vector3(normals[index].X, normals[index].Y, normals[index].Z),
				textureCoordinates is null
					? Vector2.Zero
					: new Vector2(textureCoordinates[index].X, textureCoordinates[index].Y),
				weights,
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
		var indexBuffer = MeshLoader.CreateIndexBuffer(device, indicesArray);
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

		return mesh;
	}

	private static int[] BuildBoneOffsets(MeshMetadata? metadata, int vertexCount)
	{
		var offsets = new int[vertexCount];
		if (metadata is null)
		{
			return offsets;
		}

		foreach (var subMesh in metadata.SubMeshes)
		{
			var first = Math.Clamp(subMesh.FirstVertex, 0, vertexCount);
			var end = Math.Clamp(subMesh.FirstVertex + subMesh.VertexCount, first, vertexCount);
			for (var index = first; index < end; index++)
			{
				offsets[index] = subMesh.FirstBone;
			}
		}

		return offsets;
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
}
