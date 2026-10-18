using NewEdenKit.ESI.Generator.Models;
using NewEdenKit.ESI.Generator.Naming;
using NewEdenKit.ESI.Generator.Spec;

namespace NewEdenKit.ESI.Generator.Emit;

/// <summary>
/// Emits <c>NewEdenKit.ESI/Generated/</c>: per tag, the public <c>I&lt;Tag&gt;</c> service interface and its internal
/// <c>&lt;Tag&gt;Service</c> implementation, plus <c>EsiServices</c>, which registers them all. Each operation becomes
/// one method, named like its <c>Operation</c> constant, that hands the factory the operation ID, the response type and a
/// configurator setting route values, query values, the body and pagination.
/// </summary>
internal static class ServicesEmitter
{
    public const string Directory = "NewEdenKit.ESI/Generated";
    private const string Namespace = "NewEdenKit.ESI.Services";
    private const string ModelsNamespace = "NewEdenKit.ESI.Models";

    // The generated method bodies use these names; a spec parameter with one of them would shadow or clash.
    private static readonly HashSet<string> ReservedParameterNames = new(StringComparer.Ordinal) { "p", "token", "body", "page", "cursor" };

    // Types the service files see through their usings or namespace. A model with one of these names would be ambiguous.
    private static readonly string[] LibraryTypeNames =
    [
        "Operation", "Parameter", "EsiRoutes", "EsiSpec", "EsiJsonContext", "EsiService", "EsiServices",
        "IEsiRequest", "IEsiClientFactory", "IEsiRequestParameters", "IEsiService", "CursorPage", "EsiNoContent",
    ];

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while",
    };

    public static IReadOnlyList<GeneratedFile> Emit(OpenApiSpec spec, IReadOnlyList<OperationName> operations, ModelSet models, string header)
    {
        var routeConstants = RoutesEmitter.ParameterNames(spec, "path");
        var queryConstants = RoutesEmitter.ParameterNames(spec, "query");
        var tags = operations.GroupBy(o => o.TagClass).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        CheckModelNames(models, tags.Select(t => t.Key));

        var files = new List<GeneratedFile>();
        foreach (var tag in tags)
        {
            var tagName = tag.First().Operation.Tag;
            var methods = tag.OrderBy(n => n.Constant, StringComparer.Ordinal)
                .Select(n => BuildMethod(n, models, routeConstants, queryConstants))
                .ToList();

            files.Add(new GeneratedFile($"{Directory}/I{tag.Key}.g.cs", EmitInterface(tag.Key, tagName, methods, header)));
            files.Add(new GeneratedFile($"{Directory}/{tag.Key}Service.g.cs", EmitImplementation(tag.Key, tagName, methods, header)));
        }

        files.Add(new GeneratedFile($"{Directory}/EsiServices.g.cs", EmitRegistration(tags.Select(t => t.Key), header)));
        return files;
    }

    private static ServiceMethod BuildMethod(
        OperationName name,
        ModelSet models,
        Dictionary<string, string> routeConstants,
        Dictionary<string, string> queryConstants)
    {
        var op = name.Operation;
        if (name.Constant == "Use")
        {
            throw new GenerationException($"{op.OperationId}: the method name 'Use' is taken by the middleware API. Rename it in generator.json operationConstants.");
        }

        TypeRef ParameterType(SpecParameter p) => models.ParameterTypes[$"{op.OperationId}.{p.Name}"];

        var parameters = op.Parameters.Where(p => p.Location is "path" or "query").ToList();
        var pathParameters = parameters.Where(p => p.Location == "path").ToDictionary(p => p.Name, StringComparer.Ordinal);
        var query = parameters.Where(p => p.Location == "query" && !RoutesEmitter.PaginationParameters.Contains(p.Name)).ToList();

        var route = RoutesEmitter.Placeholders(op.Path)
            .Select(wire => pathParameters.TryGetValue(wire, out var p)
                ? Value(p, ParameterType(p), v => $"p.Route.Set(Parameter.Route.{routeConstants[wire]}, {v});")
                : throw new GenerationException($"{op.OperationId}: path placeholder '{wire}' has no path parameter."));
        var requiredQuery = query.Where(p => p.Required)
            .Select(p => Value(p, ParameterType(p), v => $"p.Query.Set(Parameter.Query.{queryConstants[p.Name]}, {v});"));
        var optionalQuery = query.Where(p => !p.Required)
            .Select(p => Value(p, ParameterType(p), v => $"p.Query.Set(Parameter.Query.{queryConstants[p.Name]}, {v});"));

        var list = new List<MethodParameter>();
        list.AddRange(route);
        list.AddRange(requiredQuery);
        if (models.BodyTypes.TryGetValue(op.OperationId, out var bodyType))
        {
            if (!op.BodyRequired)
            {
                throw new GenerationException($"{op.OperationId}: optional request bodies aren't supported. Add an override.");
            }

            list.Add(new MethodParameter("body", bodyType.Syntax(null), Optional: false, "The request body.",
                $"p.Body(body, EsiJsonContext.Default.{bodyType.InfoName});", Guard: !bodyType.IsValueType)
            { Uses = bodyType });
        }

        if (op.Scope is not null)
        {
            list.Add(new MethodParameter("token", "string", Optional: false,
                $"An SSO access token with the <c>{CodeWriter.Xml(op.Scope)}</c> scope.", Statement: null, Guard: false));
        }

        list.AddRange(optionalQuery);
        list.AddRange(Pagination(op, parameters));

        var clash = list.GroupBy(p => p.Name.TrimStart('@'), StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw new GenerationException($"{op.OperationId}: several parameters are named '{clash.Key}'.");
        }

        return new ServiceMethod(name, models.ResponseTypes.GetValueOrDefault(op.OperationId), list);
    }

    /// <summary>A route or query parameter: its C# parameter and the statement that sets it.</summary>
    private static MethodParameter Value(SpecParameter parameter, TypeRef type, Func<string, string> statement)
    {
        var name = ParameterName(parameter.Name);
        var optional = !parameter.Required;

        // Enums go on the wire through their generated converter; the client never formats an enum itself.
        var value = type switch
        {
            ModelRef { Type: EnumModel model } when optional => $"{name}.HasValue ? {model.Name}Converter.ToWire({name}.Value) : null",
            ModelRef { Type: EnumModel model } => $"{model.Name}Converter.ToWire({name})",
            ListRef { Item: ModelRef { Type: EnumModel model } } => $"{name}{(optional ? "?" : "")}.Select({model.Name}Converter.ToWire).ToArray()",
            _ => name,
        };

        var doc = CodeWriter.Xml(Sentence(parameter.Description ?? $"The {parameter.Name} {parameter.Location} parameter."));
        if (optional)
        {
            doc += parameter.Default is null
                ? " Not sent when null."
                : $" Not sent when null (ESI then uses <c>{CodeWriter.Xml(parameter.Default)}</c>).";
        }

        return new MethodParameter(name, type.Syntax(null), optional, doc, statement(value), Guard: !optional && !type.IsValueType)
        {
            Uses = type,
        };
    }

    /// <summary><c>page</c> → <c>int? page</c>; <c>before</c> + <c>after</c> + <c>limit</c> → <c>CursorPage? cursor</c>.</summary>
    private static IEnumerable<MethodParameter> Pagination(SpecOperation op, IReadOnlyList<SpecParameter> parameters)
    {
        var paging = parameters.Where(p => p.Location == "query" && RoutesEmitter.PaginationParameters.Contains(p.Name)).ToList();
        if (paging.Any(p => p.Required))
        {
            throw new GenerationException($"{op.OperationId}: pagination parameters must be optional.");
        }

        var names = paging.Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        switch (string.Join(",", names))
        {
            case "":
                yield break;
            case "page":
                yield return new MethodParameter("page", "int", Optional: true,
                    "The page to fetch, from 1. Not sent when null, which gives the first page; the response's X-Pages header says how many there are.",
                    "p.ApplyPagination(page);", Guard: false);
                break;
            case "after,before,limit":
                yield return new MethodParameter("cursor", "CursorPage", Optional: true,
                    "Where to continue (before or after a cursor from a previous response) and the page size. Not sent when null, which gives the first page.",
                    "p.ApplyPagination(cursor);", Guard: false)
                { UsesCursor = true };
                break;
            default:
                throw new GenerationException(
                    $"{op.OperationId}: pagination parameters [{string.Join(", ", names)}] are neither page nor before/after/limit.");
        }
    }

    private static string EmitInterface(string tagClass, string tag, IReadOnlyList<ServiceMethod> methods, string header)
    {
        var writer = Start(header, Usings(methods, implementation: false))
            .Summary($"The ESI endpoints tagged '{tag}'. Each method builds a request; nothing is sent until ExecuteAsync.")
            .Line("/// <remarks>")
            .Line("/// Resolve it as a transient service. Middleware added with <c>Use</c> applies to requests this instance builds")
            .Line("/// afterwards, inside the global middleware and outside the request's own.")
            .Line("/// </remarks>")
            .Open($"public partial interface I{tagClass} : IEsiService<I{tagClass}>");

        var first = true;
        foreach (var method in methods)
        {
            if (!first)
            {
                writer.Line();
            }

            first = false;
            WriteMethodDocs(writer, method);
            writer.Line($"{Signature(method)};");
        }

        return writer.Close().ToString();
    }

    private static void WriteMethodDocs(CodeWriter writer, ServiceMethod method)
    {
        var op = method.Name.Operation;
        writer.Summary(Sentence(op.Summary ?? op.OperationId));

        var remarks = $"<c>{op.Method.ToUpperInvariant()} {CodeWriter.Xml(op.Path)}</c>.";
        if (op.Description is { } description && !string.Equals(description.Trim(), op.Summary?.Trim(), StringComparison.Ordinal))
        {
            remarks += " " + CodeWriter.Xml(Sentence(description));
        }

        if (op.Scope is not null)
        {
            remarks += $" Requires the <c>{CodeWriter.Xml(op.Scope)}</c> scope.";
        }

        writer.Line($"/// <remarks>{remarks}</remarks>");
        foreach (var parameter in method.Parameters)
        {
            // Already escaped where spec text went in, since the docs built here contain markup.
            writer.Line($"/// <param name=\"{parameter.Name.TrimStart('@')}\">{parameter.Doc}</param>");
        }

        var result = method.Response is null ? "no body" : $"<c>{CodeWriter.Xml(method.Response.Syntax(null))}</c>";
        writer.Line($"/// <returns>The request, for {result}. Call <c>ExecuteAsync</c> to send it.</returns>");

        var guarded = method.Parameters.Where(p => p.Guard).Select(p => $"<paramref name=\"{p.Name.TrimStart('@')}\"/>").ToList();
        if (guarded.Count > 0)
        {
            writer.Line($"/// <exception cref=\"ArgumentNullException\">{string.Join(" or ", guarded)} is <see langword=\"null\"/>.</exception>");
        }

        if (op.Scope is not null)
        {
            writer.Line("/// <exception cref=\"ArgumentException\"><paramref name=\"token\"/> is null, empty or whitespace.</exception>");
        }
    }

    private static string EmitImplementation(string tagClass, string tag, IReadOnlyList<ServiceMethod> methods, string header)
    {
        var writer = Start(header, Usings(methods, implementation: true))
            .Summary($"Builds requests for the ESI endpoints tagged '{tag}'.")
            .Open($"internal sealed partial class {tagClass}Service(IEsiClientFactory factory) : EsiService<I{tagClass}>(factory), I{tagClass}");

        var first = true;
        foreach (var method in methods)
        {
            if (!first)
            {
                writer.Line();
            }

            first = false;
            writer.Line("/// <inheritdoc />");
            WriteMethodBody(writer, method);
        }

        return writer.Close().ToString();
    }

    private static void WriteMethodBody(CodeWriter writer, ServiceMethod method)
    {
        var call = Call(method);
        var guards = method.Parameters.Where(p => p.Guard).ToList();
        if (guards.Count == 0)
        {
            writer.Line($"public {Signature(method)} =>");
            call.ForEach(line => writer.Line("    " + line));
            return;
        }

        writer.Open($"public {Signature(method)}");
        foreach (var guard in guards)
        {
            writer.Line($"ArgumentNullException.ThrowIfNull({guard.Name});");
        }

        writer.Line().Line("return " + call[0]);
        call.Skip(1).ToList().ForEach(line => writer.Line(line));
        writer.Close();
    }

    /// <summary>The <c>CreateEsiClient</c> call, as lines, so it can follow either <c>=&gt;</c> or <c>return</c>.</summary>
    private static List<string> Call(ServiceMethod method)
    {
        var name = method.Name;
        var arguments = new List<List<string>> { new() { $"Operation.{name.TagClass}.{name.Constant}" } };
        if (method.Response is { } response)
        {
            arguments.Add([$"EsiJsonContext.Default.{response.InfoName}"]);
        }

        var statements = method.Parameters.Select(p => p.Statement).OfType<string>().ToList();
        arguments.Add(statements.Count == 0
            ? ["null"]
            : ["p =>", "{", .. statements.Select(s => "    " + s), "}"]);
        arguments.Add([method.Parameters.Any(p => p.Name == "token") ? "token" : "null"]);

        // The response type is always written out, never inferred, so it's visibly the same T as the type info.
        var lines = new List<string> { method.Response is null ? "CreateEsiClient(" : $"CreateEsiClient<{ResponseSyntax(method)}>(" };
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            lines.AddRange(argument.Take(argument.Count - 1).Select(l => "    " + l));
            lines.Add("    " + argument[^1] + (i < arguments.Count - 1 ? "," : ");"));
        }

        return lines;
    }

    private static string Signature(ServiceMethod method)
    {
        var parameters = method.Parameters.Select(p => $"{p.Syntax}{(p.Optional ? "? " : " ")}{p.Name}{(p.Optional ? " = null" : "")}");
        return $"{ReturnType(method)} {method.Name.Constant}({string.Join(", ", parameters)})";
    }

    private static string ReturnType(ServiceMethod method) => $"IEsiRequest<{ResponseSyntax(method)}>";

    private static string ResponseSyntax(ServiceMethod method) => method.Response?.Syntax(null) ?? "EsiNoContent";

    private static string EmitRegistration(IEnumerable<string> tagClasses, string header)
    {
        var writer = Start(header, ["Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Extensions"])
            .Summary("Registers every generated ESI service.")
            .Open("internal static partial class EsiServices")
            .Summary("Adds each service interface as transient. A service the app has already registered is left as it is.")
            .Open("public static IServiceCollection AddGeneratedServices(IServiceCollection services)");
        foreach (var tagClass in tagClasses)
        {
            writer.Line($"services.TryAddTransient<I{tagClass}, {tagClass}Service>();");
        }

        return writer.Line("return services;").Close().Close().ToString();
    }

    private static List<string> Usings(IReadOnlyList<ServiceMethod> methods, bool implementation)
    {
        var usings = new SortedSet<string>(StringComparer.Ordinal) { "NewEdenKit.ESI.Abstractions.Requests" };
        if (methods.Any(m => m.Parameters.Any(p => p.UsesCursor)))
        {
            usings.Add("NewEdenKit.ESI.Abstractions.Routing");
        }

        if (methods.Any(m => m.Response is null))
        {
            usings.Add("NewEdenKit.ESI.Abstractions.Results");
        }

        var types = methods.SelectMany(m => m.Parameters.Select(p => p.Uses).Append(m.Response)).OfType<TypeRef>().ToList();
        foreach (var model in types.SelectMany(ModelTypes))
        {
            usings.Add(model.TopLevel.TagNamespace is { } ns ? $"{ModelsNamespace}.{ns}" : ModelsNamespace);
        }

        if (implementation)
        {
            usings.Add("NewEdenKit.ESI.Routes");
            if (methods.Any(m => m.Response is not null || m.Parameters.Any(p => p.Name == "body")))
            {
                usings.Add(ModelsNamespace); // EsiJsonContext
            }
        }

        return [.. usings];
    }

    private static IEnumerable<ModelType> ModelTypes(TypeRef type) => type switch
    {
        ListRef list => ModelTypes(list.Item),
        MapRef map => ModelTypes(map.Value),
        ModelRef model => [model.Type],
        _ => [],
    };

    /// <summary>The service files import the library's namespaces and every model namespace they use, so no model may share a name with a library type.</summary>
    private static void CheckModelNames(ModelSet models, IEnumerable<string> tagClasses)
    {
        var reserved = LibraryTypeNames.Concat(tagClasses.SelectMany(t => new[] { $"I{t}", $"{t}Service" })).ToHashSet(StringComparer.Ordinal);
        var clash = models.TopLevel.Concat(models.Enums)
            .SelectMany(t => t is EnumModel ? new[] { (Name: t.Name, t.Path), (Name: t.Name + "Converter", t.Path) } : [(t.Name, t.Path)])
            .FirstOrDefault(t => reserved.Contains(t.Name));
        if (clash.Name is not null)
        {
            throw new GenerationException($"Type '{clash.Name}' ({clash.Path}) has the same name as a library type the services use. Rename it in generator.json.");
        }
    }

    /// <summary><c>character_id</c> → <c>characterId</c>; C# keywords get an <c>@</c>.</summary>
    private static string ParameterName(string wire)
    {
        var pascal = Names.Pascal(wire);
        var name = char.ToLowerInvariant(pascal[0]) + pascal[1..];
        if (ReservedParameterNames.Contains(name))
        {
            throw new GenerationException($"Parameter '{wire}' would be named '{name}', which the generated services already use.");
        }

        return Keywords.Contains(name) ? "@" + name : name;
    }

    private static string Sentence(string text)
    {
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.EndsWith('.') ? text : text + ".";
    }

    private static CodeWriter Start(string header, IReadOnlyList<string> usings)
    {
        var writer = new CodeWriter().Line(header.TrimEnd('\n')).Line();
        foreach (var u in usings)
        {
            writer.Line($"using {u};");
        }

        return writer.Line().Line($"namespace {Namespace};").Line();
    }

    private sealed record ServiceMethod(OperationName Name, TypeRef? Response, IReadOnlyList<MethodParameter> Parameters);

    /// <param name="Name">The C# parameter name.</param>
    /// <param name="Syntax">The C# type, without the <c>?</c> optional parameters get.</param>
    /// <param name="Optional">Nullable, defaulted to <see langword="null"/>, and not sent when null.</param>
    /// <param name="Doc">The <c>&lt;param&gt;</c> text, already escaped.</param>
    /// <param name="Statement">The configurator statement that sends it; <see langword="null"/> for the token.</param>
    /// <param name="Guard">A required reference: checked for null before the request is built.</param>
    private sealed record MethodParameter(string Name, string Syntax, bool Optional, string Doc, string? Statement, bool Guard)
    {
        /// <summary>The model type it refers to, for usings.</summary>
        public TypeRef? Uses { get; init; }

        public bool UsesCursor { get; init; }
    }
}
