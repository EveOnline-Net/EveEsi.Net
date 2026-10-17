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
    SpecRateLimit? RateLimit);

/// <summary>A parameter after <c>$ref</c> resolution. <see cref="Location"/> is <c>path</c>, <c>query</c> or <c>header</c>.</summary>
internal sealed record SpecParameter(string Name, string Location, bool Required);

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
