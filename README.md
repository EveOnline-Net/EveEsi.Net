# EveEsi.Net

Typed, middleware-driven .NET client for [EVE Online's ESI API](https://esi.evetech.net).

> **Status:** early development. Nothing is published to NuGet yet.

- Every ESI endpoint, grouped by section (`ICharacters`, `IAssets`, …), generated from the pinned OpenAPI spec.
- Stateless: it builds, sends and decodes ESI calls. Nothing else.
- Caching, ETags, scope checks, language, rate limits and retries are opt-in **middleware**,
  applied globally, per service or per request.
- Every call returns `EsiResult<T>`. HTTP errors never throw.
- Targets `net8.0`, `net9.0` and `net10.0`. Trim and AOT compatible.

```csharp
services.AddEveEsi(o =>
{
    o.ApplicationName = "MyApp/1.2";       // your app, sent in the User-Agent
    o.Contact = "admin@example.com";       // how CCP can reach you
    o.Use<ETagMiddleware>();
});

var result = await assets
    .CharacterAssets(characterId, token)
    .ExecuteAsync(ct);

if (result.IsSuccess)
{
    foreach (var asset in result.Value!) { /* … */ }
}
```

## Packages

| Package | Contents |
|---|---|
| `EveEsi.Net` | Services and `AddEveEsi()`. Start here. |
| `EveEsi.Net.Abstractions` | Contracts: routes, middleware, requests, results, cache store. |
| `EveEsi.Net.Models` | DTOs, enums and unions. |
| `EveEsi.Net.Routes` | `Endpoints` and `Parameter` catalogues. |
| `EveEsi.Net.Client` | Request factory, pipeline and HTTP execution. |
| `EveEsi.Net.Middleware` | Standard middleware and an in-memory cache store. |
| `EveEsi.Net.Caching.Distributed` | Cache store over `IDistributedCache`. |
| `EveEsi.Net.Caching.Redis` | Cache store over StackExchange.Redis. |

## Building

```
dotnet build
dotnet test
```

See [docs/architecture.md](docs/architecture.md) for the design and
[docs/contributing.md](docs/contributing.md) to contribute.

## License

[GPL-3.0-or-later](LICENSE). Applications that use this library must also be GPL-licensed and open source.
