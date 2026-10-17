using System.Text;

namespace EveEsi.Net.Generator.Naming;

internal static class Names
{
    /// <summary>
    /// <c>character_id</c> → <c>CharacterId</c>, <c>Access List</c> → <c>AccessList</c>. Splits on anything that isn't a
    /// letter or digit and upper-cases each part's first letter, keeping the rest as written.
    /// </summary>
    /// <exception cref="GenerationException">The result isn't a valid C# identifier.</exception>
    public static string Pascal(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var part in text.SplitOn(c => !char.IsAsciiLetterOrDigit(c)))
        {
            builder.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        }

        return Identifier(builder.ToString(), text);
    }

    /// <summary>Checks that a configured or derived name is a valid C# identifier.</summary>
    /// <exception cref="GenerationException">It isn't.</exception>
    public static string Identifier(string name, string source)
    {
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]) || !name.All(char.IsAsciiLetterOrDigit))
        {
            throw new GenerationException($"'{source}' doesn't produce a valid identifier ('{name}'). Map it in generator.json.");
        }

        return name;
    }

    private static IEnumerable<string> SplitOn(this string text, Func<char, bool> isSeparator)
    {
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || isSeparator(text[i]))
            {
                if (i > start)
                {
                    yield return text[start..i];
                }

                start = i + 1;
            }
        }
    }
}
