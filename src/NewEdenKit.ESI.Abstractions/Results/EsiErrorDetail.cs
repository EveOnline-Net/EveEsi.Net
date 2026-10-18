using System.Text.Json;

namespace NewEdenKit.ESI.Abstractions.Results;

/// <summary>One issue from the <c>details</c> list of an ESI error body.</summary>
/// <param name="Location">Where the error occurred, e.g. <c>body.items[3].tags</c> or <c>path.character_id</c>.</param>
/// <param name="Message">The error message.</param>
/// <param name="Value">The offending value, as sent back by ESI.</param>
public sealed record EsiErrorDetail(string? Location, string? Message, JsonElement? Value = null);
