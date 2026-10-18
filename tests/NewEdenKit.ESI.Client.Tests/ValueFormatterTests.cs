using System.Globalization;
using NewEdenKit.ESI.Client.Internal;

namespace NewEdenKit.ESI.Client.Tests;

public sealed class ValueFormatterTests
{
    private enum OrderType
    {
        Unknown,
        Buy,
    }

    [Fact]
    public void Format_IdAboveInt32Max_KeepsFullValue() =>
        EsiValueFormatter.Format(1_000_000_000_000L).ShouldBe("1000000000000");

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Format_Bool_IsLowercase(bool value, string expected) => EsiValueFormatter.Format(value).ShouldBe(expected);

    [Fact]
    public void Format_Double_IgnoresCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("da-DK");
        try
        {
            EsiValueFormatter.Format(-5.5).ShouldBe("-5.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Format_Guid_IsHyphenatedLowercase() =>
        EsiValueFormatter.Format(Guid.Parse("0E8F7A4C-1B2D-4C3E-9F00-AABBCCDDEEFF")).ShouldBe("0e8f7a4c-1b2d-4c3e-9f00-aabbccddeeff");

    [Fact]
    public void Format_Dates_AreIso()
    {
        EsiValueFormatter.Format(new DateOnly(2026, 8, 18)).ShouldBe("2026-08-18");
        EsiValueFormatter.Format(new DateTimeOffset(2026, 8, 18, 14, 30, 0, TimeSpan.FromHours(2))).ShouldBe("2026-08-18T12:30:00Z");
    }

    [Fact]
    public void Format_Enum_Throws() => Should.Throw<ArgumentException>(() => EsiValueFormatter.Format(OrderType.Buy));

    [Fact]
    public void FormatMany_List_GivesOneStringPerItem() =>
        EsiValueFormatter.FormatMany(new long[] { 1, 3_000_000_000 }).ShouldBe(["1", "3000000000"]);

    [Fact]
    public void FormatMany_String_IsNotSplitIntoChars() => EsiValueFormatter.FormatMany("abc").ShouldBe(["abc"]);
}
