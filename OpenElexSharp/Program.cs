using OpenElexSharp;
using OpenElexSharp.Assets;

LaunchOptions options;
try
{
    options = LaunchOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine("Usage: OpenElexSharp [--elex-path <path>] [--smoke-test]");
    return 1;
}

AssetFileSystem assets;
ElexInstallation installation;
try
{
    var installationPath = ElexInstallation.ResolvePath(
        options.ElexPath,
        Environment.GetEnvironmentVariable(ElexInstallation.PathEnvironmentVariable));
    installation = ElexInstallation.Open(installationPath);
    assets = AssetFileSystem.MountFromInstallation(installation);
}
catch (Exception exception) when (exception is ElexInstallationException or MountConfigurationException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

Console.WriteLine($"ELEX installation: {installation.RootPath}");
var packedSources = assets.Sources.OfType<PackedAssetSource>().ToArray();
var looseSourceCount = assets.Sources.Count - packedSources.Length;
var packedEntryCount = packedSources.Sum(source => source.EntryCount);
Console.WriteLine(
    $"Mounted {looseSourceCount} loose source(s), {packedSources.Length} packed source(s) " +
    $"({packedEntryCount} files), {assets.PackedMounts.Count} packed mount(s).");

foreach (var diagnostic in assets.Diagnostics.Take(5))
{
    Console.Error.WriteLine($"Asset warning: {diagnostic}");
}

Console.WriteLine(options.SmokeTest
    ? "OpenElexSharp starting (smoke test)."
    : "OpenElexSharp starting.");

using (var game = new OpenElexGame(options, assets))
{
    game.Run();
}

Console.WriteLine("OpenElexSharp exited.");
return 0;
