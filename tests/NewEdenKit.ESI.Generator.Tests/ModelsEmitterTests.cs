using NewEdenKit.ESI.Generator.Config;
using NewEdenKit.ESI.Generator.Emit;
using NewEdenKit.ESI.Generator.Models;
using NewEdenKit.ESI.Generator.Spec;

namespace NewEdenKit.ESI.Generator.Tests;

public sealed class ModelsEmitterTests
{
    [Fact]
    public void Generate_FixtureSpec_MatchesSnapshots()
    {
        var result = Generate(Snapshot.Fixture("models.openapi.json"));

        Snapshot.Match("Models", result.Files, ModelsEmitter.Directory);
    }

    [Fact]
    public void Build_EnumsWithTheSameValueSet_AreShared()
    {
        var models = Build(Snapshot.Fixture("models.openapi.json"));

        var gender = models.Enums.Single(e => e.Paths.Contains("CharactersDetail.gender"));
        gender.Paths.ShouldContain("CharactersSkinrGet[].other_gender");
        gender.Name.ShouldBe("CharactersDetailGender");
    }

    [Fact]
    public void Build_TypesFollowFormatsAndRequired()
    {
        var detail = (RecordModel)Build(Snapshot.Fixture("models.openapi.json")).TopLevel.Single(t => t.Name == "CharactersDetail");

        string Type(string wire) => detail.Properties.Single(p => p.Wire == wire).Type.Syntax(detail);
        Type("corporation_id").ShouldBe("long");
        Type("alliance_id").ShouldBe("long");
        Type("birthday").ShouldBe("DateTimeOffset");
        Type("title_id").ShouldBe("Guid");
        Type("security_status").ShouldBe("double");
        Type("position").ShouldBe("PositionModel");
        Type("skills").ShouldBe("IReadOnlyList<SkillsItem>");
        Type("labels").ShouldBe("IReadOnlyDictionary<string, string>");
        Type("extra").ShouldBe("JsonElement");
        detail.Properties.Single(p => p.Wire == "verified").Nullable.ShouldBeTrue();
        detail.Properties.Single(p => p.Wire == "name").Nullable.ShouldBeFalse();
    }

    [Fact]
    public void Build_UnionVariants_KeepNullabilityAndFoldEsiUnknown()
    {
        var item = Build(Snapshot.Fixture("models.openapi.json")).TopLevel.Single(t => t.Name == "CharactersSkinrGetItem");
        var price = (UnionModel)item.Nested.Single(n => n.Name == "PriceModel");

        price.Variants.Select(v => (v.Wire, v.Nullable)).ShouldBe([("isk", false), ("plex", false), ("free", true)]);
    }

    [Fact]
    public void Build_ResponsesBodiesAndParameterEnums_AreCollected()
    {
        var models = Build(Snapshot.Fixture("models.openapi.json"));

        models.ResponseTypes["GetCharactersDetail"].Syntax(null).ShouldBe("CharactersDetail");
        models.ResponseTypes["GetCharactersSkinr"].Syntax(null).ShouldBe("IReadOnlyList<CharactersSkinrGetItem>");
        models.ResponseTypes.ContainsKey("PostUiRename").ShouldBeFalse();
        models.BodyTypes["PostUiRename"].Syntax(null).ShouldBe("PostUiRenameBody");
        models.ParameterEnums["GetCharactersSkinr.order_type"].Name.ShouldBe("GetCharactersSkinrOrderType");
    }

    [Fact]
    public void Build_TypesUsedByOneTag_GoInThatTagsNamespace()
    {
        var models = Build(Snapshot.Fixture("models.openapi.json"));

        string? Namespace(string name) => models.TopLevel.Concat(models.Enums).Single(t => t.Name == name).TagNamespace;
        Namespace("CharactersDetail").ShouldBe("Character");
        Namespace("CharactersSkinrGetItem").ShouldBe("Cosmetics");
        Namespace("PostUiRenameBody").ShouldBe("UserInterface");
        Namespace("GetCharactersSkinrOrderType").ShouldBe("Cosmetics");
        Namespace("CharactersDetailGender").ShouldBeNull("used by Character and Cosmetics, so shared");
    }

    [Fact]
    public void Generate_TypeNamedLikeATagNamespace_FailsUntilRenamed()
    {
        var spec = """
            {
              "components": {
                "parameters": { "CompatibilityDate": { "in": "header", "name": "X-Compatibility-Date", "schema": { "enum": ["2026-08-18"] } } },
                "schemas": { "Status": { "type": "object", "properties": { "players": { "type": "integer" } } } }
              },
              "paths": { "/status": { "get": { "operationId": "GetStatus", "tags": ["Status"],
                "responses": { "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Status" } } } } } } } }
            }
            """;

        ShouldFail(spec, "same name as a tag namespace");
        Should.NotThrow(() => Generate(spec, """{ "schemas": { "Status": "ServerStatus" } }"""));
    }

    [Fact]
    public void Generate_EnumValueThatIsNotAnIdentifier_FailsUntilMapped()
    {
        var spec = Spec(Schema("Thing", """{ "type": "object", "properties": { "color": { "enum": ["#0000fe"], "type": "string" } } }"""));

        ShouldFail(spec, "Map it in generator.json enums.ThingColor.members");
        Should.NotThrow(() => Generate(spec, """{ "enums": { "Color": { "match": ["Thing.color"], "members": { "#0000fe": "Blue" } } } }"""));
    }

    [Fact]
    public void Generate_EnumValuesMappingToTheSameMember_Fail() =>
        ShouldFail(Spec(Schema("Thing", """{ "type": "object", "properties": { "kind": { "enum": ["a_b", "a-b"], "type": "string" } } }""")),
            "several values map to the member 'AB'");

    [Fact]
    public void Generate_PropertyNamedLikeItsType_FailsUntilRenamed()
    {
        var spec = Spec(Schema("Route", """{ "type": "object", "properties": { "route": { "type": "string" } } }"""));

        ShouldFail(spec, "has the same name as its type 'Route'");
        Should.NotThrow(() => Generate(spec, """{ "schemas": { "Route": "RouteResponse" } }"""));
    }

    [Theory]
    [InlineData("""{ "allOf": [] }""", "'allOf' isn't supported")]
    [InlineData("""{ "type": "integer", "format": "int16" }""", "unknown integer format")]
    [InlineData("""{ "type": "string", "format": "email" }""", "unknown string format")]
    [InlineData("""{ "type": "object", "oneOf": [{ "type": "object", "properties": { "a": {}, "b": {} } }] }""", "single-property object")]
    [InlineData("""{ "enum": [1, 2], "type": "integer" }""", "only string enums")]
    public void Generate_UnsupportedSchema_Fails(string property, string message) =>
        ShouldFail(Spec(Schema("Thing", $$"""{ "type": "object", "properties": { "p": {{property}} } }""")), message);

    [Fact]
    public void Generate_ConfigForMissingSchemaPath_Fails() =>
        ShouldFail(Spec(Schema("Thing", """{ "type": "object" }""")), "no record or union at [Thing.gone]", """{ "schemas": { "Thing.gone": "Gone" } }""");

    [Fact]
    public void Generate_EnumConfigForMissingPath_Fails() =>
        ShouldFail(Spec(Schema("Thing", """{ "type": "object" }""")), "no enum at 'Thing.kind'", """{ "enums": { "Kind": { "match": ["Thing.kind"] } } }""");

    [Fact]
    public void ConfigParse_UnsupportedSection_Fails() =>
        Should.Throw<GenerationException>(() => GeneratorConfig.Parse("""{ "overrides": { "X": "Y" } }""")).Message.ShouldContain("aren't supported yet");

    private static GenerationResult Generate(string spec, string config = "{}") =>
        CodeGenerator.Generate(OpenApiSpec.Parse(spec), GeneratorConfig.Parse(config));

    private static ModelSet Build(string spec) => new ModelBuilder(OpenApiSpec.Parse(spec), GeneratorConfig.Empty).Build();

    private static void ShouldFail(string spec, string messagePart, string config = "{}") =>
        Should.Throw<GenerationException>(() => Generate(spec, config)).Message.ShouldContain(messagePart);

    private static string Schema(string name, string schema) => $"\"{name}\": {schema}";

    private static string Spec(params string[] schemas) =>
        $$"""
        {
          "components": {
            "parameters": { "CompatibilityDate": { "in": "header", "name": "X-Compatibility-Date", "schema": { "enum": ["2026-08-18"] } } },
            "schemas": { {{string.Join(",", schemas)}} }
          },
          "paths": {}
        }
        """;
}
