using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EveEsi.Net.Abstractions.Middleware;

namespace EveEsi.Net.Middleware.Caching;

/// <summary>
/// The cache key for a call, shared by <see cref="CacheMiddleware"/> and <see cref="ETagMiddleware"/> so they find each
/// other's entries. Use it in custom caching middleware too.
/// </summary>
/// <remarks>
/// The key covers everything that changes the response: the method, the path and query (so each page is its own
/// entry), an <c>X-Tenant</c> or <c>Accept-Language</c> set on the request, and, on secured routes only, who is asking.
/// The caller is a SHA-256 hash of the access token's <c>sub</c> claim (the character), never the token itself, so the
/// key survives token refreshes and leaks nothing. Public routes share entries between callers.
/// </remarks>
public static class EsiCacheKey
{
    /// <summary>Builds the key, e.g. <c>esi:GET /characters/1/assets?page=2|sub=3F2A…</c>.</summary>
    /// <param name="context">The call.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static string Create(EsiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request;
        var key = new StringBuilder("esi:").Append(request.Method.Method).Append(' ').Append(request.ToRelativeUri());
        AppendHeader(key, context, "X-Tenant", "tenant");
        AppendHeader(key, context, "Accept-Language", "lang");
        if (context.Route.RequiresAuthentication && !string.IsNullOrEmpty(context.AccessToken))
        {
            key.Append("|sub=").Append(CallerHash(context.AccessToken));
        }

        return key.ToString();
    }

    private static void AppendHeader(StringBuilder key, EsiContext context, string header, string label)
    {
        if (context.Request.Headers.TryGetValue(header, out var value) && !string.IsNullOrEmpty(value))
        {
            key.Append('|').Append(label).Append('=').Append(value);
        }
    }

    /// <summary>A hash of the token's subject, or of the whole token when it isn't a readable JWT.</summary>
    internal static string CallerHash(string token)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(Subject(token) ?? token), hash);
        return Convert.ToHexString(hash[..16]);
    }

    /// <summary>The <c>sub</c> claim of an SSO JWT, read without validating the token.</summary>
    private static string? Subject(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        var bytes = new byte[payload.Length];
        if (!Convert.TryFromBase64String(payload, bytes, out var length))
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(bytes.AsSpan(0, length));
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isSubject = reader.ValueTextEquals("sub"u8);
                reader.Read();
                if (isSubject && reader.TokenType == JsonTokenType.String)
                {
                    return reader.GetString();
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall back to hashing the token.
        }

        return null;
    }
}
