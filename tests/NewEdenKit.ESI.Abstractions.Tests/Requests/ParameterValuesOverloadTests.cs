using NewEdenKit.ESI.Abstractions.Requests;

namespace NewEdenKit.ESI.Abstractions.Tests.Requests;

/// <summary>
/// Generated services pass optional query values straight through (<c>long? typeId = null</c>), so overload
/// resolution must pick the right <c>Set</c> for every value shape without casts.
/// </summary>
public sealed class ParameterValuesOverloadTests
{
    private const string TypeId = "type_id";
    private const string Search = "search";

    [Fact]
    public void Set_NonNullableValue_UsesValueOverload()
    {
        var values = new RecordingParameterValues();

        values.Set(TypeId, 34L);

        values.Calls.ShouldBe(["value:34"]);
    }

    [Theory]
    [InlineData(34L, "nullable:34")]
    [InlineData(null, "nullable:null")]
    public void Set_NullableValueType_UsesNullableOverload(long? typeId, string expected)
    {
        var values = new RecordingParameterValues();

        values.Set(TypeId, typeId);

        values.Calls.ShouldBe([expected]);
    }

    [Fact]
    public void Set_NullReferenceValue_UsesValueOverload()
    {
        var values = new RecordingParameterValues();
        string? search = null;

        values.Set(Search, search);

        values.Calls.ShouldBe(["value:null"]);
    }

    private sealed class RecordingParameterValues : IEsiParameterValues
    {
        public List<string> Calls { get; } = [];

        public IEsiParameterValues Set<T>(string name, T? value)
            where T : notnull
        {
            Calls.Add($"value:{value?.ToString() ?? "null"}");
            return this;
        }

        public IEsiParameterValues Set<T>(string name, T? value)
            where T : struct
        {
            Calls.Add($"nullable:{value?.ToString() ?? "null"}");
            return this;
        }
    }
}
