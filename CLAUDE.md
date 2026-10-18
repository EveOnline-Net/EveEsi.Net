# EveEsi.Net

Public NuGet library for calling EVE Online's ESI API (`https://esi.evetech.net`).
Repository: https://github.com/EveOnline-Net/EveEsi.Net (default branch `main`). License: GPL-3.0-or-later.
Stateless: it only builds, sends and decodes ESI calls. Caching, ETags, scope checks,
rate limiting etc. are **middleware**, applied at three levels: global, service, request.

```csharp
var result = await assets                     // IAssets, resolved as a transient service
    .CharacterAssets(characterId, token)      // token: string, required on secured endpoints
    .Use(async (ctx, next) => { /* per-request middleware */ await next(ctx); })
    .ExecuteAsync(ct);                        // EsiResult<T>, never throws on HTTP errors

// Services are request builders. Internally each method is just:
CreateEsiClient<IReadOnlyList<CharacterAsset>>(
    Operation.Assets.CharacterAssets,                    // const operation ID, key into the EsiRoutes table
    EsiJsonContext.Default.ListOfCharacterAsset,         // the service supplies the response type
    p =>
    {
        p.Route.Set(Parameter.Route.CharacterId, characterId);
        p.ApplyPagination(page);              // int? page, or CursorPage? for cursor routes
    },
    token);
```

## Core Tech Stack
- Targets: `net8.0;net9.0;net10.0`, C# 12, nullable enabled, trim/AOT compatible
- HTTP: one `HttpClient` from `IHttpClientFactory`, wrapped by our own middleware pipeline
- JSON: `System.Text.Json` with a source-generated `JsonSerializerContext` (no Newtonsoft, no reflection)
- Testing: xUnit + Shouldly + NSubstitute

## Documentation (read the relevant one before working in an area)
- [docs/architecture.md](docs/architecture.md): projects, dependencies, request flow, middleware levels, result type
- [docs/generator.md](docs/generator.md): how the spec becomes code (DTOs, enums, unions, routes, services)
- [docs/design-decisions.md](docs/design-decisions.md): decision log with the reasons. Check it before reopening a settled question.
- [docs/contributing.md](docs/contributing.md): branching, commit/PR conventions, PR checklist, spec update process
- [docs/releasing.md](docs/releasing.md): versioning (SemVer from 0.1.0, one version for all packages), tag-driven releases
- [docs/usage.md](docs/usage.md), [README.md](README.md), [llms.txt](llms.txt): for people and AI assistants **using**
  the library. Their code comes from `tests/EveEsi.Net.Tests/Docs/UsageExamples.cs` (see contributing.md, "Consumer docs");
  update them when consumer-facing behaviour changes.

Rules in `.claude/rules/` load automatically. Some are scoped to paths via frontmatter.

## The OpenAPI spec
`openapi.json` is ~600 KB (233 operations, 349 schemas). **Never read it whole or @-import it.**
Query it with targeted scripts or grep (by path, `operationId`, or schema name). See
`.claude/rules/openapi-consumption.md`.

## Common Commands
- Build: `dotnet build`
- Test: `dotnet test`
- Format: `dotnet format`
- Regenerate code from spec: `dotnet run --project tools/EveEsi.Net.Generator -- --spec openapi.json --out src/`,
  then record API: `dotnet format analyzers <project> --diagnostics RS0016 --severity info --include-generated`

## Hard rules
- Never hand-edit `*.g.cs` files. Change the generator or `generator.json`, then regenerate.
- No HTTP-status exceptions: every call returns `EsiResult<T>`.
- C# types follow the spec's `format` exactly. ESI IDs are `int64` → `long`; never `int` unless the spec says `int32`.
  Never infer a type from a property name (`*_id` may be `long`, `Guid` or `string`).
- The library holds no state between calls. Anything stateful (cache, rate-limit counters) sits behind an abstraction the user supplies.
