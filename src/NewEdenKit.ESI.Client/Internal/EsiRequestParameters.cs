using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NewEdenKit.ESI.Abstractions.Requests;
using NewEdenKit.ESI.Abstractions.Routing;

namespace NewEdenKit.ESI.Client.Internal;

/// <summary>
/// What the configurator fills in: route and query pairs, the serialized body. The built request keeps this object;
/// <see cref="Freeze"/> makes it read-only once the configurator has run.
/// </summary>
internal sealed class EsiRequestParameters : IEsiRequestParameters
{
    private readonly EsiParameterValues _route = new(isRoute: true);
    private readonly EsiParameterValues _query = new(isRoute: false);
    private bool _frozen;

    public IEsiParameterValues Route => _route;

    public IEsiParameterValues Query => _query;

    public IReadOnlyList<KeyValuePair<string, string>> RoutePairs => _route.Pairs;

    public IReadOnlyList<KeyValuePair<string, string>> QueryPairs => _query.Pairs;

    public ReadOnlyMemory<byte>? BodyBytes { get; private set; }

    public void Freeze()
    {
        _frozen = true;
        _route.Freeze();
        _query.Freeze();
    }

    internal static void ThrowIfFrozen(bool frozen)
    {
        if (frozen)
        {
            throw new InvalidOperationException("Request parameters can't be changed after the request is built.");
        }
    }

    public void Body<TBody>(TBody value, JsonTypeInfo<TBody> bodyType)
    {
        ArgumentNullException.ThrowIfNull(bodyType);
        ThrowIfFrozen(_frozen);
        BodyBytes = JsonSerializer.SerializeToUtf8Bytes(value, bodyType);
    }

    public void ApplyPagination(int? page)
    {
        ThrowIfFrozen(_frozen);
        if (page is { } p)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(p, 1, nameof(page));
            _query.Set("page", p);
        }
    }

    public void ApplyPagination(CursorPage? cursor)
    {
        ThrowIfFrozen(_frozen);
        if (cursor is { } c)
        {
            _query.Set("before", c.Before);
            _query.Set("after", c.After);
            _query.Set("limit", c.Limit);
        }
    }
}
