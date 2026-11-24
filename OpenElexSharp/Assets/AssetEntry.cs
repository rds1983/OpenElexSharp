namespace OpenElexSharp.Assets;

/// <summary>Describes one file exposed by a mounted asset source.</summary>
/// <param name="VirtualPath">The normalized virtual path, using forward slashes.</param>
/// <param name="Length">The file size in bytes.</param>
/// <param name="SourceName">The mount that provides the file.</param>
/// <param name="PhysicalPath">The physical path for loose files; <see langword="null"/> for packed files.</param>
public sealed record AssetEntry(string VirtualPath, long Length, string SourceName, string? PhysicalPath);
