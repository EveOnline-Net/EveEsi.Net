# EveEsi.Net

Typed, middleware-driven .NET client for [EVE Online's ESI API](https://esi.evetech.net).

> **Status:** early development. Nothing is published to NuGet yet.

- **Every ESI endpoint**, generated from the pinned OpenAPI spec: one injectable service per ESI tag (`IAssets`,
  `ICharacter`, `IMarket`, …), one method per operation, typed models with 64-bit IDs.
- **Every call returns `EsiResult<T>`.** HTTP errors, timeouts and unreadable responses are results, never exceptions.
- **Caching and ETags in one line** (`EnableCaching()`), and your own **middleware** at three levels: every call, one
  service, or one request.
- Stateless, trim and AOT compatible. Targets `net8.0`, `net9.0` and `net10.0`.

## Quick start

```
dotnet add package EveEsi.Net
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

**Read next:** the [usage guide](https://github.com/EveOnline-Net/EveEsi.Net/blob/main/docs/usage.md) covers
finding the method for an endpoint, tokens and scopes, error handling, page and cursor pagination, request bodies,
caching, middleware, unions and enums, testing, and common mistakes. Every example in it is compiled in CI.

**For AI coding assistants:** start from [`llms.txt`](https://github.com/EveOnline-Net/EveEsi.Net/blob/main/llms.txt)
and the usage guide. Each service method's XML docs give the ESI method, path and required scope.

## Packages

| Package | Contents |
|---|---|
| `EveEsi.Net` | Services and `AddEveEsi()`. Start here. |
| `EveEsi.Net.Middleware` | `EnableCaching()`, cache and ETag middleware, in-memory and file cache stores. |
| `EveEsi.Net.Models` | Response and request models, enums and unions. |
| `EveEsi.Net.Abstractions` | Contracts for writing your own middleware or cache store. |
| `EveEsi.Net.Routes` | `Operation` and `Parameter` catalogues and the route table. |
| `EveEsi.Net.Client` | Request factory, pipeline and HTTP execution (internal). |
| `EveEsi.Net.Caching.Distributed` | Cache store over `IDistributedCache` (planned). |
| `EveEsi.Net.Caching.Redis` | Cache store over StackExchange.Redis (planned). |

## Contributing

```
dotnet build
dotnet test
```

See [architecture](https://github.com/EveOnline-Net/EveEsi.Net/blob/main/docs/architecture.md) for the design and
[contributing](https://github.com/EveOnline-Net/EveEsi.Net/blob/main/docs/contributing.md) for the workflow.

## License

[GPL-3.0-or-later](https://github.com/EveOnline-Net/EveEsi.Net/blob/main/LICENSE). Applications that use this library
must also be GPL-licensed and open source.
