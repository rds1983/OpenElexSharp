using System.IO.Compression;
using System.Text;

namespace OpenElexSharp.Tests;

/// <summary>Builds small synthetic G3V0 PAK archives for tests.</summary>
internal sealed class TestPakBuilder
{
    private const uint Magic = 0x30563347;
    private const uint Packed = 0x00020000;
    private const uint Archive = 0x00000020;
    private const uint Compressed = 0x00000800;
    private const uint Directory = 0x00000010;
    private const ulong TestTime = 116_444_736_000_000_000;

    private readonly DirNode _root = new(string.Empty);

    public TestPakBuilder AddFile(
        string path,
        string content,
        bool compressed = false,
        uint extraAttributes = 0)
    {
        return AddFile(path, Encoding.UTF8.GetBytes(content), compressed, extraAttributes);
    }

    public TestPakBuilder AddFile(
        string path,
        byte[] content,
        bool compressed = false,
        uint extraAttributes = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        var directory = _root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            directory = directory.GetOrAddDirectory(segments[index]);
        }

        directory.Files.Add(new FileNode(segments[^1], content, compressed, extraAttributes));
        return this;
    }

    public byte[] Build()
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);
        writer.Write(new byte[48]);

        var locations = new Dictionary<FileNode, (long Offset, int StoredSize)>();
        CollectFiles(_root, writer, locations);

        var rootOffset = output.Position;
        WriteDirectory(writer, _root, locations);

        var volumeSize = output.Length;
        output.Position = 0;
        writer.Write(1u);
        writer.Write(Magic);
        writer.Write(0u); // revision
        writer.Write(0u); // encryption
        writer.Write(1u); // compression mode
        writer.Write(0xFEEDFACEu);
        writer.Write(48L);
        writer.Write(rootOffset);
        writer.Write(volumeSize);

        return output.ToArray();
    }

    private static void CollectFiles(
        DirNode directory,
        BinaryWriter writer,
        Dictionary<FileNode, (long Offset, int StoredSize)> locations)
    {
        foreach (var file in directory.Files)
        {
            var offset = writer.BaseStream.Position;
            var stored = file.Compressed ? Compress(file.Content) : file.Content;
            writer.Write(stored);
            locations[file] = (offset, stored.Length);
        }

        foreach (var child in directory.Directories)
        {
            CollectFiles(child, writer, locations);
        }
    }
    private static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(content);
        }

        return output.ToArray();
    }

    private static void WriteDirectory(
        BinaryWriter writer,
        DirNode directory,
        Dictionary<FileNode, (long Offset, int StoredSize)> locations)
    {
        WriteString(writer, directory.Name);
        WriteTimes(writer);
        writer.Write(Packed | Directory);
        writer.Write((uint)(directory.Files.Count + directory.Directories.Count));

        foreach (var file in directory.Files)
        {
            var (offset, storedSize) = locations[file];
            var attributes = Packed | Archive | (file.Compressed ? Compressed : 0) | file.ExtraAttributes;
            writer.Write(attributes);
            WriteString(writer, file.Name);
            writer.Write(offset);
            WriteTimes(writer);
            writer.Write(Packed | Archive);
            writer.Write(0u);
            writer.Write(file.Compressed ? 1u : 0u);
            writer.Write((uint)storedSize);
            writer.Write((uint)file.Content.Length);
        }

        foreach (var child in directory.Directories)
        {
            writer.Write(Packed | Directory);
            WriteDirectory(writer, child, locations);
        }
    }

    private static void WriteTimes(BinaryWriter writer)
    {
        writer.Write(TestTime);
        writer.Write(TestTime);
        writer.Write(TestTime);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        writer.Write((uint)bytes.Length);
        if (bytes.Length > 0)
        {
            writer.Write(bytes);
            writer.Write((byte)0);
        }
    }

    private sealed class DirNode
    {
        public DirNode(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public List<FileNode> Files { get; } = [];

        public List<DirNode> Directories { get; } = [];

        public DirNode GetOrAddDirectory(string name)
        {
            var existing = Directories.FirstOrDefault(directory =>
                string.Equals(directory.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }

            var created = new DirNode(name);
            Directories.Add(created);
            return created;
        }
    }

    private sealed record FileNode(string Name, byte[] Content, bool Compressed, uint ExtraAttributes);
}
