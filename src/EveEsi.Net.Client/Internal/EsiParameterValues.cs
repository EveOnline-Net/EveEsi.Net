using EveEsi.Net.Abstractions.Requests;

namespace EveEsi.Net.Client.Internal;

/// <summary>
/// Collects route or query values as wire-string pairs. Setting a name again replaces its value.
/// Frozen once the request is built, so a built request can't change underneath an execution.
/// </summary>
internal sealed class EsiParameterValues(bool isRoute) : IEsiParameterValues
{
    private readonly List<KeyValuePair<string, string>> _pairs = [];
    private bool _frozen;

    public IReadOnlyList<KeyValuePair<string, string>> Pairs => _pairs;

    public void Freeze() => _frozen = true;

    public IEsiParameterValues Set<T>(string name, T? value)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EsiRequestParameters.ThrowIfFrozen(_frozen);
        if (value is not null)
        {
            Add(name, value);
        }

        return this;
    }

    public IEsiParameterValues Set<T>(string name, T? value)
        where T : struct
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EsiRequestParameters.ThrowIfFrozen(_frozen);
        if (value is { } v)
        {
            Add(name, v);
        }

        return this;
    }

    private void Add(string name, object value)
    {
        var strings = EsiValueFormatter.FormatMany(value);
        if (isRoute && strings.Count != 1)
        {
            throw new ArgumentException($"Route value '{name}' must be a single value.", nameof(value));
        }

        _pairs.RemoveAll(p => string.Equals(p.Key, name, StringComparison.Ordinal));
        foreach (var s in strings)
        {
            _pairs.Add(new KeyValuePair<string, string>(name, s));
        }
    }
}
