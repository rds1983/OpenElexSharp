namespace OpenElexSharp.Diagnostics;

/// <summary>
/// Process-wide log used by the game host. Entries are mirrored to the console and to a rotating
/// file; the file behaviour lives in <see cref="LogFile"/> so it can be tested without touching
/// global state.
/// </summary>
public static class Log
{
	private static readonly object Sync = new();
	private static LogFile? _file;
	private static bool _consoleEnabled = true;
	private static bool _fileFailureReported;

	/// <summary>Path of the active log file, or <see langword="null"/> when file logging is off.</summary>
	public static string? FilePath => _file?.Path;

	/// <summary>Whether a log file is currently open.</summary>
	public static bool IsInitialized => _file is not null;

	/// <summary>
	/// Starts file logging. A path that cannot be opened is reported once and logging continues
	/// on the console.
	/// </summary>
	/// <param name="path">Log file path; missing directories are created by <see cref="LogFile"/>.</param>
	/// <param name="console">Whether entries are also written to the console.</param>
	/// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
	public static void Initialize(string path, bool console = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		lock (Sync)
		{
			_file?.Dispose();
			_file = null;
			_consoleEnabled = console;
			_fileFailureReported = false;
			try
			{
				_file = new LogFile(path);
			}
			catch (Exception exception) when (
				exception is IOException or UnauthorizedAccessException or NotSupportedException or
					ArgumentException)
			{
				Console.Error.WriteLine($"Log warning: cannot open '{path}': {exception.Message}");
			}
		}
	}

	/// <summary>Closes the log file. Further entries still go to the console.</summary>
	public static void Shutdown()
	{
		lock (Sync)
		{
			_file?.Dispose();
			_file = null;
		}
	}

	/// <summary>Writes an informational entry.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public static void Info(string message) => Write("INFO", message, null, error: false);

	/// <summary>Writes a warning entry.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public static void Warning(string message) => Write("WARN", message, null, error: true);

	/// <summary>Writes an error entry, optionally with its exception and stack trace.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public static void Error(string message, Exception? exception = null) =>
		Write("ERROR", message, exception, error: true);

	private static void Write(string level, string message, Exception? exception, bool error)
	{
		ArgumentNullException.ThrowIfNull(message);

		var timestamp = DateTimeOffset.Now;
		lock (Sync)
		{
			if (_consoleEnabled)
			{
				var console = error ? Console.Error : Console.Out;
				console.WriteLine($"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}");
				if (exception is not null)
				{
					console.WriteLine(exception);
				}
			}

			if (_file is null)
			{
				return;
			}

			try
			{
				_file.Write(level, message, exception);
			}
			catch (Exception exceptionToLog) when (
				exceptionToLog is IOException or UnauthorizedAccessException or ObjectDisposedException or
					NotSupportedException)
			{
				if (!_fileFailureReported)
				{
					_fileFailureReported = true;
					Console.Error.WriteLine($"Log warning: writing failed: {exceptionToLog.Message}");
				}

				_file.Dispose();
				_file = null;
			}
		}
	}
}
