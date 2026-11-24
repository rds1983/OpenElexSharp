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
}
