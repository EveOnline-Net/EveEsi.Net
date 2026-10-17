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
  EveEsi.Net.Routes/         RouteBuilder (hand-written) + Endpoints and Parameter catalogues (generated)
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
| `RouteDefinition` / `RouteDefinition<TResponse>` | Static, immutable description of one endpoint: method, path template, route/query parameters, body type, required scopes, required roles, cache hints, rate-limit group, pagination style, response type. |
| `RouteParameter<T>` / `QueryParameter<T>` | Typed parameter keys (wire name + CLR type). Generated into the `Parameter` catalogue. |
| `CursorPage` | Cursor pagination input: `Before`, `After` (strings), `Limit` (`long?`). |
| `IEsiClientFactory` | Creates a per-request `IEsiRequest<T>` from an endpoint, a parameter configurator and an optional token. |
| `IEsiRequestParameters` | What the configurator lambda receives: `Route`, `Query`, `Body`, `ApplyPagination(...)`. |
| `IEsiRequest<T>` | The built, ready-to-run request: `.Use(...)`, `.ExecuteAsync(ct)`. The only public request type; the implementation is `internal sealed` in `EveEsi.Net.Client`. Service methods and the factory always return the interface. |
| `EsiContext` | Mutable state passed through the pipeline: route, resolved parameters, outgoing `HttpRequestMessage`, access token (`string?`), `EsiResponse` (once there is one), `Items` bag, `CancellationToken`. |
| `EsiResponse` | **Raw** response: status, headers, body bytes. Middleware sees and can replace this. Deserialization happens after the pipeline. |
| `IEsiMiddleware`, `EsiNext` | Middleware contract (below). |
| `IEsiCacheStore` | Required abstraction for caching middleware: get/set raw `EsiResponse` entries by key with expiry. |
| `EsiResult<T>` | What callers get back (below). |

**Access tokens are plain `string`s.** The consumer gets them from EVE SSO and passes them in. There is
no token wrapper type. Middleware that needs claims (e.g. scopes) decodes the JWT payload itself.

## Services: request builders

A service (`ICharacters`, `IAssets`, …) **only builds requests**. Each method turns its typed arguments into
an `IEsiRequest<T>` through `IEsiClientFactory`. It never sends anything or holds results.

```csharp
public IEsiRequest<IReadOnlyList<CharacterAsset>> GetCharacterAssets(long characterId, string token, int? page = null)
    => factory.CreateEsiClient<IReadOnlyList<CharacterAsset>>(Endpoints.Assets.CharacterAssets, p =>
    {
        p.Route.Set(Parameter.Route.CharacterId, characterId);
        p.ApplyPagination(page);
    }, token);

public IEsiRequest<SkinrListings> GetParagonHubSkinr(long characterId, string token, CursorPage? cursor = null)
    => factory.CreateEsiClient<SkinrListings>(Endpoints.ParagonHub.CharacterSkinr, p =>
    {
        p.Route.Set(Parameter.Route.CharacterId, characterId);
        p.ApplyPagination(cursor);
    }, token);
```

### Building blocks
- **Response type `T`**: set by the service method's return type (`GetCharacter` → `IEsiRequest<CharacterInfo>`)
  and passed **explicitly** to the factory: `CreateEsiClient<CharacterInfo>(...)`. The factory signature is
  `CreateEsiClient<T>(RouteDefinition<T> endpoint, …)`, so the compiler still rejects an endpoint whose response
  type doesn't match.
- **`Endpoints.<Tag>.<Name>`**: static `RouteDefinition<T>` fields, one nested class per spec **tag**
  (`Endpoints.Assets`, `Endpoints.Character`, …), generated and built once through `RouteBuilder`.
  Services follow the same grouping: one service per tag.
- **`Parameter.Route.<Name>` / `Parameter.Query.<Name>`**: static, typed parameter keys, generated once per
  distinct parameter in the spec (77 today). `p.Route.Set(Parameter.Route.CharacterId, "x")` doesn't compile,
  because `CharacterId` is `RouteParameter<long>`. ESI IDs are 64-bit; types always follow the spec's
  `format` (see [generator.md](generator.md#numeric-and-id-types-the-specs-format-is-the-only-source-of-truth)).
- **`p.ApplyPagination(int? page)`**: page-based routes. **`p.ApplyPagination(CursorPage? cursor)`**: cursor routes.
  `null` means "no pagination parameter sent".
- **`p.Body(value)`**: request body for the 22 POST/PUT operations, serialized with `EsiJsonContext`.
- **`token`**: `string?`. Required (non-nullable parameter on the service method) for secured endpoints;
  omitted for public ones.

### Validation (programmer errors throw at build time, inside `CreateEsiClient`)
- A required route parameter is missing, or a parameter doesn't belong to the endpoint → `ArgumentException`.
- The pagination type doesn't match the route's `PaginationStyle` → `InvalidOperationException`.
- The endpoint requires scopes and `token` is null or empty → `ArgumentException`.
- A body is given to an endpoint without a body, or a required body is missing → `ArgumentException`.

### Lifetime and service-level middleware
- Services are registered as **transient**.
- Generated services derive from a hand-written `EsiService` base class that holds the instance's
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
| Global | `EsiOptions` (in `AddEveEsi(o => …)`) | `EsiOptions` |
| Service | each service interface, e.g. `ICharacters : IEsiService<ICharacters>` | the service interface |
| Request | `IEsiRequest<T>` | `IEsiRequest<T>` |

- `Use` returns the target's own type, so chaining keeps the concrete interface
  (`characters.Use(...).GetCharacter(id)` still sees `ICharacters`).
- The callback type is `Func<EsiContext, EsiNext, ValueTask>` only. A second `Task` overload would make
  `async` lambdas ambiguous (CS0121).
- Global level only: `o.Use<TMiddleware>()` resolves the middleware from DI, so it can take injected
  dependencies such as `IEsiCacheStore`. Service and request levels take instances or callbacks.
- Null arguments throw `ArgumentNullException`.

### The three levels

| Level | Where it is registered | Lifetime |
|---|---|---|
| **Global** | `services.AddEveEsi(o => o.Use(...))` or `o.Use<T>()` | Whole application. Applies to every call from every service. |
| **Service** | `characters.Use(...)` on a resolved service instance | That instance. Services are **transient**, so a newly resolved `ICharacters` starts with no service-level middleware but still gets global middleware. |
| **Request** | `client.Use(...)` on an `IEsiRequest<T>` | That one call. |

**Order (outermost → innermost):** global → service → request → HTTP send.
Within a level, middleware runs in registration order.
A client takes a snapshot of its service's middleware when it's created, so calling `characters.Use(...)`
later doesn't affect clients that already exist.

### Standard middleware (EveEsi.Net.Middleware)
All opt-in. The core client sends a call without any of them.
- `ETagMiddleware`: sends `If-None-Match` from the cache store; on `304`, returns the stored body.
- `CacheMiddleware`: serves from `IEsiCacheStore` until expiry, using route cache hints and `Expires`/`Cache-Control`.
- `ScopeCheckMiddleware`: decodes the access token's `scp` claim (without validating it) and compares it
  against `RouteDefinition.RequiredScopes`. Fails before sending.
- `LanguageMiddleware`: sets `Accept-Language`.
- `ErrorLimitMiddleware` / `RateLimitMiddleware`: read ESI error-limit and rate-limit headers and back off (route carries `x-rate-limit` group).
- `RetryMiddleware`: retries transient failures (5xx, 420/429 with backoff).

The in-memory `IEsiCacheStore` ships here. Other stores ship as `EveEsi.Net.Caching.*` packages.

### What is *not* middleware
The client itself always sets the headers the API requires:
- `X-Compatibility-Date`: from the pinned spec (generated constant).
- `X-Tenant`: from `EsiClientOptions.Tenant`, default `tranquility`.
- `Authorization: Bearer <token>`: when a token was passed.
- `User-Agent`: from `EsiClientOptions.UserAgent`. CCP asks for contact info here.

## Pagination
ESI has two styles, and the library leaves the looping to the caller:
- **Page-based** (`?page=N`, response header `X-Pages`, 40 operations): `p.ApplyPagination(int? page)`;
  `X-Pages` is exposed on the result headers.
- **Cursor-based** (`before` / `after` / `limit`, `Cursor` object in the body, 12 operations):
  `p.ApplyPagination(CursorPage? cursor)`; the next cursor is part of the response DTO.

The route's `PaginationStyle` (`None` / `Page` / `Cursor`) is available to middleware and is checked by `ApplyPagination`.

## Result type

```csharp
public sealed class EsiResult<T>
{
    public bool IsSuccess { get; }
    public bool IsNotModified { get; }     // 304 that no middleware resolved
    public HttpStatusCode StatusCode { get; }
    public T? Value { get; }
    public EsiError? Error { get; }        // ESI error body, or transport/deserialization failure
    public EsiResponseHeaders Headers { get; }  // ETag, Expires, Last-Modified, X-Pages, error/rate-limit, raw access
}
```
- HTTP error statuses → `IsSuccess == false` with `Error`. **No exceptions.**
- Network/transport failures → failed result, `Error.Kind = Transport`, with the exception attached.
- Exceptions are thrown only for cancellation (`OperationCanceledException`) and programmer errors
  (the build-time validation above).

## Request flow

```
service.GetX(args, token)
  → factory.CreateEsiClient<T>(Endpoints.X.Y, p => {...}, token)
       run configurator, validate against the RouteDefinition  → IEsiRequest<T> (+ service middleware snapshot)
  .Use(...)                       → adds request middleware
  .ExecuteAsync(ct)
     build EsiContext (URL from template + route values, query string, body, required headers, bearer token)
     run pipeline: global → service → request → [HTTP send via HttpClient]
     context.Response (raw)  →  deserialize with EsiJsonContext  →  EsiResult<T>
```
