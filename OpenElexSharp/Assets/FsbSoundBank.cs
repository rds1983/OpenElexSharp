using System.Buffers.Binary;
using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>One sample entry in an FSB5 sound bank.</summary>
/// <param name="Index">Sample index inside the bank.</param>
/// <param name="Name">Optional sample name from the bank name table.</param>
/// <param name="Channels">Channel count.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
/// <param name="SampleCount">Number of PCM frames.</param>
/// <param name="DataOffset">Offset of the sample data inside the FSB data section.</param>
/// <param name="DataSize">Encoded byte size.</param>
/// <param name="HasLoop">Whether the sample declares a loop.</param>
/// <param name="LoopStart">Loop start frame.</param>
/// <param name="LoopEnd">Loop end frame.</param>
public sealed record FsbSample(
	int Index,
	string? Name,
	int Channels,
	int SampleRate,
	long SampleCount,
	int DataOffset,
	int DataSize,
	bool HasLoop,
	int LoopStart,
	int LoopEnd)
{
	/// <summary>Playback duration in seconds.</summary>
	public double Duration => SampleRate <= 0 ? 0 : SampleCount / (double)SampleRate;
}

/// <summary>
/// An FMOD FSB5 sound bank as stored in ELEX <c>snd</c> and <c>dlg</c> resources. The payload is an
/// outer GAR5 wrapper followed by an <c>FSB5</c> bank. ELEX uses FADPCM (codec 16) for voice and
/// music; decoding is left to a later playback task.
/// </summary>
public sealed class FsbSoundBank
{
	/// <summary>Outer GAR5 wrapper size observed for sound resources.</summary>
	public const int Gar5WrapperSize = 44;

	/// <summary>FSB5 base header size for version 1.</summary>
	public const int BaseHeaderSizeV1 = 0x3C;

	/// <summary>FSB5 base header size for version 0.</summary>
	public const int BaseHeaderSizeV0 = 0x40;

	private const int MaxSampleCount = 100_000;
	private const int MaxNameLength = 512;

	private FsbSoundBank(
		string name,
		string sourcePath,
		string typeToken,
		int version,
		int codec,
		int sampleHeaderSize,
		int nameTableSize,
		int sampleDataSize,
		FsbSample[] samples)
	{
		Name = name;
		SourcePath = sourcePath;
		TypeToken = typeToken;
		Version = version;
		Codec = codec;
		SampleHeaderSize = sampleHeaderSize;
		NameTableSize = nameTableSize;
		SampleDataSize = sampleDataSize;
		Samples = samples;
	}

	/// <summary>Logical resource name.</summary>
	public string Name { get; }

	/// <summary>Virtual path of the owning ROM resource.</summary>
	public string SourcePath { get; }

	/// <summary>Resource type token, normally <c>snd</c> or <c>dlg</c>.</summary>
	public string TypeToken { get; }

	/// <summary>FSB5 header version.</summary>
	public int Version { get; }

	/// <summary>FMOD codec identifier.</summary>
	public int Codec { get; }

	/// <summary>Human-readable codec name.</summary>
	public string CodecName => GetCodecName(Codec);

	/// <summary>Bytes per sample header.</summary>
	public int SampleHeaderSize { get; }

	/// <summary>Bytes in the optional name table.</summary>
	public int NameTableSize { get; }

	/// <summary>Encoded sample data size.</summary>
	public int SampleDataSize { get; }

	/// <summary>Samples in the bank.</summary>
	public IReadOnlyList<FsbSample> Samples { get; }

	/// <summary>Parses an FSB5 bank wrapped in GAR5.</summary>
	/// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
	/// <exception cref="GenomeFormatException">The bank is malformed.</exception>
	public static FsbSoundBank Parse(string name, string sourcePath, string typeToken, byte[] data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length < Gar5WrapperSize + BaseHeaderSizeV1 ||
			BinaryPrimitives.ReadUInt32LittleEndian(data) != Gar5Header.Magic ||
			BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Gar5WrapperSize)) != 0x35425346)
		{
			throw new GenomeFormatException($"Sound bank '{name}' has no GAR5/FSB5 header.");
		}

		var version = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Gar5WrapperSize + 0x04));
		var sampleCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Gar5WrapperSize + 0x08));
		var sampleHeaderSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Gar5WrapperSize + 0x0C));
		var nameTableSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Gar5WrapperSize + 0x10));
		var sampleDataSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Gar5WrapperSize + 0x14));
		var codec = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Gar5WrapperSize + 0x18));
		var baseHeaderSize = version == 1 ? BaseHeaderSizeV1 :
			version == 0 ? BaseHeaderSizeV0 :
			throw new GenomeFormatException($"Sound bank '{name}' has FSB5 version {version}.");
		if (sampleCount < 1 || sampleCount > MaxSampleCount ||
			sampleHeaderSize < sampleCount * 8 || nameTableSize < 0 || sampleDataSize < 0)
		{
			throw new GenomeFormatException($"Sound bank '{name}' has invalid header counts.");
		}

		var sampleHeaderStart = Gar5WrapperSize + baseHeaderSize;
		var dataStart = sampleHeaderStart + sampleHeaderSize + nameTableSize;
		if (sampleHeaderStart + sampleHeaderSize > data.Length ||
			dataStart + sampleDataSize > data.Length)
		{
			throw new GenomeFormatException($"Sound bank '{name}' is truncated.");
		}

		var samples = new FsbSample[sampleCount];
		var offset = sampleHeaderStart;
		for (var index = 0; index < samples.Length; index++)
		{
			if (offset + 8 > sampleHeaderStart + sampleHeaderSize)
			{
				throw new GenomeFormatException($"Sound bank '{name}' sample header {index} is truncated.");
			}

			var sampleMode = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
			offset += 8;
			var frameCount = (long)((sampleMode >> 34) & 0x3FFF_FFFF);
			var dataOffset = (int)(((sampleMode >> 7) & 0x07FF_FFFF) << 5);
			var channels = (int)((sampleMode >> 5) & 0x03) switch
			{
				0 => 1,
				1 => 2,
				2 => 6,
				_ => 8,
			};
			var sampleRate = (int)((sampleMode >> 1) & 0x0F) switch
			{
				0 => 4000,
				1 => 8000,
				2 => 11000,
				3 => 11025,
				4 => 16000,
				5 => 22050,
				6 => 24000,
				7 => 32000,
				8 => 44100,
				9 => 48000,
				10 => 96000,
				_ => throw new GenomeFormatException(
					$"Sound bank '{name}' sample {index} has an invalid sample rate."),
			};
			var hasLoop = false;
			var loopStart = 0;
			var loopEnd = 0;
			if ((sampleMode & 1) != 0)
			{
				var extraOffset = offset;
				while (extraOffset + 4 <= sampleHeaderStart + sampleHeaderSize)
				{
					var extra = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(extraOffset));
					var extraType = (int)((extra >> 25) & 0x7F);
					var extraSize = (int)((extra >> 1) & 0x00FF_FFFF);
					var continues = (extra & 1) != 0;
					extraOffset += 4;
					if (extraOffset + extraSize > sampleHeaderStart + sampleHeaderSize)
					{
						throw new GenomeFormatException(
							$"Sound bank '{name}' sample {index} has a truncated extra flag.");
					}

					switch (extraType)
					{
						case 0x01 when extraSize >= 1:
							channels = data[extraOffset];
							break;
						case 0x02 when extraSize >= 4:
							sampleRate = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(extraOffset));
							break;
						case 0x03 when extraSize >= 4:
							hasLoop = true;
							loopStart = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(extraOffset));
							loopEnd = extraSize >= 8 ?
								BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(extraOffset + 4)) + 1 :
								0;
							break;
					}

					extraOffset += extraSize;
					if (!continues)
					{
						break;
					}
				}

				offset = extraOffset;
			}

			var dataSize = index + 1 < samples.Length ?
				GetNextDataOffset(data, offset, sampleHeaderStart + sampleHeaderSize) - dataOffset :
				sampleDataSize - dataOffset;
			if (dataOffset < 0 || dataSize < 0 || dataOffset + dataSize > sampleDataSize)
			{
				throw new GenomeFormatException($"Sound bank '{name}' sample {index} data is out of range.");
			}

			samples[index] = new FsbSample(
				index,
				null,
				channels,
				sampleRate,
				frameCount,
				dataOffset,
				dataSize,
				hasLoop,
				loopStart,
				loopEnd);
		}

		if (offset > sampleHeaderStart + sampleHeaderSize)
		{
			throw new GenomeFormatException($"Sound bank '{name}' sample headers overlap the data section.");
		}

		var names = ReadNames(data, sampleHeaderStart + sampleHeaderSize, nameTableSize, sampleCount);
		for (var index = 0; index < samples.Length; index++)
		{
			if (names[index] is not null)
			{
				samples[index] = samples[index] with { Name = names[index] };
			}
		}

		return new FsbSoundBank(
			name,
			sourcePath,
			typeToken,
			version,
			codec,
			sampleHeaderSize,
			nameTableSize,
			sampleDataSize,
			samples);
	}

	private static int GetNextDataOffset(byte[] data, int offset, int end)
	{
		if (offset + 8 > end)
		{
			throw new GenomeFormatException("FSB5 sample headers are truncated.");
		}

		var mode = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
		return (int)(((mode >> 7) & 0x07FF_FFFF) << 5);
	}

	private static string?[] ReadNames(
		byte[] data,
		int nameTableStart,
		int nameTableSize,
		int sampleCount)
	{
		var names = new string?[sampleCount];
		if (nameTableSize == 0)
		{
			return names;
		}

		for (var index = 0; index < sampleCount; index++)
		{
			var offsetPosition = nameTableStart + index * 4;
			if (offsetPosition + 4 > nameTableStart + nameTableSize)
			{
				break;
			}

			var relative = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offsetPosition));
			var nameStart = nameTableStart + (int)relative;
			if (relative > nameTableSize || nameStart >= data.Length)
			{
				continue;
			}

			var zero = Array.IndexOf(data, (byte)0, nameStart, Math.Min(MaxNameLength, data.Length - nameStart));
			if (zero < 0)
			{
				continue;
			}

			names[index] = Encoding.Latin1.GetString(data, nameStart, zero - nameStart);
		}

		return names;
	}

	/// <summary>Returns the FMOD codec name.</summary>
	public static string GetCodecName(int codec) => codec switch
	{
		0x01 => "PCM8",
		0x02 => "PCM16",
		0x03 => "PCM24",
		0x04 => "PCM32",
		0x05 => "PCMFLOAT",
		0x06 => "GCADPCM",
		0x07 => "IMAADPCM",
		0x08 => "VAG",
		0x09 => "HEVAG",
		0x0A => "XMA",
		0x0B => "MPEG",
		0x0C => "CELT",
		0x0D => "ATRAC9",
		0x0E => "XWMA",
		0x0F => "VORBIS",
		0x10 => "FADPCM",
		0x11 => "OPUS",
		_ => $"0x{codec:x2}",
	};
}
