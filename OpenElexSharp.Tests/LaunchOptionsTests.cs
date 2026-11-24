namespace OpenElexSharp.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Parse_NoArguments_ReturnsDefaults()
    {
        var options = LaunchOptions.Parse([]);

        Assert.False(options.SmokeTest);
        Assert.Null(options.ElexPath);
    }

    [Theory]
    [InlineData("--smoke-test")]
    [InlineData("--SMOKE-TEST")]
    [InlineData("--Smoke-Test")]
    public void Parse_SmokeTestArgument_EnablesSmokeTest(string argument)
    {
        var options = LaunchOptions.Parse([argument]);

        Assert.True(options.SmokeTest);
    }

    [Fact]
    public void Parse_RepeatedSmokeTestArgument_EnablesSmokeTest()
    {
        var options = LaunchOptions.Parse(["--smoke-test", "--smoke-test"]);

        Assert.True(options.SmokeTest);
    }

    [Fact]
    public void Parse_ElexPathAsSeparateArgument_SetsPath()
    {
        var options = LaunchOptions.Parse(["--elex-path", @"C:\Games\ELEX"]);

        Assert.Equal(@"C:\Games\ELEX", options.ElexPath);
    }

    [Fact]
    public void Parse_ElexPathWithEquals_SetsPath()
    {
        var options = LaunchOptions.Parse([@"--elex-path=C:\Games\ELEX"]);

        Assert.Equal(@"C:\Games\ELEX", options.ElexPath);
    }

    [Fact]
    public void Parse_ElexPathArgumentIsCaseInsensitive()
    {
        var options = LaunchOptions.Parse([@"--ELEX-PATH=C:\Games\ELEX"]);

        Assert.Equal(@"C:\Games\ELEX", options.ElexPath);
    }

    [Fact]
    public void Parse_ElexPathIsTrimmed()
    {
        var options = LaunchOptions.Parse(["--elex-path", @"  C:\Games\ELEX  "]);

        Assert.Equal(@"C:\Games\ELEX", options.ElexPath);
    }

    [Fact]
    public void Parse_RepeatedElexPath_LastValueWins()
    {
        var options = LaunchOptions.Parse(
            ["--elex-path", @"C:\First", @"--elex-path=C:\Second"]);

        Assert.Equal(@"C:\Second", options.ElexPath);
    }

    [Fact]
    public void Parse_CombinedOptions_SetsAllValues()
    {
        var options = LaunchOptions.Parse(["--smoke-test", "--elex-path", @"C:\Games\ELEX"]);

        Assert.True(options.SmokeTest);
        Assert.Equal(@"C:\Games\ELEX", options.ElexPath);
    }

    [Fact]
    public void Parse_MissingElexPathValue_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--elex-path"]));

        Assert.Contains("--elex-path", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyElexPathValue_Throws(string value)
    {
        var exception = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--elex-path", value]));

        Assert.Contains("--elex-path", exception.Message);
    }

    [Fact]
    public void Parse_EmptyElexPathWithEquals_Throws()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--elex-path="]));
    }

    [Fact]
    public void Parse_NullArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LaunchOptions.Parse(null!));
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("")]
    [InlineData("smoke-test")]
    public void Parse_UnknownArgument_Throws(string argument)
    {
        var exception = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse([argument]));

        Assert.Contains(argument, exception.Message);
    }
}
