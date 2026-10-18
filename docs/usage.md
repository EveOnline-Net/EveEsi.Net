# Using NewEdenKit.ESI

How to call EVE Online's ESI API from an application with NewEdenKit.ESI. Written for developers and for AI coding
assistants: every example below is compiled and checked in CI (`tests/NewEdenKit.ESI.Tests/Docs/UsageExamples.cs`),
so it matches the current API.

## Contents
1. [Packages](#packages)
2. [Register the client](#register-the-client)
3. [Find the method for an endpoint](#find-the-method-for-an-endpoint)
4. [Make a call](#make-a-call)
5. [Handle the result](#handle-the-result)
6. [Pagination](#pagination)
7. [Request bodies](#request-bodies)
8. [Caching](#caching)
9. [Middleware](#middleware)
10. [Types](#types)
11. [Testing code that uses NewEdenKit.ESI](#testing-code-that-uses-newedenkitesi)
12. [Rules and common mistakes](#rules-and-common-mistakes)

## Packages
| Package | Install it when |
|---|---|
| `NewEdenKit.ESI` | Always. Services, models, `AddEveEsi()`, `EnableCaching()`, `CacheMiddleware`, `ETagMiddleware`, in-memory and file stores: one package. |
| `NewEdenKit.ESI.Abstractions` | Comes with `NewEdenKit.ESI`. Reference it alone only for a library that adds middleware or a cache store. |

The namespaces still follow the internal assemblies (`NewEdenKit.ESI.Services`, `NewEdenKit.ESI.Models.<Tag>`,
`NewEdenKit.ESI.Middleware.Caching`, `NewEdenKit.ESI.Abstractions.*`), but they all come from those two packages.

Targets .NET 8, 9 and 10. Trim and AOT compatible. Uses `Microsoft.Extensions.DependencyInjection`.

## Register the client
Register once, at startup. `ApplicationName` and `Contact` are required: CCP uses them to reach whoever runs the
application, so they describe **your** app, not this library.

<!-- snippet: register -->
```csharp
services.AddEveEsi(o =>
    {
        o.ApplicationName = "MyApp/1.2";      // your app, sent in the User-Agent (required)
        o.Contact = "admin@example.com";      // how CCP can reach you (required)
    })
    .EnableCaching();                         // optional: response cache + ETags, in a local folder
```

`AddEveEsi` validates its options immediately; a missing or malformed `ApplicationName`/`Contact` throws
`InvalidOperationException` at startup. It returns a builder for everything else:

<!-- snippet: register-options -->
```csharp
services.AddEveEsi(o =>
    {
        o.ApplicationName = "MyApp/1.2";
        o.Contact = "admin@example.com";
        o.Tenant = "singularity";             // default "tranquility"
    })
    .EnableCaching(c => c.AddInMemoryCacheStore(store => store.MaxEntries = 50_000))
    .Use<AcceptLanguageMiddleware>()          // global middleware, runs for every call
    .ConfigureHttpClient(http => http.SetHandlerLifetime(TimeSpan.FromMinutes(5)));
```

- `ApplicationName` is a User-Agent product token: `Name` or `Name/Version`, no spaces.
- `Contact` is an email address, website, EVE character or Discord handle, without parentheses.
- The library sends `User-Agent: MyApp/1.2 (admin@example.com) NewEdenKit.ESI/<version> (compatibility-date <date>)`,
  `X-Compatibility-Date` and `X-Tenant` on every call. Don't set them yourself.
- Call `AddEveEsi` once per service collection; a second call throws.

## Find the method for an endpoint
- There is one service interface per ESI tag, in `NewEdenKit.ESI.Services`: `IAlliance`, `IAssets`, `ICharacter`,
  `IContracts`, `IMarket`, `IUniverse`, `IWallet`, … Inject the interface; services are transient.
- Each ESI operation is one method, **named after its ESI `operationId`**: `GET /characters/{character_id}/assets` is
  `IAssets.GetCharactersCharacterIdAssets`, `GET /markets/{region_id}/orders` is `IMarket.GetMarketsRegionIdOrders`.
- Every method's XML doc `<remarks>` starts with the HTTP method and path and names the required scope, e.g.
  `GET /characters/{character_id}/assets. … Requires the esi-assets.read_assets.v1 scope.` Search the docs (or
  IntelliSense) for the path to find the method.
- Parameters, in order: path values, required query values, the request body, `string token` (secured endpoints
  only), optional query values (`null` = not sent), then `int? page` or `CursorPage? cursor`.
- Response models live in `NewEdenKit.ESI.Models.<Tag>` (e.g. `NewEdenKit.ESI.Models.Assets`); shared types such as `Cursor`
  are in `NewEdenKit.ESI.Models`.

## Make a call
A service method only **builds** a request (`IEsiRequest<T>`); nothing is sent until `ExecuteAsync`.

<!-- snippet: public-call -->
```csharp
public static async Task<string> CharacterName(ICharacter characters, long characterId, CancellationToken ct)
{
    var result = await characters.GetCharactersDetail(characterId).ExecuteAsync(ct);

    return result.IsSuccess
        ? result.Value.Name
        : $"lookup failed: {result}";
}
```

Secured endpoints take the SSO access token as a `string`. Getting and refreshing tokens (EVE SSO / OAuth2) is up
to your application; the library only sends the token as `Authorization: Bearer`.

<!-- snippet: secured-call -->
```csharp
public static async Task<double?> WalletBalance(IWallet wallet, long characterId, string accessToken, CancellationToken ct)
{
    // Secured endpoints take the SSO access token as a plain string; its scopes must include the endpoint's.
    var result = await wallet.GetCharactersCharacterIdWallet(characterId, accessToken).ExecuteAsync(ct);
    return result.IsSuccess ? result.Value : null;
}
```

- A secured method called with a null, empty or whitespace token throws `ArgumentException` before anything is
  sent. A null required string, list or body throws `ArgumentNullException`.
- A token without the endpoint's scope gets ESI's `403`, as a failed result.

## Handle the result
`ExecuteAsync` returns `EsiResult<T>` and **never throws for HTTP errors, timeouts or bad JSON**. Only cancellation
(`OperationCanceledException`) and programmer errors (bad arguments) throw.

<!-- snippet: results -->
```csharp
var result = await characters.GetCharactersDetail(characterId).ExecuteAsync(ct);

if (result.IsSuccess)
{
    return result.Value.Name;                 // Value is non-null when IsSuccess is true
}

return result.Error!.Kind switch
{
    EsiErrorKind.Http when result.StatusCode == HttpStatusCode.NotFound => "no such character",
    EsiErrorKind.Http => $"ESI said {(int?)result.StatusCode}: {result.Error.Message}",
    EsiErrorKind.Transport => "ESI couldn't be reached; try again later",
    EsiErrorKind.Deserialization => "ESI sent something this library version can't read",
    _ => result.ToString(),
};
```

| Member | Meaning |
|---|---|
| `IsSuccess` | 2xx with a readable body. `Value` is non-null exactly when this is true. |
| `Value` | The decoded response. Endpoints without a body return `EsiNoContent`. |
| `StatusCode` | The HTTP status, or `null` when no response arrived (`Transport` errors). |
| `Error` | `Kind` (`Http`, `Transport`, `Deserialization`), `Message` (ESI's `error` text), `Details`, `Exception`, `RawBody`. |
| `Headers` | Typed response headers: `Expires`, `ETag`, `Pages`, `ErrorLimitRemain`, `RateLimitRemaining`, `RequestId`, … |
| `IsNotModified` | A `304` without a value. Only seen if you send `If-None-Match` yourself without `ETagMiddleware`. |

## Pagination
**Page-based** endpoints (the method has `int? page`): request page 1, read the total from `Headers.Pages`
(`X-Pages`), then request the rest.

<!-- snippet: page-pagination -->
```csharp
public static async Task<List<CharactersCharacterIdAssetsGetItem>> AllAssets(
    IAssets assets, long characterId, string accessToken, CancellationToken ct)
{
    var all = new List<CharactersCharacterIdAssetsGetItem>();
    for (var page = 1; ; page++)
    {
        var result = await assets.GetCharactersCharacterIdAssets(characterId, accessToken, page).ExecuteAsync(ct);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Assets page {page} failed: {result}");
        }

        all.AddRange(result.Value);
        if (page >= (result.Headers.Pages ?? 1))    // X-Pages: the total page count
        {
            return all;
        }
    }
}
```

**Cursor-based** endpoints (the method has `CursorPage? cursor`): the first call without a cursor returns the most
recent records and a `Cursor` with `Before` / `After` tokens. Pass `new CursorPage(Before: …)` to walk back in time,
`new CursorPage(After: …)` later to fetch only what's new, and `Limit` to set the page size.

<!-- snippet: cursor-pagination -->
```csharp
public static async Task<List<FreelanceJobsDetailFreelancejob>> AllFreelanceJobs(IFreelanceJobs jobs, CancellationToken ct)
{
    var all = new List<FreelanceJobsDetailFreelancejob>();
    CursorPage? cursor = null;                    // null: the most recent records
    while (true)
    {
        var result = await jobs.GetFreelanceJobsListing(cursor: cursor).ExecuteAsync(ct);
        if (!result.IsSuccess || result.Value.FreelanceJobs.Count == 0)
        {
            return all;
        }

        all.AddRange(result.Value.FreelanceJobs);
        if (result.Value.Cursor?.Before is not { } before)
        {
            return all;
        }

        cursor = new CursorPage(Before: before);  // walk backwards in time
    }
}
```

## Request bodies
POST and PUT endpoints take the body as a typed parameter (often an `IReadOnlyList<long>` of IDs):

<!-- snippet: body -->
```csharp
var result = await universe.PostUniverseNames([2_112_000_000, 98_000_001, 30_000_142]).ExecuteAsync(ct);
```

## Caching
`EnableCaching()` turns on two middleware with a shared store:
- `CacheMiddleware` answers from the cache while ESI says a response is fresh (`Cache-Control`/`Expires`), without
  calling ESI;
- `ETagMiddleware` revalidates stale entries with `If-None-Match`; a `304` comes back to you as a normal success with
  the stored value.

Stores: `c.AddFileCacheStore("path")` (survives restarts; the default, in a per-app folder under local application
data, when no store is chosen) or `c.AddInMemoryCacheStore()`. `c.DisableResponseCache()` / `c.DisableETags()` leave
one middleware out. Secured responses are cached per character (a hash of the token's subject), never shared, and the
raw token is never stored. Only GET requests are cached.

### Your own cache store
To keep the cache anywhere else (Redis, a database, a cache shared by several instances), implement `IEsiCacheStore`
and choose it with `UseStore<TStore>()` (or `UseStore(sp => …)` to build it yourself). A store only keeps raw entries; the middleware decides what is fresh and what to
revalidate. If your storage is unreachable, return `null` from `GetAsync` and skip the write, so calls go to ESI
instead of failing.

<!-- snippet: register-custom-store -->
```csharp
services.AddEveEsi(o =>
    {
        o.ApplicationName = "MyApp/1.2";
        o.Contact = "admin@example.com";
    })
    .EnableCaching(c => c.UseStore<MyCacheStore>());   // a singleton; its constructor can take registered services
```

<!-- snippet: custom-store -->
```csharp
/// <summary>
/// A cache store over your own storage. The dictionary stands in for it: in a real store, write the entry (status,
/// headers, body, ExpiresAt, RetainUntil) to Redis, a database, … and drop it after RetainUntil (or a retention you
/// choose past ExpiresAt). Shared by concurrent calls; keys never contain an access token.
/// </summary>
public sealed class MyCacheStore : IEsiCacheStore
{
    private readonly ConcurrentDictionary<string, EsiCacheEntry> _storage = new();

    public ValueTask<EsiCacheEntry?> GetAsync(string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_storage.TryGetValue(key, out var entry) ? entry : null);

    public ValueTask SetAsync(string key, EsiCacheEntry entry, CancellationToken cancellationToken)
    {
        _storage[key] = entry;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        _storage.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }
}
```

## Middleware
Middleware sees every call as an `EsiContext`: `Route` (operation ID, scope, cache hints, rate-limit group), `Request`
(route and query values, headers, body), `AccessToken`, `Response` after `next`, and `Items` for per-call state. It
runs in three levels, outermost first: **global** (the builder) → **service** (one service instance) → **request**
(one call). Each level accepts an `IEsiMiddleware` or an async callback.

One call:

<!-- snippet: request-middleware -->
```csharp
var result = await status.GetStatus()
    .Use(async (context, next) =>
    {
        context.Request.Headers["Accept-Language"] = "de";    // before the call
        await next(context);
        var remaining = context.Response?.Headers.ErrorLimitRemain;   // after the call
    })
    .ExecuteAsync(ct);
```

One service instance:

<!-- snippet: service-middleware -->
```csharp
// Services are transient: middleware added here applies to requests this instance builds afterwards.
var german = status.Use(new AcceptLanguageMiddleware());
var result = await german.GetStatus().ExecuteAsync(ct);
```

A reusable middleware, registered globally with `.Use<AcceptLanguageMiddleware>()` (resolved from DI, so it can
take constructor dependencies):

<!-- snippet: middleware-class -->
```csharp
/// <summary>Asks ESI for German names and descriptions where it has them.</summary>
public sealed class AcceptLanguageMiddleware : IEsiMiddleware
{
    public async ValueTask InvokeAsync(EsiContext context, EsiNext next)
    {
        context.Request.Headers["Accept-Language"] = "de";
        await next(context);
    }
}
```

Rules for middleware:
- Instances are shared by concurrent calls: keep no per-call state in fields; use `context.Items`.
- To answer without calling ESI, set `context.Response` and return without calling `next`.
- Call `next` at most once (retry middleware is the exception) and honour `context.CancellationToken`.
- Read endpoint facts from `context.Route`; never parse the path.

## Types
- **IDs are `long`** (ESI's `int64`), even when today's values would fit an `int`. Never narrow them.
- Types follow the spec: `date-time` is `DateTimeOffset`, `uuid` is `Guid`, lists are `IReadOnlyList<T>`, maps are
  `IReadOnlyDictionary<string, T>`.
- Models are read-only `record`s. Fields ESI always sends are `required` and non-nullable; everything else is nullable.
- Every **enum** has `Unknown = 0`, which is what a value newer than this library version reads as. Handle it.
- `oneOf` **unions** are abstract records with one nested record per variant plus `Unknown`. Pattern-match them:

<!-- snippet: unions -->
```csharp
public static string Owner(SovereigntySystemsSolarsystem system) => system.Claim switch
{
    SovereigntySystemsSolarsystem.ClaimModel.Alliance a => $"alliance {a.Value.AllianceId}",
    SovereigntySystemsSolarsystem.ClaimModel.Faction f => $"faction {f.Value.FactionId}",
    SovereigntySystemsSolarsystem.ClaimModel.Unclaimed => "unclaimed",
    SovereigntySystemsSolarsystem.ClaimModel.Unknown u => $"something new: {u.Name}",   // a variant newer than this library
    _ => throw new UnreachableException(),
};
```

## Testing code that uses NewEdenKit.ESI
Depend on the service interfaces and mock them. `EsiResult<T>.Success`, `.Failure` and `.NotModified` create
results:

<!-- snippet: testing -->
```csharp
var request = Substitute.For<IEsiRequest<ServerStatus>>();
request.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(EsiResult<ServerStatus>.Success(
    HttpStatusCode.OK,
    new ServerStatus { Players = 31_234, ServerVersion = "2345678", StartTime = DateTimeOffset.UtcNow, Vip = false },
    EsiResponseHeaders.Empty));

var status = Substitute.For<IStatus>();
status.GetStatus().Returns(request);
```

## Rules and common mistakes
- **Don't** call ESI with your own `HttpClient`; use the services, which send the required headers and handle errors.
- **Don't** wrap calls in `try/catch` for HTTP errors; check `IsSuccess`. Do pass a `CancellationToken`.
- **Don't** read `Value` without checking `IsSuccess` first.
- **Don't** store `IEsiRequest<T>` objects or service instances in singletons for later; build a request when you
  need it. Requests are cheap.
- **Don't** set `User-Agent`, `X-Compatibility-Date` or `X-Tenant`; configure `EsiOptions` instead.
- **Don't** use `int` for IDs or forget `Unknown` enum and union values.
- **Do** follow `Headers.Pages` or the cursor for paginated endpoints; a single call returns one page.
- **Do** enable caching for anything called repeatedly: ESI asks clients to respect `Expires`, and the error limit
  (`Headers.ErrorLimitRemain`) is shared by your whole application.
