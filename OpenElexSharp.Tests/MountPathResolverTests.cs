using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class MountPathResolverTests
{
    private static readonly string AppPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OpenElexSharp"));

    [Fact]
    public void Resolve_RelativePath_CombinesWithBase()
    {
        var resolver = CreateResolver();

        Assert.Equal(
            Path.Combine(AppPath, "data", "extern"),
            resolver.Resolve("data/extern", AppPath));
    }

    [Fact]
    public void Resolve_EmptyPath_ReturnsBase()
    {
        var resolver = CreateResolver();

        Assert.Equal(AppPath, resolver.Resolve(string.Empty, AppPath));
    }

    [Fact]
    public void Resolve_AbsolutePath_IsKept()
    {
        var resolver = CreateResolver();
        var absolutePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Elsewhere", "assets"));

        Assert.Equal(absolutePath, resolver.Resolve(absolutePath, AppPath));
    }

    [Fact]
    public void Resolve_AppMacro_ExpandsToAppPath()
    {
        var resolver = CreateResolver();

        Assert.Equal(
            Path.Combine(AppPath, "scripts"),
            resolver.Resolve("$(app)/scripts", Path.GetTempPath()));
    }

    [Fact]
    public void Resolve_MacroNameIsCaseInsensitive()
    {
        var resolver = CreateResolver();

        Assert.Equal(
            Path.Combine(AppPath, "data"),
            resolver.Resolve("$(APP)/data", Path.GetTempPath()));
    }

    [Fact]
    public void Resolve_MultipleMacros_ExpandsAll()
    {
        var resolver = CreateResolver();

        Assert.Equal(
            Path.Combine(AppPath, "extern"),
            resolver.Resolve("$(app)/$(sub)", Path.GetTempPath()));
    }

    [Fact]
    public void Resolve_MacroProducingRootedPath_KeepsRoot()
    {
        var resolver = CreateResolver();
        var absolutePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "absolute"));
        resolver = new MountPathResolver(AppPath, new Dictionary<string, string> { ["root"] = absolutePath });

        Assert.Equal(absolutePath, resolver.Resolve("$(root)", AppPath));
    }

    [Fact]
    public void Resolve_UnknownMacro_Throws()
    {
        var resolver = CreateResolver();

        var exception = Assert.Throws<MountConfigurationException>(() => resolver.Resolve("$(missing)/data", AppPath));

        Assert.Contains("$(missing)", exception.Message);
    }

    [Fact]
    public void Resolve_UnterminatedMacro_Throws()
    {
        var resolver = CreateResolver();

        Assert.Throws<MountConfigurationException>(() => resolver.Resolve("$(app/data", AppPath));
    }

    [Fact]
    public void Resolve_ParentSegments_AreNormalized()
    {
        var resolver = CreateResolver();
        var basePath = Path.Combine(AppPath, "system");

        Assert.Equal(AppPath, resolver.Resolve("..", basePath));
    }

    [Fact]
    public void Constructor_NullMacros_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MountPathResolver(AppPath, null!));
    }

    [Fact]
    public void Constructor_EmptyAppPath_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new MountPathResolver("  ", new Dictionary<string, string>()));
    }

    private static MountPathResolver CreateResolver() =>
        new(
            AppPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["app"] = AppPath,
                ["sub"] = "extern",
            });
}
