using EveEsi.Net.Abstractions.Routing;

namespace EveEsi.Net.Abstractions.Requests;

/// <summary>What the configurator passed to <see cref="IEsiClientFactory.CreateEsiClient{T}"/> fills in.</summary>
public interface IEsiRequestParameters
{
    /// <summary>Path parameter values.</summary>
    IEsiParameterValues Route { get; }

    /// <summary>Query parameter values.</summary>
    IEsiParameterValues Query { get; }

    /// <summary>Sets the request body. It's serialized with the endpoint's source-generated type info.</summary>
    /// <typeparam name="TBody">The body type. Must match the endpoint's body type.</typeparam>
    /// <param name="value">The body.</param>
    /// <exception cref="ArgumentException">The endpoint takes no body, or <typeparamref name="TBody"/> doesn't match it.</exception>
    void Body<TBody>(TBody value);

    /// <summary>Sets the page of a page-based endpoint. <see langword="null"/> sends no <c>page</c> parameter.</summary>
    /// <exception cref="InvalidOperationException">The endpoint isn't page-based.</exception>
    void ApplyPagination(int? page);

    /// <summary>Sets the cursor of a cursor-based endpoint. <see langword="null"/> sends no cursor parameters.</summary>
    /// <exception cref="InvalidOperationException">The endpoint isn't cursor-based.</exception>
    void ApplyPagination(CursorPage? cursor);
}
