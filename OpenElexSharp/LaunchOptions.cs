namespace OpenElexSharp;

/// <summary>
/// Command-line options accepted by the engine executable.
/// </summary>
public sealed class LaunchOptions
{
    /// <summary>The argument that runs the engine for a few frames and exits.</summary>
    public const string SmokeTestArgument = "--smoke-test";

    /// <summary>
    /// Gets a value indicating whether the engine should open a window, render a few frames
    /// and exit successfully. Intended for verifying that the graphics stack works.
    /// </summary>
    public bool SmokeTest { get; private init; }

    /// <summary>
    /// Parses command-line arguments.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An argument is not recognized.</exception>
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var smokeTest = false;
        foreach (var arg in args)
        {
            if (string.Equals(arg, SmokeTestArgument, StringComparison.OrdinalIgnoreCase))
            {
                smokeTest = true;
                continue;
            }

            throw new ArgumentException($"Unknown command-line argument '{arg}'.", nameof(args));
        }

        return new LaunchOptions { SmokeTest = smokeTest };
    }
}
