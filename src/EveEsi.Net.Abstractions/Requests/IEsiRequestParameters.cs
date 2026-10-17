using System.Text.Json.Serialization.Metadata;
using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Requests;

/// <summary>What the configurator passed to <see cref="IEsiClientFactory.CreateEsiClient{T}"/> fills in.</summary>
/// <remarks>
/// The route doesn't describe parameters, pagination or body; the generated service signatures decide what is set.
/// A missing route value still fails when the request URI is built.
/// </remarks>
public interface IEsiRequestParameters
{
    /// <summary>Path parameter values.</summary>
    IEsiParameterValues Route { get; }

    /// <summary>Query parameter values.</summary>
    IEsiParameterValues Query { get; }

    /// <summary>Sets the request body.</summary>
    /// <typeparam name="TBody">The body type.</typeparam>
    /// <param name="value">The body.</param>
    /// <param name="bodyType">Serialization metadata for the body, from the source-generated JSON context.</param>
    void Body<TBody>(TBody value, JsonTypeInfo<TBody> bodyType);

    /// <summary>Sets the <c>page</c> query value. <see langword="null"/> sends nothing.</summary>
    void ApplyPagination(int? page);

    /// <summary>Sets the <c>before</c> / <c>after</c> / <c>limit</c> query values. <see langword="null"/> sends nothing.</summary>
    void ApplyPagination(CursorPage? cursor);
}
