namespace OpenElexSharp.Assets;

/// <summary>
/// A validated ELEX installation. The root is the game folder that contains <c>data</c> and
/// <c>system</c>; the app path is the folder the original executable lives in, which is used as
/// the base for relative mount roots.
/// </summary>
public sealed class ElexInstallation
{
	/// <summary>Environment variable that points at the game folder.</summary>
	public const string PathEnvironmentVariable = "ELEX_INSTALL_PATH";

	/// <summary>Path of the mount configuration, relative to the installation root.</summary>
	public const string MountConfigurationRelativePath = "data/ini/mount_packed.xml";

	private ElexInstallation(string rootPath, string appPath)
	{
		RootPath = rootPath;
		AppPath = appPath;
	}

	/// <summary>The game root that contains <c>data</c> and <c>system</c>.</summary>
	public string RootPath { get; }

	/// <summary>The directory the original executable lives in (<c>system</c>), or the root if absent.</summary>
	public string AppPath { get; }

	/// <summary>The full path of <c>data/ini/mount_packed.xml</c>.</summary>
	public string MountConfigurationPath =>
		Path.Combine(RootPath, "data", "ini", "mount_packed.xml");

	/// <summary>
	/// Validates an installation path and creates a descriptor for it.
	/// </summary>
	/// <exception cref="ElexInstallationException">
	/// The directory does not exist or does not contain an ELEX mount configuration.
	/// </exception>
	public static ElexInstallation Open(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		string rootPath;
		try
		{
			rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
		{
			throw new ElexInstallationException($"Invalid ELEX installation path '{path}'.", exception);
		}

		if (!Directory.Exists(rootPath))
		{
			throw new ElexInstallationException($"ELEX installation folder not found: '{rootPath}'.");
		}

		var mountConfigurationPath = Path.Combine(rootPath, "data", "ini", "mount_packed.xml");
		if (!File.Exists(mountConfigurationPath))
		{
			throw new ElexInstallationException(
				$"'{rootPath}' does not look like an ELEX installation: " +
				$"'{MountConfigurationRelativePath}' is missing.");
		}

		var systemPath = Path.Combine(rootPath, "system");
		var appPath = Directory.Exists(systemPath) ? systemPath : rootPath;
		return new ElexInstallation(rootPath, appPath);
	}

	/// <summary>
	/// Picks the installation path: an explicit command-line value wins over the environment.
	/// </summary>
	/// <exception cref="ElexInstallationException">Neither source is set.</exception>
	public static string ResolvePath(string? explicitPath, string? environmentPath)
	{
		if (!string.IsNullOrWhiteSpace(explicitPath))
		{
			return explicitPath.Trim();
		}

		if (!string.IsNullOrWhiteSpace(environmentPath))
		{
			return environmentPath.Trim();
		}

		throw new ElexInstallationException(
			"ELEX installation path is not configured. " +
			$"Pass --elex-path <path> or set the {PathEnvironmentVariable} environment variable.");
	}
}
