using EveEsi.Net.Generator.Config;
using EveEsi.Net.Generator.Naming;

namespace EveEsi.Net.Generator.Models;

/// <summary>
/// Collects every string enum in the spec and de-duplicates them by value set: identical sets become one C# enum.
/// Names and members are assigned once all occurrences are known.
/// </summary>
internal sealed class EnumRegistry
{
    private readonly Dictionary<string, EnumModel> _bySet = new(StringComparer.Ordinal);

    public EnumModel Add(IReadOnlyList<string> values, string path)
    {
        var key = string.Join('\u0001', values.Order(StringComparer.Ordinal));
        if (!_bySet.TryGetValue(key, out var model))
        {
            _bySet[key] = model = new EnumModel(path, values);
        }

        model.Paths.Add(path);
        return model;
    }

    /// <summary>Names every enum (from generator.json, else from its first path) and derives its members.</summary>
    public IReadOnlyList<EnumModel> Finalize(IReadOnlyDictionary<string, EnumConfig> config)
    {
        var configured = MatchConfig(config);
        foreach (var model in _bySet.Values)
        {
            configured.TryGetValue(model, out var entry);
            model.Name = entry.Name ?? DefaultName(model.Paths.Min!);
            AssignMembers(model, entry.Config);
        }

        return [.. _bySet.Values.OrderBy(e => e.Name, StringComparer.Ordinal)];
    }

    private Dictionary<EnumModel, (string? Name, EnumConfig? Config)> MatchConfig(IReadOnlyDictionary<string, EnumConfig> config)
    {
        var byPath = _bySet.Values.SelectMany(e => e.Paths.Select(p => (Path: p, Enum: e))).ToDictionary(x => x.Path, x => x.Enum, StringComparer.Ordinal);
        var result = new Dictionary<EnumModel, (string?, EnumConfig?)>();

        foreach (var (name, entry) in config.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            Names.Identifier(name, $"enums.{name}");
            if (entry.Match.Count == 0)
            {
                throw new GenerationException($"generator.json enums.{name}: 'match' must list at least one schema path.");
            }

            var matched = entry.Match.Select(path => byPath.TryGetValue(path, out var e)
                ? e
                : throw new GenerationException($"generator.json enums.{name}: no enum at '{path}'.")).Distinct().ToList();

            if (matched.Count != 1)
            {
                throw new GenerationException($"generator.json enums.{name}: the matched paths have different value sets.");
            }

            if (!result.TryAdd(matched[0], (name, entry)))
            {
                throw new GenerationException($"generator.json enums.{name}: that enum is already named '{result[matched[0]].Item1}'.");
            }
        }

        return result;
    }

    private static void AssignMembers(EnumModel model, EnumConfig? config)
    {
        var mapped = config?.Members ?? new Dictionary<string, string>();
        var stale = mapped.Keys.Where(k => !model.Values.Contains(k, StringComparer.Ordinal)).ToArray();
        if (stale.Length > 0)
        {
            throw new GenerationException($"generator.json enums.{model.Name}.members: unknown values [{string.Join(", ", stale)}].");
        }

        foreach (var value in model.Values)
        {
            // ESI's own "unknown" means the same as an unrecognised value, so it's carried by Unknown = 0.
            if (string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase) && !mapped.ContainsKey(value))
            {
                model.UnknownWireValue = value;
                continue;
            }

            string name;
            try
            {
                name = mapped.TryGetValue(value, out var m) ? Names.Identifier(m, $"enums.{model.Name}.members.{value}") : Names.Pascal(value);
            }
            catch (GenerationException)
            {
                throw new GenerationException(
                    $"Enum {model.Name} ({model.Paths.Min}): value '{value}' isn't a valid member name. Map it in generator.json enums.{model.Name}.members.");
            }

            model.Members.Add(new EnumMemberModel(value, name));
        }

        var clash = model.Members.Select(m => m.Name).Append("Unknown").GroupBy(n => n, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw new GenerationException($"Enum {model.Name}: several values map to the member '{clash.Key}'. Map them in generator.json.");
        }
    }

    /// <summary><c>CharactersCharacterIdWalletJournalGet[].ref_type</c> → <c>CharactersCharacterIdWalletJournalGetItemRefType</c>.</summary>
    public static string DefaultName(string path)
    {
        var text = path.StartsWith("op:", StringComparison.Ordinal) ? path[3..] : path;
        text = text.Replace("[]", ".Item", StringComparison.Ordinal).Replace("{}", ".Value", StringComparison.Ordinal);
        return Names.Pascal(text);
    }
}
