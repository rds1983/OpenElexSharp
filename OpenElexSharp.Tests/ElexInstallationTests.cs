using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class ElexInstallationTests
{
    [Fact]
    public void Open_ValidInstallation_ResolvesRootAndAppPath()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("system");
        temp.CreateFile("data/ini/mount_packed.xml", "<MountList />");

        var installation = ElexInstallation.Open(temp.Root);

        Assert.Equal(temp.Root, installation.RootPath);
        Assert.Equal(Path.Combine(temp.Root, "system"), installation.AppPath);
        Assert.Equal(
            Path.Combine(temp.Root, "data", "ini", "mount_packed.xml"),
            installation.MountConfigurationPath);
    }

    [Fact]
    public void Open_WithoutSystemFolder_UsesRootAsAppPath()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("data/ini/mount_packed.xml", "<MountList />");

        var installation = ElexInstallation.Open(temp.Root);

        Assert.Equal(temp.Root, installation.AppPath);
    }

    [Fact]
    public void Open_MissingFolder_Throws()
    {
        using var temp = new TempDirectory();
        var missing = Path.Combine(temp.Root, "missing");

        var exception = Assert.Throws<ElexInstallationException>(() => ElexInstallation.Open(missing));

        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public void Open_MissingMountConfiguration_Throws()
    {
        using var temp = new TempDirectory();
        temp.CreateDirectory("system");

        var exception = Assert.Throws<ElexInstallationException>(() => ElexInstallation.Open(temp.Root));

        Assert.Contains("mount_packed.xml", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_EmptyPath_Throws(string path)
    {
        Assert.Throws<ArgumentException>(() => ElexInstallation.Open(path));
    }

    [Fact]
    public void Open_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ElexInstallation.Open(null!));
    }

    [Fact]
    public void Open_InvalidPath_ThrowsInstallationException()
    {
        Assert.Throws<ElexInstallationException>(() => ElexInstallation.Open("invalid\0path"));
    }

    [Fact]
    public void ResolvePath_ExplicitPathWins()
    {
        Assert.Equal(@"C:\Explicit", ElexInstallation.ResolvePath(@"C:\Explicit", @"C:\Environment"));
    }

    [Fact]
    public void ResolvePath_FallsBackToEnvironment()
    {
        Assert.Equal(@"C:\Environment", ElexInstallation.ResolvePath(null, @"C:\Environment"));
    }

    [Fact]
    public void ResolvePath_IgnoresWhitespaceValues()
    {
        Assert.Equal(@"C:\Environment", ElexInstallation.ResolvePath("  ", @"C:\Environment"));
    }

    [Fact]
    public void ResolvePath_TrimsValue()
    {
        Assert.Equal(@"C:\Explicit", ElexInstallation.ResolvePath(@"  C:\Explicit  ", null));
    }

    [Fact]
    public void ResolvePath_NothingConfigured_Throws()
    {
        var exception = Assert.Throws<ElexInstallationException>(() => ElexInstallation.ResolvePath(null, null));

        Assert.Contains(ElexInstallation.PathEnvironmentVariable, exception.Message);
        Assert.Contains("--elex-path", exception.Message);
    }
}
