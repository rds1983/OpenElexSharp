namespace OpenElexSharp;

/// <summary>
/// Command-line options accepted by the engine executable.
/// </summary>
public sealed class LaunchOptions
{
	/// <summary>The argument that runs the engine for a few frames and exits.</summary>
	public const string SmokeTestArgument = "--smoke-test";

	/// <summary>The argument that points at the ELEX installation folder.</summary>
	public const string ElexPathArgument = "--elex-path";

	/// <summary>The argument that loads an image resource for the texture test scene.</summary>
	public const string TextureArgument = "--texture";

	/// <summary>The argument that loads a mesh resource for the static-mesh test scene.</summary>
	public const string MeshArgument = "--mesh";

	/// <summary>The argument that loads a motion network and displays a skeleton pose.</summary>
	public const string AnimationArgument = "--animation";

	/// <summary>The argument that loads a collision mesh and draws it as a wireframe.</summary>
	public const string CollisionArgument = "--collision";

	/// <summary>The argument that writes the rendered back buffer to a PNG file.</summary>
	public const string CaptureArgument = "--capture";

	/// <summary>The argument that chooses the frame saved by <see cref="CaptureArgument"/>.</summary>
	public const string CaptureFrameArgument = "--capture-frame";

	/// <summary>The argument that runs the engine for a fixed number of seconds and exits.</summary>
	public const string SoakTestArgument = "--soak-test";

	/// <summary>The argument that deliberately throws after startup to exercise the crash screen.</summary>
	public const string CrashTestArgument = "--crash-test";

	/// <summary>
	/// Gets a value indicating whether the engine should open a window, render a few frames
	/// and exit successfully. Intended for verifying that the graphics stack works.
	/// </summary>
	public bool SmokeTest { get; private init; }

	/// <summary>
	/// Gets the explicitly configured ELEX installation path, or <see langword="null"/> to use
	/// the <see cref="Assets.ElexInstallation.PathEnvironmentVariable"/> environment variable.
	/// </summary>
	public string? ElexPath { get; private init; }

	/// <summary>
	/// Gets the image resource name (or name substring) to display in the texture test scene.
	/// </summary>
	public string? Texture { get; private init; }

	/// <summary>
	/// Gets the mesh resource name (or name substring) to display in the static-mesh test scene.
	/// </summary>
	public string? Mesh { get; private init; }

	/// <summary>
	/// Gets the motion-network resource name (or substring) for the skeleton pose scene. When set,
	/// <see cref="Mesh"/> names the <c>skn</c> resource whose skeleton is displayed.
	/// </summary>
	public string? Animation { get; private init; }

	/// <summary>
	/// Gets the collision resource name (or substring) drawn as a wireframe in the test scene.
	/// </summary>
	public string? Collision { get; private init; }

	/// <summary>
	/// Gets the PNG path that receives a copy of the rendered back buffer after a few frames.
	/// </summary>
	public string? CapturePath { get; private init; }

	/// <summary>
	/// Gets the frame number captured to <see cref="CapturePath"/>, or <see langword="null"/> for
	/// the default (frame 4).
	/// </summary>
	public int? CaptureFrame { get; private init; }

	/// <summary>
	/// Gets how long a soak run should render before exiting, or <see langword="null"/> for a
	/// normal session.
	/// </summary>
	public int? SoakSeconds { get; private init; }

	/// <summary>
	/// Gets a value indicating whether the game should throw after a few frames to verify the
	/// crash screen.
	/// </summary>
	public bool CrashTest { get; private init; }

	/// <summary>
	/// Parses command-line arguments.
	/// </summary>
	/// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">An argument is not recognized or a value is missing.</exception>
	public static LaunchOptions Parse(IReadOnlyList<string> args)
	{
		ArgumentNullException.ThrowIfNull(args);

		var smokeTest = false;
		string? elexPath = null;
		string? texture = null;
		string? mesh = null;
		string? animation = null;
		string? collision = null;
		string? capturePath = null;
		int? captureFrame = null;
		int? soakSeconds = null;
		var crashTest = false;

		for (var index = 0; index < args.Count; index++)
		{
			var argument = args[index];
			if (string.Equals(argument, SmokeTestArgument, StringComparison.OrdinalIgnoreCase))
			{
				smokeTest = true;
			}
			else if (string.Equals(argument, ElexPathArgument, StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Count)
				{
					throw new ArgumentException($"{ElexPathArgument} requires a value.", nameof(args));
				}

				elexPath = ParsePath(args[++index], nameof(args));
			}
			else if (argument.StartsWith(ElexPathArgument + "=", StringComparison.OrdinalIgnoreCase))
			{
				elexPath = ParsePath(argument[(ElexPathArgument.Length + 1)..], nameof(args));
			}
			else if (string.Equals(argument, TextureArgument, StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Count)
				{
					throw new ArgumentException($"{TextureArgument} requires a value.", nameof(args));
				}

				texture = ParseValue(TextureArgument, args[++index], nameof(args));
			}
			else if (argument.StartsWith(TextureArgument + "=", StringComparison.OrdinalIgnoreCase))
			{
				texture = ParseValue(
					TextureArgument,
					argument[(TextureArgument.Length + 1)..],
					nameof(args));
			}
			else if (string.Equals(argument, MeshArgument, StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Count)
				{
					throw new ArgumentException($"{MeshArgument} requires a value.", nameof(args));
				}

				mesh = ParseValue(MeshArgument, args[++index], nameof(args));
			}
			else if (argument.StartsWith(MeshArgument + "=", StringComparison.OrdinalIgnoreCase))
			{
				mesh = ParseValue(
					MeshArgument,
					argument[(MeshArgument.Length + 1)..],
					nameof(args));
			}
			else if (string.Equals(argument, AnimationArgument, StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Count)
				{
					throw new ArgumentException($"{AnimationArgument} requires a value.", nameof(args));
				}

				animation = ParseValue(AnimationArgument, args[++index], nameof(args));
			}
			else if (argument.StartsWith(AnimationArgument + "=", StringComparison.OrdinalIgnoreCase))
			{
				animation = ParseValue(
					AnimationArgument,
					argument[(AnimationArgument.Length + 1)..],
					nameof(args));
			}
			else if (string.Equals(argument, CollisionArgument, StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Count)
				{
					throw new ArgumentException($"{CollisionArgument} requires a value.", nameof(args));
				}

				collision = ParseValue(CollisionArgument, args[++index], nameof(args));
			}
			else if (argument.StartsWith(CollisionArgument + "=", StringComparison.OrdinalIgnoreCase))
			{
				collision = ParseValue(
					CollisionArgument,
					argument[(CollisionArgument.Length + 1)..],
					nameof(args));
			}
			else if (string.Equals(argument, CaptureArgument, StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Count)
				{
					throw new ArgumentException($"{CaptureArgument} requires a value.", nameof(args));
				}

				capturePath = ParseValue(CaptureArgument, args[++index], nameof(args));
			}
			else if (argument.StartsWith(CaptureArgument + "=", StringComparison.OrdinalIgnoreCase))
			{
				capturePath = ParseValue(
					CaptureArgument,
					argument[(CaptureArgument.Length + 1)..],
					nameof(args));
			}
			else if (string.Equals(argument, CaptureFrameArgument, StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Count)
				{
					throw new ArgumentException($"{CaptureFrameArgument} requires a value.", nameof(args));
				}

				captureFrame = ParsePositiveInt(CaptureFrameArgument, args[++index], nameof(args));
			}
			else if (argument.StartsWith(CaptureFrameArgument + "=", StringComparison.OrdinalIgnoreCase))
			{
				captureFrame = ParsePositiveInt(
					CaptureFrameArgument,
					argument[(CaptureFrameArgument.Length + 1)..],
					nameof(args));
			}
			else if (string.Equals(argument, SoakTestArgument, StringComparison.OrdinalIgnoreCase))
			{
				if (index + 1 >= args.Count)
				{
					throw new ArgumentException($"{SoakTestArgument} requires a value.", nameof(args));
				}

				soakSeconds = ParsePositiveInt(SoakTestArgument, args[++index], nameof(args));
			}
			else if (argument.StartsWith(SoakTestArgument + "=", StringComparison.OrdinalIgnoreCase))
			{
				soakSeconds = ParsePositiveInt(
					SoakTestArgument,
					argument[(SoakTestArgument.Length + 1)..],
					nameof(args));
			}
			else if (string.Equals(argument, CrashTestArgument, StringComparison.OrdinalIgnoreCase))
			{
				crashTest = true;
			}
			else
			{
				throw new ArgumentException($"Unknown command-line argument '{argument}'.", nameof(args));
			}
		}

		return new LaunchOptions
		{
			SmokeTest = smokeTest,
			ElexPath = elexPath,
			Texture = texture,
			Mesh = mesh,
			Animation = animation,
			Collision = collision,
			CapturePath = capturePath,
			CaptureFrame = captureFrame,
			SoakSeconds = soakSeconds,
			CrashTest = crashTest,
		};
	}

	private static string ParsePath(string value, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{ElexPathArgument} requires a non-empty path.", parameterName);
		}

		return value.Trim();
	}

	private static string ParseValue(string argument, string value, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException($"{argument} requires a non-empty value.", parameterName);
		}

		return value.Trim();
	}

	private static int ParsePositiveInt(string argument, string value, string parameterName)
	{
		if (!int.TryParse(
				value.Trim(),
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture,
				out var result) ||
			result <= 0)
		{
			throw new ArgumentException(
				$"{argument} requires a positive integer.",
				parameterName);
		}

		return result;
	}
}
