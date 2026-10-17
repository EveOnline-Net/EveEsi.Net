# Design decisions

Settled decisions and their reasons. Add new entries at the bottom. To change one, mark it
**Superseded by #N** rather than deleting it.

---

### 1. Public NuGet package named `EveEsi.Net`
Root namespace and package prefix are `EveEsi.Net`. Sub-packages are named `EveEsi.Net.<Area>`.

### 2. Target net8.0, net9.0, net10.0
Covers the current LTS and STS releases. Because net8 is included, nothing can depend on .NET 9+ only APIs
(e.g. `[JsonStringEnumMemberName]`) without a polyfill or a generated alternative.

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
by `tools/EveEsi.Net.Generator`. The core (client, pipeline, middleware, abstractions) is hand-written.
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
In-memory store ships in `EveEsi.Net.Middleware`. Adapters with third-party dependencies ship as
`EveEsi.Net.Caching.Distributed`, `EveEsi.Net.Caching.Redis`, …
*Why:* the core package doesn't pull in Redis or other infrastructure dependencies.

### 15. System.Text.Json with a source-generated context only
No reflection-based serialization and no Newtonsoft. Keeps the library trim and AOT compatible.

### 16. Services are request builders over `IEsiClientFactory`
Service methods only build requests:
`factory.CreateEsiClient<T>(Endpoints.<Section>.<Name>, EsiJsonContext.Default.T, p => { p.Route.Set(...); p.ApplyPagination(...); }, token, …)`
returns an `IEsiRequest<T>`, which the caller can add middleware to and execute.
*Why:* services stay thin and uniform (easy to generate). All request-building logic and validation lives
in one place, the factory and parameter builder. Advanced users can call the factory directly with any endpoint.

### 17. Static `Endpoints` and `Parameter` catalogues, grouped by spec tag
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
`DelegateMiddleware`. The global level also offers `Use<TMiddleware>()` for DI-resolved middleware.
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
