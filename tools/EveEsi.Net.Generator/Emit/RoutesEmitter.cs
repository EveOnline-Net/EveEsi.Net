using System.Globalization;
using System.Text.RegularExpressions;
using EveEsi.Net.Generator.Config;
using EveEsi.Net.Generator.Naming;
using EveEsi.Net.Generator.Spec;

namespace EveEsi.Net.Generator.Emit;

/// <summary>
/// Emits the spec-level catalogues into <c>EveEsi.Net.Routes/Generated/</c>: <c>EsiSpec</c> (compatibility date),
/// <c>Parameter</c> (wire names), <c>Operation</c> (operation IDs by tag) and <c>EsiRoutes</c> (the route table).
/// </summary>
internal static partial class RoutesEmitter
{
    public const string Directory = "EveEsi.Net.Routes/Generated";
    private const string Namespace = "EveEsi.Net.Routes";

    /// <summary>Handled by <c>ApplyPagination</c>, so they never appear in the Parameter catalogue.</summary>
    public static readonly IReadOnlySet<string> PaginationParameters = new HashSet<string>(StringComparer.Ordinal) { "page", "before", "after", "limit" };

    public static IReadOnlyList<GeneratedFile> Emit(OpenApiSpec spec, IReadOnlyList<OperationName> operations, string header)
    {
        var routeParameters = ParameterNames(spec, "path");
        var queryParameters = ParameterNames(spec, "query");

        return
        [
            new($"{Directory}/EsiSpec.g.cs", EmitSpec(spec, header)),
            new($"{Directory}/Parameter.g.cs", EmitParameters(routeParameters, queryParameters, header)),
            new($"{Directory}/Operation.g.cs", EmitOperations(operations, header)),
            .. EmitRoutes(operations, routeParameters, header),
        ];
    }

    private static string EmitSpec(OpenApiSpec spec, string header) =>
        Start(header)
            .Summary("Facts about the ESI spec this library was generated from.")
            .Open("public static partial class EsiSpec")
            .Summary("The ESI compatibility date, sent as X-Compatibility-Date on every call. Not configurable.")
            .Line($"public const string CompatibilityDate = {CodeWriter.Literal(spec.CompatibilityDate)};")
            .Close()
            .ToString();

    private static string EmitParameters(
        IReadOnlyDictionary<string, string> routeParameters,
        IReadOnlyDictionary<string, string> queryParameters,
        string header)
    {
        var writer = Start(header)
            .Summary("Wire names of ESI route and query parameters, for p.Route.Set / p.Query.Set and path templates.")
            .Open("public static partial class Parameter");

        WriteParameterClass(writer, "Route", "Path parameter names.", "path", routeParameters);
        writer.Line();
        WriteParameterClass(writer, "Query", "Query parameter names. Pagination parameters are set through ApplyPagination.", "query", queryParameters);
        return writer.Close().ToString();
    }

    private static void WriteParameterClass(
        CodeWriter writer, string className, string summary, string location, IReadOnlyDictionary<string, string> names)
    {
        writer.Summary(summary).Open($"public static partial class {className}");
        var first = true;
        foreach (var (wireName, identifier) in names.OrderBy(n => n.Value, StringComparer.Ordinal))
        {
            if (!first)
            {
                writer.Line();
            }

            first = false;
            writer.Summary($"The {wireName} {location} parameter.")
                .Line($"public const string {identifier} = {CodeWriter.Literal(wireName)};");
        }

        writer.Close();
    }

    private static string EmitOperations(IReadOnlyList<OperationName> operations, string header)
    {
        var writer = Start(header)
            .Summary("ESI operation IDs, grouped by spec tag. Pass them to CreateEsiClient; they key EsiRoutes.All.")
            .Open("public static partial class Operation");

        var firstTag = true;
        foreach (var tag in operations.GroupBy(o => o.TagClass).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (!firstTag)
            {
                writer.Line();
            }

            firstTag = false;
            writer.Summary($"Operations tagged '{tag.First().Operation.Tag}'.").Open($"public static partial class {tag.Key}");
            var first = true;
            foreach (var name in tag.OrderBy(n => n.Constant, StringComparer.Ordinal))
            {
                if (!first)
                {
                    writer.Line();
                }

                first = false;
                var op = name.Operation;
                writer.Summary($"{op.Summary ?? op.OperationId} ({op.Method.ToUpperInvariant()} {op.Path}).")
                    .Line($"public const string {name.Constant} = {CodeWriter.Literal(op.OperationId)};");
            }

            writer.Close();
        }

        return writer.Close().ToString();
    }

    /// <summary>
    /// The route table, split by tag: one small aggregator file plus one <c>EsiRoutes.&lt;Tag&gt;.g.cs</c> per tag. The
    /// aggregator calls each tag's registration explicitly; no reflection, so it stays trim and AOT safe.
    /// </summary>
    private static IEnumerable<GeneratedFile> EmitRoutes(
        IReadOnlyList<OperationName> operations, IReadOnlyDictionary<string, string> routeParameters, string header)
    {
        var tags = operations.GroupBy(o => o.TagClass).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();

        var aggregator = Start(header, "System.Collections.Frozen", "EveEsi.Net.Abstractions.Routing")
            .Summary("Every ESI endpoint, keyed by operation ID (see Operation). Each tag registers its routes in its own file.")
            .Open("public static partial class EsiRoutes")
            .Summary("All routes, keyed by operation ID.")
            .Line("public static IReadOnlyDictionary<string, RouteDefinition> All { get; } = Build();")
            .Line()
            .Open("private static FrozenDictionary<string, RouteDefinition> Build()")
            .Line("var routes = new Dictionary<string, RouteDefinition>(StringComparer.Ordinal);");
        foreach (var tag in tags)
        {
            aggregator.Line($"Register{tag.Key}(routes);");
        }

        aggregator.Line("return routes.ToFrozenDictionary(StringComparer.Ordinal);").Close();
        yield return new GeneratedFile($"{Directory}/EsiRoutes.g.cs", aggregator.Close().ToString());

        foreach (var tag in tags)
        {
            var writer = Start(header, "EveEsi.Net.Abstractions.Routing")
                .Open("public static partial class EsiRoutes")
                .Summary($"Routes tagged '{tag.First().Operation.Tag}'.")
                .Open($"private static void Register{tag.Key}(Dictionary<string, RouteDefinition> routes)");

            var first = true;
            foreach (var name in tag.OrderBy(n => n.Operation.OperationId, StringComparer.Ordinal))
            {
                if (!first)
                {
                    writer.Line();
                }

                first = false;
                WriteRoute(writer, name, routeParameters);
            }

            yield return new GeneratedFile($"{Directory}/EsiRoutes.{tag.Key}.g.cs", writer.Close().Close().ToString());
        }
    }

    private static void WriteRoute(CodeWriter writer, OperationName name, IReadOnlyDictionary<string, string> routeParameters)
    {
        var op = name.Operation;
        var key = $"Operation.{name.TagClass}.{name.Constant}";
        var arguments = new List<string>
        {
            key,
            $"HttpMethod.{char.ToUpperInvariant(op.Method[0])}{op.Method[1..]}",
            PathTemplate(op, routeParameters),
        };
        if (op.Scope is not null)
        {
            arguments.Add($"scope: {CodeWriter.Literal(op.Scope)}");
        }

        if (op.Cache is { } cache)
        {
            arguments.Add($"cache: new RouteCacheHints({Seconds(cache.MaxAgeSeconds)}, RouteCacheMode.{cache.Mode}"
                + (cache.TombstoneSeconds is null ? ")" : $", {Seconds(cache.TombstoneSeconds)})"));
        }

        if (op.RateLimit is { } rateLimit)
        {
            arguments.Add($"rateLimit: new RateLimitPolicy({CodeWriter.Literal(rateLimit.Group)}, "
                + $"{rateLimit.MaxTokens.ToString(CultureInfo.InvariantCulture)}, {Window(rateLimit.Window)})");
        }

        writer.Line($"routes.Add({key}, new(");
        for (var i = 0; i < arguments.Count; i++)
        {
            writer.Line($"    {arguments[i]}{(i < arguments.Count - 1 ? "," : "));")}");
        }
    }

    /// <summary>The path with each placeholder written as its Parameter.Route constant.</summary>
    private static string PathTemplate(SpecOperation op, IReadOnlyDictionary<string, string> routeParameters)
    {
        var declared = op.Parameters.Where(p => p.Location == "path").Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var placeholders = Placeholders(op.Path);
        if (!declared.SetEquals(placeholders))
        {
            throw new GenerationException(
                $"{op.OperationId}: path placeholders [{string.Join(", ", placeholders)}] don't match path parameters [{string.Join(", ", declared)}].");
        }

        if (placeholders.Count == 0)
        {
            return CodeWriter.Literal(op.Path);
        }

        var interpolated = Placeholder().Replace(op.Path, m => $"{{{{{{Parameter.Route.{routeParameters[m.Groups[1].Value]}}}}}}}");
        return "$" + CodeWriter.Literal(interpolated);
    }

    private static string Seconds(long? seconds) =>
        seconds is { } s ? $"TimeSpan.FromSeconds({s.ToString(CultureInfo.InvariantCulture)})" : "null";

    private static string Window(SpecWindow window) =>
        $"TimeSpan.From{window.Unit}({window.Amount.ToString(CultureInfo.InvariantCulture)})";

    /// <summary>Wire name → <c>Parameter.Route</c> / <c>Parameter.Query</c> constant name, for every non-pagination parameter in one location.</summary>
    public static Dictionary<string, string> ParameterNames(OpenApiSpec spec, string location)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in spec.Operations.SelectMany(o => o.Parameters).Where(p => p.Location == location))
        {
            if (location == "query" && PaginationParameters.Contains(parameter.Name))
            {
                continue;
            }

            names.TryAdd(parameter.Name, Names.Pascal(parameter.Name));
        }

        var clash = names.GroupBy(n => n.Value, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw new GenerationException(
                $"{location} parameters [{string.Join(", ", clash.Select(c => c.Key))}] all map to the constant '{clash.Key}'.");
        }

        return names;
    }

    /// <summary>
    /// Every operation's tag class and constant name (<c>Operation.&lt;TagClass&gt;.&lt;Constant&gt;</c>), from
    /// <c>generator.json</c> operationConstants or the operation ID. The services use the same names for their methods.
    /// </summary>
    public static List<OperationName> OperationNames(OpenApiSpec spec, GeneratorConfig config, ICollection<string> notes)
    {
        var known = spec.Operations.Select(o => o.OperationId).ToHashSet(StringComparer.Ordinal);
        var stale = config.OperationConstants.Keys.Where(k => !known.Contains(k)).Order(StringComparer.Ordinal).ToArray();
        if (stale.Length > 0)
        {
            throw new GenerationException($"generator.json operationConstants: unknown operation IDs [{string.Join(", ", stale)}].");
        }

        var tagClasses = spec.Operations.Select(o => o.Tag).Distinct(StringComparer.Ordinal)
            .ToDictionary(t => t, Names.Pascal, StringComparer.Ordinal);
        var tagClash = tagClasses.GroupBy(t => t.Value, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (tagClash is not null)
        {
            throw new GenerationException($"Tags [{string.Join(", ", tagClash.Select(t => t.Key))}] all map to the class '{tagClash.Key}'.");
        }

        var names = new List<OperationName>();
        var defaulted = 0;
        foreach (var op in spec.Operations)
        {
            string constant;
            if (config.OperationConstants.TryGetValue(op.OperationId, out var mapped))
            {
                constant = Names.Identifier(mapped, $"operationConstants.{op.OperationId}");
            }
            else
            {
                constant = Names.Identifier(op.OperationId, op.OperationId);
                defaulted++;
            }

            names.Add(new OperationName(op, tagClasses[op.Tag], constant));
        }

        var clash = names.GroupBy(n => (n.TagClass, n.Constant)).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw new GenerationException(
                $"Operations [{string.Join(", ", clash.Select(c => c.Operation.OperationId))}] all map to Operation.{clash.Key.TagClass}.{clash.Key.Constant}.");
        }

        if (defaulted > 0)
        {
            notes.Add($"{defaulted} of {names.Count} operation constants use their operation ID as the name (map them in generator.json operationConstants).");
        }

        return names;
    }

    private static CodeWriter Start(string header, params string[] usings)
    {
        var writer = new CodeWriter().Line(header.TrimEnd('\n')).Line();
        foreach (var u in usings)
        {
            writer.Line($"using {u};");
        }

        if (usings.Length > 0)
        {
            writer.Line();
        }

        return writer.Line($"namespace {Namespace};").Line();
    }

    /// <summary>The wire names of a path's placeholders, in path order.</summary>
    public static List<string> Placeholders(string path) => [.. Placeholder().Matches(path).Select(m => m.Groups[1].Value)];

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex Placeholder();
}

/// <summary>An operation with its <c>Operation.&lt;TagClass&gt;.&lt;Constant&gt;</c> name.</summary>
internal sealed record OperationName(SpecOperation Operation, string TagClass, string Constant);
