using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace OpenElexSharp.Tests;

internal static class Gec2TestBuilder
{
	public static byte[] String(string value)
	{
		var bytes = Encoding.Latin1.GetBytes(value);
		var data = new byte[bytes.Length + 2];
		BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)bytes.Length);
		bytes.CopyTo(data, 2);
		return data;
	}

	public static byte[] Matrix(Matrix4x4 matrix)
	{
		var data = new byte[64];
		ReadOnlySpan<float> values =
		[
			matrix.M11, matrix.M12, matrix.M13, matrix.M14,
			matrix.M21, matrix.M22, matrix.M23, matrix.M24,
			matrix.M31, matrix.M32, matrix.M33, matrix.M34,
			matrix.M41, matrix.M42, matrix.M43, matrix.M44,
		];
		for (var index = 0; index < values.Length; index++)
		{
			BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(index * 4), values[index]);
		}

		return data;
	}

	public static byte[] Object(
		uint classHash,
		ushort version,
		(uint Hash, ushort Version, byte[] Data)[] classes,
		(uint TypeHash, uint NameHash, byte[] Data)[]? properties = null)
	{
		var body = new List<byte>();
		properties ??= [];
		WriteUInt16(body, (ushort)properties.Length);
		foreach (var property in properties)
		{
			WriteUInt32(body, property.TypeHash);
			WriteUInt32(body, property.NameHash);
			WriteUInt32(body, (uint)property.Data.Length);
			body.AddRange(property.Data);
		}

		WriteUInt32(body, (uint)classes.Length);
		foreach (var item in classes)
		{
			WriteUInt32(body, item.Hash);
			WriteUInt16(body, item.Version);
			WriteUInt32(body, (uint)item.Data.Length);
			body.AddRange(item.Data);
		}

		var data = new byte[14 + body.Count];
		"GEC2"u8.CopyTo(data);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), classHash);
		BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), version);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(10), (uint)body.Count);
		body.CopyTo(data, 14);
		return data;
	}

	public static byte[] Wrap(byte[] payload, string? resourceName = null)
	{
		var outer = new byte[44];
		"GAR5"u8.CopyTo(outer);
		BinaryPrimitives.WriteUInt32LittleEndian(outer.AsSpan(4), 0x20);
		var inner = new List<byte>();
		inner.AddRange("GAR5"u8.ToArray());
		WriteUInt32(inner, 0x20);
		if (resourceName is not null)
		{
			var name = Encoding.Latin1.GetBytes(resourceName);
			WriteUInt16(inner, 1);
			WriteUInt16(inner, (ushort)name.Length);
			inner.AddRange(name);
		}

		inner.AddRange(payload);
		var data = new byte[outer.Length + inner.Count];
		outer.CopyTo(data, 0);
		inner.CopyTo(data, outer.Length);
		return data;
	}

	public static byte[] PropertySet(
		uint classHash,
		ushort version,
		params (uint TypeHash, uint NameHash, byte[] Data)[] properties) =>
		Object(classHash, version, [], properties);

	private static void WriteUInt16(List<byte> data, ushort value)
	{
		data.Add((byte)value);
		data.Add((byte)(value >> 8));
	}

	private static void WriteUInt32(List<byte> data, uint value)
	{
		data.Add((byte)value);
		data.Add((byte)(value >> 8));
		data.Add((byte)(value >> 16));
		data.Add((byte)(value >> 24));
	}
}
