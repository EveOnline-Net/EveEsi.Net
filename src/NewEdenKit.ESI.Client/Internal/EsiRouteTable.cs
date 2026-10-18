using System.Diagnostics.CodeAnalysis;
using NewEdenKit.ESI.Abstractions.Routing;

namespace NewEdenKit.ESI.Client.Internal;

/// <summary>The generated route table, as the factory sees it. Keys are operation IDs.</summary>
internal sealed class EsiRouteTable
{
    private readonly IReadOnlyDictionary<string, RouteDefinition> _routes;

    public EsiRouteTable(IReadOnlyDictionary<string, RouteDefinition> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        foreach (var (key, route) in routes)
        {
            if (!string.Equals(key, route.OperationId, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Route table key '{key}' doesn't match its route's operation ID '{route.OperationId}'.", nameof(routes));
            }
        }

        _routes = routes;
    }

    public bool TryGet(string operationId, [NotNullWhen(true)] out RouteDefinition? route) =>
        _routes.TryGetValue(operationId, out route);
}
