using System.Collections;
using System.Globalization;

namespace NewEdenKit.ESI.Client.Internal;

/// <summary>Turns parameter values into wire strings. Culture-invariant and AOT safe (no reflection).</summary>
internal static class EsiValueFormatter
{
    public static string Format(object value) => value switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        DateTimeOffset d => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        Guid g => g.ToString("D"),
        // Enum wire values come from generated converters; services convert before setting.
        Enum => throw new ArgumentException(
            $"Enum '{value.GetType().Name}' has no wire format here. Convert it to its wire string before setting it."),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new ArgumentException($"Type '{value.GetType().Name}' can't be sent as a parameter value."),
    };

    /// <summary>A scalar gives one string; a collection gives one string per item.</summary>
    public static IReadOnlyList<string> FormatMany(object value)
    {
        if (value is string || value is not IEnumerable items)
        {
            return [Format(value)];
        }

        var result = new List<string>();
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("Collection parameter values can't contain null items.");
            }

            result.Add(Format(item));
        }

        return result;
    }
}
