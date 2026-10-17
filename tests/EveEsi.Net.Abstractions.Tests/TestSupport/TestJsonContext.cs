using System.Text.Json.Serialization;

namespace EveEsi.Net.Abstractions.Tests.TestSupport;

public sealed record TestDto(long Id);

public sealed record TestBody(string Name);

[JsonSerializable(typeof(TestDto))]
[JsonSerializable(typeof(TestBody))]
[JsonSerializable(typeof(IReadOnlyList<long>))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
