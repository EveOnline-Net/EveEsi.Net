using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NewEdenKit.ESI.Models.Character;
using NewEdenKit.ESI.Models.Contracts;
using NewEdenKit.ESI.Models.CorporationProjects;
using NewEdenKit.ESI.Models.Killmails;
using NewEdenKit.ESI.Models.Mail;
using NewEdenKit.ESI.Models.Market;
using NewEdenKit.ESI.Models.ParagonHub;
using NewEdenKit.ESI.Models.Wallet;

namespace NewEdenKit.ESI.Models.Tests;

/// <summary>Real-shaped ESI payloads through the generated models and the source-generated EsiJsonContext.</summary>
public sealed class GeneratedModelTests
{
    private static readonly EsiJsonContext Context = EsiJsonContext.Default;

    [Fact]
    public void Character_ReadsRequiredOptionalAndIdsAboveInt32()
    {
        var character = Read("character.json", Context.CharactersDetail);

        character.Name.ShouldBe("CCP Bartender");
        character.Gender.ShouldBe(CharactersDetailGender.Male);
        character.AllianceId.ShouldBe(1354830081);
        character.Birthday.ShouldBe(new DateTimeOffset(2015, 3, 24, 11, 37, 0, TimeSpan.Zero));
        character.FactionId.ShouldBeNull();
    }

    [Fact]
    public void Character_MissingRequiredProperty_Throws()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize("""{ "name": "x" }""", Context.CharactersDetail));
    }

    [Fact]
    public void WalletJournal_KnownAndUnrecognisedEnumValues()
    {
        var journal = Read("wallet-journal.json", Context.ListOfCharactersCharacterIdWalletJournalGetItem);

        journal[0].RefType.ShouldBe(CharactersCharacterIdWalletJournalGetItemRefType.MarketEscrow);
        journal[0].Id.ShouldBe(21_000_000_000);
        journal[0].Balance.ShouldBe(500_000_000_000.0);
        journal[1].RefType.ShouldBe(CharactersCharacterIdWalletJournalGetItemRefType.Unknown);
        journal[1].Amount.ShouldBeNull();
    }

    [Fact]
    public void Enum_WritesItsWireValue()
    {
        JsonSerializer.Serialize(CharactersCharacterIdWalletJournalGetItemRefType.MarketEscrow, Context.CharactersCharacterIdWalletJournalGetItemRefType)
            .ShouldBe("\"market_escrow\"");
    }

    [Fact]
    public void Enum_UnknownWithoutWireValue_CannotBeWritten()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            CharactersCharacterIdWalletJournalGetItemRefTypeConverter.ToWire(CharactersCharacterIdWalletJournalGetItemRefType.Unknown));
    }

    [Fact]
    public void Enum_EsiUnknownValue_ReadsAndWritesAsUnknown()
    {
        CharactersCharacterIdContractsGetItemTypeConverter.FromWire("unknown").ShouldBe(CharactersCharacterIdContractsGetItemType.Unknown);
        CharactersCharacterIdContractsGetItemTypeConverter.ToWire(CharactersCharacterIdContractsGetItemType.Unknown).ShouldBe("unknown");
    }

    [Theory]
    [InlineData("#0000fe", MailLabelColor.Color0000FE)]
    [InlineData("#ffffff", MailLabelColor.ColorFFFFFF)]
    [InlineData("#123456", MailLabelColor.Unknown)]
    public void Enum_HexValues_UseMappedMembers(string wire, MailLabelColor expected)
    {
        MailLabelColorConverter.FromWire(wire).ShouldBe(expected);
    }

    [Fact]
    public void MarketOrders_DigitAndWordRanges_ShareOneEnum()
    {
        var orders = Read("market-orders.json", Context.ListOfCharactersCharacterIdOrdersGetItem);

        orders[0].Range.ShouldBe(OrderRange.Jumps10);
        orders[1].Range.ShouldBe(OrderRange.SolarSystem);
        orders[1].LocationId.ShouldBe(1_035_466_617_946);
        OrderRangeConverter.ToWire(OrderRange.Jumps10).ShouldBe("10");
    }

    [Fact]
    public void SkinrListings_UnionVariantsIncludingUnknown()
    {
        var listings = Read("skinr-listings.json", Context.CharactersParagonHubSkinr).Listings;

        listings[0].Price.ShouldBe(new CharactersParagonHubSkinrItem.PriceModel.Isk(1500000.5));
        listings[0].Target.ShouldBe(new CharactersParagonHubSkinrItem.TargetModel.CorporationId(98000001));
        listings[1].Price.ShouldBe(new CharactersParagonHubSkinrItem.PriceModel.Plex(500));
        listings[1].Target.ShouldBe(new CharactersParagonHubSkinrItem.TargetModel.Public(true));

        var unknown = listings[2].Price.ShouldBeOfType<CharactersParagonHubSkinrItem.PriceModel.Unknown>();
        unknown.Name.ShouldBe("loyalty_points");
        unknown.Value.GetProperty("amount").GetInt32().ShouldBe(10);
    }

    [Fact]
    public void Union_RoundTrips_IncludingUnknownVariant()
    {
        var listings = Read("skinr-listings.json", Context.CharactersParagonHubSkinr);

        var json = JsonSerializer.Serialize(listings, Context.CharactersParagonHubSkinr);
        var again = JsonSerializer.Deserialize(json, Context.CharactersParagonHubSkinr)!;

        again.Listings[0].Price.ShouldBe(listings.Listings[0].Price);
        json.ShouldContain("\"price\":{\"loyalty_points\":{\"corporation_id\":1000035,\"amount\":10}}");
    }

    [Theory]
    [InlineData("""{ "isk": 1, "plex": 2 }""")]
    [InlineData("""{ }""")]
    [InlineData("""[ 1 ]""")]
    public void Union_NotASinglePropertyObject_Throws(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, Context.CharactersParagonHubSkinrItemPriceModel));
    }

    [Fact]
    public void Union_EsiOwnUnknownVariant_ReadsAsUnknown()
    {
        var configuration = JsonSerializer.Deserialize("""{ "unknown": { "data": { "anything": 1 } } }""", Context.CorporationsProjectsDetailConfigurationModel);

        configuration.ShouldBeOfType<CorporationsProjectsDetail.ConfigurationModel.Unknown>().Name.ShouldBe("unknown");
    }

    [Fact]
    public void Killmail_NestedTypesAllTheWayDown()
    {
        var killmail = Read("killmail.json", Context.KillmailsKillmailIdKillmailHashGet);

        killmail.Victim.Position!.Z.ShouldBe(3_000_000_000_000.0);
        var container = killmail.Victim.Items!.ShouldHaveSingleItem();
        container.QuantityDestroyed.ShouldBe(1);
        container.Items!.ShouldHaveSingleItem().QuantityDropped.ShouldBe(1000);
        killmail.Attackers.ShouldHaveSingleItem().FinalBlow.ShouldBeTrue();
    }

    private static T Read<T>(string file, JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", file)), typeInfo)
        ?? throw new InvalidOperationException($"{file} deserialized to null.");
}
