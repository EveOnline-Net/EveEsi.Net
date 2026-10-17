namespace EveEsi.Net.Abstractions.Routing;

/// <summary>Whether an endpoint takes a request body (<c>requestBody</c> in the spec).</summary>
public enum BodyRequirement
{
    /// <summary>The endpoint takes no body.</summary>
    None,

    /// <summary>A body may be sent.</summary>
    Optional,

    /// <summary>A body must be sent.</summary>
    Required,
}
