namespace EveEsi.Net.Generator.Config;

/// <summary>One <c>generator.json</c> <c>enums</c> entry: which enum it names, and member names for awkward values.</summary>
/// <param name="Match">Schema paths using the enum's value set (one is enough; all must share the set).</param>
/// <param name="Members">Wire value → member name, for values that aren't valid identifiers (or to rename).</param>
internal sealed record EnumConfig(IReadOnlyList<string> Match, IReadOnlyDictionary<string, string> Members);
