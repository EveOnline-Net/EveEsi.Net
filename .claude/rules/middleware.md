---
paths:
  - "src/EveEsi.Net.Abstractions/**"
  - "src/EveEsi.Net.Client/**"
  - "src/EveEsi.Net.Middleware/**"
  - "src/EveEsi.Net.Caching.*/**"
---

# Client, pipeline and middleware

Full design: [docs/architecture.md](../../docs/architecture.md).

## Factory and request building
- `IEsiClientFactory.CreateEsiClient<T>(operationId, responseType, configure, token, serviceMiddleware)` (operation ID
  from the `Operation.<Tag>.<Name>` constants) looks the
  route up in the `EsiRoutes` table and runs the configurator. It throws on an unknown operation ID and on a missing
  token for a route with a scope. Parameters, pagination and body are enforced by the generated service signatures.
- Services never do validation or HTTP themselves. They only call the factory (through the `EsiService<TSelf>` base).
- Tokens are `string?`. Middleware that needs claims decodes the JWT payload (base64url, no validation).

## Pipeline
- Order: global → service → request → HTTP send. Within a level, registration order.
- An `IEsiRequest<T>` snapshots its service's middleware when it's created.
- Middleware works on the **raw** `EsiResponse` (status, headers, bytes). Deserialization happens after the pipeline.
- `Client` and `Middleware` never reference `Models` or `Routes`. Use `RouteDefinition` metadata only.

## Registering middleware (`Use`)
- Every level (global `EsiOptions`, service interfaces, `IEsiRequest<T>`) implements
  `IEsiMiddlewareTarget<TSelf>` and so accepts **both** an `IEsiMiddleware` instance and an async callback
  `Func<EsiContext, EsiNext, ValueTask>`.
- Each level implements only `Use(IEsiMiddleware)`. The callback overload is the single shared extension
  method that wraps it in `DelegateMiddleware`. Don't add per-level callback overloads.
- Never add a `Func<…, Task>` overload. It makes `async` lambdas ambiguous.
- `Use` returns `TSelf` so chaining keeps the concrete type.
- A new place that accepts middleware must implement `IEsiMiddlewareTarget<TSelf>`. Never add a one-off `Use`.

## Writing middleware
- Implement `IEsiMiddleware.InvokeAsync(EsiContext context, EsiNext next)`.
- Middleware instances are shared across concurrent calls. **No mutable instance state.**
  Keep per-call state in `context.Items`. Keep cross-call state behind an injected abstraction (`IEsiCacheStore`).
- To short-circuit, set `context.Response` and return without calling `next`.
- Call `next` at most once, except in `RetryMiddleware`.
- Honour `context.CancellationToken`.
- Read endpoint facts (operation ID, scope, cache hints, rate limit) from `context.Route`.
  Never parse the path to work them out.
- Standard middleware is opt-in and can't be required by the core client.

## Client responsibilities (not middleware)
- Always sets `X-Compatibility-Date` (generated constant), `X-Tenant` (`EsiClientOptions.Tenant`),
  `User-Agent` (`EsiClientOptions.UserAgent`) and `Authorization: Bearer` when a token is present.
- Uses a single typed/named `HttpClient` from `IHttpClientFactory`. Never `new HttpClient()`.
- Turns every outcome into `EsiResult<T>`. Transport failures and deserialization failures become failed results.
  Only `OperationCanceledException` and argument errors propagate.

## Cache stores
- `IEsiCacheStore` implementations store raw responses (body + relevant headers + expiry).
- In-memory store lives in `EveEsi.Net.Middleware`. Anything with a third-party dependency goes in an `EveEsi.Net.Caching.<Name>` project.
- Cache keys include method, resolved URL, tenant and a hash of the token subject for authed routes
  (never the raw token).
