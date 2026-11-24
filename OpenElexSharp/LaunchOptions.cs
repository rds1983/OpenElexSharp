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
    /// Parses command-line arguments.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An argument is not recognized or a value is missing.</exception>
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var smokeTest = false;
        string? elexPath = null;

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
            else
            {
                throw new ArgumentException($"Unknown command-line argument '{argument}'.", nameof(args));
            }
        }

        return new LaunchOptions { SmokeTest = smokeTest, ElexPath = elexPath };
    }

    private static string ParsePath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{ElexPathArgument} requires a non-empty path.", parameterName);
        }

        return value.Trim();
    }
}
