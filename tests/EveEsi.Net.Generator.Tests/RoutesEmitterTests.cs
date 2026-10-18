using EveEsi.Net.Generator.Config;
using EveEsi.Net.Generator.Emit;
using EveEsi.Net.Generator.Spec;

namespace EveEsi.Net.Generator.Tests;

public sealed class RoutesEmitterTests
{
    private const string Config = """{ "operationConstants": { "GetCharactersCharacterIdAssets": "CharacterAssets" } }""";

    [Fact]
    public void Generate_FixtureSpec_MatchesSnapshots()
    {
        var result = Generate(Snapshot.Fixture("routes.openapi.json"), Config);

        Snapshot.Match("Routes", result.Files, RoutesEmitter.Directory);
    }

    [Fact]
    public void Generate_FixtureSpec_IsDeterministic()
    {
        var first = Generate(Snapshot.Fixture("routes.openapi.json"), Config);
        var second = Generate(Snapshot.Fixture("routes.openapi.json"), Config);

        second.Files.Select(f => f.Content).ShouldBe(first.Files.Select(f => f.Content));
    }

    [Fact]
    public void Generate_UnmappedOperations_AreReported()
    {
        var result = Generate(Snapshot.Fixture("routes.openapi.json"), Config);

        result.Notes.ShouldContain(n => n.StartsWith("4 of 5 operation constants", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_TwoScopes_Fails() =>
        ShouldFail(Operation(extra: """, "security": [{ "OAuth2": ["a", "b"] }]"""), "exactly one OAuth2 scope");

    [Fact]
    public void Generate_UnknownCacheMode_Fails() =>
        ShouldFail(Operation(extra: """, "x-server-cache-mode": "sometimes" """), "unknown cache mode");

    [Fact]
    public void Generate_DisagreeingCacheModes_Fails() =>
        ShouldFail(Operation(extra: """, "x-server-cache-mode": "ttl-based", "x-cache-mode": "event-based" """), "disagree");

    [Theory]
    [InlineData("15")]
    [InlineData("15d")]
    [InlineData("m")]
    [InlineData("0m")]
    public void Generate_UnparseableRateLimitWindow_Fails(string window) =>
        ShouldFail(Operation(extra: $$""", "x-rate-limit": { "group": "g", "max-tokens": 1, "window-size": "{{window}}" }"""), "window-size");

    [Fact]
    public void Generate_PlaceholderWithoutPathParameter_Fails() =>
        ShouldFail(Operation(path: "/things/{thing_id}"), "don't match path parameters");

    [Fact]
    public void Generate_OperationWithTwoTags_Fails() =>
        ShouldFail(Operation(tags: """["A", "B"]"""), "exactly one tag");

    [Fact]
    public void Generate_ConfiguredNamesCollide_Fails()
    {
        var spec = Spec(
            OperationJson("/a", "GetA", "T"),
            OperationJson("/b", "GetB", "T"));

        ShouldFail(spec, "all map to Operation.T.Same", """{ "operationConstants": { "GetA": "Same", "GetB": "Same" } }""");
    }

    [Fact]
    public void Generate_ConfigForUnknownOperation_Fails() =>
        ShouldFail(Operation(), "unknown operation IDs [GetGone]", """{ "operationConstants": { "GetGone": "Gone" } }""");

    [Fact]
    public void Generate_ConfiguredNameNotAnIdentifier_Fails() =>
        ShouldFail(Operation(), "valid identifier", """{ "operationConstants": { "GetThing": "Get Thing" } }""");

    [Fact]
    public void ConfigParse_UnknownSection_Fails()
    {
        var ex = Should.Throw<GenerationException>(() => GeneratorConfig.Parse("""{ "operationConstnts": {} }"""));

        ex.Message.ShouldContain("unknown section");
    }

    private static GenerationResult Generate(string spec, string config) =>
        CodeGenerator.Generate(OpenApiSpec.Parse(spec), GeneratorConfig.Parse(config));

    private static void ShouldFail(string spec, string messagePart, string config = "{}")
    {
        var ex = Should.Throw<GenerationException>(() => Generate(spec, config));
        ex.Message.ShouldContain(messagePart);
    }

    private static string Operation(string path = "/thing", string tags = """["Things"]""", string extra = "") =>
        Spec($$"""
            "{{path}}": { "get": { "operationId": "GetThing", "summary": "Thing", "tags": {{tags}}, "parameters": []{{extra}} } }
            """);

    private static string OperationJson(string path, string operationId, string tag) =>
        $$"""
        "{{path}}": { "get": { "operationId": "{{operationId}}", "tags": ["{{tag}}"] } }
        """;

    private static string Spec(params string[] paths) =>
        $$"""
        {
          "components": { "parameters": { "CompatibilityDate": { "in": "header", "name": "X-Compatibility-Date", "schema": { "enum": ["2026-08-18"] } } } },
          "paths": { {{string.Join(",", paths)}} }
        }
        """;
}
