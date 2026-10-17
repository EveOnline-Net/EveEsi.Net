namespace EveEsi.Net.Abstractions.Routing;

/// <summary>How an endpoint pages its results.</summary>
public enum PaginationStyle
{
    /// <summary>The endpoint isn't paginated.</summary>
    None,

    /// <summary>Page-based: <c>?page=N</c> in, <c>X-Pages</c> header out.</summary>
    Page,

    /// <summary>Cursor-based: <c>before</c> / <c>after</c> / <c>limit</c> in, a cursor object in the response body out.</summary>
    Cursor,
}
