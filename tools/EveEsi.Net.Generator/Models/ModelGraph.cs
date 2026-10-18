namespace EveEsi.Net.Generator.Models;

/// <summary>A C# type as used in a property, list, map, response or body.</summary>
internal abstract class TypeRef
{
    /// <summary>The <c>EsiJsonContext</c> property name for this type (unique per type).</summary>
    public abstract string InfoName { get; }

    public abstract bool IsValueType { get; }

    /// <summary>C# syntax for this type, written inside <paramref name="scope"/> (<see langword="null"/> = namespace level).</summary>
    public abstract string Syntax(ModelType? scope);
}

internal sealed class PrimitiveRef(string syntax, string infoName, bool isValueType) : TypeRef
{
    public static readonly PrimitiveRef Int64 = new("long", "Int64", true);
    public static readonly PrimitiveRef Int32 = new("int", "Int32", true);
    public static readonly PrimitiveRef Double = new("double", "Double", true);
    public static readonly PrimitiveRef Boolean = new("bool", "Boolean", true);
    public static readonly PrimitiveRef String = new("string", "String", false);
    public static readonly PrimitiveRef DateTimeOffset = new("DateTimeOffset", "DateTimeOffset", true);
    public static readonly PrimitiveRef DateOnly = new("DateOnly", "DateOnly", true);
    public static readonly PrimitiveRef Guid = new("Guid", "Guid", true);
    public static readonly PrimitiveRef JsonElement = new("JsonElement", "JsonElement", true);

    public override string InfoName => infoName;

    public override bool IsValueType => isValueType;

    public override string Syntax(ModelType? scope) => syntax;
}

internal sealed class ListRef(TypeRef item) : TypeRef
{
    public TypeRef Item => item;

    public override string InfoName => "ListOf" + item.InfoName;

    public override bool IsValueType => false;

    public override string Syntax(ModelType? scope) => $"IReadOnlyList<{item.Syntax(scope)}>";
}

internal sealed class MapRef(TypeRef value) : TypeRef
{
    public TypeRef Value => value;

    public override string InfoName => "MapOf" + value.InfoName;

    public override bool IsValueType => false;

    public override string Syntax(ModelType? scope) => $"IReadOnlyDictionary<string, {value.Syntax(scope)}>";
}

internal sealed class ModelRef(ModelType type) : TypeRef
{
    public ModelType Type => type;

    public override string InfoName => type.InfoName;

    public override bool IsValueType => type is EnumModel;

    // A direct child of the scope can be named simply; anything else is qualified from the namespace, so an inner
    // type with the same simple name can never shadow it.
    public override string Syntax(ModelType? scope) => scope is not null && type.Parent == scope ? type.Name : type.QualifiedName;
}

/// <summary>A generated type: record, union or enum. Records and unions may contain nested types.</summary>
internal abstract class ModelType(string name, string path, ModelType? parent, string? description)
{
    /// <summary>The C# name. Enum names are assigned after all their occurrences are known.</summary>
    public string Name { get; set; } = name;

    /// <summary>Where the schema was found, e.g. <c>CharactersDetail.position</c>. The key for generator.json.</summary>
    public string Path { get; } = path;

    public ModelType? Parent { get; } = parent;

    public string? Description { get; } = description;

    public List<ModelType> Nested { get; } = [];

    /// <summary>
    /// Tags of the operations that use this type, directly or through other types. Set on top-level types only.
    /// </summary>
    public SortedSet<string> Tags { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The sub-namespace (a tag's PascalCase name) for a top-level type used by exactly one tag; <see langword="null"/>
    /// for the root namespace (shared or unused types). Nested types live in their parent's namespace.
    /// </summary>
    public string? TagNamespace { get; set; }

    public ModelType TopLevel => Parent?.TopLevel ?? this;

    public string QualifiedName => Parent is null ? Name : $"{Parent.QualifiedName}.{Name}";

    public string InfoName => QualifiedName.Replace(".", "", StringComparison.Ordinal);
}

internal sealed class RecordModel(string name, string path, ModelType? parent, string? description)
    : ModelType(name, path, parent, description)
{
    public List<PropertyModel> Properties { get; } = [];
}

internal sealed class UnionModel(string name, string path, ModelType? parent, string? description)
    : ModelType(name, path, parent, description)
{
    public List<UnionVariant> Variants { get; } = [];
}

internal sealed class EnumModel(string path, IReadOnlyList<string> values) : ModelType("", path, null, null)
{
    /// <summary>Wire values in the order of their first occurrence.</summary>
    public IReadOnlyList<string> Values { get; } = values;

    /// <summary>Every schema path that uses this value set, ordered.</summary>
    public SortedSet<string> Paths { get; } = new(StringComparer.Ordinal);

    /// <summary>Members other than <c>Unknown</c>.</summary>
    public List<EnumMemberModel> Members { get; } = [];

    /// <summary>ESI's own "unknown" value, if the set has one; <c>Unknown = 0</c> carries it.</summary>
    public string? UnknownWireValue { get; set; }
}

internal sealed record EnumMemberModel(string Wire, string Name);

internal sealed record PropertyModel(string Wire, string Name, TypeRef Type, bool Required, bool Nullable, string? Description);

/// <param name="Nullable">The variant's value may be JSON null (<c>type: [..., "null"]</c>).</param>
internal sealed record UnionVariant(string Wire, string Name, TypeRef Value, bool Nullable, string? Description);

/// <summary>Everything the models emitter writes, plus the response, body and parameter types the services use.</summary>
/// <param name="ResponseTypes">By operation ID; operations without a response body are absent.</param>
/// <param name="BodyTypes">By operation ID; operations without a request body are absent.</param>
/// <param name="ParameterTypes">By <c>operationId.wire_name</c>, for every path and query parameter.</param>
/// <param name="ParameterEnums">The enum of each enum (or enum list) parameter, by <c>operationId.wire_name</c>.</param>
internal sealed record ModelSet(
    IReadOnlyList<ModelType> TopLevel,
    IReadOnlyList<EnumModel> Enums,
    IReadOnlyDictionary<string, TypeRef> ResponseTypes,
    IReadOnlyDictionary<string, TypeRef> BodyTypes,
    IReadOnlyDictionary<string, TypeRef> ParameterTypes,
    IReadOnlyDictionary<string, EnumModel> ParameterEnums);
