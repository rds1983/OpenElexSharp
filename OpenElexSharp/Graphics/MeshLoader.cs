using DigitalRiseModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenElexSharp.Assets;
using NumericsVector3 = System.Numerics.Vector3;

namespace OpenElexSharp.Graphics;

/// <summary>
/// Additional per-part metadata that does not belong in DigitalRiseModel's <see cref="DrMeshPart"/>.
/// </summary>
/// <param name="Bounds">Bounding sphere used by viewers and future scene culling.</param>
public sealed record DrMeshPartTag(BoundingSphere Bounds);

/// <summary>Builds DigitalRiseModel mesh parts from parsed GMSH resources.</summary>
public static class MeshLoader
{
    /// <summary>Builds a <see cref="DrMeshPart"/> and generates smooth normals when needed.</summary>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="GmshFormatException">The mesh has no usable triangle/position data.</exception>
    public static DrMeshPart Load(GraphicsDevice device, GmshMesh source)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsTriangleList || source.Indices.Count == 0 || source.Positions.Count == 0)
        {
            throw new GmshFormatException($"Mesh '{source.Name}' has no usable triangle list.");
        }

        var positions = source.Positions;
        var normals = ComputeNormals(positions, source.Indices);
        var vertices = new VertexPositionNormalTexture[positions.Count];
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index] = new VertexPositionNormalTexture(
                new Vector3(positions[index].X, positions[index].Y, positions[index].Z),
                new Vector3(normals[index].X, normals[index].Y, normals[index].Z),
                Vector2.Zero);
        }

        var indices = new int[source.Indices.Count];
        for (var index = 0; index < indices.Length; index++)
        {
            indices[index] = checked((int)source.Indices[index]);
        }

        var vertexBuffer = new VertexBuffer(
            device,
            VertexPositionNormalTexture.VertexDeclaration,
            vertices.Length,
            BufferUsage.WriteOnly);
        vertexBuffer.SetData(vertices);

        var use32BitIndices = indices.Any(index => index > ushort.MaxValue);
        var indexBuffer = use32BitIndices
            ? new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly)
            : new IndexBuffer(device, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
        if (use32BitIndices)
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

        var (box, sphere) = ComputeBounds(positions);
        var part = new DrMeshPart(
            vertexBuffer,
            indexBuffer,
            box,
            PrimitiveType.TriangleList,
            vertices.Length,
            indices.Length / 3)
        {
            Tag = new DrMeshPartTag(sphere),
        };
        return part;
    }

    /// <summary>Generates normalized smooth normals from the face list.</summary>
    public static NumericsVector3[] ComputeNormals(
        IReadOnlyList<NumericsVector3> positions,
        IReadOnlyList<uint> indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        var normals = new NumericsVector3[positions.Count];
        for (var index = 0; index + 2 < indices.Count; index += 3)
        {
            var a = positions[checked((int)indices[index])];
            var b = positions[checked((int)indices[index + 1])];
            var c = positions[checked((int)indices[index + 2])];
            var normal = NumericsVector3.Cross(b - a, c - a);
            normals[checked((int)indices[index])] += normal;
            normals[checked((int)indices[index + 1])] += normal;
            normals[checked((int)indices[index + 2])] += normal;
        }

        for (var index = 0; index < normals.Length; index++)
        {
            normals[index] = normals[index].LengthSquared() > 1e-12f
                ? NumericsVector3.Normalize(normals[index])
                : NumericsVector3.UnitY;
        }

        return normals;
    }

    private static (BoundingBox Box, BoundingSphere Sphere) ComputeBounds(
        IReadOnlyList<NumericsVector3> positions)
    {
        var min = positions[0];
        var max = positions[0];
        foreach (var position in positions)
        {
            min = NumericsVector3.Min(min, position);
            max = NumericsVector3.Max(max, position);
        }

        var center = (min + max) * 0.5f;
        var radius = Math.Max(0.001f, NumericsVector3.Distance(center, max));
        var box = new BoundingBox(
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(max.X, max.Y, max.Z));
        var sphere = new BoundingSphere(new Vector3(center.X, center.Y, center.Z), radius);
        return (box, sphere);
    }
}
