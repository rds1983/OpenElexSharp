using System.Text;

namespace OpenElexSharp.Tests;

/// <summary>Builds small synthetic RT02 resource databases for tests.</summary>
internal sealed class TestResourceDatabaseBuilder
{
	private readonly List<Record> _records = [];

	public TestResourceDatabaseBuilder AddResource(string name, int classBlockSize = 20)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (classBlockSize < 8)
		{
			throw new ArgumentOutOfRangeException(nameof(classBlockSize));
		}

		_records.Add(new Record(name, classBlockSize));
		return this;
	}

	public byte[] Build(uint classHash = 0x12345678, string classTag = "SN11")
	{
		using var output = new MemoryStream();
		using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);

		writer.Write(Encoding.ASCII.GetBytes("GAR5"));
		writer.Write(0x20u);
		writer.Write(Encoding.ASCII.GetBytes("RT02"));
		writer.Write(classHash);
		writer.Write(Encoding.ASCII.GetBytes(classTag.PadRight(4)[..4]));
		writer.Write((uint)_records.Count);

		foreach (var record in _records)
		{
			writer.Write(new byte[32]);
			var name = Encoding.Latin1.GetBytes(record.Name);
			writer.Write((uint)name.Length);
			writer.Write(name);
			writer.Write((byte)',');
			writer.Write(new byte[8]); // Placeholder for reference metadata.
			writer.Write((uint)record.ClassBlockSize);
			var block = new byte[record.ClassBlockSize];
			"GEC2"u8.CopyTo(block);
			writer.Write(block);
		}

		return output.ToArray();
	}

	private sealed record Record(string Name, int ClassBlockSize);
}
