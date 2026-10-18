using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace NewEdenKit.ESI.Routes.Tests;

/// <summary>Sanity checks over the committed generated output, so a bad regeneration fails the build.</summary>
public sealed partial class GeneratedRoutesTests
{
    [Fact]
    public void EsiRoutes_KeysMatchOperationIds()
    {
        foreach (var (key, route) in EsiRoutes.All)
        {
            route.OperationId.ShouldBe(key);
        }
    }

    [Fact]
    public void EsiRoutes_ContainsExactlyTheOperationConstants()
    {
        var constants = ConstantValues(typeof(Operation));

        constants.Count.ShouldBe(constants.Distinct().Count(), "Operation IDs must be unique across tags.");
        EsiRoutes.All.Keys.Order(StringComparer.Ordinal).ShouldBe(constants.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EsiRoutes_PlaceholdersAreKnownRouteParameters()
    {
        var routeParameters = ConstantValues(typeof(Parameter.Route)).ToHashSet(StringComparer.Ordinal);

        foreach (var route in EsiRoutes.All.Values)
        {
            foreach (Match match in Placeholder().Matches(route.PathTemplate))
            {
                routeParameters.ShouldContain(match.Groups[1].Value, route.ToString());
            }
        }
    }

    [Fact]
    public void EsiSpec_CompatibilityDate_IsIsoDate()
    {
        DateOnly.TryParseExact(EsiSpec.CompatibilityDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            .ShouldBeTrue();
    }

    private static List<string> ConstantValues(Type type) =>
        [.. type.GetNestedTypes().Append(type)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)];

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex Placeholder();
}
