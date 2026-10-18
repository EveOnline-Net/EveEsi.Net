using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using EveEsi.Net.Models.CorporationProjects;
using EveEsi.Net.Models.Cosmetics;
using EveEsi.Net.Models.FreelanceJobs;
using EveEsi.Net.Models.Sovereignty;
using EveEsi.Net.Models.Structures;

namespace EveEsi.Net.Models.Tests;

/// <summary>
/// The generated oneOf unions, one test per shape ESI uses: primitive, ID, object and nullable variant values;
/// unions as properties, array items and map values; and the Unknown fallback for every union in the library.
/// </summary>
public sealed class PolymorphicModelTests
{
    private static readonly EsiJsonContext Context = EsiJsonContext.Default;

    [Fact]
    public void ObjectAndPrimitiveVariants_InOneUnion()
    {
        var systems = ReadEach("sovereignty-claims.json", Context.SovereigntySystemsSolarsystem);

        var faction = systems[0].Claim.ShouldBeOfType<SovereigntySystemsSolarsystem.ClaimModel.Faction>();
        faction.Value.FactionId.ShouldBe(500001);
        systems[1].Claim.ShouldBe(new SovereigntySystemsSolarsystem.ClaimModel.Unclaimed(true));
    }

    [Fact]
    public void PrimitiveVariants_IntegerAndBoolean()
    {
        Read("""{ "remaining": 3000000000 }""", Context.CharactersCosmeticsSkinrComponentsItemRunsModel)
            .ShouldBe(new CharactersCosmeticsSkinrComponentsItem.RunsModel.Remaining(3_000_000_000));
        Read("""{ "unlimited": true }""", Context.CharactersCosmeticsSkinrComponentsItemRunsModel)
            .ShouldBe(new CharactersCosmeticsSkinrComponentsItem.RunsModel.Unlimited(true));
    }

    [Fact]
    public void NullableVariantValue_ReadsNullAndValue()
    {
        var hubs = ReadEach("sovereignty-hub-transport.json", Context.CorporationsStructuresSovereigntyHubsDetailTransport);

        hubs[0].State.ShouldBe(new CorporationsStructuresSovereigntyHubsDetailTransport.StateModel.Transit(null));
        hubs[1].State.ShouldBe(new CorporationsStructuresSovereigntyHubsDetailTransport.StateModel.Transit(true));
        var export = hubs[0].Configuration.ShouldBeOfType<CorporationsStructuresSovereigntyHubsDetailTransport.ConfigurationModel.Export>();
        export.Value.Amount.ShouldBe(1000);
        hubs[1].Configuration.ShouldBeOfType<CorporationsStructuresSovereigntyHubsDetailTransport.ConfigurationModel.Import>()
            .Value.Sources.ShouldBeEmpty();
    }

    [Fact]
    public void NullableVariantValue_WritesNull()
    {
        var json = JsonSerializer.Serialize<CorporationsStructuresSovereigntyHubsDetailTransport.StateModel>(
            new CorporationsStructuresSovereigntyHubsDetailTransport.StateModel.Transit(null),
            Context.CorporationsStructuresSovereigntyHubsDetailTransportStateModel);

        json.ShouldBe("""{"transit":null}""");
    }

    [Fact]
    public void UnionAsArrayItem_ReadsEachVariantAndUnknown()
    {
        var locations = Read(File("project-locations.json"), Context.CorporationsProjectsDetailConfigurationcapturefwcomplex).Locations!;

        locations[0].ShouldBe(new CorporationsProjectsDetailConfigurationcapturefwcomplex.LocationsItem.SolarSystemId(30000142));
        locations[1].ShouldBe(new CorporationsProjectsDetailConfigurationcapturefwcomplex.LocationsItem.ConstellationId(20000020));
        locations[2].ShouldBe(new CorporationsProjectsDetailConfigurationcapturefwcomplex.LocationsItem.RegionId(10000002));
        locations[3].ShouldBeOfType<CorporationsProjectsDetailConfigurationcapturefwcomplex.LocationsItem.Unknown>().Name.ShouldBe("wormhole_id");
    }

    [Fact]
    public void UnionAsMapValue_ReadsEachVariantAndUnknown()
    {
        var parameters = Read(File("freelance-job-configuration.json"), Context.FreelanceJobsDetailConfiguration).Parameters;

        parameters["use_corporation_hangar"].ShouldBeOfType<FreelanceJobsDetailConfiguration.ParametersValue.Boolean>().Value.Value.ShouldBeTrue();
        parameters["eligible"].ShouldBeOfType<FreelanceJobsDetailConfiguration.ParametersValue.Options>().Value.Selected.ShouldBe(["self", "corporation"]);
        parameters["added_later_by_ccp"].ShouldBeOfType<FreelanceJobsDetailConfiguration.ParametersValue.Unknown>().Name.ShouldBe("slider");
    }

    [Fact]
    public void ObjectVariant_AndUnknownObjectVariant()
    {
        var slots = ReadEach("skinr-layout-slots.json", Context.CosmeticsSkinrLayoutslot);

        slots[0].Configuration.ShouldBeOfType<CosmeticsSkinrLayoutslot.ConfigurationModel.Nanocoating>().Value.Id.ShouldBe(67890);
        var decal = slots[1].Configuration.ShouldBeOfType<CosmeticsSkinrLayoutslot.ConfigurationModel.Unknown>();
        decal.Name.ShouldBe("decal");
        decal.Value.GetProperty("scale").GetDouble().ShouldBe(0.5);
    }

    [Theory]
    [InlineData("project-locations.json")]
    [InlineData("freelance-job-configuration.json")]
    public void ContainersOfUnions_RoundTripUnchanged(string file)
    {
        var json = File(file);
        var typeInfo = file.StartsWith("project", StringComparison.Ordinal)
            ? (JsonTypeInfo)Context.CorporationsProjectsDetailConfigurationcapturefwcomplex
            : Context.FreelanceJobsDetailConfiguration;

        var written = JsonSerializer.Serialize(JsonSerializer.Deserialize(json, typeInfo), typeInfo);

        JsonNode.DeepEquals(JsonNode.Parse(written), JsonNode.Parse(json)).ShouldBeTrue(written);
    }

    [Fact]
    public void EveryUnion_ReadsAndWritesAnUnrecognisedVariantUnchanged()
    {
        const string json = """{"variant_from_a_newer_esi":{"a":[1,2],"b":null}}""";
        var unions = UnionTypes();

        unions.Count.ShouldBeGreaterThan(30, "the generated unions weren't found");
        foreach (var union in unions)
        {
            var typeInfo = Context.GetTypeInfo(union).ShouldNotBeNull($"{union} isn't registered in EsiJsonContext");

            var value = JsonSerializer.Deserialize(json, typeInfo)!;

            value.GetType().Name.ShouldBe("Unknown", union.FullName);
            JsonSerializer.Serialize(value, typeInfo).ShouldBe(json, union.FullName);
        }
    }

    [Fact]
    public void EveryUnion_RejectsAnythingButASinglePropertyObject()
    {
        foreach (var union in UnionTypes())
        {
            var typeInfo = Context.GetTypeInfo(union)!;
            foreach (var json in new[] { """{"a":1,"b":2}""", "{}", "[]", "1", "\"text\"" })
            {
                Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, typeInfo), $"{union.FullName}: {json}");
            }
        }
    }

    /// <summary>Every generated union: an abstract record with a nested Unknown variant.</summary>
    private static List<Type> UnionTypes() =>
        [.. typeof(EsiJsonContext).Assembly.GetTypes().Where(t =>
            t.IsAbstract && t.IsNested && t.GetNestedType("Unknown") is { } unknown && unknown.IsSubclassOf(t))];

    private static T Read<T>(string json, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(json, typeInfo) ?? throw new InvalidOperationException("Deserialized to null.");

    private static List<T> ReadEach<T>(string file, JsonTypeInfo<T> typeInfo) =>
        [.. JsonDocument.Parse(File(file)).RootElement.EnumerateArray().Select(e => e.Deserialize(typeInfo)!)];

    private static string File(string name) => System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));
}
