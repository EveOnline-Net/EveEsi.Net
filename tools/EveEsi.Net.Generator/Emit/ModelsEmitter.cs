using EveEsi.Net.Generator.Models;

namespace EveEsi.Net.Generator.Emit;

/// <summary>
/// Emits <c>EveEsi.Net.Models/Generated/</c>: one file per top-level record or union (with its nested types), one file
/// per enum (with its converter), and <c>EsiJsonContext</c> registering every type.
/// </summary>
internal static class ModelsEmitter
{
    public const string Directory = "EveEsi.Net.Models/Generated";
    private const string Namespace = "EveEsi.Net.Models";

    public static IReadOnlyList<GeneratedFile> Emit(ModelSet models, string header)
    {
        var files = new List<GeneratedFile>();
        foreach (var type in models.TopLevel)
        {
            var writer = Start(header, type.TagNamespace, "System.Text.Json", "System.Text.Json.Serialization");
            WriteType(writer, type);
            files.Add(new GeneratedFile(FilePath(type), writer.ToString()));
        }

        var typeNames = models.TopLevel.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var model in models.Enums)
        {
            if (typeNames.Contains(model.Name + "Converter"))
            {
                throw new GenerationException($"Enum {model.Name}'s converter clashes with the type '{model.Name}Converter'. Rename one in generator.json.");
            }

            var writer = Start(header, model.TagNamespace, "System.Runtime.Serialization", "System.Text.Json", "System.Text.Json.Serialization");
            WriteEnum(writer, model);
            files.Add(new GeneratedFile(FilePath(model), writer.ToString()));
        }

        files.Add(new GeneratedFile($"{Directory}/EsiJsonContext.g.cs", EmitContext(models, header)));
        return files;
    }

    private static void WriteType(CodeWriter writer, ModelType type)
    {
        switch (type)
        {
            case RecordModel record:
                WriteRecord(writer, record);
                break;
            case UnionModel union:
                WriteUnion(writer, union);
                break;
            default:
                throw new InvalidOperationException($"Unexpected nested type {type.GetType().Name}.");
        }
    }

    private static void WriteRecord(CodeWriter writer, RecordModel record)
    {
        writer.Summary(record.Description ?? $"The {record.Path} object.");
        if (record.Nested.Count == 0 && record.Properties.Count == 0)
        {
            writer.Line($"public sealed partial record {record.Name};");
            return;
        }

        writer.Open($"public sealed partial record {record.Name}");
        var first = true;
        foreach (var nested in record.Nested)
        {
            Separate(writer, ref first);
            WriteType(writer, nested);
        }

        foreach (var property in record.Properties)
        {
            Separate(writer, ref first);
            var type = property.Type.Syntax(record) + (property.Nullable ? "?" : "");
            var required = property.Required && !property.Nullable ? "required " : "";
            writer.Summary(property.Description ?? $"The {property.Wire} field.")
                .Line($"[JsonPropertyName({CodeWriter.Literal(property.Wire)})]")
                .Line($"public {required}{type} {property.Name} {{ get; init; }}");
        }

        writer.Close();
    }

    private static void WriteUnion(CodeWriter writer, UnionModel union)
    {
        var variants = string.Join(", ", union.Variants.Select(v => v.Name));
        writer.Summary($"{Sentence(union.Description ?? $"The {union.Path} union")} One of: {variants}; anything else reads as Unknown.")
            .Line($"[JsonConverter(typeof({union.QualifiedName}.Converter))]")
            .Open($"public abstract partial record {union.Name}")
            .Open($"private {union.Name}()")
            .Close();

        foreach (var variant in union.Variants)
        {
            writer.Line()
                .Summary($"The {variant.Wire} variant.{(variant.Description is null ? "" : " " + variant.Description)}")
                .Line("/// <param name=\"Value\">The variant's value.</param>")
                .Line($"public sealed partial record {variant.Name}({variant.Value.Syntax(union)}{(variant.Nullable ? "?" : "")} Value) : {union.Name};");
        }

        writer.Line()
            .Summary("A variant this library version doesn't know, or ESI's own unknown variant. Written back unchanged.")
            .Line("/// <param name=\"Name\">The variant's property name.</param>")
            .Line("/// <param name=\"Value\">The variant's raw value.</param>")
            .Line($"public sealed partial record Unknown(string Name, JsonElement Value) : {union.Name};");

        foreach (var nested in union.Nested)
        {
            writer.Line();
            WriteType(writer, nested);
        }

        writer.Line();
        WriteUnionConverter(writer, union);
        writer.Close();
    }

    private static void WriteUnionConverter(CodeWriter writer, UnionModel union)
    {
        var name = union.Name;
        writer.Summary($"Reads and writes {name} as an object with exactly one property, named after the variant.")
            .Open($"internal sealed class Converter : JsonConverter<{name}>")
            .Open($"public override {name} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)")
            .Open("if (reader.TokenType != JsonTokenType.StartObject || !reader.Read() || reader.TokenType != JsonTokenType.PropertyName)")
            .Line($"throw new JsonException({CodeWriter.Literal($"Expected an object with one property for {name}.")});")
            .Close()
            .Line()
            .Line("var name = reader.GetString()!;")
            .Line("reader.Read();")
            .Line($"{name} result = name switch")
            .Line("{");
        foreach (var variant in union.Variants)
        {
            var read = $"EsiJson.Read<{variant.Value.Syntax(union)}>(ref reader, options)";
            if (variant.Nullable)
            {
                read = $"reader.TokenType == JsonTokenType.Null ? null : {read}";
            }

            writer.Line($"    {CodeWriter.Literal(variant.Wire)} => new {variant.Name}({read}),");
        }

        writer.Line("    _ => new Unknown(name, JsonElement.ParseValue(ref reader)),")
            .Line("};")
            .Line()
            .Open("if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)")
            .Line($"throw new JsonException({CodeWriter.Literal($"Expected exactly one property for {name}.")});")
            .Close()
            .Line()
            .Line("return result;")
            .Close()
            .Line()
            .Open($"public override void Write(Utf8JsonWriter writer, {name} value, JsonSerializerOptions options)")
            .Line("writer.WriteStartObject();")
            .Line("switch (value)")
            .Line("{");
        foreach (var variant in union.Variants)
        {
            writer.Line($"    case {variant.Name} v:")
                .Line($"        writer.WritePropertyName({CodeWriter.Literal(variant.Wire)});");
            if (variant.Nullable)
            {
                writer.Line("        if (v.Value is { } inner)")
                    .Line("        {")
                    .Line("            EsiJson.Write(writer, inner, options);")
                    .Line("        }")
                    .Line("        else")
                    .Line("        {")
                    .Line("            writer.WriteNullValue();")
                    .Line("        }")
                    .Line();
            }
            else
            {
                writer.Line("        EsiJson.Write(writer, v.Value, options);");
            }

            writer.Line("        break;");
        }

        writer.Line("    case Unknown v:")
            .Line("        writer.WritePropertyName(v.Name);")
            .Line("        v.Value.WriteTo(writer);")
            .Line("        break;")
            .Line("    default:")
            .Line($"        throw new JsonException($\"Unsupported {name} variant {{value.GetType().Name}}.\");")
            .Line("}")
            .Line()
            .Line("writer.WriteEndObject();")
            .Close()
            .Close();
    }

    private static void WriteEnum(CodeWriter writer, EnumModel model)
    {
        var name = model.Name;
        var where = model.Paths.Count == 1 ? model.Paths.Min! : $"{model.Paths.Min} and {model.Paths.Count - 1} more";
        writer.Summary($"Values of {where}.")
            .Line($"[JsonConverter(typeof({name}Converter))]")
            .Open($"public enum {name}");

        writer.Summary(model.UnknownWireValue is null
            ? "A value this library version doesn't know."
            : $"ESI's own {model.UnknownWireValue} value, or a value this library version doesn't know.");
        if (model.UnknownWireValue is not null)
        {
            writer.Line($"[EnumMember(Value = {CodeWriter.Literal(model.UnknownWireValue)})]");
        }

        writer.Line("Unknown = 0,");
        foreach (var member in model.Members)
        {
            writer.Line()
                .Summary(member.Wire)
                .Line($"[EnumMember(Value = {CodeWriter.Literal(member.Wire)})]")
                .Line($"{member.Name},");
        }

        writer.Close()
            .Line()
            .Summary($"Reads and writes {name} as its wire string. Unrecognised values read as {name}.Unknown.")
            .Open($"public sealed class {name}Converter : JsonConverter<{name}>")
            .Line("/// <inheritdoc />")
            .Line($"public override {name} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>")
            .Line($"    reader.TokenType == JsonTokenType.String ? FromWire(reader.GetString()) : throw new JsonException({CodeWriter.Literal($"Expected a string for {name}.")});")
            .Line()
            .Line("/// <inheritdoc />")
            .Line($"public override void Write(Utf8JsonWriter writer, {name} value, JsonSerializerOptions options) => writer.WriteStringValue(ToWire(value));")
            .Line()
            .Summary($"Maps a wire string to its member; unrecognised values give {name}.Unknown.")
            .Line($"public static {name} FromWire(string? value) => value switch")
            .Line("{");
        foreach (var member in model.Members)
        {
            writer.Line($"    {CodeWriter.Literal(member.Wire)} => {name}.{member.Name},");
        }

        writer.Line($"    _ => {name}.Unknown,")
            .Line("};")
            .Line()
            .Summary("Maps a member to its wire string.")
            .Line($"/// <exception cref=\"ArgumentOutOfRangeException\">The value has no wire string{(model.UnknownWireValue is null ? $" ({name}.Unknown)" : "")}.</exception>")
            .Line($"public static string ToWire({name} value) => value switch")
            .Line("{");
        if (model.UnknownWireValue is not null)
        {
            writer.Line($"    {name}.Unknown => {CodeWriter.Literal(model.UnknownWireValue)},");
        }

        foreach (var member in model.Members)
        {
            writer.Line($"    {name}.{member.Name} => {CodeWriter.Literal(member.Wire)},");
        }

        writer.Line($"    _ => throw new ArgumentOutOfRangeException(nameof(value), value, {CodeWriter.Literal($"{name} value has no wire string.")}),")
            .Line("};")
            .Close();
    }

    private static string EmitContext(ModelSet models, string header)
    {
        var registrations = new SortedDictionary<string, string>(StringComparer.Ordinal);

        void Add(TypeRef type)
        {
            var syntax = type.Syntax(null);
            if (registrations.TryGetValue(type.InfoName, out var existing))
            {
                if (existing != syntax)
                {
                    throw new GenerationException($"JSON metadata name '{type.InfoName}' is used by both {existing} and {syntax}.");
                }

                return;
            }

            registrations[type.InfoName] = syntax;
            switch (type)
            {
                case ListRef list:
                    Add(list.Item);
                    break;
                case MapRef map:
                    Add(map.Value);
                    break;
            }
        }

        void AddType(ModelType type)
        {
            Add(new ModelRef(type));
            switch (type)
            {
                case RecordModel record:
                    record.Properties.ForEach(p => Add(p.Type));
                    break;
                case UnionModel union:
                    union.Variants.ForEach(v => Add(v.Value));
                    Add(PrimitiveRef.JsonElement);
                    break;
            }

            type.Nested.ForEach(AddType);
        }

        foreach (var type in models.TopLevel)
        {
            AddType(type);
        }

        foreach (var model in models.Enums)
        {
            Add(new ModelRef(model));
        }

        foreach (var type in models.ResponseTypes.Values.Concat(models.BodyTypes.Values))
        {
            Add(type);
        }

        var tagNamespaces = models.TopLevel.Concat(models.Enums)
            .Select(t => t.TagNamespace).OfType<string>().Distinct().Order(StringComparer.Ordinal)
            .Select(t => $"{Namespace}.{t}");
        var writer = Start(header, null, ["System.Text.Json", "System.Text.Json.Serialization", .. tagNamespaces])
            .Summary("Source-generated JSON metadata for every ESI model, response and request body. AOT and trim safe.")
            .Line("[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]");
        foreach (var (infoName, syntax) in registrations)
        {
            writer.Line($"[JsonSerializable(typeof({syntax}), TypeInfoPropertyName = {CodeWriter.Literal(infoName)})]");
        }

        return writer.Line("internal sealed partial class EsiJsonContext : JsonSerializerContext;").ToString();
    }

    private static string Sentence(string text)
    {
        text = text.Trim();
        return text.EndsWith('.') ? text : text + ".";
    }

    private static void Separate(CodeWriter writer, ref bool first)
    {
        if (!first)
        {
            writer.Line();
        }

        first = false;
    }

    /// <summary>Tagged types live in <c>Generated/&lt;Tag&gt;/</c>; root-namespace types directly in <c>Generated/</c>.</summary>
    private static string FilePath(ModelType type) =>
        type.TagNamespace is null ? $"{Directory}/{type.Name}.g.cs" : $"{Directory}/{type.TagNamespace}/{type.Name}.g.cs";

    private static CodeWriter Start(string header, string? tagNamespace, params string[] usings)
    {
        var writer = new CodeWriter().Line(header.TrimEnd('\n')).Line();
        foreach (var u in usings)
        {
            writer.Line($"using {u};");
        }

        var ns = tagNamespace is null ? Namespace : $"{Namespace}.{tagNamespace}";
        return writer.Line().Line($"namespace {ns};").Line();
    }
}
