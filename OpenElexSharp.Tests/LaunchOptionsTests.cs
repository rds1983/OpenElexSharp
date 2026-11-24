namespace OpenElexSharp.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Parse_NoArguments_ReturnsDefaults()
    {
        var options = LaunchOptions.Parse([]);

        Assert.False(options.SmokeTest);
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
