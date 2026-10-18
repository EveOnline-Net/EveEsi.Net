using EveEsi.Net.Generator.Config;
using EveEsi.Net.Generator.Emit;
using EveEsi.Net.Generator.Spec;

namespace EveEsi.Net.Generator.Tests;

public sealed class ServicesEmitterTests
{
    [Fact]
    public void Generate_FixtureSpec_MatchesSnapshots()
    {
        var result = Generate(Snapshot.Fixture("services.openapi.json"));

        Snapshot.Match("Services", result.Files, ServicesEmitter.Directory);
    }

    [Fact]
    public void Generate_MappedOperationConstant_NamesTheMethodToo()
    {
        var result = Generate(Snapshot.Fixture("services.openapi.json"), """{ "operationConstants": { "GetKillmailsKillmailIdKillmailHash": "Killmail" } }""");

        var service = Content(result, "KillmailsService.g.cs");
        service.ShouldContain("public IEsiRequest<GetKillmailsKillmailIdKillmailHashResponse> Killmail(long killmailId, string killmailHash)");
        service.ShouldContain("Operation.Killmails.Killmail,");
    }

    [Theory]
    [InlineData("""{ "in": "query", "name": "page", "required": true, "schema": { "type": "integer" } }""", "pagination parameters must be optional")]
    [InlineData("""{ "in": "query", "name": "before", "schema": { "type": "string" } }""", "neither page nor before/after/limit")]
    [InlineData("""{ "in": "query", "name": "token", "schema": { "type": "string" } }""", "which the generated services already use")]
    [InlineData("""{ "in": "query", "name": "filter", "schema": { "type": "object", "properties": { "a": { "type": "string" } } } }""", "can't be sent as a route or query value")]
    public void Generate_UnsupportedParameters_Fail(string parameters, string message) =>
        ShouldFail(Operation("GetThings", "/things", parameters), message);

    [Fact]
    public void Generate_PathAndQueryParameterWithTheSameName_Fail() =>
        ShouldFail(Operation("GetThings", "/things/{thing_id}",
            """{ "in": "path", "name": "thing_id", "required": true, "schema": { "type": "string" } }, { "in": "query", "name": "thing_id", "schema": { "type": "string" } }"""),
            "several parameters are named 'thingId'");

    [Fact]
    public void Generate_OptionalBody_Fails() =>
        ShouldFail(Operation("PostThings", "/things", "", method: "post",
            extra: """, "requestBody": { "content": { "application/json": { "schema": { "type": "string" } } } }"""),
            "optional request bodies aren't supported");

    [Fact]
    public void Generate_MethodNamedUse_FailsUntilRenamed()
    {
        var spec = Operation("GetThings", "/things", "");

        ShouldFail(spec, "the method name 'Use' is taken", """{ "operationConstants": { "GetThings": "Use" } }""");
        Should.NotThrow(() => Generate(spec, """{ "operationConstants": { "GetThings": "Things" } }"""));
    }

    [Fact]
    public void Generate_ModelNamedLikeALibraryType_Fails() =>
        ShouldFail(Operation("GetThings", "/things", "", response: """{ "type": "object", "properties": { "a": { "type": "string" } } }"""),
            "same name as a library type", """{ "schemas": { "op:GetThings.response": "Operation" } }""");

    private static GenerationResult Generate(string spec, string config = "{}") =>
        CodeGenerator.Generate(OpenApiSpec.Parse(spec), GeneratorConfig.Parse(config));

    private static string Content(GenerationResult result, string file) =>
        result.Files.Single(f => f.RelativePath == $"{ServicesEmitter.Directory}/{file}").Content;

    private static void ShouldFail(string spec, string messagePart, string config = "{}") =>
        Should.Throw<GenerationException>(() => Generate(spec, config)).Message.ShouldContain(messagePart);

    private static string Operation(string id, string path, string parameters, string method = "get", string extra = "", string? response = null) =>
        $$"""
        {
          "components": { "parameters": { "CompatibilityDate": { "in": "header", "name": "X-Compatibility-Date", "schema": { "enum": ["2026-08-18"] } } } },
          "paths": { "{{path}}": { "{{method}}": { "operationId": "{{id}}", "tags": ["Things"], "parameters": [{{parameters}}]{{extra}},
            "responses": { "200": { "content": { "application/json": { "schema": {{response ?? """{ "type": "string" }"""}} } } } } } } }
        }
        """;
}
