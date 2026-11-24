using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class PakArchiveTests
{
    [Fact]
    public void Open_ValidArchive_ReadsHeaderAndEntries()
    {
        using var temp = new TempDirectory();
        var bytes = new TestPakBuilder()
            .AddFile("localization/en.str", "hello")
            .AddFile("0_en_snd/0/w_snd.rom", new byte[] { 1, 2, 3, 4 })
            .Build();

        var archive = Open(temp, bytes);

        Assert.Equal(1u, archive.Version);
        Assert.Equal(1u, archive.Compression);
        Assert.Equal(48, archive.DataOffset);
        Assert.Equal(2, archive.Entries.Count);

        var text = archive.Entries.Single(entry => entry.VirtualPath == "localization/en.str");
        Assert.Equal(5, text.UncompressedSize);
        Assert.Equal(5, text.StoredSize);
        Assert.False(text.IsCompressed);
        Assert.Equal("hello", ReadText(archive, text));

        var binary = archive.Entries.Single(entry => entry.VirtualPath == "0_en_snd/0/w_snd.rom");
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ReadBytes(archive, binary));
    }

    [Fact]
    public void OpenRead_CompressedEntry_Decompresses()
    {
        using var temp = new TempDirectory();
        var content = string.Concat(Enumerable.Repeat("compressed content ", 200));
        var bytes = new TestPakBuilder().AddFile("text.txt", content, compressed: true).Build();

        var archive = Open(temp, bytes);
        var entry = Assert.Single(archive.Entries);

        Assert.True(entry.IsCompressed);
        Assert.True(entry.StoredSize < entry.UncompressedSize);
        Assert.Equal(content, ReadText(archive, entry));
    }

    [Fact]
    public void OpenRead_EmptyFile_ReturnsEmptyStream()
    {
        using var temp = new TempDirectory();
        var bytes = new TestPakBuilder().AddFile("empty.bin", Array.Empty<byte>()).Build();

        var archive = Open(temp, bytes);
        using var stream = archive.OpenRead(Assert.Single(archive.Entries));

        Assert.Equal(0, stream.Length);
        Assert.Equal(-1, stream.ReadByte());
    }

    [Fact]
    public void Open_DeletedAndEncryptedEntries_AreSkipped()
    {
        using var temp = new TempDirectory();
        var bytes = new TestPakBuilder()
            .AddFile("live.txt", "live")
            .AddFile("deleted.txt", "deleted", extraAttributes: 0x8000)
            .AddFile("encrypted.txt", "encrypted", extraAttributes: 0x4000)
            .Build();

        var archive = Open(temp, bytes);

        Assert.Equal(["live.txt"], archive.Entries.Select(entry => entry.VirtualPath));
    }

    [Fact]
    public void Open_NestedEmptyDirectories_AreSupported()
    {
        using var temp = new TempDirectory();
        var bytes = new TestPakBuilder().AddFile("a/b/c/deep.txt", "deep").Build();

        var archive = Open(temp, bytes);

        Assert.Equal("a/b/c/deep.txt", Assert.Single(archive.Entries).VirtualPath);
    }

    [Fact]
    public void Open_InvalidMagic_Throws()
    {
        using var temp = new TempDirectory();
        var bytes = new TestPakBuilder().AddFile("a.txt", "a").Build();
        Encoding.ASCII.GetBytes("XXXX").CopyTo(bytes, 4);

        var exception = Assert.Throws<PakFormatException>(() => Open(temp, bytes));

        Assert.Contains("G3V0", exception.Message);
    }

    [Theory]
    [InlineData(0, 2u)]
    [InlineData(12, 1u)]
    [InlineData(16, 3u)]
    public void Open_UnsupportedHeaderValues_Throw(int offset, uint value)
    {
        using var temp = new TempDirectory();
        var bytes = new TestPakBuilder().AddFile("a.txt", "a").Build();
        BitConverter.GetBytes(value).CopyTo(bytes, offset);

        Assert.Throws<PakFormatException>(() => Open(temp, bytes));
    }

    [Fact]
    public void Open_TooSmall_Throws()
    {
        using var temp = new TempDirectory();

        Assert.Throws<PakFormatException>(() => Open(temp, new byte[20]));
    }

    [Fact]
    public void Open_TruncatedDirectory_Throws()
    {
        using var temp = new TempDirectory();
        var bytes = new TestPakBuilder().AddFile("a.txt", "a").Build();

        Assert.Throws<PakFormatException>(() => Open(temp, bytes[..40]));
    }

    [Fact]
    public void Open_DirectoryTruncatedMidEntry_Throws()
    {
        using var temp = new TempDirectory();
        var original = new TestPakBuilder()
            .AddFile("a.txt", "a")
            .AddFile("b.txt", "b")
            .Build();
        var bytes = original[..^8];
        BitConverter.GetBytes((ulong)bytes.Length).CopyTo(bytes, 40);

        Assert.Throws<PakFormatException>(() => Open(temp, bytes));
    }

    [Fact]
    public void OpenRead_CorruptDeflateData_Throws()
    {
        using var temp = new TempDirectory();
        var content = string.Concat(Enumerable.Repeat("compress me ", 500));
        var bytes = new TestPakBuilder().AddFile("data.bin", content, compressed: true).Build();
        var path = Path.Combine(temp.Root, "corrupt.pak");
        File.WriteAllBytes(path, bytes);
        var archive = PakArchive.Open(path);
        var entry = Assert.Single(archive.Entries);

        var corrupt = (byte[])bytes.Clone();
        Array.Clear(corrupt, (int)entry.Offset, (int)entry.StoredSize);
        File.WriteAllBytes(path, corrupt);
        var corruptArchive = PakArchive.Open(path);
        var corruptEntry = Assert.Single(corruptArchive.Entries);

        Assert.Throws<PakFormatException>(() => corruptArchive.OpenRead(corruptEntry));
    }

    [Fact]
    public void Open_DirectoryDoesNotEndAtVolumeSize_Throws()
    {
        using var temp = new TempDirectory();
        var original = new TestPakBuilder().AddFile("a.txt", "a").Build();
        var bytes = new byte[original.Length + 4];
        original.CopyTo(bytes, 0);
        BitConverter.GetBytes((ulong)bytes.Length).CopyTo(bytes, 40);

        Assert.Throws<PakFormatException>(() => Open(temp, bytes));
    }

    [Fact]
    public void Open_EntryOutsideDataArea_Throws()
    {
        using var temp = new TempDirectory();
        var bytes = new TestPakBuilder().AddFile("a.txt", "a").Build();
        // Point the first file entry at an offset beyond the data area. The first entry starts at
        // the root directory, so locate the stored offset via the parsed archive and patch it.
        var archive = Open(temp, bytes);
        var entry = Assert.Single(archive.Entries);
        var offsetPosition = FindEntryOffsetPosition(bytes, entry.VirtualPath);
        BitConverter.GetBytes((ulong)(bytes.Length + 100)).CopyTo(bytes, offsetPosition);

        Assert.Throws<PakFormatException>(() => Open(temp, bytes));
    }

    [Fact]
    public void Open_PathTraversal_Throws()
    {
        using var temp = new TempDirectory();
        var bytes = new TestPakBuilder().AddFile("../evil.txt", "evil").Build();

        Assert.Throws<PakFormatException>(() => Open(temp, bytes));
    }

    [Fact]
    public void OpenRead_NullEntry_Throws()
    {
        using var temp = new TempDirectory();
        var archive = Open(temp, new TestPakBuilder().AddFile("a.txt", "a").Build());

        Assert.Throws<ArgumentNullException>(() => archive.OpenRead(null!));
    }

    private static PakArchive Open(TempDirectory temp, byte[] bytes)
    {
        var path = Path.Combine(temp.Root, "test.pak");
        File.WriteAllBytes(path, bytes);
        return PakArchive.Open(path);
    }

    private static string ReadText(PakArchive archive, PakEntry entry)
    {
        using var stream = archive.OpenRead(entry);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(PakArchive archive, PakEntry entry)
    {
        using var stream = archive.OpenRead(entry);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static int FindEntryOffsetPosition(byte[] bytes, string virtualPath)
    {
        var name = Encoding.Latin1.GetBytes(virtualPath.Split('/')[^1]);
        var index = bytes.AsSpan().IndexOf(name);
        Assert.True(index >= 0);
        return index + name.Length + 1; // name + NUL terminator
    }
}
