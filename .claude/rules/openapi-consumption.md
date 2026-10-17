---
paths:
  - "openapi.json"
  - "generator.json"
  - "tools/**"
  - "src/EveEsi.Net.Models/**"
  - "src/EveEsi.Net.Routes/**"
  - "src/EveEsi.Net/Generated/**"
---

# Working with the OpenAPI spec

Full design: [docs/generator.md](../../docs/generator.md).

## Reading the spec
- `openapi.json` is ~600 KB. Never read it whole. Query it with a short script, e.g.
  `python -c "import json;d=json.load(open('openapi.json',encoding='utf-8'));print(json.dumps(d['paths']['/characters/{character_id}/assets'],indent=1))"`
- Look things up by path, `operationId`, tag or `components/schemas/<Name>`.
- The spec is pinned to one compatibility date (`components/parameters/CompatibilityDate`). Don't hand-edit it.

## Spec → code mapping
- **Tag** → service (`I<Tag>`) and endpoint class (`Endpoints.<Tag>`).
- **Path/query params** → typed method params, camelCase (`character_id` → `characterId`), and a typed key in
  `Parameter.Route` / `Parameter.Query`. Pagination params (`page`, `before`, `after`, `limit`) are excluded;
  they become `int? page` / `CursorPage? cursor` and go through `ApplyPagination`.
- **`requestBody`** → body parameter on the service method, `p.Body(value)`, and body type on the route.
- **`security`** → required `string token` parameter + `RequiresScope(...)` on the route.
- **`x-required-roles`** → `RequiresRole(...)`. **`x-cache-age` / `x-cache-mode`** → cache hints.
  **`x-rate-limit`** → `RateLimitGroup(...)`. **`X-Pages`** → `PaginationStyle.Page`. **`x-pagination: cursor`** → `PaginationStyle.Cursor`.
- **Shared header params** (`IfNoneMatch`, `IfModifiedSince`, `AcceptLanguage`, `CompatibilityDate`, `Tenant`)
  never become method or route parameters.
- **`default` response** (`Error` schema) → `EsiError` on a failed result. It's never a return type.
- **`$ref` to a primitive** (e.g. `CharacterID`) → the primitive type, not a wrapper.

## Types
- Objects → `sealed partial record`, `[JsonPropertyName("snake_case")]` on every property, PascalCase names.
- Non-nullable only if the property is in `required` and not `["<type>", "null"]`.
- Arrays → `IReadOnlyList<T>`. Maps (`additionalProperties`) → `IReadOnlyDictionary<string, T>`.
- **Numeric types follow the spec's `format` exactly. ESI IDs are 64-bit.**
  - `int64` → `long`, `int32` → `int`, integer with no format → `long`. In practice only `page` is `int32`.
  - `double`/`float`/no format → `double`. `uuid` → `Guid`. `date-time` → `DateTimeOffset`. `date` → `DateOnly`.
  - Resolve `$ref` aliases (`CharacterID`, `TypeID`, …) to their own format. All are `int64` today.
  - Never infer a type from a name: `*_id` can be `long`, `Guid` or `string`. Never narrow `long` → `int`.
  - Applies to DTO properties, `Parameter` keys, service method parameters, route definitions and bodies.
- Enums → C# enum with `Unknown = 0` + generated converter. Never `JsonStringEnumConverter`.
- `oneOf` → abstract record + nested sealed variants + `Unknown` variant + generated converter.
- Every type is registered in `EsiJsonContext`.

## Changing generated output
- Never edit `*.g.cs`. Change the generator or `generator.json` and regenerate.
- Bad names → add a mapping in `generator.json`, don't add a convention special case.
- A schema the generator can't handle → `overrides` entry + hand-written type in `EveEsi.Net.Models/Custom/`.
- Generator failures on new spec versions are intended. Resolve them in config, never by loosening checks.
