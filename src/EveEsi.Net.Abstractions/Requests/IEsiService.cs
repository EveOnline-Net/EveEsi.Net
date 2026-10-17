using EveEsi.Net.Abstractions.Middleware;

namespace EveEsi.Net.Abstractions.Requests;

/// <summary>
/// A section service (<c>ICharacters</c>, <c>IAssets</c>, …). Services only build requests. Middleware added with
/// <c>Use</c> applies to requests the instance builds afterwards.
/// </summary>
/// <typeparam name="TSelf">The service interface, so <c>Use</c> keeps it for chaining.</typeparam>
public interface IEsiService<out TSelf> : IEsiMiddlewareTarget<TSelf>
    where TSelf : IEsiService<TSelf>
{
}
