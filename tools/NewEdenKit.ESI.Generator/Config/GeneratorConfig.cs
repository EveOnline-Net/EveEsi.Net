using System.Text.Json;

namespace NewEdenKit.ESI.Generator.Config;

/// <summary>The contents of <c>generator.json</c>.</summary>
internal sealed class GeneratorConfig
{
    private static readonly string[] KnownSections = ["operations", "operationConstants", "schemas", "enums", "unions", "overrides"];

    /// <summary>Operation ID → name of its <c>Operation.&lt;Tag&gt;.&lt;Name&gt;</c> constant.</summary>
    public IReadOnlyDictionary<string, string> OperationConstants { get; init; } = new Dictionary<string, string>();

    /// <summary>Schema path (e.g. <c>CharactersDetail.position</c>) → C# name of the record or union generated there.</summary>
    public IReadOnlyDictionary<string, string> Schemas { get; init; } = new Dictionary<string, string>();

    /// <summary>Enum name → which value set it names, and member names for awkward values.</summary>
    public IReadOnlyDictionary<string, EnumConfig> Enums { get; init; } = new Dictionary<string, EnumConfig>();

    public static GeneratorConfig Empty { get; } = new();

    public static GeneratorConfig Load(string path) => Parse(File.ReadAllText(path), path);

    public static GeneratorConfig Parse(string json, string source = "generator.json")
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new GenerationException($"{source}: the root must be an object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!KnownSections.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new GenerationException($"{source}: unknown section '{property.Name}'. Known: {string.Join(", ", KnownSections)}.");
            }
        }

        foreach (var unsupported in new[] { "unions", "overrides", "operations" })
        {
            if (root.TryGetProperty(unsupported, out var section) && section.ValueKind == JsonValueKind.Object && section.EnumerateObject().Any())
            {
                throw new GenerationException($"{source}: '{unsupported}' entries aren't supported yet.");
            }
        }

        return new GeneratorConfig
        {
            OperationConstants = ReadStringMap(root, "operationConstants", source),
            Schemas = ReadStringMap(root, "schemas", source),
            Enums = ReadEnums(root, source),
        };
    }

    private static Dictionary<string, EnumConfig> ReadEnums(JsonElement root, string source)
    {
        var enums = new Dictionary<string, EnumConfig>(StringComparer.Ordinal);
        if (!root.TryGetProperty("enums", out var element))
        {
            return enums;
        }

        foreach (var entry in element.EnumerateObject())
        {
            var where = $"{source}: enums.{entry.Name}";
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                throw new GenerationException($"{where} must be an object with 'match' and optional 'members'.");
            }

            var match = entry.Value.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Array
                ? m.EnumerateArray().Select(x => x.GetString() ?? throw new GenerationException($"{where}.match must contain strings.")).ToList()
                : throw new GenerationException($"{where}.match must be an array of schema paths.");
            var members = ReadStringMap(entry.Value, "members", $"{where}");
            enums[entry.Name] = new EnumConfig(match, members);
        }

        return enums;
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement root, string section, string source)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty(section, out var element))
        {
            return map;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new GenerationException($"{source}: '{section}' must be an object of string values.");
        }

        foreach (var entry in element.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.Value.GetString()))
            {
                throw new GenerationException($"{source}: '{section}.{entry.Name}' must be a non-empty string.");
            }

            map[entry.Name] = entry.Value.GetString()!;
        }

        return map;
    }
}
