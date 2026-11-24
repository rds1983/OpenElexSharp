using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// An ELEX world definition (<c>wrl</c> resource). The payload is a GAR5-wrapped <c>gCTerrain</c>
/// GEC2 object followed by the ordered list of scene-sector names.
/// </summary>
public sealed class ElexWorld
{
	/// <summary>Hash of <c>class gCTerrain</c>.</summary>
	public const uint TerrainClassHash = 0x207A4D1A;

	/// <summary>Hash of <c>class bCString</c>.</summary>
	public const uint StringTypeHash = 0xA83E6117;

	/// <summary>Hash of the <c>FileName</c> property name.</summary>
	public const uint FileNamePropertyHash = 0xAB93AA26;

	private const int Gar5WrapperSize = 44;
	private const int MaxNameLength = 512;

	private ElexWorld(string name, string sourcePath, string terrainFile, string[] sectorNames)
	{
		Name = name;
		SourcePath = sourcePath;
		TerrainFile = terrainFile;
		SectorNames = sectorNames;
	}

	/// <summary>Logical resource name.</summary>
	public string Name { get; }

	/// <summary>Virtual path of the owning ROM resource.</summary>
	public string SourcePath { get; }

	/// <summary>Terrain resource path from the <c>FileName</c> property.</summary>
	public string TerrainFile { get; }

	/// <summary>Scene-sector names in world order.</summary>
	public IReadOnlyList<string> SectorNames { get; }

	/// <summary>Parses a <c>wrl</c> resource.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">The world payload is malformed.</exception>
	public static ElexWorld Parse(string name, string sourcePath, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length < Gar5WrapperSize + 8 ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic)
		{
			throw new GenomeFormatException($"World '{name}' has no GAR5 wrapper.");
		}

		var objectOffset = FindObjectOffset(data, Gar5WrapperSize, TerrainClassHash, name);
		var terrain = GenomeObject.Parse(data, objectOffset);
		var fileNameProperty = terrain.Properties.FirstOrDefault(property =>
			property.TypeHash == StringTypeHash && property.NameHash == FileNamePropertyHash)
			?? throw new GenomeFormatException($"World '{name}' has no terrain FileName property.");
		var terrainFile = ReadString(fileNameProperty.Data, name, "FilePath");

		var position = terrain.Offset + terrain.Size;
		if (position + 2 > data.Length)
		{
			throw new GenomeFormatException($"World '{name}' is missing its sector count.");
		}

		var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
		position += 2;
		var sectors = new string[count];
		for (var index = 0; index < sectors.Length; index++)
		{
			if (position + 2 > data.Length)
			{
				throw new GenomeFormatException($"World '{name}' sector name {index} is truncated.");
			}

			var length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
			position += 2;
			if (length == 0 || length > MaxNameLength || position + length > data.Length)
			{
				throw new GenomeFormatException($"World '{name}' sector name {index} has an invalid length.");
			}

			sectors[index] = Encoding.Latin1.GetString(data, position, length);
			position += length;
		}

		return new ElexWorld(name, sourcePath, terrainFile, sectors);
	}

	private static int FindObjectOffset(byte[] data, int start, uint expectedClass, string name)
	{
		var position = start;
		while (position + 4 <= data.Length)
		{
			var relative = data.AsSpan(position).IndexOf("GEC2"u8);
			if (relative < 0)
			{
				break;
			}

			var offset = position + relative;
			try
			{
				var header = GenomeObject.Parse(data, offset);
				if (header.ClassHash == expectedClass)
				{
					return offset;
				}
			}
			catch (GenomeFormatException)
			{
				// A printable name may contain the magic; keep looking.
			}

			position = offset + 4;
		}

		throw new GenomeFormatException($"World '{name}' has no {GenomeNameTable.GetNameOrHash(expectedClass)} object.");
	}

	private static string ReadString(byte[] data, string name, string property)
	{
		if (data.Length < 2)
		{
			throw new GenomeFormatException($"World '{name}' {property} is truncated.");
		}

		var length = BinaryPrimitives.ReadUInt16LittleEndian(data);
		if (length == 0 || length + 2 != data.Length)
		{
			throw new GenomeFormatException($"World '{name}' {property} has an invalid length.");
		}

		return Encoding.Latin1.GetString(data, 2, length);
	}
}
