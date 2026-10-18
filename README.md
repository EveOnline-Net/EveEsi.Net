# NewEdenKit.ESI

Typed, middleware-driven .NET client for [EVE Online's ESI API](https://esi.evetech.net).

> **Status:** preview. Packages are on NuGet as prereleases; the API may still change before 1.0.

- **Every ESI endpoint**, generated from the pinned OpenAPI spec: one injectable service per ESI tag (`IAssets`,
  `ICharacter`, `IMarket`, …), one method per operation, typed models with 64-bit IDs.
- **Every call returns `EsiResult<T>`.** HTTP errors, timeouts and unreadable responses are results, never exceptions.
- **Caching and ETags in one line** (`EnableCaching()`), and your own **middleware** at three levels: every call, one
  service, or one request.
- Stateless, trim and AOT compatible. Targets `net8.0`, `net9.0` and `net10.0`.

## Quick start

```
dotnet add package NewEdenKit.ESI
```

<!-- snippet: register -->
```csharp
services.AddEveEsi(o =>
    {
        o.ApplicationName = "MyApp/1.2";      // your app, sent in the User-Agent (required)
        o.Contact = "admin@example.com";      // how CCP can reach you (required)
    })
    .EnableCaching();                         // optional: response cache + ETags, in a local folder
```

<!-- snippet: quickstart -->
```csharp
// Inject a service (IAssets, ICharacter, IMarket, …), build the request, execute it.
var result = await assets.GetCharactersCharacterIdAssets(characterId, accessToken, page: 1).ExecuteAsync(ct);
if (result.IsSuccess)
{
    foreach (var asset in result.Value) { total += asset.Quantity; }
}
else
{
    // Never throws for HTTP errors: inspect result.StatusCode and result.Error (Kind, Message).
}
```

**Read next:** the [usage guide](https://github.com/NewEdenKit/NewEdenKit.ESI/blob/main/docs/usage.md) covers
finding the method for an endpoint, tokens and scopes, error handling, page and cursor pagination, request bodies,
caching, middleware, unions and enums, testing, and common mistakes. Every example in it is compiled in CI.

**For AI coding assistants:** start from [`llms.txt`](https://github.com/NewEdenKit/NewEdenKit.ESI/blob/main/llms.txt)
and the usage guide. Each service method's XML docs give the ESI method, path and required scope.

## Packages

| Package | Contents |
|---|---|
| `NewEdenKit.ESI` | Everything you need: services, models, `AddEveEsi()`, `EnableCaching()` and the cache stores. Install this one. |
| `NewEdenKit.ESI.Abstractions` | Contracts (results, middleware, cache store, builders). Comes with `NewEdenKit.ESI`; reference it alone only to write middleware or a cache store in a separate library. |

`NewEdenKit.ESI.Caching.Distributed` (over `IDistributedCache`) and `NewEdenKit.ESI.Caching.Redis` (over
StackExchange.Redis) are planned and not published yet.

## Contributing

```
dotnet build
dotnet test
```

See [architecture](https://github.com/NewEdenKit/NewEdenKit.ESI/blob/main/docs/architecture.md) for the design and
[contributing](https://github.com/NewEdenKit/NewEdenKit.ESI/blob/main/docs/contributing.md) for the workflow.

## License

[GPL-3.0-or-later](https://github.com/NewEdenKit/NewEdenKit.ESI/blob/main/LICENSE). Applications that use this library
must also be GPL-licensed and open source.
