namespace EveEsi.Net;

/// <summary>What <c>AddEveEsi(o =&gt; …)</c> configures: who is calling ESI, and where.</summary>
/// <remarks>
/// <see cref="ApplicationName"/> and <see cref="Contact"/> are required. CCP uses them to reach whoever operates the
/// application, so they describe <em>your</em> application, not this library. The library adds its own name, version and
/// ESI compatibility date to the <c>User-Agent</c> after them. Middleware and cache stores are added on the builder
/// <c>AddEveEsi</c> returns.
/// </remarks>
public sealed class EsiOptions
{
    /// <summary>ESI's production address.</summary>
    public static readonly Uri DefaultBaseAddress = new("https://esi.evetech.net/");

    /// <summary>The tenant sent as <c>X-Tenant</c> unless set: the live server.</summary>
    public const string DefaultTenant = "tranquility";

    /// <summary>
    /// Your application's name, optionally with its version, as a User-Agent product token: <c>MyApp/1.2</c>.
    /// Letters, digits and <c>!#$%&amp;'*+-.^_`|~</c> only; no spaces.
    /// </summary>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// How CCP can reach the application's operator: an email address, a website, an EVE character or a Discord
    /// handle. Sent as a User-Agent comment, so it can't contain parentheses.
    /// </summary>
    public string? Contact { get; set; }

    /// <summary>The ESI address. Defaults to <see cref="DefaultBaseAddress"/>.</summary>
    public Uri BaseAddress { get; set; } = DefaultBaseAddress;

    /// <summary>The server to call, sent as <c>X-Tenant</c>. Defaults to <see cref="DefaultTenant"/>.</summary>
    public string Tenant { get; set; } = DefaultTenant;
}
