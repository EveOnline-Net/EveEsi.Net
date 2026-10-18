namespace NewEdenKit.ESI.Abstractions.Results;

/// <summary>Why a call failed: the ESI error body, or a transport or deserialization failure.</summary>
public sealed record EsiError
{
    /// <summary>Creates an error.</summary>
    /// <param name="kind">What caused the failure.</param>
    /// <param name="message">A human-readable message; ESI's <c>error</c> field for HTTP errors.</param>
    public EsiError(EsiErrorKind kind, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Kind = kind;
        Message = message;
    }

    /// <summary>What caused the failure.</summary>
    public EsiErrorKind Kind { get; }

    /// <summary>A human-readable message; ESI's <c>error</c> field for HTTP errors.</summary>
    public string Message { get; }

    /// <summary>Individual issues from ESI's <c>details</c> list. Empty when ESI sent none.</summary>
    public IReadOnlyList<EsiErrorDetail> Details { get; init; } = [];

    /// <summary>The exception behind a transport or deserialization failure.</summary>
    public Exception? Exception { get; init; }

    /// <summary>The raw response body, when it couldn't be read as an ESI error.</summary>
    public string? RawBody { get; init; }
}
