# Design decisions

Settled decisions and their reasons. Add new entries at the bottom. To change one, mark it
**Superseded by #N** rather than deleting it.

---

### 1. Public NuGet package named `NewEdenKit.ESI`
Root namespace and package prefix are `NewEdenKit.ESI`. Sub-packages are named `NewEdenKit.ESI.<Area>`.

### 2. Target net8.0, net9.0, net10.0
Covers the current LTS and STS releases. Because net8 is included, nothing can depend on .NET 9+ only APIs
(e.g. `[JsonStringEnumMemberName]`) without a polyfill or a generated alternative.
*Update:* each target depends on the `Microsoft.Extensions.*` line of its own runtime (#41).

### 3. Stateless library
The library only makes ESI calls. It holds no tokens, no cache and no counters between calls.
Anything stateful sits behind an abstraction the consumer supplies (`IEsiCacheStore`).
*Why:* predictable behaviour, safe in any hosting model, and the consumer stays in control of storage.

### 4. Cross-cutting concerns are middleware, at three levels
Global (DI registration), service (on a transient service instance), request (on `IEsiRequest<T>`).
They run in that order, outermost first.
*Why:* ETags, caching, language and scope checks vary by application. Keeping them out of the core keeps it small.
*Considered:* `DelegatingHandler`s. Rejected because they're configured per `HttpClient`, can't see typed
route metadata, and can't be attached to a single request.

### 5. Services are transient; service-level middleware lives on the instance
Resolving `ICharacters` gives a fresh instance with no service middleware. Global middleware still applies.

### 6. Per-request middleware is kept
Useful for one-off logging, tracing and overrides. It isn't the way auth works; see #7.

### 7. Token is a required `string` parameter on secured endpoints
Taken from the spec's `security` block. Public endpoints have no token parameter.
The token is the consumer's raw SSO access token as a `string`. There is no wrapper type.
*Why:* the compiler enforces it, callers can't forget auth, and consumers don't have to convert their tokens.

### 8. ETag, language and similar headers are middleware concerns
The core does not expose `WithETag` / `WithLanguage`. Standard middleware is provided for them.
Exceptions: the client always sends `X-Compatibility-Date` (the API requires it), `X-Tenant`
(option, default `tranquility`), `Authorization` and `User-Agent`.

### 9. Pagination is left to the caller
ESI has page-based (`X-Pages`) and cursor-based (`before`/`after`) pagination. The library exposes the
parameters and the paging info but doesn't loop.

### 10. Results, not exceptions
Every call returns `EsiResult<T>`. HTTP errors and transport failures become failed results.
Exceptions are thrown only for cancellation and programmer errors.

### 11. Generate the repetitive parts with our own generator; commit the output
DTOs, enums, unions, converters, route definitions and services are generated from `openapi.json`
by `tools/NewEdenKit.ESI.Generator`. The core (client, pipeline, middleware, abstractions) is hand-written.
*Why:* 233 operations and 349 schemas change with each compatibility date. Off-the-shelf generators
(NSwag, Refit, Kiota) impose their own client shape and bypass our pipeline. Committing the output makes
spec changes reviewable and keeps the build free of the generator.
*Considered:* a Roslyn source generator. Rejected because the output is invisible in PRs and harder to debug.

### 12. Enums are C# enums with an `Unknown = 0` fallback
Unknown wire values deserialize to `Unknown` and never throw. Converters are generated (see #2).
*Considered:* string-backed structs (keep the raw value) and a hybrid of the two. Rejected for simplicity.

### 13. `oneOf` unions are closed record hierarchies with generated converters
The spec's unions use a wrapper-key shape with no discriminator, so `[JsonPolymorphic]` can't handle them.
Each union gets an `Unknown` variant.

### 14. `IEsiCacheStore` is required; cache adapters are separate packages
In-memory store ships in `NewEdenKit.ESI.Middleware`. Adapters with third-party dependencies ship as
`NewEdenKit.ESI.Caching.Distributed`, `NewEdenKit.ESI.Caching.Redis`, …
*Why:* the core package doesn't pull in Redis or other infrastructure dependencies.
*Update:* the store is chosen inside `.EnableCaching(c => c.AddInMemoryCacheStore())` or
`c.AddFileCacheStore(directory)` (#36, #37); without a choice it defaults to a file store. The file store has no
dependencies, so it ships in `NewEdenKit.ESI.Middleware` too. Middleware added with `Use<CacheMiddleware>()` outside
`EnableCaching` needs the app to register a store itself. *Superseded* in part by #40: there are no adapter packages.

### 15. System.Text.Json with a source-generated context only
No reflection-based serialization and no Newtonsoft. Keeps the library trim and AOT compatible.

### 16. Services are request builders over `IEsiClientFactory`
Service methods only build requests:
`factory.CreateEsiClient<T>("<operationId>", EsiJsonContext.Default.T, p => { p.Route.Set(...); p.ApplyPagination(...); }, token, …)`
returns an `IEsiRequest<T>`, which the caller can add middleware to and execute.
*Why:* services stay thin and uniform (easy to generate). All request-building logic and validation lives
in one place, the factory and parameter builder. Advanced users can call the factory directly with any endpoint.

### 17. Static `Endpoints` and `Parameter` catalogues, grouped by spec tag
**Route storage superseded by #29** (services are still grouped by tag).
Route definitions are static fields built once through `RouteBuilder`, grouped by the operation's spec **tag**
(`Endpoints.Assets.CharacterAssets`, not `Endpoints.Character.Assets`). Services use the same grouping.
*Considered:* grouping by URL segment. Rejected because most endpoints live under `/characters/{id}/`,
which would make `ICharacters` very large.

Services return `IEsiRequest<T>`. The concrete request type is internal; consumers only ever see the interface.

The response type `T` is written explicitly in `CreateEsiClient<T>(...)` and matches the service method's
return type and the `JsonTypeInfo<T>` the service passes; the compiler checks those agree. Routes carry no types (see #26).
Parameter names are generated string constants (see #27); value types are enforced by the service signatures. Pagination has its own `ApplyPagination(int?)` / `ApplyPagination(CursorPage?)` overloads,
checked against the route's pagination style.

### 18. C# types follow the spec's `format` exactly. IDs are `long`.
ESI IDs are 64-bit (`int64`). That includes every ID alias schema such as `CharacterID` and `TypeID`.
The generator maps types only from `$ref`/`format`, never from property names, and never narrows.
An integer with no format maps to `long`. UUID IDs map to `Guid`.
*Why:* item, structure and many other IDs already exceed `int.MaxValue`. A narrowed type would overflow
silently or fail to deserialize at runtime, and changing it later breaks the public API.

### 19. Middleware can be an `IEsiMiddleware` or an async callback, at every level
Global, service and request levels all accept `Use(IEsiMiddleware)` and
`Use(Func<EsiContext, EsiNext, ValueTask>)`. All levels implement `IEsiMiddlewareTarget<TSelf>`, which has a
single `Use(IEsiMiddleware)` member. The callback overload is one shared extension method that wraps it in
`DelegateMiddleware`. The global level (`IEsiBuilder`, see #36) also offers `Use<TMiddleware>()` for DI-resolved middleware.
*Why:* one implementation means the overloads can't differ between levels.
Only a `ValueTask` callback overload exists, because a parallel `Task` overload makes `async` lambdas ambiguous.

### 20. GitHub, trunk-based branching, squash-merge
`main` is protected and always releasable. Short-lived `feat/`, `fix/`, `spec/`, `docs/`, `chore/` branches are
squash-merged through PRs with Conventional Commit titles. `release/X.Y` branches exist only to service an older line.

### 21. Tag-driven releases; one shared version; SemVer from 0.1.0
A `vX.Y.Z` tag on `main` publishes every package to nuget.org with the same version (MinVer, Trusted Publishing).
Prereleases are published only on demand, via `-preview.N` tags. Before 1.0, breaking changes bump the minor version.
*Why one version:* consumers never have to work out which package versions are compatible.

### 22. No spec automation; community spec PRs
Spec updates are done by hand in a `spec/<compat-date>` PR, and anyone can contribute one. There's no scheduled
job that checks for new specs.

### 23. No deprecation period for ESI changes
We consume ESI; we don't shape it. When ESI removes or changes an endpoint or field, the generated API follows
straight away and the version bump reflects it (breaking → major, or minor before 1.0). No `[Obsolete]` shims.
Breaking changes are caught with PublicApiAnalyzers and package validation.

### 24. License: GPL-3.0-or-later
Anyone can use, fork and change the library. Distributed modified copies, **and applications that use the
library**, must also be GPL-licensed and open source.
*Why:* nobody should be able to take the project, close the source and make money from it.
Most EVE third-party apps are open source anyway.
*Considered:* MIT (allows closed-source commercial copies), MPL-2.0 (only the library's own files stay open;
closed apps could use it), and PolyForm Noncommercial (not open source, and would discourage contributors).
*Note:* server-side use without distribution isn't covered by the GPL (that would need AGPL).
Contributions are accepted under the same license.

### 25. Middleware edits an `EsiRequestMessage`, not an `HttpRequestMessage`
`EsiContext.Request` is our own mutable request (method, URI, headers, body bytes). The client builds a fresh
`HttpRequestMessage` from it on every send and applies the headers it owns (`Authorization`, `X-Compatibility-Date`,
`X-Tenant`, `User-Agent`) at that point. `EsiContext.AccessToken` is settable, so a refresh middleware can swap it.
*Why:* `HttpClient` refuses to send the same `HttpRequestMessage` twice, which would break `RetryMiddleware`.

### 26. Routes describe the endpoint only; services supply the types
**Route fields narrowed by #29.**
`RouteDefinition` is non-generic: method, path, parameters, scopes, roles, cache hints, rate limit, pagination and
`BodyRequirement`. The service passes `JsonTypeInfo<T>` from `EsiJsonContext` to `CreateEsiClient<T>(...)`, and the
body's type info to `p.Body(value, typeInfo)`. Endpoints with no response body use the non-generic
`CreateEsiClient(...)`, which returns `IEsiRequest<EsiNoContent>`.
*Why:* a route is endpoint metadata; what the bytes decode to is the caller's concern. `Client` still never references
`Models`, because the type info arrives from the service. The compile-time check moves from route ↔ `T` to
type info ↔ `T`; both come from the same spec operation in generated code.

### 27. Route and query values are string key/value pairs; parameter names are string constants
`EsiRequestMessage` holds `RouteValues` and `Query` as `KeyValuePair<string, string>` lists (a query name repeats per
array item) and builds the URI from them. Parameter names are generated `const string`s (`Parameter.Route.CharacterId = "character_id"`), used as the
`Set` key, as the placeholder in path templates and in the route's query parameter list. `p.Route` and `p.Query` share
one interface, `IEsiParameterValues`. A wrong name or a value of the wrong type compiles in a hand-written
`CreateEsiClient` call; unknown names are rejected when the request is built, and generated services are typed from
the spec. The client converts values to strings. Enum parameters are keyed as `string` and converted
by the generated service. `RouteDefinition` lists query parameter names and which are required; route parameter
names come from the path template.
*Why:* everything goes on the wire as a string, so typed key objects, formatters, locations and parameter equality
were machinery without a purpose; services already carry the types. Pairs are also what middleware wants to read and edit.

### 28. `EsiResult<T>.StatusCode` is nullable
`null` means no response was received (transport failure). Results are created through public
`Success` / `NotModified` / `Failure` factories, so consumers can build them in their own tests.
*Why:* a fake status such as `0` is easy to mistake for a real one.

### 29. One route table keyed by operation ID; routes hold only method, path, scope, cache and rate limit
All routes live in one generated static dictionary, `EsiRoutes.All` (`operationId` → `RouteDefinition`).
Operation IDs exist as generated `const string`s grouped by tag (`Operation.Character.CharacterInfo = "GetCharactersDetail"`);
services pass the constant to `CreateEsiClient`, and the factory looks the route up (unknown ID → `ArgumentException`).
`RouteDefinition` holds the operation ID, method, path template, an optional scope, cache hints and rate limit. Query
parameter lists, pagination style, body requirement and roles are gone; `RouteBuilder` is gone.
*Why:* parameters, pagination and body are already enforced by the generated service signatures, so describing them
again on the route was duplication. A flat table keyed by the spec's own identifier is simpler to generate, review
and look up than nested per-tag route objects; the per-tag constants keep names discoverable and typo-proof. `Client` gets the table through DI, since it can't reference `Routes`.

### 30. Registration owns the HTTP setup; the consumer identifies their app; the compatibility date is fixed
`AddEveEsi(o => …)` requires `ApplicationName` and `Contact` and builds the `User-Agent` from them plus the library's
product token and the compatibility date it speaks
(`MyApp/1.2 (admin@example.com) NewEdenKit.ESI/0.1.0 (compatibility-date 2026-08-18)`). It configures the typed client's `HttpClient`
(`BaseAddress`, `User-Agent`, `X-Compatibility-Date`, `X-Tenant`) through `AddHttpClient<EsiHttpClient>`. Nothing else
sets these headers; `EsiHttpClient` only sends. `X-Compatibility-Date` is the generated `EsiSpec.CompatibilityDate`
and isn't configurable.
*Why:* the user agent is the consumer's application, not the library; CCP needs to reach whoever runs it. A library
version is generated from one spec, so sending any other compatibility date would mismatch the generated types.
Keeping header setup in registration leaves the HTTP client a plain sender and fails fast on missing identity.

### 31. Generated models: read-only records, nested types, attributed enums
Response and body models are `sealed partial record`s with `{ get; init; }` properties. Inline objects and unions are
**nested types** of the record that uses them, named property + `Model` (array items + `Item`, map values + `Value`);
named schemas are top-level. Every enum member carries `[EnumMember(Value = "<wire>")]` and every enum has a
`[JsonConverter]` to its generated switch-based converter.
*Why:* nesting keeps each response's shape in one place and avoids hundreds of loosely named top-level types; the
suffix is required because C# forbids a nested type and a property with the same name. Read-only models can't be
mutated after deserialization. `EnumMember` is the standard way to show a wire value; the converter still uses a
`switch` so it stays AOT safe.

### 32. `EsiJsonContext` is internal
Generated services reach it through `InternalsVisibleTo`. Making it public would add ~1,500 source-generated members
(whose nullability the public-API analyzer can't track) that churn with every model change. It can be made public
later without breaking anyone.

### 33. Model namespaces follow spec tags
Types used by exactly one tag's operations live in `NewEdenKit.ESI.Models.<Tag>`; shared and unused types stay in the
root `NewEdenKit.ESI.Models`. Request and response types are *not* split into separate namespaces.
*Why:* 208 of 211 object schemas belong to a single tag, so the split is clean and mirrors `Operation.<Tag>`,
`EsiRoutes.<Tag>` and the services, and it cuts autocomplete from ~390 types to a few dozen per tag. Requests are only
13 types with no overlap with responses; a separate namespace would mostly be empty and would split shared enums.
*Trade-off:* a type moves namespace if a spec change makes it shared, which is a breaking change (see #23).

### 34. Generated services: methods named like their operation constants, internal implementations
Each tag gets a public `I<Tag>` interface and an `internal sealed` `<Tag>Service`. Each method has the same name as
its `Operation.<Tag>` constant, and parameters follow a fixed order: route, required query, body, token, optional query,
pagination. Optional parameters are nullable with a `null` default and aren't sent; ESI's default is documented, not
copied into C#. Registration uses `TryAddTransient`.
Mapped names leave out the HTTP verb (`characters.CharacterInfo(...)`, not `GetCharacterInfo`): to a consumer the verb is a
transport detail. Only where two operations share a path does the name say which is which.
*Why:* one `generator.json` entry names both the constant and the method, so they can't drift apart. Consumers depend
on the interface, which can be mocked, so the implementation can change freely. A `null` default lets ESI apply its own
default, which can change without a library release. `TryAdd` lets an app or a test replace one service.

### 35. Cache and ETag middleware share one entry, keyed by the token's subject
`CacheMiddleware` and `ETagMiddleware` read and write the same `IEsiCacheStore` entry, keyed by `EsiCacheKey`. On
secured routes the key holds a hash of the access token's `sub` claim, not of the token. A 304 is turned back into the
stored 200, and the freshness lifetime is `max-age`, else `Expires − Date`, else the spec's `x-cache-age`.
*Why:* access tokens rotate every 20 minutes, so a token-based key would throw the cache away on every refresh, while
the subject (the character) is what decides what the response contains. Measuring `Expires` against ESI's own `Date`
keeps a skewed local clock from stretching or shrinking lifetimes. Returning the stored value on 304 means callers
never need to handle "not modified" when ETag middleware is on.
*Trade-off:* the subject is read without validating the token. That's only a cache partition; ESI still authorizes the
call whenever it's actually sent.

### 36. Registration is a builder: `AddEveEsi()` returns `IEsiBuilder`
`services.AddEveEsi(o => …)` returns an `IEsiBuilder` that carries the global middleware (`Use`, `Use<T>()`), caching
(`EnableCaching`, see #37) and HTTP client
settings (`ConfigureHttpClient`). `EsiOptions` is configuration only. `IEsiBuilder` lives in Abstractions, which now
depends on `Microsoft.Extensions.DependencyInjection.Abstractions`. The `IServiceCollection` store extensions and
`EsiOptions.Use` are gone: the builder is the one way to register.
*Why:* with middleware and stores spread over several packages, one chain keeps everything that belongs to the ESI
client together and discoverable, the way `IHttpClientBuilder` does for HTTP clients. Defining it in Abstractions lets
every package extend it without referencing the main package.

### 37. Caching is switched on with its own builder: `EnableCaching(c => …)`
`.EnableCaching(c => c.AddFileCacheStore("cache/esi"))` adds `CacheMiddleware` and `ETagMiddleware` to the global chain
and registers the store chosen on the `IEsiCacheBuilder` (Abstractions). Either middleware can be left out
(`DisableResponseCache`, `DisableETags`). With no store chosen, a file store in a per-application folder under the
user's local application data is used. Store packages extend `IEsiCacheBuilder` and call `UseStore`, which allows one
store. Store methods are no longer on `IEsiBuilder`.
*Why:* caching is one feature made of two middleware and a store that have to agree; one switch makes the common case a
single line and keeps the store choice next to the thing that uses it. Defaulting to the file store means caching
survives restarts out of the box. The folder is per application because the cache key doesn't include the configured
tenant or base address, which are per application.

### 38. The project is NewEdenKit.ESI
The project, its packages, namespaces and repository are named `NewEdenKit.ESI` (it was `EveEsi.Net`), under the
NewEdenKit organization. `ESI` is upper case in package IDs and namespaces, as the project name is written. Type names
keep their `Esi` prefix (`EsiOptions`, `EsiResult<T>`, `IEsiBuilder`) and `AddEveEsi()` keeps its name: both describe
EVE's ESI, not the brand. The extension classes lost the old brand (`EsiServiceCollectionExtensions`,
`EsiBuilderExtensions`).
*Why:* the library is the ESI part of a wider NewEdenKit family. Renaming before the first release costs nothing; after
it, every package ID would change on nuget.org.
*Trade-off:* `NewEdenKit.ESI` doesn't follow .NET's guideline of PascalCasing acronyms longer than two letters
(`System.Xml`); chosen deliberately to match the project's name.

### 39. Two packages: NewEdenKit.ESI and NewEdenKit.ESI.Abstractions
Only `NewEdenKit.ESI` and `NewEdenKit.ESI.Abstractions` are published. Models, Routes, Client and Middleware remain
their own assemblies and projects, but `NewEdenKit.ESI` references them with `PrivateAssets="all"` and packs their
DLLs, XML docs and PDBs into its own package. Unimplemented projects (`Caching.*`) aren't packable.
*Why:* the inner libraries only exist to serve the main one; nobody should install `Routes` or `Client` on their
own, and one package to install is simpler. Abstractions stays separate so extension packages (cache stores,
middleware libraries) can depend on the contracts without the whole client (#36).
*Supersedes* the per-library packages in #14. The `NewEdenKit.ESI.Caching.*` store packages once planned here were
dropped in #40.

### 40. Cache storage beyond memory and files is the application's concern
The library ships the in-memory and file stores and nothing else. The planned `NewEdenKit.ESI.Caching.Redis` and
`NewEdenKit.ESI.Caching.Distributed` packages (#14, #39) are dropped and their placeholder projects removed.
Applications that want Redis, a database or anything else implement `IEsiCacheStore` (three methods) and plug it in
with `EnableCaching(c => c.UseStore<TStore>())` (resolved from the container, like `Use<TMiddleware>()`) or
`c.UseStore(sp => …)`.
*Why:* how a cache is hosted, connected, bounded and monitored depends on the application's infrastructure, and the
contract is small enough to implement directly. Each adapter package would be a dependency to version, test against a
real server and release in step with the client, for little gain over a few lines in the app.

### 41. Dependencies follow the target framework
Each target framework gets the `Microsoft.Extensions.*` packages of its own runtime line: net8.0 → 8.0.x, net9.0 →
9.0.x, net10.0 → 10.0.x, each on its latest patch (conditional `PackageVersion` groups in `Directory.Packages.props`).
The packages carry one dependency group per framework. Dependabot updates patches within a line and never moves a line
to another major.
*Why:* with every target on the 8.0.x packages, a net10 app that brought nothing newer itself stayed on them, and
8.0.x stops getting fixes when .NET 8 leaves support (November 2026). Apps on net10 are on the 10.x packages in
practice anyway (ASP.NET Core's shared framework, `Microsoft.Extensions.Hosting`), so requiring them costs nothing.
*Supersedes* "lowest supported line for every target".
