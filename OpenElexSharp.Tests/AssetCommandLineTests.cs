using OpenElexSharp.Assets;

namespace OpenElexSharp.Tests;

public class AssetCommandLineTests
{
    [Fact]
    public void IsAssetCommand_RecognizesCommand()
    {
        Assert.True(AssetCommandLine.IsAssetCommand(["assets", "list"]));
        Assert.True(AssetCommandLine.IsAssetCommand(["ASSETS", "stats"]));
        Assert.False(AssetCommandLine.IsAssetCommand(["--smoke-test"]));
        Assert.False(AssetCommandLine.IsAssetCommand([]));
    }

    [Fact]
    public void Parse_NoSubcommand_ReturnsHelp()
    {
        Assert.Equal(AssetCommandKind.Help, AssetCommandLine.Parse(["assets"]).Kind);
        Assert.Equal(AssetCommandKind.Help, AssetCommandLine.Parse(["assets", "help"]).Kind);
        Assert.Equal(AssetCommandKind.Help, AssetCommandLine.Parse(["assets", "--help"]).Kind);
    }

    [Fact]
    public void Parse_ListDefaults_ReturnsEmptyQuery()
    {
        var command = AssetCommandLine.Parse(["assets", "list"]);

        Assert.Equal(AssetCommandKind.List, command.Kind);
        Assert.Null(command.Filter);
        Assert.Null(command.Extension);
        Assert.Null(command.Source);
        Assert.Null(command.Language);
        Assert.Equal(100, command.Limit);
        Assert.False(command.IncludeBinaries);
        Assert.Null(command.ElexPath);
    }

    [Fact]
    public void Parse_ListWithFilters_ReadsAllValues()
    {
        var command = AssetCommandLine.Parse(
        [
            "assets", "list", "textures",
            "--ext", "dds",
            "--source", "c_1_na",
            "--language", "EN",
            "--limit", "25",
            "--include-binaries",
            "--elex-path", @"C:\Games\ELEX",
        ]);

        Assert.Equal(AssetCommandKind.List, command.Kind);
        Assert.Equal("textures", command.Filter);
        Assert.Equal(".dds", command.Extension);
        Assert.Equal("c_1_na", command.Source);
        Assert.Equal("en", command.Language);
        Assert.Equal(25, command.Limit);
        Assert.True(command.IncludeBinaries);
        Assert.Equal(@"C:\Games\ELEX", command.ElexPath);
    }

    [Fact]
    public void Parse_FindRequiresSubstring()
    {
        var command = AssetCommandLine.Parse(["assets", "find", "localization"]);

        Assert.Equal(AssetCommandKind.List, command.Kind);
        Assert.Equal("localization", command.Filter);
    }

    [Fact]
    public void Parse_FindWithoutSubstring_Throws()
    {
        Assert.Throws<ArgumentException>(() => AssetCommandLine.Parse(["assets", "find"]));
    }

    [Fact]
    public void Parse_AllDisablesLimit()
    {
        var command = AssetCommandLine.Parse(["assets", "list", "a", "--all"]);

        Assert.Equal(0, command.Limit);
    }

    [Fact]
    public void Parse_OptionsSupportEqualsSyntax()
    {
        var command = AssetCommandLine.Parse(
            ["assets", "list", "--ext=txt", "--source=extern", "--language=en", "--limit=3", @"--elex-path=C:\ELEX"]);

        Assert.Equal(".txt", command.Extension);
        Assert.Equal("extern", command.Source);
        Assert.Equal("en", command.Language);
        Assert.Equal(3, command.Limit);
        Assert.Equal(@"C:\ELEX", command.ElexPath);
    }

    [Fact]
    public void Parse_Stats_ReadsElexPath()
    {
        var command = AssetCommandLine.Parse(["assets", "stats", "--elex-path", @"C:\ELEX"]);

        Assert.Equal(AssetCommandKind.Stats, command.Kind);
        Assert.Equal(@"C:\ELEX", command.ElexPath);
    }

    [Fact]
    public void Parse_Resources_ReadsFilters()
    {
        var command = AssetCommandLine.Parse(
            ["assets", "resources", "INFO_", "--type", "snd", "--language", "EN", "--limit", "5"]);

        Assert.Equal(AssetCommandKind.Resources, command.Kind);
        Assert.Equal("INFO_", command.Filter);
        Assert.Equal("snd", command.TypeToken);
        Assert.Equal("en", command.Language);
        Assert.Equal(5, command.Limit);
    }

    [Fact]
    public void Parse_ResourcesWithoutFilter_IsValid()
    {
        var command = AssetCommandLine.Parse(["assets", "resources", "--all"]);

        Assert.Equal(AssetCommandKind.Resources, command.Kind);
        Assert.Null(command.Filter);
        Assert.Equal(0, command.Limit);
    }

    [Fact]
    public void Parse_Objects_ReadsPathAndLimit()
    {
        var command = AssetCommandLine.Parse(
            ["assets", "objects", "data/compiled/documents/w_quest.hdr", "--limit", "3"]);

        Assert.Equal(AssetCommandKind.Objects, command.Kind);
        Assert.Equal("data/compiled/documents/w_quest.hdr", command.AssetPath);
        Assert.Equal(3, command.Limit);
    }

    [Fact]
    public void Parse_ObjectsWithoutPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => AssetCommandLine.Parse(["assets", "objects"]));
    }

    [Fact]
    public void Parse_StatsWithPositional_Throws()
    {
        Assert.Throws<ArgumentException>(() => AssetCommandLine.Parse(["assets", "stats", "extra"]));
    }

    [Fact]
    public void Parse_Export_ReadsPaths()
    {
        var command = AssetCommandLine.Parse(
            ["assets", "export", "data/compiled/a.dds", @"out\a.dds", "--elex-path", @"C:\ELEX"]);

        Assert.Equal(AssetCommandKind.Export, command.Kind);
        Assert.Equal("data/compiled/a.dds", command.AssetPath);
        Assert.Equal(@"out\a.dds", command.DestinationPath);
        Assert.Equal(@"C:\ELEX", command.ElexPath);
    }

    [Fact]
    public void Parse_ExportWithOneArgument_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => AssetCommandLine.Parse(["assets", "export", "only-one-path"]));
    }

    [Fact]
    public void Parse_ExportWithEmptyDestination_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => AssetCommandLine.Parse(["assets", "export", "asset", ""]));
    }

    [Fact]
    public void Parse_UnknownCommand_Throws()
    {
        Assert.Throws<ArgumentException>(() => AssetCommandLine.Parse(["assets", "paint"]));
    }

    [Fact]
    public void Parse_UnknownOption_Throws()
    {
        Assert.Throws<ArgumentException>(() => AssetCommandLine.Parse(["assets", "list", "--nope"]));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Parse_InvalidLimit_Throws(string limit)
    {
        Assert.Throws<ArgumentException>(() => AssetCommandLine.Parse(["assets", "list", "--limit", limit]));
    }

    [Fact]
    public void Parse_MissingOptionValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => AssetCommandLine.Parse(["assets", "list", "--ext"]));
        Assert.Throws<ArgumentException>(() => AssetCommandLine.Parse(["assets", "list", "--elex-path"]));
    }

    [Fact]
    public void Parse_TwoPositionalFilters_Throws()
    {
        Assert.Throws<ArgumentException>(() => AssetCommandLine.Parse(["assets", "list", "one", "two"]));
    }

    [Fact]
    public void Parse_PositionalAndFilter_ThrowsWhenDifferent()
    {
        Assert.Throws<ArgumentException>(
            () => AssetCommandLine.Parse(["assets", "list", "one", "--filter", "two"]));
    }

    [Fact]
    public void Parse_NullArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AssetCommandLine.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => AssetCommandLine.IsAssetCommand(null!));
    }
}
