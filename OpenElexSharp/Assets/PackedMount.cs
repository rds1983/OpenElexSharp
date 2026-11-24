namespace OpenElexSharp.Assets;

/// <summary>
/// A folder of PAK archives mounted at a virtual folder. Reading the archives is implemented by
/// the PAK reader task; for now the mount is recorded so the archive layer can use it.
/// </summary>
/// <param name="VirtualRoot">The normalized virtual folder that archive contents are mounted at.</param>
/// <param name="PhysicalPath">Absolute path of the folder that contains the archives.</param>
/// <param name="Enabled">Whether packed archives are enabled for the owning volume.</param>
public sealed record PackedMount(string VirtualRoot, string PhysicalPath, bool Enabled);
