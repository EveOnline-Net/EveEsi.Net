namespace NewEdenKit.ESI.Abstractions.Routing;

/// <summary>The rate-limit bucket an endpoint belongs to (<c>x-rate-limit</c>).</summary>
/// <param name="Group">The bucket name, e.g. <c>char-asset</c>. Endpoints in the same group share tokens.</param>
/// <param name="MaxTokens">Tokens available per window.</param>
/// <param name="WindowSize">The window the tokens refill over.</param>
public sealed record RateLimitPolicy(string Group, long MaxTokens, TimeSpan WindowSize);
