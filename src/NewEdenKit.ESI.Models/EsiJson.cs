using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NewEdenKit.ESI.Models;

/// <summary>
/// Reads and writes values through the serializer options' own type metadata. Generated union converters use it, so
/// they work with whatever source-generated context the options carry, without reflection.
/// </summary>
internal static class EsiJson
{
    public static T Read<T>(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize(ref reader, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)))
        ?? throw new JsonException($"Expected a {typeof(T).Name}, found null.");

    public static void Write<T>(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));
}
