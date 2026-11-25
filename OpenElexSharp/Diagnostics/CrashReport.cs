using System.Runtime.InteropServices;
using System.Text;

namespace OpenElexSharp.Diagnostics;

/// <summary>
/// Formats and stores a diagnosis-friendly crash report. The report is plain text so it can be
/// shown on the crash screen, written next to the log and attached to issue reports.
/// </summary>
public static class CrashReport
{
	/// <summary>Maximum number of characters kept for the crash screen.</summary>
	public const int MaximumLength = 16 * 1024;

	private const string TruncationSuffix = "... (report truncated)";

	/// <summary>Builds the report text for <paramref name="exception"/>.</summary>
	/// <param name="exception">The failure to describe.</param>
	/// <param name="logPath">Active log path, if any.</param>
	/// <param name="timestamp">Report time; defaults to now.</param>
	/// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
	public static string Create(
		Exception exception,
		string? logPath,
		DateTimeOffset? timestamp = null)
	{
		ArgumentNullException.ThrowIfNull(exception);

		var time = timestamp ?? DateTimeOffset.Now;
		var builder = new StringBuilder();
		builder.AppendLine("OpenElexSharp crashed.");
		builder.AppendLine($"Time: {time:yyyy-MM-dd HH:mm:ss zzz}");
		builder.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
		builder.AppendLine($"OS: {RuntimeInformation.OSDescription}");
		builder.AppendLine($"Log: {logPath ?? "(not configured)"}");
		builder.AppendLine();
		builder.AppendLine(exception.ToString());

		var report = builder.ToString();
		if (report.Length <= MaximumLength)
		{
			return report;
		}

		var keep = MaximumLength - TruncationSuffix.Length - Environment.NewLine.Length;
		return report[..keep] + Environment.NewLine + TruncationSuffix;
	}

	/// <summary>
	/// Writes the report to <c>crash-yyyyMMdd-HHmmss.txt</c> in <paramref name="directory"/>.
	/// File-system failures are swallowed so the crash screen can still be shown; the return value
	/// then is <see langword="null"/>.
	/// </summary>
	/// <param name="directory">Report directory; created when missing.</param>
	/// <param name="exception">The failure to describe.</param>
	/// <param name="logPath">Active log path, if any.</param>
	/// <param name="timestamp">Report time; defaults to now.</param>
	/// <exception cref="ArgumentException"><paramref name="directory"/> is empty or whitespace.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
	public static string? Save(
		string directory,
		Exception exception,
		string? logPath,
		DateTimeOffset? timestamp = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		ArgumentNullException.ThrowIfNull(exception);

		var time = timestamp ?? DateTimeOffset.Now;
		var path = Path.Combine(directory, $"crash-{time:yyyyMMdd-HHmmss}.txt");
		var report = Create(exception, logPath, time);
		try
		{
			Directory.CreateDirectory(directory);
			File.WriteAllText(path, report);
			return path;
		}
		catch (Exception saveException) when (
			saveException is IOException or UnauthorizedAccessException or NotSupportedException or
				ArgumentException)
		{
			return null;
		}
	}
}
