using OpenElexSharp.Diagnostics;

namespace OpenElexSharp.Tests;

public class CrashReportTests
{
	private static readonly DateTimeOffset Timestamp =
		new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

	[Fact]
	public void Create_IncludesFailureContext()
	{
		Exception exception;
		try
		{
			throw new InvalidOperationException("something broke");
		}
		catch (InvalidOperationException caught)
		{
			exception = caught;
		}

		var report = CrashReport.Create(exception, "logs/openelexsharp.log", Timestamp);

		Assert.Contains("OpenElexSharp crashed.", report);
		Assert.Contains("Time: 2026-01-02 03:04:05", report);
		Assert.Contains("Log: logs/openelexsharp.log", report);
		Assert.Contains("System.InvalidOperationException", report);
		Assert.Contains("something broke", report);
		Assert.Contains(nameof(Create_IncludesFailureContext), report);
	}

	[Fact]
	public void Create_WithoutLogPath_MarksItMissing()
	{
		var report = CrashReport.Create(new Exception("boom"), null, Timestamp);

		Assert.Contains("Log: (not configured)", report);
	}

	[Fact]
	public void Create_TruncatesLongReports()
	{
		var exception = new InvalidOperationException(new string('x', CrashReport.MaximumLength * 2));

		var report = CrashReport.Create(exception, null, Timestamp);

		Assert.True(report.Length <= CrashReport.MaximumLength);
		Assert.EndsWith("... (report truncated)", report);
	}

	[Fact]
	public void Save_WritesReportFile()
	{
		using var temp = new TempDirectory();
		var exception = new InvalidOperationException("saved");

		var path = CrashReport.Save(temp.Root, exception, "logs/openelexsharp.log", Timestamp);

		Assert.NotNull(path);
		Assert.Equal("crash-20260102-030405.txt", Path.GetFileName(path));
		var text = File.ReadAllText(path!);
		Assert.Contains("OpenElexSharp crashed.", text);
		Assert.Contains("saved", text);
		Assert.Contains("logs/openelexsharp.log", text);
	}

	[Fact]
	public void Save_CreatesMissingDirectory()
	{
		using var temp = new TempDirectory();
		var directory = Path.Combine(temp.Root, "reports", "nested");

		var path = CrashReport.Save(
			directory,
			new Exception("nested"),
			null,
			Timestamp);

		Assert.NotNull(path);
		Assert.True(File.Exists(path));
	}

	[Fact]
	public void Save_RejectsInvalidArguments()
	{
		Assert.Throws<ArgumentException>(() => CrashReport.Save(" ", new Exception(), null));
		Assert.Throws<ArgumentNullException>(() => CrashReport.Save("logs", null!, null));
	}
}
