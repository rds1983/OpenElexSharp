using System.Buffers.Binary;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

internal static class TestTemplateBuilder
{
	public static byte[] CreateTemplate(
		string entityName,
		Guid guid,
		Guid parentGuid,
		byte[][] propertySets,
		byte[][] childObjects,
		ulong timestamp = 123)
	{
		var templateObject = CreateTemplateObject(
			entityName,
			guid,
			parentGuid,
			propertySets,
			childObjects);

		using var inner = new MemoryStream();
		using var innerWriter = new BinaryWriter(inner, Encoding.Latin1, leaveOpen: true);
		innerWriter.Write(Encoding.ASCII.GetBytes("GAR5"));
		innerWriter.Write(0x20u);
		innerWriter.Write(Encoding.ASCII.GetBytes("GTP0"));
		innerWriter.Write(timestamp);
		innerWriter.Write(templateObject);

		using var outer = new MemoryStream();
		using var outerWriter = new BinaryWriter(outer, Encoding.Latin1, leaveOpen: true);
		outerWriter.Write(Encoding.ASCII.GetBytes("GAR5"));
		outerWriter.Write(0x20u);
		outerWriter.Write(new byte[36]);
		outerWriter.Write(inner.ToArray());
		return outer.ToArray();
	}

	public static byte[] CreateTemplateObject(
		string entityName,
		Guid guid,
		Guid parentGuid,
		byte[][] propertySets,
		byte[][] childObjects)
	{
		var templateEntityData = new byte[54];
		parentGuid.ToByteArray().CopyTo(templateEntityData, 0);

		using var entityStream = new MemoryStream();
		using var entityWriter = new BinaryWriter(entityStream, Encoding.Latin1, leaveOpen: true);
		entityWriter.Write(guid.ToByteArray());
		var name = Encoding.Latin1.GetBytes(entityName);
		entityWriter.Write((ushort)name.Length);
		entityWriter.Write(name);
		entityWriter.Write(new byte[6]);
		entityWriter.Write((byte)propertySets.Length);
		foreach (var propertySet in propertySets)
		{
			entityWriter.Write(propertySet);
		}

		entityWriter.Write((uint)childObjects.Length);
		foreach (var child in childObjects)
		{
			entityWriter.Write(child);
		}

		return TestGenomeBuilder.CreateObject(
			GenomeTemplate.TemplateEntityClassHash,
			6,
			[],
			[
				(GenomeTemplate.TemplateEntityClassHash, 6, templateEntityData),
				(GenomeTemplate.EntityClassHash, 4, entityStream.ToArray()),
			]);
	}

	public static byte[] CreatePropertySet(uint classHash, ushort version, uint nameHash, string value)
	{
		var data = new byte[2 + Encoding.Latin1.GetByteCount(value)];
		BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)Encoding.Latin1.GetByteCount(value));
		Encoding.Latin1.GetBytes(value).CopyTo(data, 2);
		return TestGenomeBuilder.CreateObject(
			classHash,
			version,
			[(0, nameHash, data)],
			[]);
	}
}
