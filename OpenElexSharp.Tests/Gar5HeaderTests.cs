using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class Gar5HeaderTests
{
    [Fact]
    public void Read_Rt02Header_ReadsClassFields()
    {
        using var stream = new MemoryStream(CreateRt02Header(0xAABBCCDD, "SN11", 14));

        var header = Gar5Header.Read(stream);

        Assert.Equal(0x20u, header.Value);
        Assert.Equal("RT02", header.Tag);
        Assert.Equal(0xAABBCCDDu, header.ClassHash);
        Assert.Equal("SN11", header.ClassTag);
        Assert.Equal(14u, header.ResourceCount);
        Assert.Equal(24, header.PayloadOffset);
    }

    [Fact]
    public void Read_GenericHeader_SkipsTwentyByteMetadata()
    {
        using var stream = new MemoryStream(CreateGenericHeader("STB\x07"));

        var header = Gar5Header.Read(stream);

        Assert.Equal("STB\x07", header.Tag);
        Assert.Equal(0u, header.ClassHash);
        Assert.Equal(string.Empty, header.ClassTag);
        Assert.Equal(0u, header.ResourceCount);
        Assert.Equal(32, header.PayloadOffset);
    }

    [Fact]
    public void Read_InvalidMagic_Throws()
    {
        var bytes = CreateRt02Header(0, "SN11", 0);
        bytes[0] = (byte)'X';

        Assert.Throws<Gar5FormatException>(() => Gar5Header.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void Read_TruncatedHeader_Throws()
    {
        var bytes = CreateRt02Header(0, "SN11", 0);

        Assert.Throws<Gar5FormatException>(() => Gar5Header.Read(new MemoryStream(bytes[..12])));
    }

    [Fact]
    public void Read_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Gar5Header.Read(null!));
    }

    private static byte[] CreateRt02Header(uint classHash, string classTag, uint count)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("GAR5"));
        writer.Write(0x20u);
        writer.Write(Encoding.ASCII.GetBytes("RT02"));
        writer.Write(classHash);
        writer.Write(Encoding.ASCII.GetBytes(classTag));
        writer.Write(count);
        return stream.ToArray();
    }

    private static byte[] CreateGenericHeader(string tag)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("GAR5"));
        writer.Write(0x20u);
        writer.Write(Encoding.ASCII.GetBytes(tag));
        writer.Write(new byte[20]);
        return stream.ToArray();
    }
}
