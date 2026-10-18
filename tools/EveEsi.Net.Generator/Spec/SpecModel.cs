using System.Text.Json;

namespace EveEsi.Net.Generator.Spec;

/// <summary>One spec operation, with the facts the generator maps.</summary>
internal sealed record SpecOperation(
    string OperationId,
    string Method,
    string Path,
    string Tag,
    string? Summary,
    IReadOnlyList<SpecParameter> Parameters,
    string? Scope,
    SpecCache? Cache,
    SpecRateLimit? RateLimit)
{
    /// <summary>The JSON schema of the success response, or <see langword="null"/> for endpoints without a body.</summary>
    public JsonElement? ResponseSchema { get; init; }

    /// <summary>The JSON schema of the request body, if the endpoint takes one.</summary>
    public JsonElement? BodySchema { get; init; }

    public bool BodyRequired { get; init; }

    /// <summary>The operation's <c>description</c>, if any.</summary>
    public string? Description { get; init; }
}

/// <summary>A parameter after <c>$ref</c> resolution. <see cref="Location"/> is <c>path</c>, <c>query</c> or <c>header</c>.</summary>
internal sealed record SpecParameter(string Name, string Location, bool Required)
{
    public JsonElement? Schema { get; init; }

    /// <summary>The parameter's <c>description</c>, or its schema's.</summary>
    public string? Description { get; init; }

    /// <summary>The schema's <c>default</c> as wire text (what ESI uses when the parameter isn't sent).</summary>
    public string? Default { get; init; }
}

internal enum CacheMode
{
    Unspecified,
    TtlBased,
    EventBased,
    NotCached,
}

internal sealed record SpecCache(long? MaxAgeSeconds, CacheMode Mode, long? TombstoneSeconds);

internal enum WindowUnit
{
    Seconds,
    Minutes,
    Hours,
}

internal sealed record SpecWindow(long Amount, WindowUnit Unit);

internal sealed record SpecRateLimit(string Group, long MaxTokens, SpecWindow Window);
