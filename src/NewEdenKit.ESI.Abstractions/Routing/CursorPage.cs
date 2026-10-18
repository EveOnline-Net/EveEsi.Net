namespace NewEdenKit.ESI.Abstractions.Routing;

/// <summary>Input for cursor-paginated endpoints. Members left <see langword="null"/> aren't sent.</summary>
/// <param name="Before">Return records before this cursor token.</param>
/// <param name="After">Return records after this cursor token.</param>
/// <param name="Limit">Maximum records per response.</param>
public readonly record struct CursorPage(string? Before = null, string? After = null, long? Limit = null);
