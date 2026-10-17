using System.Text.Json.Serialization.Metadata;
using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Requests;

/// <summary>What the configurator passed to <see cref="IEsiClientFactory.CreateEsiClient{T}"/> fills in.</summary>
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
    /// <exception cref="ArgumentException">The endpoint takes no body.</exception>
    void Body<TBody>(TBody value, JsonTypeInfo<TBody> bodyType);

    /// <summary>Sets the page of a page-based endpoint. <see langword="null"/> sends no <c>page</c> parameter.</summary>
    /// <exception cref="InvalidOperationException">The endpoint isn't page-based.</exception>
    void ApplyPagination(int? page);

    /// <summary>Sets the cursor of a cursor-based endpoint. <see langword="null"/> sends no cursor parameters.</summary>
    /// <exception cref="InvalidOperationException">The endpoint isn't cursor-based.</exception>
    void ApplyPagination(CursorPage? cursor);
}
