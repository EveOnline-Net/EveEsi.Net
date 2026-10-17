namespace EveEsi.Net.Generator;

/// <summary>
/// The spec or config contains something the generator can't map without guessing. Generation stops; the fix belongs
/// in <c>generator.json</c> (or the generator), never in loosened checks.
/// </summary>
internal sealed class GenerationException(string message) : Exception(message);
