# Architecture

## Goals
- Typed, discoverable access to every ESI endpoint, grouped by section (`ICharacters`, `ICorporations`, …).
- Stateless core: build request → run middleware pipeline → send → decode → return a result.
- Cross-cutting behaviour (ETags, caching, scope checks, language, rate limits, retries) is **middleware**,
  never hard-wired into the client.
- Easy to maintain as ESI changes: everything spec-shaped is generated (see [generator.md](generator.md)).

## Solution layout

```
EveEsi.Net.slnx
Directory.Build.props        shared settings: TFMs, nullable, warnings-as-errors, AOT/trim flags, docs
Directory.Packages.props     central package versions
generator.json               generator config (naming, enums, unions, overrides)
openapi.json                 pinned ESI spec (do not read whole; query it)

src/
  EveEsi.Net.Abstractions/   contracts shared by everything (hand-written)
  EveEsi.Net.Models/         DTOs, enums, unions, converters, JsonSerializerContext (generated + partials)
  EveEsi.Net.Routes/         Operation (operation ID constants), EsiRoutes table, Parameter catalogue (all generated)
  EveEsi.Net.Client/         client factory, request parameters, pipeline runner, HTTP execution (hand-written)
  EveEsi.Net.Middleware/     standard middleware + in-memory IEsiCacheStore (hand-written)
  EveEsi.Net/                section services ICharacters… (generated) + AddEveEsi() DI extensions
  EveEsi.Net.Caching.Distributed/   IEsiCacheStore over IDistributedCache    (separate package)
  EveEsi.Net.Caching.Redis/         IEsiCacheStore over StackExchange.Redis  (separate package)

tools/
  EveEsi.Net.Generator/      spec → code console tool (not shipped)

tests/
  EveEsi.Net.<Project>.Tests/    one test project per src project
  EveEsi.Net.Generator.Tests/    snapshot tests for generator output
```

### Dependency rules

```
Abstractions  ← (no project deps)
Models        ← (no project deps)
Routes        → Abstractions, Models
Client        → Abstractions
Middleware    → Abstractions
EveEsi.Net    → Abstractions, Models, Routes, Client, Middleware
Caching.*     → Abstractions
```
- Nothing references `EveEsi.Net` (the top package) except tests and consumers.
- `Client` and `Middleware` never reference `Models` or `Routes`. They work with `RouteDefinition`
  metadata and raw responses only.
- Adapters with third-party dependencies (Redis, distributed cache, …) always go in their own `Caching.*` package.

## Key abstractions (EveEsi.Net.Abstractions)

| Type | Purpose |
|---|---|
| `RouteDefinition` | Static, immutable description of one endpoint: operation ID, method, path template, scope (`string?`), cache hints, rate-limit policy. Nothing about parameters, pagination, body or types. All routes live in one generated dictionary keyed by operation ID. |
| `IEsiParameterValues` | `Set(name, value)`, used for both `p.Route` and `p.Query`. Names are the generated `Parameter.*` string constants. `null` means "not sent". |
| `CursorPage` | Cursor pagination input: `Before`, `After` (strings), `Limit` (`long?`). |
| `IEsiClientFactory` | Creates a per-request `IEsiRequest<T>` from an operation ID (looked up in the route table), the response's `JsonTypeInfo<T>` (omitted for no-content endpoints, giving `IEsiRequest<EsiNoContent>`), a parameter configurator, an optional token and the calling service's middleware snapshot. |
| `IEsiRequestParameters` | What the configurator lambda receives: `Route`, `Query`, `Body`, `ApplyPagination(...)`. A `null` query value means "not sent". |
| `IEsiRequest<T>` | The built, ready-to-run request: `.Use(...)`, `.ExecuteAsync(ct)`. The only public request type; the implementation is `internal sealed` in `EveEsi.Net.Client`. Service methods and the factory always return the interface. |
| `EsiContext` | Mutable state passed through the pipeline: route, outgoing `EsiRequestMessage`, access token (`string?`, settable), `EsiResponse` (once there is one), `Items` bag, `CancellationToken`. |
| `EsiRequestMessage` | The outgoing request middleware edits: method, path template, route values and query values as string key/value pairs (a query name repeats per array item), headers, body bytes. `ToRelativeUri()` builds the escaped path + query. The client builds a fresh `HttpRequestMessage` from it on every send, so retries are safe. |
| `EsiResponse` | **Raw** response: status, `EsiResponseHeaders`, body bytes. Middleware sees and can replace this. Deserialization happens after the pipeline. |
| `EsiResponseHeaders` | Immutable, case-insensitive headers with typed access to ETag, Expires, Last-Modified, max-age, X-Pages, error-limit, rate-limit, Retry-After and request ID. |
| `IEsiMiddleware`, `EsiNext` | Middleware contract (below). |
| `IEsiCacheStore` / `EsiCacheEntry` | Required abstraction for caching middleware: get/set/remove raw `EsiResponse` entries by key, with a freshness expiry and an optional retention time (for ETag revalidation). |
| `EsiResult<T>` / `EsiError` / `EsiNoContent` | What callers get back (below). `EsiNoContent` is `T` for endpoints that return no body (204). |

**Access tokens are plain `string`s.** The consumer gets them from EVE SSO and passes them in. There is
no token wrapper type. Middleware that needs claims (e.g. scopes) decodes the JWT payload itself.

## Services: request builders

A service (`ICharacters`, `IAssets`, …) **only builds requests**. Each method turns its typed arguments into
an `IEsiRequest<T>` through `IEsiClientFactory`. It never sends anything or holds results.

```csharp
public IEsiRequest<IReadOnlyList<CharacterAsset>> CharacterAssets(long characterId, string token, int? page = null)
    => CreateEsiClient<IReadOnlyList<CharacterAsset>>(
        Operation.Assets.CharacterAssets,                         // = "GetCharactersCharacterIdAssets"
        EsiJsonContext.Default.ListOfCharacterAsset,
        p =>
        {
            p.Route.Set(Parameter.Route.CharacterId, characterId);
            p.ApplyPagination(page);
        },
        token);

public IEsiRequest<EsiNoContent> AutopilotWaypoint(long destinationId, bool addToBeginning, bool clearOtherWaypoints, string token)
    => CreateEsiClient(Operation.UserInterface.AutopilotWaypoint, p =>   // no response body: no type info
    {
        p.Query.Set(Parameter.Query.DestinationId, destinationId);
        p.Query.Set(Parameter.Query.AddToBeginning, addToBeginning);
        p.Query.Set(Parameter.Query.ClearOtherWaypoints, clearOtherWaypoints);
    }, token);
```
`CreateEsiClient` here is the `EsiService<TSelf>` base helper: it forwards to `IEsiClientFactory` with the service's
middleware snapshot. Services are generated (see [generator.md](generator.md#services)): a public `I<Tag>` interface
and an `internal sealed` `<Tag>Service` per spec tag, each method named like its `Operation` constant.

### Building blocks
- **Response type `T`**: set by the service, not the route. The service method's return type
  (`GetCharacter` → `IEsiRequest<CharacterInfo>`), the explicit `CreateEsiClient<CharacterInfo>(...)` and the
  `JsonTypeInfo<CharacterInfo>` from `EsiJsonContext` must agree, which the compiler checks. Endpoints without a
  response body use the non-generic `CreateEsiClient(...)`, which returns `IEsiRequest<EsiNoContent>`.
- **`Operation.<Tag>.<Name>`**: generated `const string` operation IDs, one nested class per spec **tag**
  (`Operation.Character.CharacterInfo = "GetCharactersDetail"`). Services and the route table always use these
  constants, never a string literal.
- **`EsiRoutes.All`**: one generated static dictionary, operation ID → `RouteDefinition` (method, path template,
  scope, cache hints, rate limit), keyed by the `Operation.*` constants. Services pass the constant; the factory looks
  the route up. Services are grouped by tag like `Route`; the dictionary itself is flat.
- **`Parameter.Route.<Name>` / `Parameter.Query.<Name>`**: `const string` wire names, generated once per
  distinct parameter in the spec (77 today). The same constant is the `Set` key, the `{placeholder}` in the route's
  path template and the entry in its query parameter list. Value types are enforced by the generated service
  signatures, which follow the spec's `format` (see [generator.md](generator.md#numeric-and-id-types-the-specs-format-is-the-only-source-of-truth));
  ESI IDs are 64-bit.
- **`p.ApplyPagination(int? page)`**: page-based routes. **`p.ApplyPagination(CursorPage? cursor)`**: cursor routes.
  `null` means "no pagination parameter sent".
- **`p.Body(value, EsiJsonContext.Default.X)`**: request body for the 22 POST/PUT operations, serialized with the type info the service passes. The route only says whether a body is allowed or required.
- **`token`**: a required `string` parameter on the service method for secured endpoints; absent on public ones.
  The factory rejects a null, empty or whitespace token for a route with a scope.
- **Guards:** the service throws `ArgumentNullException` for a null required reference argument (a string route
  value, a list, a body) before the request is built.

### Validation
Which parameters, pagination and body an endpoint takes is enforced by the **generated service signatures**,
not by the route. The factory only checks what the route knows (programmer errors, thrown inside `CreateEsiClient`):
- The operation ID isn't in the route table → `ArgumentException`.
- The endpoint has a scope and `token` is null or empty → `ArgumentException`.
- A placeholder in the path template has no route value, or a route value is set for a name the template doesn't
  have, or a route value is a list → `ArgumentException`.
- `ApplyPagination(page)` with `page < 1` → `ArgumentOutOfRangeException`.

### Lifetime and service-level middleware
- Services are registered as **transient**, by the generated `EsiServices.AddGeneratedServices` (`TryAdd`, so an app
  can register its own implementation first).
- Generated services derive from the hand-written `EsiService<TSelf>` base class (`EveEsi.Net.Services`) that holds the instance's
  service-level middleware (`characters.Use(...)`) and passes a snapshot of it to the factory with each
  `CreateEsiClient` call. That keeps the service level separate from, and outside, request-level middleware.
- Shared headers (`If-None-Match`, `Accept-Language`, `X-Compatibility-Date`, `X-Tenant`, `If-Modified-Since`)
  never appear in service signatures or in the parameter builder.

## Middleware

### Contract
```csharp
public delegate ValueTask EsiNext(EsiContext context);

public interface IEsiMiddleware
{
    ValueTask InvokeAsync(EsiContext context, EsiNext next);
}
```
A middleware can change the request before `next`, inspect or replace `context.Response` after `next`,
or **short-circuit** by setting `context.Response` without calling `next` (e.g. cache hit).

### Registering middleware: same overloads at every level
Every place that accepts middleware supports both forms:

```csharp
x.Use(new ScopeCheckMiddleware());                 // IEsiMiddleware instance
x.Use(async (ctx, next) =>                         // async callback
{
    // before
    await next(ctx);
    // after
});
```

This is guaranteed by one shared contract rather than separate implementations per level:

```csharp
public interface IEsiMiddlewareTarget<out TSelf> where TSelf : IEsiMiddlewareTarget<TSelf>
{
    TSelf Use(IEsiMiddleware middleware);          // the only member each level implements
}

public static class EsiMiddlewareTargetExtensions
{
    public static TSelf Use<TSelf>(this TSelf target, Func<EsiContext, EsiNext, ValueTask> middleware)
        where TSelf : IEsiMiddlewareTarget<TSelf>
        => target.Use(new DelegateMiddleware(middleware));   // callback → IEsiMiddleware, in one place
}
```

| Level | Target type | `Use` returns |
|---|---|---|
| Global | `IEsiBuilder` (returned by `AddEveEsi(…)`) | `IEsiBuilder` |
| Service | each service interface, e.g. `ICharacters : IEsiService<ICharacters>` | the service interface |
| Request | `IEsiRequest<T>` | `IEsiRequest<T>` |

- `Use` returns the target's own type, so chaining keeps the concrete interface
  (`characters.Use(...).GetCharacter(id)` still sees `ICharacters`).
- The callback type is `Func<EsiContext, EsiNext, ValueTask>` only. A second `Task` overload would make
  `async` lambdas ambiguous (CS0121).
- Global level only: `builder.Use<TMiddleware>()` resolves the middleware from DI, so it can take injected
  dependencies such as `IEsiCacheStore`. Service and request levels take instances or callbacks.
- Null arguments throw `ArgumentNullException`.

### The three levels

| Level | Where it is registered | Lifetime |
|---|---|---|
| **Global** | `services.AddEveEsi(…).Use(...)` or `.Use<T>()` | Whole application. Applies to every call from every service. |
| **Service** | `characters.Use(...)` on a resolved service instance | That instance. Services are **transient**, so a newly resolved `ICharacters` starts with no service-level middleware but still gets global middleware. |
| **Request** | `client.Use(...)` on an `IEsiRequest<T>` | That one call. |

**Order (outermost → innermost):** global → service → request → HTTP send.
Within a level, middleware runs in registration order.
A client takes a snapshot of its service's middleware when it's created, so calling `characters.Use(...)`
later doesn't affect clients that already exist.

### Standard middleware (EveEsi.Net.Middleware)
All opt-in. The core client sends a call without any of them.
- `ETagMiddleware`: sends `If-None-Match` from the cache store; on `304`, returns the stored body with ESI's fresh
  headers (as a 200, so the caller gets the value) and refreshes the entry. Entries with an ETag are kept past their
  expiry so they can be revalidated. A request that already has `If-None-Match` is left alone.
- `CacheMiddleware`: serves a fresh entry without calling ESI, and stores 200s for as long as they're fresh:
  `Cache-Control: max-age`, else `Expires` measured against ESI's `Date` (so local clock skew doesn't matter), else the
  route's `x-cache-age`. Not-cached routes and `no-store`/`no-cache` responses are never stored.

  Both are GET only, share entries through `EsiCacheKey` and work in either order; a call writes its entry once.
  `EsiCacheKey` covers the method, path and query (each page is its own entry), any `X-Tenant` or `Accept-Language`
  override, and on secured routes a SHA-256 hash of the token's `sub` claim (never the token), so entries survive token
  refreshes and public routes are shared between callers.
- `ScopeCheckMiddleware`: decodes the access token's `scp` claim (without validating it) and compares it
  against `RouteDefinition.Scope`. Fails before sending.
- `LanguageMiddleware`: sets `Accept-Language`.
- `ErrorLimitMiddleware` / `RateLimitMiddleware`: read ESI error-limit and rate-limit headers and back off (route carries `x-rate-limit` group).
- `RetryMiddleware`: retries transient failures (5xx, 420/429 with backoff).

Turn both on with `.EnableCaching(c => …)` on the builder (below). It adds `CacheMiddleware` then `ETagMiddleware` at
that point in the global chain (`c.DisableResponseCache()` / `c.DisableETags()` leave one out) and registers the store
chosen in `c`. With no store chosen it uses a `FileEsiCacheStore` in `FileEsiCacheStoreOptions.DefaultDirectory`
(`EveEsi.Net/<entry assembly>/cache` in the user's local application data). Choosing two stores, calling it twice or
turning both middleware off fails at startup; an `IEsiCacheStore` the app registered itself wins. `Use<CacheMiddleware>()`
still works for a custom order, with the app registering the store.

The in-memory `IEsiCacheStore` ships here (`c.AddInMemoryCacheStore(o => o.MaxEntries = …)`): bounded by
`MaxEntries` (expired entries go first, then those due soonest) and `DefaultRetention` (how long past expiry an entry
without its own `RetainUntil` is kept). `FileEsiCacheStore` (`c.AddFileCacheStore("cache/esi")`) keeps entries in a local folder across
restarts and between processes on one machine: one file per entry named by a hash of its key, written to a temporary
file and renamed into place, with the file's last-write time set to when it may be dropped so sweeps (every
`SweepInterval`, on writes) need only the folder listing. Unreadable files are misses. Stores with third-party
dependencies ship as `EveEsi.Net.Caching.*` packages. Both middleware take an
optional `TimeProvider`, resolved from DI when registered.

### What is *not* middleware: service registration
The consumer registers the library once, identifying **their** application. The consumer is the user agent, not the library:

```csharp
services.AddEveEsi(o =>
    {
        o.ApplicationName = "MyApp/1.2";        // required
        o.Contact = "admin@example.com";        // required: email, website, EVE character or Discord
    })
    .EnableCaching(c => c.AddFileCacheStore("cache/esi"))   // caching middleware + store (optional)
    .Use<MyMiddleware>()                        // more global middleware, in order (optional)
    .ConfigureHttpClient(http => http.AddHttpMessageHandler<LoggingHandler>());   // optional
```

`AddEveEsi()` returns an **`IEsiBuilder`** (in Abstractions), so everything that belongs to the ESI client is
registered in one chain. `EsiOptions` holds only configuration (identity, base address, tenant). The builder is the
global middleware target (`Use(instance)`, `Use(callback)`, `Use<T>()`), and each package adds its own extensions to
it: `EveEsi.Net.Middleware` adds `EnableCaching`, whose `IEsiCacheBuilder` (also in Abstractions) takes
`AddInMemoryCacheStore` / `AddFileCacheStore`; the `EveEsi.Net.Caching.*` packages add their stores to that builder the
same way, through `IEsiCacheBuilder.UseStore`. `ConfigureHttpClient` exposes the typed client's `IHttpClientBuilder` (named
`EveEsi.Net`) for message handlers, a proxy or a timeout; ESI-aware behaviour (retries, caching, rate limits) belongs in
middleware instead. Add everything before the service provider is built: the global middleware list is read when it's
first resolved.

`AddEveEsi()` configures the typed client's `HttpClient` through `AddHttpClient<EsiHttpClient>(...)`. That is the only
place the ESI headers are set; no service or client constructor touches them:
- `BaseAddress`: `https://esi.evetech.net/` (`EsiOptions.BaseAddress` to override).
- `User-Agent`: the consumer's application and contact, then the library's product token and the compatibility date
  it speaks: `MyApp/1.2 (admin@example.com) EveEsi.Net/0.1.0 (compatibility-date 2026-08-18)`. CCP asks for this so
  they can contact the app's operator; the library part tells them which client and spec the traffic comes from. The
  version is the package version without build metadata.
- `X-Compatibility-Date`: **not configurable**. A generated constant, `EsiSpec.CompatibilityDate`, taken from the spec
  the library was generated from. A library version speaks exactly one compatibility date.
- `X-Tenant`: `EsiOptions.Tenant`, default `tranquility`.

The configure delegate runs once, inside `AddEveEsi()`, and the options are validated there, so a misconfiguration
fails at startup rather than on the first call (`InvalidOperationException`):
- `ApplicationName` or `Contact` missing;
- `ApplicationName` not a User-Agent product token (`Name` or `Name/Version`, no spaces);
- `Contact` containing parentheses or control characters (it's sent as a comment, so it could otherwise inject text);
- a relative `BaseAddress` or an empty `Tenant`;
- a second `AddEveEsi()` on the same collection.

What it registers:
- `AddHttpClient<EsiHttpClient>("EveEsi.Net", …)` with the address and headers above.
- The route table (`EsiRoutes.All`) and the global middleware as singletons. `.Use<T>()` registers `T` as a singleton
  (`TryAdd`, so an app's own registration wins) and resolves it when the global middleware is first needed.
- `IEsiClientFactory` and every generated service as transient (`TryAdd`).

`AddEveEsi` and the builder extensions live in the `Microsoft.Extensions.DependencyInjection` namespace, as
registration extensions conventionally do; `EsiOptions` is in `EveEsi.Net` and `IEsiBuilder` in
`EveEsi.Net.Abstractions.Registration`.

`Authorization: Bearer <token>` is the one per-request header the client adds itself, from `context.AccessToken` at
send time. Default headers apply only when the request doesn't set the same header, so middleware **can** override
them via `context.Request.Headers` (e.g. a different `X-Tenant` for one call).

### Client internals (EveEsi.Net.Client)
`EveEsi.Net.Client` has no public types. The factory, request, pipeline, decoder, route table and typed HTTP client are
`internal sealed`; `EveEsi.Net` wires them up through `InternalsVisibleTo`.
- **`EsiHttpClient`** is a typed client (`AddHttpClient<EsiHttpClient>`). It is the end of every pipeline: it builds a
  fresh `HttpRequestMessage` from the `EsiContext` (relative URI, middleware headers, body, bearer token), sends it and
  stores the raw `EsiResponse` on the context. It configures nothing itself.
- **Lifetimes:** typed clients are transient, so the factory that holds one is registered **transient** too (like the
  services). Requests hold it only for their own short life, so the handler still rotates.

Outcomes:
- 2xx → value decoded with the service's `JsonTypeInfo<T>` (or `EsiNoContent`); empty, `null` or invalid JSON → `Deserialization`.
- 304 → `NotModified`. Other statuses → `Http`, with ESI's `{"error", "details"}` body parsed (non-JSON bodies kept as `RawBody`).
- `HttpRequestException`, `IOException` and `HttpClient` timeouts → `Transport`. Only the caller's own cancellation throws.
- A middleware that neither calls `next` nor sets `context.Response` → `InvalidOperationException` (a bug, not a result).

## Pagination
ESI has two styles, and the library leaves the looping to the caller:
- **Page-based** (`?page=N`, response header `X-Pages`, 40 operations): `p.ApplyPagination(int? page)`;
  `X-Pages` is exposed on the result headers.
- **Cursor-based** (`before` / `after` / `limit`, `Cursor` object in the body, 12 operations):
  `p.ApplyPagination(CursorPage? cursor)`; the next cursor is part of the response DTO.

Which style an endpoint uses is decided by its generated service signature (`int? page` or `CursorPage? cursor`);
the route doesn't record it.

## Result type

```csharp
public sealed class EsiResult<T>
{
    public bool IsSuccess { get; }
    public bool IsNotModified { get; }     // 304 that no middleware resolved
    public HttpStatusCode? StatusCode { get; } // null when no response was received (transport failure)
    public T? Value { get; }
    public EsiError? Error { get; }        // ESI error body, or transport/deserialization failure
    public EsiResponseHeaders Headers { get; }  // ETag, Expires, Last-Modified, X-Pages, error/rate-limit, raw access
}
```
- HTTP error statuses → `IsSuccess == false` with `Error`. **No exceptions.**
- Network/transport failures → failed result, `Error.Kind = Transport`, `StatusCode == null`, with the exception attached.
- Successful responses that can't be decoded → failed result, `Error.Kind = Deserialization`.
- Created through `EsiResult<T>.Success / NotModified / Failure`, so consumers can build results in their own tests.
- Exceptions are thrown only for cancellation (`OperationCanceledException`) and programmer errors
  (the build-time validation above).

## Request flow

```
service.GetX(args, token)
  → factory.CreateEsiClient<T>("OperationId", EsiJsonContext.Default.T, p => {...}, token, serviceMiddleware)
       look up EsiRoutes.All[operationId], check token vs scope, run configurator  → IEsiRequest<T> (+ service middleware snapshot)
  .Use(...)                       → adds request middleware
  .ExecuteAsync(ct)
     build EsiContext (URL from template + route values, query string, body, required headers, bearer token)
     run pipeline: global → service → request → [HTTP send: fresh HttpRequestMessage from context.Request]
     context.Response (raw)  →  deserialize with the JsonTypeInfo<T> the service passed  →  EsiResult<T>
```
