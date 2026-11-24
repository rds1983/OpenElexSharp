using System.Text;

namespace OpenElexSharp.Assets;

/// <summary>
/// Resolves mount paths: expands <c>$(name)</c> macros and resolves relative paths against a base
/// directory.
/// </summary>
public sealed class MountPathResolver
{
	private readonly string _appPath;
	private readonly IReadOnlyDictionary<string, string> _macros;

	/// <param name="appPath">Absolute path used as the default base and for the <c>$(app)</c> macro.</param>
	/// <param name="macros">Macro values without the <c>$()</c> syntax; names are case-insensitive.</param>
	public MountPathResolver(string appPath, IReadOnlyDictionary<string, string> macros)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(appPath);
		ArgumentNullException.ThrowIfNull(macros);

		_appPath = Path.GetFullPath(appPath);
		_macros = new Dictionary<string, string>(macros, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>The application directory used as the default base path.</summary>
	public string AppPath => _appPath;

	/// <summary>Creates a resolver with the macros used by the retail ELEX mount configuration.</summary>
	public static MountPathResolver CreateDefault(ElexInstallation installation)
	{
		ArgumentNullException.ThrowIfNull(installation);

		var macros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["app"] = installation.AppPath,
			["localappdata"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			["savedgames"] = GetSavedGamesPath(),
		};

		return new MountPathResolver(installation.AppPath, macros);
	}

	/// <summary>
	/// Expands macros and resolves <paramref name="path"/> against <paramref name="basePath"/> when
	/// it is not rooted. The result is a normalized absolute path.
	/// </summary>
	/// <exception cref="MountConfigurationException">A macro is malformed or unknown.</exception>
	public string Resolve(string path, string basePath)
	{
		ArgumentNullException.ThrowIfNull(path);
		ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

		var expanded = ExpandMacros(path);
		var combined = Path.IsPathRooted(expanded) ? expanded : Path.Combine(basePath, expanded);
		return Path.GetFullPath(combined);
	}

	private string ExpandMacros(string path)
	{
		var result = new StringBuilder(path.Length);
		var position = 0;

		while (position < path.Length)
		{
			var start = path.IndexOf("$(", position, StringComparison.Ordinal);
			if (start < 0)
			{
				result.Append(path, position, path.Length - position);
				break;
			}

			result.Append(path, position, start - position);
			var end = path.IndexOf(')', start + 2);
			if (end < 0)
			{
				throw new MountConfigurationException($"Unterminated macro in mount path '{path}'.");
			}

			var name = path[(start + 2)..end];
			if (!_macros.TryGetValue(name, out var value))
			{
				throw new MountConfigurationException($"Unknown macro '$({name})' in mount path '{path}'.");
			}

			result.Append(value);
			position = end + 1;
		}

		return result.ToString();
	}

	private static string GetSavedGamesPath()
	{
		var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return string.IsNullOrEmpty(profile) ? string.Empty : Path.Combine(profile, "Saved Games");
	}
}
