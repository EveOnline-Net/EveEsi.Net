namespace EveEsi.Net.Generator.Tests;

public sealed class GeneratorOptionsTests
{
    [Fact]
    public void Parse_SpecAndOut_DefaultsConfigNextToSpec()
    {
        var options = GeneratorOptions.Parse(["--spec", "openapi.json", "--out", "src/"], out var error);

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options.SpecPath.ShouldBe("openapi.json");
        options.OutputPath.ShouldBe("src/");
        options.ConfigPath.ShouldBe(Path.Combine(Path.GetDirectoryName(Path.GetFullPath("openapi.json"))!, "generator.json"));
    }

    [Fact]
    public void Parse_ExplicitConfig_UsesIt()
    {
        var options = GeneratorOptions.Parse(["--spec", "a.json", "--out", "o", "--config", "c.json"], out _);

        options.ShouldNotBeNull();
        options.ConfigPath.ShouldBe("c.json");
    }

    [Theory]
    [InlineData]
    [InlineData("--spec", "openapi.json")]
    [InlineData("--out", "src/")]
    [InlineData("--spec", "openapi.json", "--out")]
    [InlineData("--spec", "openapi.json", "--out", "src/", "--bogus")]
    public void Parse_InvalidArguments_ReturnsNullWithError(params string[] args)
    {
        var options = GeneratorOptions.Parse(args, out var error);

        options.ShouldBeNull();
        error.ShouldNotBeNullOrWhiteSpace();
    }
}
