using System.Buffers.Binary;
using System.Text;
using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class FsbSoundBankTests
{
	[Fact]
	public void Parse_ReadsMonoFadpcmSample()
	{
		var data = BuildBank(
			samples: 1,
			channelsCode: 0,
			sampleRateCode: 9,
			frameCount: 48_000,
			dataSize: 64,
			name: null);

		var bank = FsbSoundBank.Parse("Test", "test.rom", "snd", data);

		Assert.Equal(1, bank.Version);
		Assert.Equal("FADPCM", bank.CodecName);
		var sample = Assert.Single(bank.Samples);
		Assert.Equal(1, sample.Channels);
		Assert.Equal(48_000, sample.SampleRate);
		Assert.Equal(48_000, sample.SampleCount);
		Assert.Equal(1.0, sample.Duration, 3);
		Assert.Equal(64, sample.DataSize);
		Assert.Null(sample.Name);
	}

	[Fact]
	public void Parse_ReadsStereoNameAndLoop()
	{
		var data = BuildBank(
			samples: 1,
			channelsCode: 1,
			sampleRateCode: 9,
			frameCount: 24_000,
			dataSize: 32,
			name: "voice",
			loop: (100, 200));

		var bank = FsbSoundBank.Parse("Test", "test.rom", "dlg", data);

		var sample = Assert.Single(bank.Samples);
		Assert.Equal(2, sample.Channels);
		Assert.Equal("voice", sample.Name);
		Assert.True(sample.HasLoop);
		Assert.Equal(100, sample.LoopStart);
		Assert.Equal(201, sample.LoopEnd);
		Assert.Equal("dlg", bank.TypeToken);
	}

	[Fact]
	public void Parse_ReadsMultipleSamples()
	{
		var data = BuildBank(
			samples: 2,
			channelsCode: 0,
			sampleRateCode: 9,
			frameCount: 1_000,
			dataSize: 64,
			name: null);

		var bank = FsbSoundBank.Parse("Test", "test.rom", "snd", data);

		Assert.Equal(2, bank.Samples.Count);
		Assert.Equal(1_000, bank.Samples[0].SampleCount);
		Assert.Equal(1_000, bank.Samples[1].SampleCount);
	}

	[Fact]
	public void Parse_RejectsBadMagic()
	{
		var data = BuildBank(1, 0, 9, 1_000, 16, null);
		data[44] = (byte)'X';

		Assert.Throws<GenomeFormatException>(() =>
			FsbSoundBank.Parse("Bad", "test.rom", "snd", data));
	}

	[Fact]
	public void Parse_RejectsTruncatedData()
	{
		var data = BuildBank(1, 0, 9, 1_000, 64, null);
		data = data[..^1];

		Assert.Throws<GenomeFormatException>(() =>
			FsbSoundBank.Parse("Bad", "test.rom", "snd", data));
	}

	[Fact]
	public void Parse_RejectsInvalidSampleRateCode()
	{
		var data = BuildBank(1, 0, 15, 1_000, 16, null);

		Assert.Throws<GenomeFormatException>(() =>
			FsbSoundBank.Parse("Bad", "test.rom", "snd", data));
	}

	[Fact]
	public void Parse_NullArguments_Throw()
	{
		Assert.Throws<ArgumentException>(() => FsbSoundBank.Parse("", "test.rom", "snd", []));
		Assert.Throws<ArgumentException>(() => FsbSoundBank.Parse("Test", "", "snd", []));
		Assert.Throws<ArgumentException>(() => FsbSoundBank.Parse("Test", "test.rom", "", []));
		Assert.Throws<ArgumentNullException>(() => FsbSoundBank.Parse("Test", "test.rom", "snd", null!));
	}

	private static byte[] BuildBank(
		int samples,
		int channelsCode,
		int sampleRateCode,
		int frameCount,
		int dataSize,
		string? name,
		(int Start, int End)? loop = null)
	{
		var nameBytes = name is null ? [] : Encoding.Latin1.GetBytes(name);
		var nameTableSize = name is null ? 0 : 4 + nameBytes.Length + 1;
		var sampleHeaderSize = 36;
		var sampleDataStart = 0x3C + sampleHeaderSize + nameTableSize;
		var payload = new byte[sampleDataStart + dataSize];
		"FSB5"u8.CopyTo(payload);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 1);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), (uint)samples);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x0C), (uint)sampleHeaderSize);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x10), (uint)nameTableSize);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x14), (uint)dataSize);
		BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x18), 0x10);
		var sampleStride = dataSize / samples;
		var extraFlags = loop is not null;
		for (var index = 0; index < samples; index++)
		{
			var mode = ((ulong)frameCount << 34) |
				((ulong)(index * sampleStride / 32) << 7) |
				((ulong)channelsCode << 5) |
				((ulong)sampleRateCode << 1) |
				(extraFlags ? 1UL : 0UL);
			var offset = 0x3C + index * 8;
			BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset), mode);
		}

		if (loop is not null)
		{
			var offset = 0x3C + 8;
			var extra = (3u << 25) | (8u << 1);
			BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), extra);
			BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 4), loop.Value.Start);
			BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 8), loop.Value.End);
		}

		if (name is not null)
		{
			var nameTableStart = 0x3C + sampleHeaderSize;
			BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(nameTableStart), 4);
			nameBytes.CopyTo(payload, nameTableStart + 4);
			payload[nameTableStart + 4 + nameBytes.Length] = 0;
		}

		return Gec2TestBuilder.WrapRaw(payload);
	}
}
