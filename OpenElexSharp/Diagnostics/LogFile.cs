namespace OpenElexSharp.Diagnostics;

/// <summary>
/// Thread-safe append-only text log. A file that already reached the size limit is moved to a
/// single <c>.1</c> backup when the log is opened, so repeated runs cannot grow it without
/// bound.
/// </summary>
public sealed class LogFile : IDisposable
{
	/// <summary>Default rotation threshold (5 MiB).</summary>
	public const long DefaultMaximumBytes = 5L * 1024 * 1024;

	private readonly object _sync = new();
	private StreamWriter? _writer;

	/// <summary>Opens or creates the log file at <paramref name="path"/>.</summary>
	/// <param name="path">Destination path; missing parent directories are created.</param>
	/// <param name="maximumBytes">Rotate an existing file at or above this size.</param>
	/// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumBytes"/> is zero or negative.</exception>
	/// <exception cref="IOException">The file or directory cannot be created.</exception>
	/// <exception cref="UnauthorizedAccessException">The path cannot be written.</exception>
	public LogFile(string path, long maximumBytes = DefaultMaximumBytes)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

		Path = path;
		var directory = System.IO.Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		if (File.Exists(path) && new FileInfo(path).Length >= maximumBytes)
		{
			File.Move(path, path + ".1", overwrite: true);
		}

		_writer = new StreamWriter(
			new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
		{
			AutoFlush = true,
		};
	}

	/// <summary>The absolute or relative path this log writes to.</summary>
	public string Path { get; }

	/// <summary>
	/// Appends one timestamped entry. When <paramref name="exception"/> is supplied it is written
	/// on the following lines, including its stack trace.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="level"/> is empty or whitespace.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public void Write(string level, string message, Exception? exception = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(level);
		ArgumentNullException.ThrowIfNull(message);

		var timestamp = DateTimeOffset.Now;
		lock (_sync)
		{
			if (_writer is null)
			{
				return;
			}

			_writer.WriteLine($"{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}");
			if (exception is not null)
			{
				_writer.WriteLine(exception);
			}
		}
	}

	/// <summary>Closes the file. Further writes are ignored and repeated disposal is safe.</summary>
	public void Dispose()
	{
		lock (_sync)
		{
			_writer?.Dispose();
			_writer = null;
		}
	}
}
