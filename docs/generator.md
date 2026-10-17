# Code generator

`tools/EveEsi.Net.Generator` is a console tool that turns `openapi.json` into C#.
You run it by hand when the spec changes. It isn't part of the build and doesn't ship.

```
dotnet run --project tools/EveEsi.Net.Generator -- --spec openapi.json --out src/
```

Its output (`*.g.cs`) is **committed**, so spec changes show up as reviewable diffs and consumers can step into readable code.

## Pipeline

```
parse spec → classify every schema → resolve names (generator.json) → emit types/converters/routes/services → register JSON types
```

### Classification
Every schema (named or inline) is classified as exactly one of:

| Kind | Detected by | Emitted as |
|---|---|---|
| object | `type: object` with `properties` | `sealed partial record` with `[JsonPropertyName]` |
| enum | `type: string` + `enum` | C# `enum` + generated converter (see Enums) |
| union | `oneOf` (wrapper-key shape, see Unions) | abstract record + nested variants + generated converter |
| alias | `$ref` to a primitive schema (e.g. `CharacterID`) | the primitive matching the alias's own `format` (`CharacterID` → `long`) |
| override | listed under `overrides` in `generator.json` | nothing; references the hand-written type |

Anything that doesn't classify **fails generation**. The generator never guesses.

Nullability: a property is non-nullable only if it is listed in `required` and not typed `[..., "null"]`.

### Numeric and ID types: the spec's `format` is the only source of truth
ESI IDs are 64-bit. In the current spec, all 24 ID alias schemas (`CharacterID`, `CorporationID`, `TypeID`,
`SolarSystemID`, …) are `int64`, and 866 of 906 integers are `int64`. The 40 `int32` integers match the
40 page-based operations, i.e. they appear to be the `page` parameter.

| Spec | C# |
|---|---|
| `integer` + `int64` | `long` |
| `integer` + `int32` | `int` |
| `integer`, no format | `long` (never assume 32-bit) |
| `number` + `double` / `float` / no format | `double` |
| `string` + `uuid` | `Guid` (e.g. `project_id`, `campaign_id`, `job_id`) |
| `string` + `date-time` / `date` | `DateTimeOffset` / `DateOnly` |
| `string`, no format | `string` (e.g. `skinr_id`) |

- Never pick a type from a property **name** (`*_id` doesn't mean `int`, or even numeric). Always resolve the
  `$ref` / `format`.
- Never narrow: no `int` for an `int64` and no `float` for a `double`, even if today's values fit.
- The same rule applies everywhere a type appears: DTO properties, `Parameter` keys, service method
  parameters, route definitions and request bodies.
- The generator fails on an `integer`/`number` format it doesn't recognise.

## Naming
Names come from the spec (`GetCharactersCharacterIdAssets`, `CharactersCharacterIdAssetsGetItem`),
and those read badly. `generator.json` maps them:

```json
{
  "operations": { "GetCharactersCharacterIdAssets": "GetCharacterAssets" },
  "schemas":    { "CharactersCharacterIdAssetsGetItem": "CharacterAsset" },
  "enums":      { "WalletRefType": { "match": ["<schema path>", "<schema path>"], "members": { } } },
  "unions":     { "CharactersParagonHubSkinrItem/properties/price": "ParagonPrice" },
  "overrides":  { "SomeSchema": "EveEsi.Net.Models.Custom.SomeType" }
}
```
Anything not mapped falls back to a naming convention, and the generator prints a list of unmapped names so we can tidy them over time.

## Enums (decision: C# enum with `Unknown` fallback)
Facts from the spec: 142 string enums, all inline, 76 distinct value sets. Values include
`snake_case`, `PascalCase`, hex colours (`#0000fe`), bare digits (`1`, `20`) and codes like `A0IV2`.
Notification `type` has 254 values and changes often.

Rules:
- **De-duplicate** by value set: identical sets become one type, named in config.
  Same name with different values counts as a conflict and fails generation.
- **Member names** are derived (`acceleration_gate_fee` → `AccelerationGateFee`). Values that can't
  become a C# identifier must be mapped in `members`, or generation fails.
- Every enum has `Unknown = 0` as its first member.
- Every enum gets a **generated converter**: a `switch` on the wire string in both directions, AOT safe.
  Don't use `JsonStringEnumConverter` or `[JsonStringEnumMemberName]`; the latter is .NET 9+ only, and we target net8.
- Unrecognised wire values deserialize to `Unknown`. They never throw.

## Unions (`oneOf`)
Facts from the spec: 39 `oneOf` schemas, **no discriminators**, all the same *wrapper-key* shape:
an object with exactly one property whose name identifies the variant.

```json
{ "isk": 1500000.0 }   or   { "plex": 500 }
```
Built-in `[JsonPolymorphic]` needs a discriminator property, so it can't handle these. Emitted shape:

```csharp
[JsonConverter(typeof(ParagonPriceConverter))]
public abstract partial record ParagonPrice
{
    public sealed record Isk(double Value) : ParagonPrice;
    public sealed record Plex(long Value) : ParagonPrice;
    public sealed record Unknown(string Name, JsonElement Raw) : ParagonPrice;
}
```
- The converter reads the single property name and switches on it. Writing reverses that.
- An unrecognised name produces `Unknown` and never throws.
- If a `oneOf` doesn't follow the wrapper-key shape, generation fails. Handle it with an `override`
  and a hand-written type and converter in `EveEsi.Net.Models/Custom/`.

## Parameters
`EveEsi.Net.Routes/Generated/Parameter.g.cs` is a catalogue of wire-name constants, one per distinct
(location, wire name) in the spec (77 today):

```csharp
public static partial class Parameter
{
    public static partial class Route
    {
        public const string CharacterId = "character_id";
        public const string CorporationId = "corporation_id";
        public const string ProjectId = "project_id";
    }
    public static partial class Query
    {
        public const string TypeId = "type_id";
        public const string ContactIds = "contact_ids";
        public const string OrderType = "order_type";
    }
}
```
- The same constant is the `Set` key, the `{placeholder}` in path templates and the entry in a route's query list.
- Value types live in the **service method signature** (from the spec's `format`). The client converts values to
  strings (invariant culture); arrays become one `name=value` pair per item.
- **Enums** are converted by the service with the generated converter before they're set:
  `p.Query.Set(Parameter.Query.OrderType, OrderTypeConverter.ToWire(orderType))`.
- Pagination parameters (`page`, `before`, `after`, `limit`) are **not** in the catalogue. `ApplyPagination` handles them.

## Endpoints
One `Endpoints.<Section>.g.cs` per section in `EveEsi.Net.Routes/Generated/`, built once with `RouteBuilder`
into static fields:

```csharp
public static partial class Endpoints
{
    public static partial class Assets
    {
        public static readonly RouteDefinition CharacterAssets =
            RouteBuilder.Get("GetCharactersCharacterIdAssets", $"/characters/{{{Parameter.Route.CharacterId}}}/assets")
                .Pagination(PaginationStyle.Page)
                .RequiresScope("esi-assets.read_assets.v1")
                .Cache(TimeSpan.FromSeconds(3600), RouteCacheMode.TtlBased)  // x-cache-age, x-server-cache-mode
                .RateLimit("char-asset", 1800, TimeSpan.FromMinutes(15))      // x-rate-limit
                .Build();
    }
}
```
Mapped from the spec: `operationId` → `OperationId`, `security` → scopes, `x-required-roles` → roles,
`x-cache-age` / `x-server-cache-mode` / `x-cache-mode` / `x-tombstone-ttl` → cache hints,
`x-rate-limit` (`group`, `max-tokens`, `window-size`) → rate-limit policy, `requestBody` → `.Body(BodyRequirement.Required)`,
`X-Pages` header → `Page`, `x-pagination: cursor` → `Cursor`.
Routes carry no types: response and body types are emitted into the **service** (below).
Shared header parameters (`IfNoneMatch`, `AcceptLanguage`, `CompatibilityDate`, `Tenant`, `IfModifiedSince`)
are **not** emitted as route parameters. The compatibility date becomes a generated constant the client sends.

## Services
One `I<Section>` + `<Section>` per section in `EveEsi.Net/Generated/`. Services are request builders that
derive from `EsiService`. Each operation becomes a method that calls the factory and returns `IEsiRequest<T>`:

```csharp
public IEsiRequest<IReadOnlyList<CharacterAsset>> GetCharacterAssets(long characterId, string token, int? page = null)
    => CreateEsiClient<IReadOnlyList<CharacterAsset>>(
        Endpoints.Assets.CharacterAssets,
        EsiJsonContext.Default.IReadOnlyListCharacterAsset,
        p =>
        {
            p.Route.Set(Parameter.Route.CharacterId, characterId);
            p.ApplyPagination(page);
        },
        token);
```
Signature order: route parameters, required query parameters, body, `string token` (secured only),
then optional query parameters, then the pagination parameter (`int? page` or `CursorPage? cursor`).

- **Grouping:** one service, one `Endpoints.<Tag>` class and one file per spec tag. Never group by URL segment.
- **Middleware:** each interface is emitted as `I<Tag> : IEsiService<I<Tag>>`, so it gets both `Use` overloads
  (instance and async callback) and `Use` returns `I<Tag>`.
- **Response type:** the method's return type `IEsiRequest<T>`, the explicit generic in `CreateEsiClient<T>(...)` and
  the `EsiJsonContext.Default.<T>` type info are emitted from the same resolved response type. Always write `T`
  explicitly; never rely on inference. A 204 or bodiless success response gives `IEsiRequest<EsiNoContent>` and the
  non-generic `CreateEsiClient(...)`.
- **Body:** `p.Body(body, EsiJsonContext.Default.<TBody>)`.

## Hand-written extensions
- Every generated type is `partial`. Put additions in a non-`.g.cs` file alongside it.
- Never edit `*.g.cs` by hand.

## Output requirements
- Deterministic: stable ordering (by tag, then path, then name) and stable formatting, so diffs only show real API changes.
- Every DTO, enum, union and request-body type is registered in the source-generated `EsiJsonContext`.
- XML doc comments come from the spec's `summary` / `description`.

## Updating to a new spec
1. Replace `openapi.json`.
2. Run the generator. Fix any failures by updating `generator.json` (names, enum members, overrides).
3. Review the diff: new endpoints, changed fields, removed routes.
4. Run tests, then commit spec + config + output together.
