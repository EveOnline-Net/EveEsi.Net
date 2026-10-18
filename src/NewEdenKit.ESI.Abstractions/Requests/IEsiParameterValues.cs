namespace NewEdenKit.ESI.Abstractions.Requests;

/// <summary>Route or query values for one request. <see cref="IEsiRequestParameters"/> has one of each.</summary>
/// <remarks>
/// Names are the wire names, from the generated <c>Parameter.Route</c> / <c>Parameter.Query</c> constants.
/// Values are converted to strings by the client (invariant culture; an array becomes one pair per item).
/// A <see langword="null"/> value means "not sent", which is how optional query parameters are left out.
/// A missing route value or an unknown name is caught when the request is built.
/// </remarks>
public interface IEsiParameterValues
{
    /// <summary>Sets a value, or leaves it out when <paramref name="value"/> is <see langword="null"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The wire name, e.g. <c>Parameter.Route.CharacterId</c>.</param>
    /// <param name="value">The value, or <see langword="null"/> to send nothing.</param>
    /// <returns>This instance, for chaining.</returns>
    /// <exception cref="ArgumentException">The endpoint has no such parameter in this location.</exception>
    IEsiParameterValues Set<T>(string name, T? value)
        where T : notnull;

    /// <summary>Sets a value-type value, or leaves it out when <paramref name="value"/> is <see langword="null"/>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="name">The wire name, e.g. <c>Parameter.Query.TypeId</c>.</param>
    /// <param name="value">The value, or <see langword="null"/> to send nothing.</param>
    /// <returns>This instance, for chaining.</returns>
    /// <exception cref="ArgumentException">The endpoint has no such parameter in this location.</exception>
    IEsiParameterValues Set<T>(string name, T? value)
        where T : struct;
}
