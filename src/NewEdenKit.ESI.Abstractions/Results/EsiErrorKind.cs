namespace NewEdenKit.ESI.Abstractions.Results;

/// <summary>What caused a failed <see cref="EsiResult{T}"/>.</summary>
public enum EsiErrorKind
{
    /// <summary>ESI (or middleware acting for it) returned an error status.</summary>
    Http,

    /// <summary>No response was received: DNS, connection, TLS or timeout failure.</summary>
    Transport,

    /// <summary>A successful response's body couldn't be deserialized to the expected type.</summary>
    Deserialization,
}
