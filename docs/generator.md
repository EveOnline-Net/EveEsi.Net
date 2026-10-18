# Code generator

`tools/NewEdenKit.ESI.Generator` is a console tool that turns `openapi.json` into C#.
You run it by hand when the spec changes. It isn't part of the build and doesn't ship.

```
dotnet run --project tools/NewEdenKit.ESI.Generator -- --spec openapi.json --out src/
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
| object | `type: object` with `properties` (or no properties at all) | `sealed partial record`, `init`-only properties with `[JsonPropertyName]` |
| map | `type: object` with only `additionalProperties` | `IReadOnlyDictionary<string, T>` |
| array | `type: array` | `IReadOnlyList<T>` |
| enum | `type: string` + `enum` | shared C# `enum` + generated converter (see Enums) |
| union | `oneOf` (wrapper-key shape, see Unions) | abstract record + nested variants + generated converter |
| alias | `$ref` to a primitive schema (e.g. `CharacterID`) | the primitive matching the alias's own `format` (`CharacterID` → `long`) |
| any | no `type` at all | `JsonElement` |

Anything that doesn't classify (`allOf`, `anyOf`, non-string enums, unknown formats, non-wrapper `oneOf`) **fails
generation**. The generator never guesses.

### Records are read-only, nested and strict
- Properties are `{ get; init; }`: models are immutable once deserialized.
- A property is non-nullable (and `required`) only if it is listed in `required` and not typed `[..., "null"]`.
  Everything else is nullable. A response missing a `required` property fails to deserialize
  (`EsiErrorKind.Deserialization`), because the spec promised it.
- **Inline objects become nested types** of the record that contains them, named after the property:
  `position` → `PositionModel`, array items `skills[]` → `SkillsItem`, map values → `<Name>Value`. Unions follow the
  same rule. The suffix is always added, because C# can't have a nested type and a property with the same name.
- **Named schemas** (`components/schemas`) become top-level records. A named array's inline item becomes a top-level
  `<Name>Item`. An inline request body or response becomes `<OperationId>Body` / `<OperationId>Response`.
- Clashes fail generation: a property named like its type (`Error.error`), two nested types with the same name,
  a nested type named like its parent. Fix them with a `schemas` rename in `generator.json`.

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

### Namespaces follow tags
- A top-level type (record, union or enum) used only by one tag's operations lives in
  `NewEdenKit.ESI.Models.<Tag>` (`NewEdenKit.ESI.Models.Assets`, `NewEdenKit.ESI.Models.Character`, …), in
  `Generated/<Tag>/<Name>.g.cs`. "Used" follows responses, request bodies and query-parameter enums, through nested
  types, lists, maps and union variants.
- Types used by several tags, or by none, stay in the root `NewEdenKit.ESI.Models` (`Generated/<Name>.g.cs`): today
  `Cursor`, `ErrorResponse`, `ErrorDetail` and a handful of shared enums. Root types are visible from every tag
  namespace without a `using`.
- Nested types live in their parent's namespace.
- No type may be named like a tag namespace (C# would read the name as the namespace, CS0118); the spec's `Status`
  is renamed `ServerStatus` in `generator.json`.
- A type moves namespace if a spec change makes it shared (or unshared). That's a breaking change, and is treated like
  any other ESI change (decision #23).

## Naming
Names come from the spec (`GetCharactersCharacterIdAssets`, `CharactersCharacterIdAssetsGetItem`),
and those read badly. `generator.json` maps them:

```json
{
  "operationConstants": { "GetCharactersCharacterIdAssets": "CharacterAssets" },
  "schemas": {
    "Error": "ErrorResponse",
    "KillmailsKillmailIdKillmailHashGet.victim.items[].items[]": "ContainedItem"
  },
  "enums": {
    "MailLabelColor": { "match": ["CharactersCharacterIdMailLabelsGet.labels[].color"], "members": { "#0000fe": "Color0000FE" } }
  }
}
```
- **Schema paths** name where a type was found: the schema name, then `.property`, `[]` for array items, `{}` for map
  values, `|variant` for union variant values, and `op:<OperationId>.response` / `.body` / `.<parameter>` for
  operation-level schemas. Error messages print the path to map.
- `schemas` renames the record or union generated at a path. `enums` names a value set (any one `match` path is
  enough; all listed paths must share the set) and supplies `members` for values that aren't valid identifiers.
- Mapping a path that no longer exists fails generation, so stale config can't accumulate.
- `operations`, `unions` and `overrides` aren't supported yet; non-empty entries fail.

Anything not mapped falls back to the conventions above, and the generator prints how many names are defaulted so we
can tidy them over time.

## Enums (decision: C# enum with `Unknown` fallback)
Facts from the spec: 133 string enums (including 4 on query parameters), 71 distinct value sets after
de-duplication. Values include `snake_case`, `PascalCase`, hex colours (`#0000fe`), bare digits (`1`, `20`) and codes
like `A0IV2`. Notification `type` has 254 values and changes often.

```csharp
[JsonConverter(typeof(MailLabelColorConverter))]
public enum MailLabelColor
{
    /// <summary>A value this library version doesn't know.</summary>
    Unknown = 0,

    /// <summary>#0000fe</summary>
    [EnumMember(Value = "#0000fe")]
    Color0000FE,
}

public sealed class MailLabelColorConverter : JsonConverter<MailLabelColor>
{
    // Read/Write, plus FromWire(string?) and ToWire(MailLabelColor) for services and callers.
}
```

Rules:
- **De-duplicate** by value set (order ignored): identical sets become one type, everywhere they occur. The default
  name comes from the first path (`CharactersCharacterIdWalletJournalGet[].ref_type` →
  `CharactersCharacterIdWalletJournalGetItemRefType`); `generator.json` renames it.
- **Member names** are derived (`acceleration_gate_fee` → `AccelerationGateFee`). Values that can't become a C#
  identifier (`#0000fe`, `10`) must be mapped in `members`, or generation fails. Two values mapping to one name fail.
- Every member carries **`[EnumMember(Value = "<wire>")]`**, so its wire string is visible on the member itself.
- Every enum has `Unknown = 0` first. If ESI's own set contains `unknown`/`Unknown`, `Unknown` carries that wire value
  (it means the same thing as an unrecognised value), and `ToWire(Unknown)` writes it back. Otherwise `Unknown` has no
  wire value and `ToWire(Unknown)` throws.
- Every enum has a **`[JsonConverter]`** pointing at its generated converter: a `switch` on the wire string in both
  directions, so it's AOT safe and needs no reflection (the attribute is for readers and other tools). Don't use
  `JsonStringEnumConverter` or `[JsonStringEnumMemberName]`; the latter is .NET 9+ only, and we target net8.
- Unrecognised wire values deserialize to `Unknown`. They never throw.

## Unions (`oneOf`)
Facts from the spec: 39 `oneOf` schemas, **no discriminators**, all the same *wrapper-key* shape:
an object with exactly one property whose name identifies the variant.

```json
{ "isk": 1500000.0 }   or   { "plex": 500 }
```
Built-in `[JsonPolymorphic]` needs a discriminator property, so it can't handle these. Emitted shape (nested in the
record that uses it, like any inline type):

```csharp
[JsonConverter(typeof(CharactersParagonHubSkinrItem.PriceModel.Converter))]
public abstract partial record PriceModel
{
    private PriceModel() { }                                    // closed: only the variants below

    public sealed partial record Isk(double Value) : PriceModel;
    public sealed partial record Plex(long Value) : PriceModel;
    public sealed partial record Unknown(string Name, JsonElement Value) : PriceModel;

    internal sealed class Converter : JsonConverter<PriceModel> { ... }
}
```
- The converter reads the single property name and switches on it; variant values are read through the options'
  own type metadata (`EsiJson`), so no reflection. Writing reverses that. More or fewer than one property throws.
- An unrecognised name produces `Unknown`, keeps the raw value and writes it back unchanged. ESI's own `unknown`
  variant (in `CorporationsProjectsDetail.configuration`) is handled the same way rather than generated separately.
- If a `oneOf` doesn't follow the wrapper-key shape, generation fails.

## JSON context
`EsiJsonContext` (`NewEdenKit.ESI.Models/Generated/EsiJsonContext.g.cs`) registers every generated type, every list and
map used by them, and every response and body type, each with an explicit `TypeInfoPropertyName` derived from its
nesting path (`CharactersDetail.PositionModel` → `CharactersDetailPositionModel`, `IReadOnlyList<X>` → `ListOfX`).
Explicit names are required because many nested types share simple names. The context is **internal**: generated
services in `NewEdenKit.ESI` use it through `InternalsVisibleTo`, and keeping it internal keeps ~1,500 source-generated
members out of the public API.

## Parameters
`NewEdenKit.ESI.Routes/Generated/Parameter.g.cs` is a catalogue of wire-name constants, one per distinct
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

## Operation IDs
`NewEdenKit.ESI.Routes/Generated/Operation.g.cs` is a catalogue of operation IDs as `const string`s, one nested class per
spec **tag**, so nothing refers to an operation by a raw string:

```csharp
public static partial class Operation
{
    public static partial class Assets
    {
        public const string CharacterAssets = "GetCharactersCharacterIdAssets";
    }
    public static partial class Character
    {
        public const string CharacterInfo = "GetCharactersDetail";
    }
}
```
- The value is the spec's `operationId`, unchanged. The **name** comes from the `operationConstants` section of `generator.json`
  (`"GetCharactersDetail": "CharacterInfo"`). Unmapped operations use the operation ID itself as the name (verb
  included, since GET and POST on one path would otherwise collide). The generator prints how many are unmapped.
- Two operations mapping to the same name in one tag class, a mapped name that isn't an identifier, or a mapping for an
  operation ID that no longer exists all fail generation.
- A tag's class name matches its service (`Operation.Characters` ↔ `ICharacters`), and each constant's name is also
  the service **method** name, so one `operationConstants` entry names both.

## Route table
`EsiRoutes.All` is a single static dictionary from operation ID to `RouteDefinition`, keyed by the `Operation.*`
constants. It's generated **split by tag** so each file stays small and a spec change only touches the tags it affects:

- `EsiRoutes.g.cs`: the aggregator. It calls each tag's registration method explicitly; there is no reflection or
  assembly scanning (trim and AOT safe), so adding a tag is just another generated line.
- `EsiRoutes.<Tag>.g.cs`: one private `Register<Tag>` method per tag, adding that tag's routes (ordered by operation ID).
  `routes.Add` throws on a duplicate key.

```csharp
// EsiRoutes.g.cs
public static IReadOnlyDictionary<string, RouteDefinition> All { get; } = Build();

private static FrozenDictionary<string, RouteDefinition> Build()
{
    var routes = new Dictionary<string, RouteDefinition>(StringComparer.Ordinal);
    RegisterAccessList(routes);
    RegisterAssets(routes);
    // … one line per tag
    return routes.ToFrozenDictionary(StringComparer.Ordinal);
}

// EsiRoutes.Assets.g.cs
private static void RegisterAssets(Dictionary<string, RouteDefinition> routes)
{
    routes.Add(Operation.Assets.GetCharactersCharacterIdAssets, new(
        Operation.Assets.GetCharactersCharacterIdAssets,
        HttpMethod.Get,
        $"/characters/{{{Parameter.Route.CharacterId}}}/assets",
        scope: "esi-assets.read_assets.v1",
        cache: new RouteCacheHints(TimeSpan.FromSeconds(3600), RouteCacheMode.TtlBased),
        rateLimit: new RateLimitPolicy("char-asset", 1800, TimeSpan.FromMinutes(15))));
}
```
Mapped from the spec: `operationId` → `Operation.*` constant, used as key and `OperationId`, method + path → `Method` / `PathTemplate`,
`security` → `scope`, `x-cache-age` / `x-server-cache-mode` / `x-cache-mode` / `x-tombstone-ttl` → `cache`,
`x-rate-limit` (`group`, `max-tokens`, `window-size`) → `rateLimit`.
Parameters, pagination (`X-Pages`, `x-pagination`), `requestBody` and `x-required-roles` are **not** on the route:
parameters, pagination and body are expressed in the service signature; roles aren't used yet.
Routes carry no types: response and body types are emitted into the **service** (below).
Shared header parameters (`IfNoneMatch`, `AcceptLanguage`, `CompatibilityDate`, `Tenant`, `IfModifiedSince`)
are **not** emitted as route parameters. The compatibility date becomes a generated constant,
`EsiSpec.CompatibilityDate` in `NewEdenKit.ESI.Routes/Generated/EsiSpec.g.cs`, which `AddEveEsi()` sends as
`X-Compatibility-Date`. It is not a user option.

## Services
`NewEdenKit.ESI/Generated/` holds, per spec tag, a public `I<Tag>.g.cs` interface and an `internal sealed`
`<Tag>Service.g.cs` implementation deriving from `EsiService<I<Tag>>`, plus `EsiServices.g.cs`, which registers them
all. Namespace: `NewEdenKit.ESI.Services`. Services are request builders: each operation becomes one method that calls
the factory and returns `IEsiRequest<T>`:

```csharp
/// <inheritdoc />
public IEsiRequest<IReadOnlyList<MarketsRegionIdOrdersGetItem>> GetMarketsRegionIdOrders(long regionId, GetMarketsRegionIdOrdersOrderType orderType, long? typeId = null, int? page = null) =>
    CreateEsiClient<IReadOnlyList<MarketsRegionIdOrdersGetItem>>(
        Operation.Market.GetMarketsRegionIdOrders,
        EsiJsonContext.Default.ListOfMarketsRegionIdOrdersGetItem,
        p =>
        {
            p.Route.Set(Parameter.Route.RegionId, regionId);
            p.Query.Set(Parameter.Query.OrderType, GetMarketsRegionIdOrdersOrderTypeConverter.ToWire(orderType));
            p.Query.Set(Parameter.Query.TypeId, typeId);
            p.ApplyPagination(page);
        },
        null);
```

- **Grouping:** one service per spec tag. Never group by URL segment.
- **Method name:** the operation's `Operation` constant name (from `operationConstants`, else the operation ID).
  Mapped names leave out the HTTP verb (`CharacterInfo`, not `GetCharacterInfo`), except to tell apart two
  operations on the same path. The
  method passes that constant, never a string literal. `Use` is reserved for the middleware API and fails generation.
- **Signature order:** route parameters (in path order), required query parameters, `body`, `string token` (secured
  only), optional query parameters, then `int? page` or `CursorPage? cursor`.
- **Parameter names:** the wire name in camelCase (`character_id` → `characterId`); C# keywords get an `@`. `p`,
  `token`, `body`, `page` and `cursor` are reserved, and two parameters mapping to the same name fail generation.
- **Parameter types:** resolved by the model builder like any property (`ModelSet.ParameterTypes`), so they follow the
  spec's `format`. Only primitives, enums and lists of those can be sent; anything else fails generation.
  Required parameters are non-nullable; optional ones are nullable, default to `null`, and aren't sent when null. ESI's
  own default goes in the parameter's XML doc, never into the C# default.
- **Enums** go on the wire through their converter: `XConverter.ToWire(x)`,
  `x.HasValue ? XConverter.ToWire(x.Value) : null` or `xs.Select(XConverter.ToWire).ToArray()`. The client never
  formats an enum itself.
- **Pagination:** a `page` query parameter gives `int? page`; `before` + `after` + `limit` give `CursorPage? cursor`. A
  required pagination parameter, or any other combination, fails generation.
- **Body:** `p.Body(body, EsiJsonContext.Default.<TBody>)`. All ESI bodies are required; an optional body fails
  generation until it's given an override.
- **Guards:** required reference arguments (string route values, lists, bodies) get `ArgumentNullException.ThrowIfNull`,
  so the method has a block body. The token is checked by the factory.
- **Response type:** the method's return type `IEsiRequest<T>`, the explicit generic in `CreateEsiClient<T>(...)` and
  the `EsiJsonContext.Default.<T>` type info are emitted from the same resolved response type. Always write `T`
  explicitly; never rely on inference. An endpoint without a response body gives `IEsiRequest<EsiNoContent>` and the
  non-generic `CreateEsiClient(...)`.
- **Middleware:** each interface is `I<Tag> : IEsiService<I<Tag>>`, so it gets both `Use` overloads (instance and async
  callback) and `Use` returns `I<Tag>`.
- **Docs:** the summary comes from the operation's `summary`; the remarks give the method and path, the `description`
  and the scope; each parameter's doc comes from its `description`.
- **Usings:** each file imports only the namespaces it uses, including each model tag namespace. So no model may be
  named like a library type the services see (`Operation`, `Parameter`, `EsiJsonContext`, `I<Tag>`, `<Tag>Service`,
  …); a clash fails generation.
- **Registration:** `EsiServices.AddGeneratedServices(IServiceCollection)` is internal and calls
  `TryAddTransient<I<Tag>, <Tag>Service>()` for every tag; `AddEveEsi()` calls it.

## Hand-written extensions
- Every generated type is `partial`. Put additions in a non-`.g.cs` file alongside it.
- Never edit `*.g.cs` by hand.

## Output requirements
- Deterministic: stable ordering (by tag, then path, then name) and stable formatting, so diffs only show real API changes.
- Every DTO, enum, union and request-body type is registered in the source-generated `EsiJsonContext`.
- XML doc comments come from the spec's `summary` / `description`.

## Running it
```
dotnet run --project tools/NewEdenKit.ESI.Generator -- --spec openapi.json --out src/
dotnet format analyzers src/NewEdenKit.ESI.Routes/NewEdenKit.ESI.Routes.csproj --diagnostics RS0016 --severity info --include-generated
```
Generated code goes to `NewEdenKit.ESI.Routes`, `NewEdenKit.ESI.Models` and `NewEdenKit.ESI` (services), so the second step runs
for all three.
- The first command writes `*.g.cs` and deletes stale `*.g.cs` in the directories it owns. Exit codes: `0` success,
  `1` bad arguments or missing files, `2` generation failed (the message says what to map in `generator.json`).
- The second records new public API in `PublicAPI.Unshipped.txt`. The analyzer's fix skips generated code unless
  `--include-generated` is passed. Repeat it for each project with generated code.

## Tests
`tests/NewEdenKit.ESI.Generator.Tests` runs the generator on small fixture specs (`Fixtures/`) and compares the output
with committed snapshots (`Snapshots/`, excluded from compilation). After an intended output change, regenerate the
snapshots with `UPDATE_SNAPSHOTS=1 dotnet test tests/NewEdenKit.ESI.Generator.Tests` and review the diff. Every failure
mode has its own test. `NewEdenKit.ESI.Routes.Tests` checks the committed real output (keys match operation IDs, every
placeholder is a known route parameter). `NewEdenKit.ESI.Tests` drives every generated service method through the real
factory, route table and HTTP client, and checks that it calls its own operation with every route value filled in.

## Updating to a new spec
1. Replace `openapi.json`.
2. Run the generator and the public API step above. Fix any failures by updating `generator.json` (names, enum
   members, overrides).
3. Review the diff: new endpoints, changed fields, removed routes.
4. Run tests, then commit spec + config + output together.
