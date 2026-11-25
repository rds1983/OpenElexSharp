using OpenElexSharp.Diagnostics;

namespace OpenElexSharp.Tests;

public class LogFileTests
{
	[Fact]
	public void Write_CreatesDirectoryAndAppendsEntries()
	{
		using var temp = new TempDirectory();
		var path = Path.Combine(temp.Root, "nested", "game.log");

		using (var log = new LogFile(path))
		{
			log.Write("INFO", "first entry");
			log.Write("WARN", "second entry");
		}

		var text = File.ReadAllText(path);
		Assert.Contains("[INFO] first entry", text);
		Assert.Contains("[WARN] second entry", text);
	}

	[Fact]
	public void Write_IncludesExceptionAndStackTrace()
	{
		using var temp = new TempDirectory();
		var path = temp.CreateFile("game.log");

		using (var log = new LogFile(path))
		{
			log.Write("ERROR", "boom", new InvalidOperationException("bad state"));
		}

		var text = File.ReadAllText(path);
		Assert.Contains("[ERROR] boom", text);
		Assert.Contains("System.InvalidOperationException", text);
		Assert.Contains("bad state", text);
	}

	[Fact]
	public void Constructor_RotatesFileAtSizeLimit()
	{
		using var temp = new TempDirectory();
		var path = temp.CreateFile("game.log", "01234567");

		using (var log = new LogFile(path, maximumBytes: 4))
		{
			log.Write("INFO", "fresh");
		}

		Assert.Equal("01234567", File.ReadAllText(path + ".1"));
		var text = File.ReadAllText(path);
		Assert.DoesNotContain("01234567", text);
		Assert.Contains("[INFO] fresh", text);
	}

	[Fact]
	public void Constructor_KeepsFileBelowSizeLimit()
	{
		using var temp = new TempDirectory();
		var path = temp.CreateFile("game.log", "old");

		using (var log = new LogFile(path, maximumBytes: 1024))
		{
			log.Write("INFO", "new");
		}

		Assert.False(File.Exists(path + ".1"));
		var text = File.ReadAllText(path);
		Assert.Contains("old", text);
		Assert.Contains("new", text);
	}

	[Fact]
	public void Constructor_AppendsAcrossInstances()
	{
		using var temp = new TempDirectory();
		var path = temp.CreateFile("game.log");

		using (var first = new LogFile(path))
		{
			first.Write("INFO", "first");
		}

		using (var second = new LogFile(path))
		{
			second.Write("INFO", "second");
		}

		var text = File.ReadAllText(path);
		Assert.Contains("first", text);
		Assert.Contains("second", text);
	}

	[Fact]
	public void Dispose_IsIdempotentAndIgnoresLaterWrites()
	{
		using var temp = new TempDirectory();
		var path = temp.CreateFile("game.log");
		var log = new LogFile(path);
		log.Write("INFO", "before");
		log.Dispose();
		log.Dispose();

		log.Write("INFO", "after");

		var text = File.ReadAllText(path);
		Assert.Contains("before", text);
		Assert.DoesNotContain("after", text);
	}

	[Fact]
	public void Constructor_RejectsInvalidArguments()
	{
		Assert.Throws<ArgumentException>(() => new LogFile(" "));
		Assert.Throws<ArgumentNullException>(() => new LogFile(null!));

		using var temp = new TempDirectory();
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new LogFile(temp.CreateFile("game.log"), maximumBytes: 0));
	}

	[Fact]
	public void Write_RejectsInvalidArguments()
	{
		using var temp = new TempDirectory();
		using var log = new LogFile(temp.CreateFile("game.log"));

		Assert.Throws<ArgumentException>(() => log.Write(" ", "message"));
		Assert.Throws<ArgumentNullException>(() => log.Write("INFO", null!));
	}
}
