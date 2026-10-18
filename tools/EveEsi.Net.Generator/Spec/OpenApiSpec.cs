using System.Text.Json;

namespace EveEsi.Net.Generator.Spec;

/// <summary>The parts of the ESI OpenAPI document the generator reads, already resolved and validated.</summary>
internal sealed class OpenApiSpec
{
    private static readonly string[] HttpMethods = ["get", "post", "put", "delete", "patch"];

    private OpenApiSpec(string compatibilityDate, IReadOnlyList<SpecOperation> operations, IReadOnlyDictionary<string, JsonElement> schemas)
    {
        CompatibilityDate = compatibilityDate;
        Operations = operations;
        Schemas = schemas;
    }

    /// <summary>The single compatibility date the spec is pinned to.</summary>
    public string CompatibilityDate { get; }

    /// <summary>Every operation, ordered by operation ID.</summary>
    public IReadOnlyList<SpecOperation> Operations { get; }

    /// <summary><c>components/schemas</c>, by name. Elements are detached from the parsed document.</summary>
    public IReadOnlyDictionary<string, JsonElement> Schemas { get; }

    public static OpenApiSpec Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        return Read(document.RootElement.Clone());
    }

    public static OpenApiSpec Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement.Clone());
    }

    private static OpenApiSpec Read(JsonElement root)
    {
        var components = root.GetProperty("components");
        var operations = new List<SpecOperation>();

        foreach (var pathItem in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var entry in pathItem.Value.EnumerateObject())
            {
                if (!HttpMethods.Contains(entry.Name, StringComparer.Ordinal))
                {
                    throw new GenerationException($"{pathItem.Name}: unsupported path item key '{entry.Name}'.");
                }

                operations.Add(ReadOperation(pathItem.Name, entry.Name, entry.Value, components));
            }
        }

        var duplicate = operations.GroupBy(o => o.OperationId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new GenerationException($"Operation ID '{duplicate.Key}' is used more than once.");
        }

        var schemas = components.TryGetProperty("schemas", out var schemaElement)
            ? schemaElement.EnumerateObject().ToDictionary(s => s.Name, s => s.Value, StringComparer.Ordinal)
            : [];

        return new OpenApiSpec(
            ReadCompatibilityDate(components),
            [.. operations.OrderBy(o => o.OperationId, StringComparer.Ordinal)],
            schemas);
    }

    private static string ReadCompatibilityDate(JsonElement components)
    {
#pragma warning disable CA1507 // A JSON key in the spec, not a reference to our property.
        var values = components.GetProperty("parameters").GetProperty("CompatibilityDate").GetProperty("schema").GetProperty("enum");
#pragma warning restore CA1507
        if (values.GetArrayLength() != 1)
        {
            throw new GenerationException("components/parameters/CompatibilityDate must list exactly one date.");
        }

        return values[0].GetString()!;
    }

    private static SpecOperation ReadOperation(string path, string method, JsonElement operation, JsonElement components)
    {
        var where = $"{method.ToUpperInvariant()} {path}";
        var operationId = RequiredString(operation, "operationId", where);

        var tags = operation.TryGetProperty("tags", out var tagArray) ? tagArray.EnumerateArray().Select(t => t.GetString()!).ToArray() : [];
        if (tags.Length != 1)
        {
            throw new GenerationException($"{operationId}: expected exactly one tag, found {tags.Length}.");
        }

        return new SpecOperation(
            operationId,
            method,
            path,
            tags[0],
            operation.TryGetProperty("summary", out var summary) ? summary.GetString() : null,
            ReadParameters(operation, components, operationId),
            ReadScope(operation, operationId),
            ReadCache(operation, operationId),
            ReadRateLimit(operation, operationId))
        {
            ResponseSchema = ReadResponseSchema(operation, operationId),
            BodySchema = ReadBody(operation, operationId, out var bodyRequired),
            BodyRequired = bodyRequired,
            Description = OptionalString(operation, "description"),
        };
    }

    /// <summary>The JSON schema of the first 2xx response that has a body, or <see langword="null"/> if none does.</summary>
    private static JsonElement? ReadResponseSchema(JsonElement operation, string operationId)
    {
        if (!operation.TryGetProperty("responses", out var responses))
        {
            return null;
        }

        var successes = responses.EnumerateObject()
            .Where(r => r.Name.StartsWith('2'))
            .OrderBy(r => r.Name, StringComparer.Ordinal);

        foreach (var response in successes)
        {
            if (!response.Value.TryGetProperty("content", out var content))
            {
                continue;
            }

            if (!content.TryGetProperty("application/json", out var json))
            {
                throw new GenerationException($"{operationId}: {response.Name} response isn't application/json.");
            }

            var schema = json.GetProperty("schema");
            if (schema.ValueKind == JsonValueKind.Object && !schema.EnumerateObject().Any())
            {
                continue; // `{}`: an empty 204 alongside a real 200.
            }

            return schema;
        }

        return null;
    }

    private static JsonElement? ReadBody(JsonElement operation, string operationId, out bool required)
    {
        required = false;
        if (!operation.TryGetProperty("requestBody", out var body))
        {
            return null;
        }

        if (!body.TryGetProperty("content", out var content) || !content.TryGetProperty("application/json", out var json))
        {
            throw new GenerationException($"{operationId}: request body isn't application/json.");
        }

        required = body.TryGetProperty("required", out var r) && r.GetBoolean();
        return json.GetProperty("schema");
    }

    private static List<SpecParameter> ReadParameters(JsonElement operation, JsonElement components, string operationId)
    {
        var parameters = new List<SpecParameter>();
        if (!operation.TryGetProperty("parameters", out var array))
        {
            return parameters;
        }

        foreach (var item in array.EnumerateArray())
        {
            var parameter = item;
            if (item.TryGetProperty("$ref", out var reference))
            {
                parameter = Resolve(components, reference.GetString()!, operationId);
            }

            var location = RequiredString(parameter, "in", operationId);
            var name = RequiredString(parameter, "name", operationId);
            var required = parameter.TryGetProperty("required", out var r) && r.GetBoolean();
            var schema = parameter.TryGetProperty("schema", out var s) ? s : (JsonElement?)null;
            parameters.Add(new SpecParameter(name, location, required)
            {
                Schema = schema,
                Description = OptionalString(parameter, "description") ?? (schema is { } sc ? OptionalString(sc, "description") : null),
                Default = schema is { } d && d.TryGetProperty("default", out var value) ? WireText(value) : null,
            });
        }

        return parameters;
    }

    private static string? ReadScope(JsonElement operation, string operationId)
    {
        if (!operation.TryGetProperty("security", out var security) || security.GetArrayLength() == 0)
        {
            return null;
        }

        // ESI uses one OAuth2 requirement with one scope. Anything else needs a deliberate mapping decision.
        if (security.GetArrayLength() != 1)
        {
            throw new GenerationException($"{operationId}: expected one security requirement, found {security.GetArrayLength()}.");
        }

        var requirement = security[0].EnumerateObject().ToArray();
        if (requirement.Length != 1 || requirement[0].Name != "OAuth2" || requirement[0].Value.GetArrayLength() != 1)
        {
            throw new GenerationException($"{operationId}: expected exactly one OAuth2 scope, found {security[0]}.");
        }

        return requirement[0].Value[0].GetString();
    }

    private static SpecCache? ReadCache(JsonElement operation, string operationId)
    {
        var serverMode = OptionalString(operation, "x-server-cache-mode");
        var legacyMode = OptionalString(operation, "x-cache-mode");
        if (serverMode is not null && legacyMode is not null && serverMode != legacyMode)
        {
            throw new GenerationException($"{operationId}: x-server-cache-mode '{serverMode}' and x-cache-mode '{legacyMode}' disagree.");
        }

        var modeText = serverMode ?? legacyMode;
        var mode = modeText switch
        {
            null => CacheMode.Unspecified,
            "ttl-based" => CacheMode.TtlBased,
            "event-based" => CacheMode.EventBased,
            "not-cached" => CacheMode.NotCached,
            _ => throw new GenerationException($"{operationId}: unknown cache mode '{modeText}'."),
        };

        var maxAge = OptionalSeconds(operation, "x-cache-age", operationId);
        var tombstone = OptionalSeconds(operation, "x-tombstone-ttl", operationId);

        return mode == CacheMode.Unspecified && maxAge is null && tombstone is null ? null : new SpecCache(maxAge, mode, tombstone);
    }

    private static SpecRateLimit? ReadRateLimit(JsonElement operation, string operationId)
    {
        if (!operation.TryGetProperty("x-rate-limit", out var rateLimit))
        {
            return null;
        }

        var group = RequiredString(rateLimit, "group", operationId);
        var maxTokens = rateLimit.GetProperty("max-tokens").GetInt64();
        var window = RequiredString(rateLimit, "window-size", operationId);
        return new SpecRateLimit(group, maxTokens, ParseWindow(window, operationId));
    }

    private static SpecWindow ParseWindow(string window, string operationId)
    {
        if (window.Length >= 2 && long.TryParse(window.AsSpan(0, window.Length - 1), out var amount) && amount > 0)
        {
            var unit = window[^1] switch
            {
                's' => WindowUnit.Seconds,
                'm' => WindowUnit.Minutes,
                'h' => WindowUnit.Hours,
                _ => (WindowUnit?)null,
            };
            if (unit is { } u)
            {
                return new SpecWindow(amount, u);
            }
        }

        throw new GenerationException($"{operationId}: can't parse rate-limit window-size '{window}'.");
    }

    private static JsonElement Resolve(JsonElement components, string reference, string operationId)
    {
        const string prefix = "#/components/parameters/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal)
            || !components.GetProperty("parameters").TryGetProperty(reference[prefix.Length..], out var resolved))
        {
            throw new GenerationException($"{operationId}: can't resolve parameter reference '{reference}'.");
        }

        return resolved;
    }

    private static long? OptionalSeconds(JsonElement element, string name, string operationId)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) && seconds >= 0
            ? seconds
            : throw new GenerationException($"{operationId}: '{name}' must be a non-negative whole number of seconds.");
    }

    private static string WireText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string RequiredString(JsonElement element, string name, string where) =>
        OptionalString(element, name) is { Length: > 0 } value
            ? value
            : throw new GenerationException($"{where}: missing '{name}'.");
}
