namespace OpenElexSharp.Tests;

public class LaunchOptionsTests
{
	[Fact]
	public void Parse_NoArguments_ReturnsDefaults()
	{
		var options = LaunchOptions.Parse([]);

		Assert.False(options.SmokeTest);
		Assert.False(options.Viewer);
		Assert.False(options.Stress);
		Assert.False(options.NoCull);
		Assert.False(options.Terrain);
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
		var options = LaunchOptions.Parse(
			["--smoke-test", "--elex-path", @"C:\Games\ELEX", "--texture", "AS_AlbSigns_1_DfA"]);

		Assert.True(options.SmokeTest);
		Assert.Equal(@"C:\Games\ELEX", options.ElexPath);
		Assert.Equal("AS_AlbSigns_1_DfA", options.Texture);
	}

	[Fact]
	public void Parse_MeshWithSeparateArgument_SetsValue()
	{
		var options = LaunchOptions.Parse(["--mesh", "Obj_Dmy_MaterialCube_1_L1"]);

		Assert.Equal("Obj_Dmy_MaterialCube_1_L1", options.Mesh);
	}

	[Fact]
	public void Parse_MeshWithEquals_SetsValue()
	{
		var options = LaunchOptions.Parse(["--mesh=Obj_Dmy_MaterialCube_1_L1"]);

		Assert.Equal("Obj_Dmy_MaterialCube_1_L1", options.Mesh);
	}

	[Fact]
	public void Parse_MissingMeshValue_Throws()
	{
		var exception = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--mesh"]));

		Assert.Contains("--mesh", exception.Message);
	}

	[Fact]
	public void Parse_ViewerArgument_EnablesViewer()
	{
		var options = LaunchOptions.Parse(["--viewer"]);

		Assert.True(options.Viewer);
	}

	[Fact]
	public void Parse_ViewerWithMesh_SetsBoth()
	{
		var options = LaunchOptions.Parse(["--viewer", "--mesh", "Obj_Dmy_MaterialCube_1_L1"]);

		Assert.True(options.Viewer);
		Assert.Equal("Obj_Dmy_MaterialCube_1_L1", options.Mesh);
	}

	[Fact]
	public void Parse_StressArgument_EnablesStressScene()
	{
		var options = LaunchOptions.Parse(["--stress", "--mesh", "Obj_Dmy_MaterialCube_1_L1"]);

		Assert.True(options.Stress);
		Assert.Equal("Obj_Dmy_MaterialCube_1_L1", options.Mesh);
	}

	[Fact]
	public void Parse_NoCullArgument_DisablesCulling()
	{
		var options = LaunchOptions.Parse(["--no-cull"]);

		Assert.True(options.NoCull);
	}

	[Fact]
	public void Parse_OneSector_SetsSectorList()
	{
		var options = LaunchOptions.Parse(["--sector", "ABE_AbessaCity_Buildings"]);

		Assert.Equal(["ABE_AbessaCity_Buildings"], options.Sectors);
	}

	[Fact]
	public void Parse_RepeatedSectors_PreservesOrder()
	{
		var options = LaunchOptions.Parse(
			["--sector", "ABE_AbessaCity_Buildings", "--sector=ABE_AbessaCity_Decoration"]);

		Assert.Equal(
			["ABE_AbessaCity_Buildings", "ABE_AbessaCity_Decoration"],
			options.Sectors);
	}

	[Fact]
	public void Parse_MissingSectorValue_Throws()
	{
		var exception = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--sector"]));

		Assert.Contains("--sector", exception.Message);
	}

	[Theory]
	[InlineData("--terrain")]
	[InlineData("--TERRAIN")]
	public void Parse_TerrainArgument_EnablesTerrain(string argument)
	{
		var options = LaunchOptions.Parse([argument]);

		Assert.True(options.Terrain);
		Assert.Empty(options.Sectors);
	}

	[Fact]
	public void Parse_TerrainWithSector_LoadsBoth()
	{
		var options = LaunchOptions.Parse(["--sector", "TAV_Fort_Buildings", "--terrain"]);

		Assert.True(options.Terrain);
		Assert.Equal(["TAV_Fort_Buildings"], options.Sectors);
	}

	[Fact]
	public void Parse_AnimationWithSeparateArgument_SetsValue()
	{
		var options = LaunchOptions.Parse(["--animation", "Male"]);

		Assert.Equal("Male", options.Animation);
	}

	[Fact]
	public void Parse_AnimationWithEquals_SetsValue()
	{
		var options = LaunchOptions.Parse(["--animation=Male"]);

		Assert.Equal("Male", options.Animation);
	}

	[Fact]
	public void Parse_MissingAnimationValue_Throws()
	{
		var exception = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--animation"]));

		Assert.Contains("--animation", exception.Message);
	}

	[Fact]
	public void Parse_CaptureWithSeparateArgument_SetsPath()
	{
		var options = LaunchOptions.Parse(["--capture", @"C:\Temp\shot.png"]);

		Assert.Equal(@"C:\Temp\shot.png", options.CapturePath);
	}

	[Fact]
	public void Parse_CaptureWithEquals_SetsPath()
	{
		var options = LaunchOptions.Parse([@"--capture=C:\Temp\shot.png"]);

		Assert.Equal(@"C:\Temp\shot.png", options.CapturePath);
	}

	[Fact]
	public void Parse_MissingCaptureValue_Throws()
	{
		var exception = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--capture"]));

		Assert.Contains("--capture", exception.Message);
	}

	[Fact]
	public void Parse_CaptureFrameWithSeparateArgument_SetsValue()
	{
		var options = LaunchOptions.Parse(["--capture-frame", "120"]);

		Assert.Equal(120, options.CaptureFrame);
	}

	[Fact]
	public void Parse_CaptureFrameWithEquals_SetsValue()
	{
		var options = LaunchOptions.Parse(["--capture-frame=90"]);

		Assert.Equal(90, options.CaptureFrame);
	}

	[Fact]
	public void Parse_MissingCaptureFrameValue_Throws()
	{
		var exception = Assert.Throws<ArgumentException>(
			() => LaunchOptions.Parse(["--capture-frame"]));

		Assert.Contains("--capture-frame", exception.Message);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("2.5")]
	[InlineData("later")]
	public void Parse_InvalidCaptureFrameValue_Throws(string value)
	{
		var exception = Assert.Throws<ArgumentException>(
			() => LaunchOptions.Parse(["--capture-frame", value]));

		Assert.Contains("--capture-frame", exception.Message);
	}

	[Fact]
	public void Parse_TextureWithEquals_SetsValue()
	{
		var options = LaunchOptions.Parse(["--texture=AS_AlbSigns_1_Nm"]);

		Assert.Equal("AS_AlbSigns_1_Nm", options.Texture);
	}

	[Fact]
	public void Parse_TextureIsTrimmed()
	{
		var options = LaunchOptions.Parse(["--texture", "  AS_AlbSigns_1_Nm  "]);

		Assert.Equal("AS_AlbSigns_1_Nm", options.Texture);
	}

	[Fact]
	public void Parse_MissingTextureValue_Throws()
	{
		var exception = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--texture"]));

		Assert.Contains("--texture", exception.Message);
	}

	[Fact]
	public void Parse_EmptyTextureValue_Throws()
	{
		Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--texture="]));
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

	[Fact]
	public void Parse_SoakTestWithSeparateArgument_SetsSeconds()
	{
		var options = LaunchOptions.Parse(["--soak-test", "180"]);

		Assert.Equal(180, options.SoakSeconds);
	}

	[Fact]
	public void Parse_SoakTestWithEquals_SetsSeconds()
	{
		var options = LaunchOptions.Parse(["--soak-test=45"]);

		Assert.Equal(45, options.SoakSeconds);
	}

	[Fact]
	public void Parse_SoakTestIsCaseInsensitive()
	{
		var options = LaunchOptions.Parse(["--SOAK-TEST=30"]);

		Assert.Equal(30, options.SoakSeconds);
	}

	[Fact]
	public void Parse_RepeatedSoakTest_LastValueWins()
	{
		var options = LaunchOptions.Parse(["--soak-test", "10", "--soak-test=20"]);

		Assert.Equal(20, options.SoakSeconds);
	}

	[Fact]
	public void Parse_MissingSoakTestValue_Throws()
	{
		var exception = Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--soak-test"]));

		Assert.Contains("--soak-test", exception.Message);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("1.5")]
	[InlineData("soon")]
	[InlineData("")]
	public void Parse_InvalidSoakTestValue_Throws(string value)
	{
		var exception = Assert.Throws<ArgumentException>(
			() => LaunchOptions.Parse(["--soak-test", value]));

		Assert.Contains("--soak-test", exception.Message);
	}

	[Fact]
	public void Parse_CrashTestArgument_EnablesCrashTest()
	{
		var options = LaunchOptions.Parse(["--crash-test"]);

		Assert.True(options.CrashTest);
	}

	[Fact]
	public void Parse_Defaults_HaveNoSoakOrCrashTest()
	{
		var options = LaunchOptions.Parse([]);

		Assert.Null(options.SoakSeconds);
		Assert.False(options.CrashTest);
	}
}
