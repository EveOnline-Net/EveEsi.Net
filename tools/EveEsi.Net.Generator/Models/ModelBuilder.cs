using System.Text.Json;
using EveEsi.Net.Generator.Config;
using EveEsi.Net.Generator.Naming;
using EveEsi.Net.Generator.Spec;

namespace EveEsi.Net.Generator.Models;

/// <summary>
/// Turns the spec's schemas into the type model. Named object schemas become top-level records; inline objects and
/// unions become nested types of the record that contains them (property name + <c>Model</c>, array items + <c>Item</c>,
/// map values + <c>Value</c>); string enums are shared by value set; primitives follow the spec's <c>format</c> only.
/// </summary>
internal sealed class ModelBuilder(OpenApiSpec spec, GeneratorConfig config)
{
    private readonly Dictionary<string, TypeRef> _named = new(StringComparer.Ordinal);
    private readonly List<ModelType> _topLevel = [];
    private readonly EnumRegistry _enums = new();
    private readonly HashSet<string> _usedSchemaNames = new(StringComparer.Ordinal);

    public ModelSet Build()
    {
        foreach (var name in spec.Schemas.Keys.Order(StringComparer.Ordinal))
        {
            ResolveNamed(name);
        }

        var responses = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        var bodies = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        var parameterEnums = new Dictionary<string, EnumModel>(StringComparer.Ordinal);
        foreach (var op in spec.Operations)
        {
            var id = op.OperationId;
            if (op.ResponseSchema is { } response)
            {
                responses[id] = Resolve(response, $"op:{id}.response", null, Names.Pascal(id), "Response").Type;
            }

            if (op.BodySchema is { } body)
            {
                bodies[id] = Resolve(body, $"op:{id}.body", null, Names.Pascal(id), "Body").Type;
            }

            foreach (var parameter in op.Parameters.Where(p => p.Location is "path" or "query" && p.Schema is not null))
            {
                var schema = parameter.Schema!.Value;
                var items = schema.TryGetProperty("items", out var i) ? i : (JsonElement?)null;
                if (EnumValues(schema) is { } values)
                {
                    parameterEnums[$"{id}.{parameter.Name}"] = _enums.Add(values, $"op:{id}.{parameter.Name}");
                }
                else if (items is { } it && EnumValues(it) is { } itemValues)
                {
                    parameterEnums[$"{id}.{parameter.Name}"] = _enums.Add(itemValues, $"op:{id}.{parameter.Name}[]");
                }
            }
        }

        var stale = config.Schemas.Keys.Where(k => !_usedSchemaNames.Contains(k)).Order(StringComparer.Ordinal).ToArray();
        if (stale.Length > 0)
        {
            throw new GenerationException($"generator.json schemas: no record or union at [{string.Join(", ", stale)}].");
        }

        var enums = _enums.Finalize(config.Enums);
        ValidateNames(enums);
        AssignNamespaces(responses, bodies, parameterEnums, enums);
        return new ModelSet([.. _topLevel.OrderBy(t => t.Name, StringComparer.Ordinal)], enums, responses, bodies, parameterEnums);
    }

    private TypeRef ResolveNamed(string name)
    {
        if (_named.TryGetValue(name, out var known))
        {
            return known;
        }

        if (!spec.Schemas.TryGetValue(name, out var schema))
        {
            throw new GenerationException($"Unknown schema '{name}'.");
        }

        // Records register themselves before their properties are read, so recursive schemas resolve.
        return Resolve(schema, name, null, name, "", registerAs: name).Type;
    }

    private (TypeRef Type, bool Nullable) Resolve(
        JsonElement schema, string path, ModelType? owner, string baseName, string suffix, string? registerAs = null)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            const string prefix = "#/components/schemas/";
            var target = reference.GetString()!;
            return target.StartsWith(prefix, StringComparison.Ordinal)
                ? (ResolveNamed(target[prefix.Length..]), false)
                : throw new GenerationException($"{path}: unsupported $ref '{target}'.");
        }

        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            return (Register(registerAs, BuildUnion(schema, oneOf, path, owner, TypeName(path, baseName, suffix))), false);
        }

        foreach (var keyword in new[] { "anyOf", "allOf", "not" })
        {
            if (schema.TryGetProperty(keyword, out _))
            {
                throw new GenerationException($"{path}: '{keyword}' isn't supported. Add an override.");
            }
        }

        var (type, nullable) = ReadType(schema, path);
        if (schema.TryGetProperty("enum", out _))
        {
            return type == "string" && EnumValues(schema) is { } values
                ? (Register(registerAs, new ModelRef(_enums.Add(values, path))), nullable)
                : throw new GenerationException($"{path}: only string enums are supported (found '{type}').");
        }

        var format = schema.TryGetProperty("format", out var f) ? f.GetString() : null;
        TypeRef result = type switch
        {
            "object" when schema.TryGetProperty("properties", out _) || !schema.TryGetProperty("additionalProperties", out _) =>
                BuildRecord(schema, path, owner, TypeName(path, baseName, suffix), registerAs),
            "object" => new MapRef(Resolve(schema.GetProperty("additionalProperties"), path + "{}", owner, BaseOf(baseName, suffix), "Value").Type),
            "array" => new ListRef(Resolve(
                schema.TryGetProperty("items", out var items) ? items : throw new GenerationException($"{path}: array without 'items'."),
                path + "[]", owner, BaseOf(baseName, suffix), "Item").Type),
            "integer" => format switch
            {
                "int64" or null => PrimitiveRef.Int64,
                "int32" => PrimitiveRef.Int32,
                _ => throw new GenerationException($"{path}: unknown integer format '{format}'."),
            },
            "number" => format is "double" or "float" or null
                ? PrimitiveRef.Double
                : throw new GenerationException($"{path}: unknown number format '{format}'."),
            "string" => format switch
            {
                null => PrimitiveRef.String,
                "date-time" => PrimitiveRef.DateTimeOffset,
                "date" => PrimitiveRef.DateOnly,
                "uuid" => PrimitiveRef.Guid,
                _ => throw new GenerationException($"{path}: unknown string format '{format}'."),
            },
            "boolean" => PrimitiveRef.Boolean,
            null => PrimitiveRef.JsonElement, // No type at all: any JSON value.
            _ => throw new GenerationException($"{path}: unsupported type '{type}'."),
        };

        return (Register(registerAs, result), nullable);
    }

    private TypeRef Register(string? schemaName, TypeRef type)
    {
        if (schemaName is not null)
        {
            _named.TryAdd(schemaName, type);
        }

        return type;
    }

    private TypeRef BuildRecord(JsonElement schema, string path, ModelType? owner, string name, string? registerAs)
    {
        var record = new RecordModel(name, path, owner, Description(schema));
        Attach(record, owner);
        var reference = Register(registerAs, new ModelRef(record));

        var required = schema.TryGetProperty("required", out var r)
            ? r.EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                var (type, nullable) = Resolve(property.Value, $"{path}.{property.Name}", record, Names.Pascal(property.Name), "Model");
                var isRequired = required.Contains(property.Name);
                record.Properties.Add(new PropertyModel(
                    property.Name, Names.Pascal(property.Name), type, isRequired, nullable || !isRequired, Description(property.Value)));
            }
        }

        return reference;
    }

    private ModelRef BuildUnion(JsonElement schema, JsonElement oneOf, string path, ModelType? owner, string name)
    {
        var union = new UnionModel(name, path, owner, Description(schema));
        Attach(union, owner);

        foreach (var variant in oneOf.EnumerateArray())
        {
            // ESI unions are "wrapper key" objects: exactly one property, whose name says which variant it is.
            if (!variant.TryGetProperty("properties", out var properties) || properties.EnumerateObject().Count() != 1)
            {
                throw new GenerationException($"{path}: oneOf variant isn't a single-property object. Add an override.");
            }

            var property = properties.EnumerateObject().Single();
            if (string.Equals(property.Name, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                continue; // ESI's own "unknown" variant is read by the Unknown fallback, which keeps the raw value.
            }

            var (value, nullable) = Resolve(property.Value, $"{path}|{property.Name}", union, Names.Pascal(property.Name), "Model");
            union.Variants.Add(new UnionVariant(property.Name, Names.Pascal(property.Name), value, nullable, Description(property.Value)));
        }

        return new ModelRef(union);
    }

    private void Attach(ModelType type, ModelType? owner)
    {
        if (owner is null)
        {
            _topLevel.Add(type);
        }
        else
        {
            owner.Nested.Add(type);
        }
    }

    private string TypeName(string path, string baseName, string suffix)
    {
        if (config.Schemas.TryGetValue(path, out var mapped))
        {
            _usedSchemaNames.Add(path);
            return Names.Identifier(mapped, $"schemas.{path}");
        }

        return Names.Identifier(baseName + suffix, path);
    }

    // Arrays and maps pass their base name down: `skills` → items `SkillsItem`, not `SkillsModelItem`.
    private static string BaseOf(string baseName, string suffix) => suffix == "Model" ? baseName : baseName + suffix;

    private static (string? Type, bool Nullable) ReadType(JsonElement schema, string path)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return (null, false);
        }

        if (type.ValueKind == JsonValueKind.String)
        {
            return (type.GetString(), false);
        }

        var types = type.EnumerateArray().Select(t => t.GetString()!).ToList();
        var nonNull = types.Where(t => t != "null").ToList();
        return nonNull.Count == 1
            ? (nonNull[0], types.Count > nonNull.Count)
            : throw new GenerationException($"{path}: union types [{string.Join(", ", types)}] aren't supported.");
    }

    private static List<string>? EnumValues(JsonElement schema) =>
        schema.TryGetProperty("enum", out var values) && values.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String)
            ? [.. values.EnumerateArray().Select(v => v.GetString()!)]
            : null;

    private static string? Description(JsonElement schema) =>
        schema.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

    /// <summary>
    /// A top-level type (or enum) used by exactly one tag's operations goes in <c>EveEsi.Net.Models.&lt;Tag&gt;</c>;
    /// everything else (shared or unused) stays in the root namespace.
    /// </summary>
    private void AssignNamespaces(
        Dictionary<string, TypeRef> responses,
        Dictionary<string, TypeRef> bodies,
        Dictionary<string, EnumModel> parameterEnums,
        IReadOnlyList<EnumModel> enums)
    {
        var tags = spec.Operations.ToDictionary(o => o.OperationId, o => o.Tag, StringComparer.Ordinal);
        foreach (var op in spec.Operations)
        {
            var seen = new HashSet<ModelType>();
            foreach (var type in new[] { responses.GetValueOrDefault(op.OperationId), bodies.GetValueOrDefault(op.OperationId) })
            {
                if (type is not null)
                {
                    MarkUsed(type, op.Tag, seen);
                }
            }
        }

        foreach (var (key, model) in parameterEnums)
        {
            model.Tags.Add(tags[key[..key.IndexOf('.', StringComparison.Ordinal)]]);
        }

        var all = _topLevel.Concat(enums).ToList();
        foreach (var type in all)
        {
            type.TagNamespace = type.Tags.Count == 1 ? Names.Pascal(type.Tags.Min!) : null;
        }

        var namespaces = all.Select(t => t.TagNamespace).OfType<string>().ToHashSet(StringComparer.Ordinal);
        // A type named like a namespace is ambiguous wherever both are in scope (CS0118), so no type may share a
        // tag namespace's name.
        var clash = all.FirstOrDefault(t => namespaces.Contains(t.Name));
        if (clash is not null)
        {
            throw new GenerationException(
                $"Type '{clash.Name}' ({clash.Path}) has the same name as a tag namespace. Rename it in generator.json.");
        }
    }

    private static void MarkUsed(TypeRef type, string tag, HashSet<ModelType> seen)
    {
        switch (type)
        {
            case ListRef list:
                MarkUsed(list.Item, tag, seen);
                break;
            case MapRef map:
                MarkUsed(map.Value, tag, seen);
                break;
            case ModelRef { Type: var model } when seen.Add(model):
                model.TopLevel.Tags.Add(tag);
                switch (model)
                {
                    case RecordModel record:
                        record.Properties.ForEach(p => MarkUsed(p.Type, tag, seen));
                        break;
                    case UnionModel union:
                        union.Variants.ForEach(v => MarkUsed(v.Value, tag, seen));
                        break;
                }

                break;
        }
    }

    private void ValidateNames(IReadOnlyList<EnumModel> enums)
    {
        var topLevel = _topLevel.Cast<ModelType>().Concat(enums).GroupBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (topLevel is not null)
        {
            throw new GenerationException(
                $"Several types are named '{topLevel.Key}': [{string.Join(", ", topLevel.Select(t => t.Path))}]. Rename one in generator.json.");
        }

        foreach (var type in _topLevel)
        {
            ValidateMembers(type);
        }
    }

    private static void ValidateMembers(ModelType type)
    {
        var members = type.Nested.Select(n => (n.Name, What: $"nested type at {n.Path}")).ToList();
        if (type is RecordModel record)
        {
            members.AddRange(record.Properties.Select(p => (p.Name, What: $"property '{p.Wire}'")));
        }

        if (type is UnionModel union)
        {
            members.AddRange(union.Variants.Select(v => (v.Name, What: $"variant '{v.Wire}'")));
            members.Add(("Unknown", "the Unknown fallback"));
            members.Add(("Converter", "the converter"));
        }

        foreach (var member in members.Where(m => m.Name == type.Name))
        {
            throw new GenerationException($"{type.Path}: {member.What} has the same name as its type '{type.Name}'. Rename one in generator.json.");
        }

        var clash = members.GroupBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw new GenerationException(
                $"{type.Path}: [{string.Join(", ", clash.Select(c => c.What))}] are all named '{clash.Key}'. Rename one in generator.json.");
        }

        foreach (var nested in type.Nested)
        {
            ValidateMembers(nested);
        }
    }
}
