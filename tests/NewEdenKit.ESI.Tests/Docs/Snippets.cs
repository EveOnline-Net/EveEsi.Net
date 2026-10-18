using System.Reflection;
using System.Text.RegularExpressions;

namespace NewEdenKit.ESI.Tests.Docs;

/// <summary>
/// Reads the <c>#region snippet: name</c> blocks from <c>UsageExamples.cs</c> and the
/// <c>&lt;!-- snippet: name --&gt;</c> code blocks from the consumer docs.
/// </summary>
internal static partial class Snippets
{
    public static string RepositoryRoot { get; } = Path.GetFullPath(Path.Combine(
        typeof(Snippets).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "ProjectDirectory").Value!,
        "..", ".."));

    /// <summary>The docs consumers (and AI assistants) read, relative to the repository root.</summary>
    public static IReadOnlyList<string> Documents { get; } = ["README.md", "docs/usage.md", "llms.txt"];

    public static string ExamplesPath => Path.Combine(RepositoryRoot, "tests", "NewEdenKit.ESI.Tests", "Docs", "UsageExamples.cs");

    public static string PathOf(string document) => Path.Combine(RepositoryRoot, document);

    /// <summary>Each snippet region in the examples, dedented, by name.</summary>
    public static Dictionary<string, string> FromExamples()
    {
        var source = File.ReadAllText(ExamplesPath).ReplaceLineEndings("\n");
        return Region().Matches(source).ToDictionary(m => m.Groups["name"].Value, m => Dedent(m.Groups["body"].Value), StringComparer.Ordinal);
    }

    /// <summary>Each marked code block in a document, as (name, code). A name may appear more than once.</summary>
    public static List<(string Name, string Code)> FromDocument(string document)
    {
        var text = File.ReadAllText(PathOf(document)).ReplaceLineEndings("\n");
        return [.. Block().Matches(text).Select(m => (m.Groups["name"].Value, m.Groups["body"].Value.TrimEnd('\n')))];
    }

    /// <summary>Replaces every marked block in a document with the example's code.</summary>
    public static void Update(string document, IReadOnlyDictionary<string, string> examples)
    {
        var path = PathOf(document);
        var text = File.ReadAllText(path).ReplaceLineEndings("\n");
        text = Block().Replace(text, m => examples.TryGetValue(m.Groups["name"].Value, out var code)
            ? $"<!-- snippet: {m.Groups["name"].Value} -->\n```csharp\n{code}\n```"
            : m.Value);
        File.WriteAllText(path, text);
    }

    private static string Dedent(string body)
    {
        var lines = body.TrimEnd().Split('\n');
        var indent = lines.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    [GeneratedRegex(@"^[ \t]*#region snippet: (?<name>[\w-]+)\n(?<body>(?:.|\n)*?)^[ \t]*#endregion", RegexOptions.Multiline)]
    private static partial Regex Region();

    [GeneratedRegex(@"<!-- snippet: (?<name>[\w-]+) -->\n```csharp\n(?<body>(?:.|\n)*?)```", RegexOptions.Multiline)]
    private static partial Regex Block();
}
