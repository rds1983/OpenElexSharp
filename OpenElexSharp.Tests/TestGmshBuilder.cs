using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace OpenElexSharp.Tests;

/// <summary>Builds small synthetic GMSH v2 meshes for tests.</summary>
internal static class TestGmshBuilder
{
    public static byte[] Create(
        Vector3[] positions,
        uint[] indices,
        GmshVertexAttributeData[]? attributes = null,
        GmshVertexStreamData[]? streams = null,
        uint[][]? materials = null,
        bool wrap = true)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        attributes ??= [];
        streams ??= [];
        materials ??= [];

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("GMSH"));
        writer.Write(2u);
        var headerPosition = output.Position;
        writer.Write(new byte[14 * 4]);

        var indexOffset = (uint)output.Position;
        writer.Write((uint)indices.Length);
        foreach (var index in indices)
        {
            writer.Write(index);
        }

        writer.Write((uint)positions.Length);
        foreach (var position in positions)
        {
            writer.Write(position.X);
            writer.Write(position.Y);
            writer.Write(position.Z);
        }

        var attributeOffset = (uint)output.Position;
        foreach (var attribute in attributes)
        {
            writer.Write(attribute.Usage);
            writer.Write(attribute.UsageIndex);
            writer.Write(attribute.Format);
            writer.Write(attribute.Stream);
            writer.Write(attribute.Unknown0);
            writer.Write(attribute.Unknown1);
            writer.Write(attribute.Unknown2);
        }

        var streamOffset = (uint)output.Position;
        foreach (var stream in streams)
        {
            writer.Write(stream.Usage);
            writer.Write(stream.Unknown);
            writer.Write(stream.Offset);
            writer.Write(stream.Stride);
        }

        var materialOffset = (uint)output.Position;
        foreach (var material in materials)
        {
            if (material.Length != 10)
            {
                throw new ArgumentException("A material record must contain exactly ten values.", nameof(materials));
            }

            foreach (var value in material)
            {
                writer.Write(value);
            }
        }

        var meshHeaderOffset = (uint)output.Position;
        output.Position = headerPosition;
        writer.Write(indexOffset);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(meshHeaderOffset);
        writer.Write((uint)attributes.Length);
        writer.Write(attributeOffset);
        writer.Write(1u);
        writer.Write(attributeOffset);
        writer.Write((uint)streams.Length);
        writer.Write(streamOffset);
        writer.Write((uint)materials.Length);
        writer.Write(materialOffset);
        writer.Write(0u);
        writer.Write(0u);

        var data = output.ToArray();
        return wrap ? Wrap(data) : data;
    }

    public static byte[] Wrap(byte[] gmsh)
    {
        ArgumentNullException.ThrowIfNull(gmsh);
        var result = new byte[44 + gmsh.Length];
        Encoding.ASCII.GetBytes("GAR5").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 0x20);
        gmsh.CopyTo(result, 44);
        return result;
    }

    internal sealed record GmshVertexAttributeData(
        int Usage,
        int UsageIndex,
        uint Format,
        uint Stream,
        uint Unknown0 = 0,
        uint Unknown1 = 0,
        uint Unknown2 = 0);

    internal sealed record GmshVertexStreamData(
        int Usage,
        uint Unknown,
        uint Offset,
        uint Stride);
}
