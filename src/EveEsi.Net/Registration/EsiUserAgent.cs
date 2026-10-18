using System.Net.Http.Headers;
using System.Reflection;
using EveEsi.Net.Routes;

namespace EveEsi.Net.Registration;

/// <summary>
/// Builds the <c>User-Agent</c>: the consumer's application and contact first, then this library and the ESI
/// compatibility date it speaks, e.g. <c>MyApp/1.2 (admin@example.com) EveEsi.Net/0.1.0 (compatibility-date 2026-08-18)</c>.
/// </summary>
internal static class EsiUserAgent
{
    public const string Product = "EveEsi.Net";

    /// <summary>This library's version, without build metadata (<c>0.1.0-alpha.0.8</c>, not <c>…+3f2a1c</c>).</summary>
    public static string LibraryVersion { get; } = ReadLibraryVersion();

    /// <exception cref="InvalidOperationException">A required value is missing or can't be sent in a User-Agent.</exception>
    public static IReadOnlyList<ProductInfoHeaderValue> Build(EsiOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApplicationName))
        {
            throw Invalid("ApplicationName is required: your application's name, e.g. \"MyApp/1.2\". CCP uses it to identify your traffic.");
        }

        if (string.IsNullOrWhiteSpace(options.Contact))
        {
            throw Invalid("Contact is required: an email address, website, EVE character or Discord handle CCP can reach you at.");
        }

        if (!ProductInfoHeaderValue.TryParse(options.ApplicationName, out var application) || application.Product is null)
        {
            throw Invalid($"ApplicationName \"{options.ApplicationName}\" isn't a User-Agent product token. Use \"Name\" or \"Name/Version\" without spaces.");
        }

        // A comment can nest parentheses, but a stray one would end it early or swallow what follows.
        if (options.Contact.AsSpan().IndexOfAny('(', ')') >= 0
            || !ProductInfoHeaderValue.TryParse($"({options.Contact.Trim()})", out var contact))
        {
            throw Invalid($"Contact \"{options.Contact}\" can't be sent in a User-Agent comment. Leave out parentheses and control characters.");
        }

        return
        [
            application,
            contact,
            new ProductInfoHeaderValue(Product, LibraryVersion),
            new ProductInfoHeaderValue($"(compatibility-date {EsiSpec.CompatibilityDate})"),
        ];
    }

    private static string ReadLibraryVersion()
    {
        var assembly = typeof(EsiUserAgent).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }

    private static InvalidOperationException Invalid(string message) => new($"AddEveEsi(): {message}");
}
