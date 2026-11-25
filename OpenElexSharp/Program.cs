using OpenElexSharp;
using OpenElexSharp.Assets;
using OpenElexSharp.Diagnostics;

if (AssetCommandLine.IsAssetCommand(args))
{
	AssetCommandLine command;
	try
	{
		command = AssetCommandLine.Parse(args);
	}
	catch (ArgumentException exception)
	{
		Console.Error.WriteLine(exception.Message);
		Console.Error.WriteLine(AssetCommandLine.Usage);
		return 1;
	}

	if (command.Kind == AssetCommandKind.Help)
	{
		Console.WriteLine(AssetCommandLine.Usage);
		return 0;
	}

	AssetFileSystem assets;
	try
	{
		(_, assets) = MountAssets(command.ElexPath);
	}
	catch (Exception exception) when (exception is ElexInstallationException or MountConfigurationException)
	{
		Console.Error.WriteLine(exception.Message);
		return 2;
	}

	foreach (var diagnostic in assets.Diagnostics)
	{
		Console.Error.WriteLine($"Asset warning: {diagnostic}");
	}

	return AssetCatalogConsole.Run(
		command,
		AssetCatalog.Build(assets),
		assets,
		Console.Out,
		Console.Error);
}

LaunchOptions options;
try
{
	options = LaunchOptions.Parse(args);
}
catch (ArgumentException exception)
{
	Console.Error.WriteLine(exception.Message);
	Console.Error.WriteLine(
		"Usage: OpenElexSharp [--elex-path <path>] [--texture <image resource>] [--mesh <mesh resource>] " +
		"[--viewer] [--stress] [--no-cull] [--animation <net resource>] [--collision <col resource>] " +
		"[--capture <png path>] [--capture-frame <n>] [--soak-test <seconds>] [--crash-test] [--smoke-test]");
	Console.Error.WriteLine("       OpenElexSharp assets list|stats|export ...  (run 'OpenElexSharp assets help')");
	return 1;
}

AssetFileSystem gameAssets;
ElexInstallation gameInstallation;
try
{
	(gameInstallation, gameAssets) = MountAssets(options.ElexPath);
}
catch (Exception exception) when (exception is ElexInstallationException or MountConfigurationException)
{
	Console.Error.WriteLine(exception.Message);
	return 2;
}

Log.Initialize(Path.Combine("logs", "openelexsharp.log"));
AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
	if (eventArgs.ExceptionObject is Exception exception)
	{
		Log.Error("Unhandled exception.", exception);
	}
};

try
{
	Log.Info($"ELEX installation: {gameInstallation.RootPath}");
	var packedSources = gameAssets.Sources.OfType<PackedAssetSource>().ToArray();
	var looseSourceCount = gameAssets.Sources.Count - packedSources.Length;
	var packedEntryCount = packedSources.Sum(source => source.EntryCount);
	Log.Info(
		$"Mounted {looseSourceCount} loose source(s), {packedSources.Length} packed source(s) " +
		$"({packedEntryCount} files), {gameAssets.PackedMounts.Count} packed mount(s).");

	foreach (var diagnostic in gameAssets.Diagnostics.Take(5))
	{
		Log.Warning($"Asset warning: {diagnostic}");
	}

	Log.Info(options.SmokeTest
		? "OpenElexSharp starting (smoke test)."
		: "OpenElexSharp starting.");

	using (var game = new OpenElexGame(options, gameAssets))
	{
		game.Run();
	}

	Log.Info("OpenElexSharp exited.");
	return 0;
}
catch (Exception exception)
{
	return ReportCrash(exception, options);
}
finally
{
	Log.Shutdown();
}

static int ReportCrash(Exception exception, LaunchOptions options)
{
	Log.Error("Fatal error.", exception);
	var crashPath = CrashReport.Save("logs", exception, Log.FilePath);
	if (crashPath is not null)
	{
		Log.Error($"Crash report written to '{crashPath}'.");
	}

	var report = CrashReport.Create(exception, Log.FilePath);
	if (crashPath is not null)
	{
		report += Environment.NewLine + $"Crash report: {crashPath}";
	}

	try
	{
		using var screen = new CrashScreenGame(
			report,
			options.SmokeTest ? TimeSpan.FromSeconds(2) : null,
			options.CapturePath);
		screen.Run();
	}
	catch (Exception screenException)
	{
		// The crash screen is best-effort: a broken graphics stack must still leave a trace.
		Log.Error("Could not show the crash screen.", screenException);
		Console.Error.WriteLine(report);
	}

	return 3;
}

static (ElexInstallation Installation, AssetFileSystem Assets) MountAssets(string? explicitPath)
{
	var installationPath = ElexInstallation.ResolvePath(
		explicitPath,
		Environment.GetEnvironmentVariable(ElexInstallation.PathEnvironmentVariable));
	var installation = ElexInstallation.Open(installationPath);
	return (installation, AssetFileSystem.MountFromInstallation(installation));
}
