namespace OpenElexSharp.Assets;

/// <summary>One file stored in a <see cref="PakArchive"/>.</summary>
/// <param name="VirtualPath">Archive path with forward slashes.</param>
/// <param name="Offset">Absolute offset of the stored bytes.</param>
/// <param name="StoredSize">Number of bytes stored in the archive.</param>
/// <param name="UncompressedSize">Size after decompression (equal to <paramref name="StoredSize"/> when not compressed).</param>
/// <param name="IsCompressed">Whether the stored bytes are raw DEFLATE compressed.</param>
/// <param name="Attributes">Archive attributes from the file entry.</param>
/// <param name="CreatedTime">Creation time as a Windows FILETIME (0 when unset).</param>
/// <param name="AccessedTime">Last access time as a Windows FILETIME (0 when unset).</param>
/// <param name="ModifiedTime">Last write time as a Windows FILETIME (0 when unset).</param>
public sealed record PakEntry(
	string VirtualPath,
	long Offset,
	long StoredSize,
	long UncompressedSize,
	bool IsCompressed,
	uint Attributes,
	ulong CreatedTime,
	ulong AccessedTime,
	ulong ModifiedTime);
