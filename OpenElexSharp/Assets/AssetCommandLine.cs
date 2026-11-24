namespace OpenElexSharp.Assets;

/// <summary>Commands exposed by the headless asset inspector.</summary>
public enum AssetCommandKind
{
	Help,
	List,
	Stats,
	Export,
	Resources,
	Objects,
	Templates,
	Textures,
	Meshes,
	Materials,
	Skeletons,
}

/// <summary>Parsed command line for <c>OpenElexSharp assets ...</c>.</summary>
public sealed class AssetCommandLine
{
	/// <summary>The subcommand that starts an asset inspection run.</summary>
	public const string CommandName = "assets";

	/// <summary>Usage text.</summary>
	public const string Usage = """
        Usage:
          OpenElexSharp assets list [substring] [--ext <ext>] [--source <text>] [--language <code>] [--limit <n>|--all] [--include-binaries] [--elex-path <path>]
          OpenElexSharp assets find <substring> [--ext <ext>] [--source <text>] [--language <code>] [--limit <n>|--all] [--include-binaries] [--elex-path <path>]
          OpenElexSharp assets stats [--elex-path <path>]
          OpenElexSharp assets resources [substring] [--type <token>] [--language <code>] [--limit <n>|--all] [--elex-path <path>]
          OpenElexSharp assets objects <assetPath> [--limit <n>|--all] [--properties] [--elex-path <path>]
          OpenElexSharp assets templates [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
          OpenElexSharp assets textures [substring] [--limit <n>|--all] [--properties] [--decode] [--elex-path <path>]
          OpenElexSharp assets meshes [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
          OpenElexSharp assets materials [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
          OpenElexSharp assets skeletons [substring] [--limit <n>|--all] [--properties] [--elex-path <path>]
          OpenElexSharp assets export <assetPath> <destination> [--elex-path <path>]

        list           Prints matching assets as: path, size, source (tab-separated).
        find           Alias for list that requires a substring.
        stats          Prints totals by extension and source.
        resources      Prints logical resources resolved through RT02 databases.
        objects        Lists GEC2 objects in a DT01 header document.
        templates      Lists entity/item templates and inheritance information.
        textures       Inspects DDS image resources and texture metadata.
        meshes         Inspects GMSH static meshes and vertex layouts.
        materials      Inspects material resources, shaders and texture bindings.
        skeletons      Inspects embedded skeletons in skn resources.
        export         Writes one non-executable asset to a file.

        Filters are case-insensitive. --limit defaults to 100; use --all for no limit.
        Executables are hidden from list/find unless --include-binaries is given and
        cannot be exported.
        """;

	private AssetCommandLine()
	{
	}

	public AssetCommandKind Kind { get; private init; }

	public string? Filter { get; private init; }

	public string? Extension { get; private init; }

	public string? Source { get; private init; }

	public string? Language { get; private init; }

	public string? TypeToken { get; private init; }

	public int Limit { get; private init; } = 100;

	public bool IncludeBinaries { get; private init; }

	public bool ShowProperties { get; private init; }

	public bool Decode { get; private init; }

	public string? AssetPath { get; private init; }

	public string? DestinationPath { get; private init; }

	public string? ElexPath { get; private init; }

	/// <summary>Returns whether the arguments start the asset inspector.</summary>
	public static bool IsAssetCommand(IReadOnlyList<string> args)
	{
		ArgumentNullException.ThrowIfNull(args);
		return args.Count > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Parses <c>assets [command] [options]</c>.
	/// </summary>
	/// <exception cref="ArgumentException">The command line is invalid.</exception>
	public static AssetCommandLine Parse(IReadOnlyList<string> args)
	{
		ArgumentNullException.ThrowIfNull(args);
		if (!IsAssetCommand(args))
		{
			throw new ArgumentException($"Expected the '{CommandName}' command.", nameof(args));
		}

		if (args.Count == 1)
		{
			return new AssetCommandLine { Kind = AssetCommandKind.Help };
		}

		AssetCommandKind kind;
		var isFind = false;
		switch (args[1].ToLowerInvariant())
		{
			case "list":
				kind = AssetCommandKind.List;
				break;
			case "find":
				kind = AssetCommandKind.List;
				isFind = true;
				break;
			case "stats":
				kind = AssetCommandKind.Stats;
				break;
			case "resources":
				kind = AssetCommandKind.Resources;
				break;
			case "objects":
				kind = AssetCommandKind.Objects;
				break;
			case "templates":
				kind = AssetCommandKind.Templates;
				break;
			case "textures":
				kind = AssetCommandKind.Textures;
				break;
			case "meshes":
				kind = AssetCommandKind.Meshes;
				break;
			case "materials":
				kind = AssetCommandKind.Materials;
				break;
			case "skeletons":
				kind = AssetCommandKind.Skeletons;
				break;
			case "export":
				kind = AssetCommandKind.Export;
				break;
			case "help":
			case "-h":
			case "--help":
				return new AssetCommandLine { Kind = AssetCommandKind.Help };
			default:
				throw new ArgumentException($"Unknown asset command '{args[1]}'.", nameof(args));
		}

		string? filter = null;
		string? extension = null;
		string? source = null;
		string? language = null;
		string? typeToken = null;
		string? elexPath = null;
		var limit = 100;
		var includeBinaries = false;
		var showProperties = false;
		var decode = false;
		var positionals = new List<string>();

		for (var index = 2; index < args.Count; index++)
		{
			var argument = args[index];
			var separator = argument.IndexOf('=');
			var name = (separator >= 0 ? argument[..separator] : argument).ToLowerInvariant();
			var inlineValue = separator >= 0 ? argument[(separator + 1)..] : null;

			string TakeValue()
			{
				if (inlineValue is not null)
				{
					if (string.IsNullOrWhiteSpace(inlineValue))
					{
						throw new ArgumentException($"Option '{name}' requires a value.", nameof(args));
					}

					return inlineValue.Trim();
				}

				if (index + 1 >= args.Count)
				{
					throw new ArgumentException($"Option '{name}' requires a value.", nameof(args));
				}

				var value = args[++index];
				if (string.IsNullOrWhiteSpace(value))
				{
					throw new ArgumentException($"Option '{name}' requires a value.", nameof(args));
				}

				return value.Trim();
			}

			switch (name)
			{
				case "--help":
				case "-h":
					return new AssetCommandLine { Kind = AssetCommandKind.Help };
				case "--filter":
					filter = TakeValue();
					break;
				case "--ext":
					extension = NormalizeExtension(TakeValue());
					break;
				case "--source":
					source = TakeValue();
					break;
				case "--language":
					language = TakeValue().ToLowerInvariant();
					break;
				case "--type":
					typeToken = TakeValue().ToLowerInvariant();
					break;
				case "--limit":
					var limitText = TakeValue();
					if (!int.TryParse(limitText, System.Globalization.NumberStyles.Integer,
							System.Globalization.CultureInfo.InvariantCulture, out limit) || limit < 0)
					{
						throw new ArgumentException($"Invalid limit '{limitText}'.", nameof(args));
					}

					break;
				case "--all":
					if (inlineValue is not null)
					{
						throw new ArgumentException("Option '--all' does not take a value.", nameof(args));
					}

					limit = 0;
					break;
				case "--include-binaries":
					if (inlineValue is not null)
					{
						throw new ArgumentException("Option '--include-binaries' does not take a value.", nameof(args));
					}

					includeBinaries = true;
					break;
				case "--properties":
					if (inlineValue is not null)
					{
						throw new ArgumentException("Option '--properties' does not take a value.", nameof(args));
					}

					showProperties = true;
					break;
				case "--decode":
					if (inlineValue is not null)
					{
						throw new ArgumentException("Option '--decode' does not take a value.", nameof(args));
					}

					decode = true;
					break;
				case "--elex-path":
					elexPath = TakeValue();
					break;
				default:
					if (argument.StartsWith('-'))
					{
						throw new ArgumentException($"Unknown option '{argument}'.", nameof(args));
					}

					positionals.Add(argument);
					break;
			}
		}

		if (kind == AssetCommandKind.Export)
		{
			if (positionals.Count != 2 ||
				string.IsNullOrWhiteSpace(positionals[0]) ||
				string.IsNullOrWhiteSpace(positionals[1]))
			{
				throw new ArgumentException("export requires an asset path and a destination.", nameof(args));
			}

			return new AssetCommandLine
			{
				Kind = kind,
				AssetPath = positionals[0],
				DestinationPath = positionals[1],
				ElexPath = elexPath,
			};
		}

		if (kind == AssetCommandKind.Objects)
		{
			if (positionals.Count != 1 || string.IsNullOrWhiteSpace(positionals[0]))
			{
				throw new ArgumentException("objects requires an asset path.", nameof(args));
			}

			return new AssetCommandLine
			{
				Kind = kind,
				AssetPath = positionals[0],
				Limit = limit,
				ShowProperties = showProperties,
				ElexPath = elexPath,
			};
		}

		if (kind == AssetCommandKind.Stats)
		{
			if (positionals.Count != 0)
			{
				throw new ArgumentException("stats does not accept positional arguments.", nameof(args));
			}

			return new AssetCommandLine { Kind = kind, ElexPath = elexPath };
		}

		if (positionals.Count > 1)
		{
			throw new ArgumentException("Only one path substring can be supplied.", nameof(args));
		}

		if (positionals.Count == 1)
		{
			filter = MergeFilter(filter, positionals[0]);
		}

		if (isFind && string.IsNullOrWhiteSpace(filter))
		{
			throw new ArgumentException("find requires a path substring.", nameof(args));
		}

		return new AssetCommandLine
		{
			Kind = kind,
			Filter = filter,
			Extension = extension,
			Source = source,
			Language = language,
			TypeToken = typeToken,
			Limit = limit,
			IncludeBinaries = includeBinaries,
			ShowProperties = showProperties,
			Decode = decode,
			ElexPath = elexPath,
		};
	}

	private static string MergeFilter(string? existing, string positional)
	{
		if (string.IsNullOrWhiteSpace(existing))
		{
			return positional;
		}

		if (string.Equals(existing, positional, StringComparison.Ordinal))
		{
			return existing;
		}

		throw new ArgumentException("Only one path substring can be supplied.");
	}

	private static string NormalizeExtension(string value)
	{
		var trimmed = value.Trim();
		return (trimmed.StartsWith('.') ? trimmed : "." + trimmed).ToLowerInvariant();
	}
}
