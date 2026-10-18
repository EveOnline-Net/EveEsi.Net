using System.Globalization;
using System.Text;
using System.Text.Json;
using NewEdenKit.ESI.Abstractions.Http;
using NewEdenKit.ESI.Abstractions.Results;

namespace NewEdenKit.ESI.Client.Internal;

/// <summary>Reads ESI's error body (<c>{"error": "...", "details": [...]}</c>) without the Models assembly.</summary>
internal static class EsiErrorParser
{
    public static EsiError Parse(EsiResponse response)
    {
        var fallback = string.Create(CultureInfo.InvariantCulture, $"ESI returned {(int)response.StatusCode}.");
        if (response.Body.IsEmpty)
        {
            return new EsiError(EsiErrorKind.Http, fallback);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                return new EsiError(EsiErrorKind.Http, error.GetString()!) { Details = ReadDetails(root) };
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. an HTML error page from a proxy): fall through and keep the raw body.
        }

        return new EsiError(EsiErrorKind.Http, fallback) { RawBody = Encoding.UTF8.GetString(response.Body.Span) };
    }

    private static List<EsiErrorDetail> ReadDetails(JsonElement root)
    {
        var details = new List<EsiErrorDetail>();
        if (!root.TryGetProperty("details", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return details;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            details.Add(new EsiErrorDetail(
                GetString(item, "location"),
                GetString(item, "message"),
                item.TryGetProperty("value", out var value) ? value.Clone() : null));
        }

        return details;
    }

    private static string? GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
