using System.Text.Json;

namespace EveEsi.Net.Generator.Config;

/// <summary>The contents of <c>generator.json</c>.</summary>
internal sealed class GeneratorConfig
{
    private static readonly string[] KnownSections = ["operations", "operationConstants", "schemas", "enums", "unions", "overrides"];

    /// <summary>Operation ID → name of its <c>Operation.&lt;Tag&gt;.&lt;Name&gt;</c> constant.</summary>
    public IReadOnlyDictionary<string, string> OperationConstants { get; init; } = new Dictionary<string, string>();

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

        return new GeneratorConfig { OperationConstants = ReadStringMap(root, "operationConstants", source) };
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
