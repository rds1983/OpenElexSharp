namespace OpenElexSharp.Assets;

/// <summary>Classifies file extensions that must not be opened as game assets.</summary>
public static class AssetKinds
{
	private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".exe", ".dll", ".sys", ".com", ".scr", ".cpl", ".ocx", ".drv",
		".so", ".dylib", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".xll",
	};

	/// <summary>Returns whether an extension denotes an executable or loadable binary.</summary>
	public static bool IsExecutable(string extension)
	{
		ArgumentNullException.ThrowIfNull(extension);
		var normalized = extension.StartsWith('.') ? extension : "." + extension;
		return ExecutableExtensions.Contains(normalized);
	}
}
