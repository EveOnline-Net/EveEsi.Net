---
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/*.props"
---

# C# style

## Project settings (Directory.Build.props, don't override per project)
- `TargetFrameworks`: `net8.0;net9.0;net10.0`, `LangVersion` 12
- `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` true
- `GenerateDocumentationFile` true on packable projects
- `IsAotCompatible` / `IsTrimmable` true on all `src/` projects
- Package versions only in `Directory.Packages.props` (central package management)

## API surface
- Only what consumers need is `public`. Implementations are `internal sealed`.
  Use `InternalsVisibleTo` for tests.
- Every public member has XML docs.
- Async methods end in `Async`, return `Task`/`ValueTask`, and take `CancellationToken` as the last
  parameter (`ct` or `cancellationToken`, defaulted only on public entry points).
- No .NET 9+ only APIs unless guarded with `#if NET9_0_OR_GREATER` and given a net8 fallback.
- No reflection-based serialization or `Activator`. Everything must work trimmed/AOT.

## Code
- File-scoped namespaces matching the folder: `EveEsi.Net.<Project>.<Folder>`.
- One public type per file (generated files may group nested types).
- Use primary constructors for DI-injected services.
- Prefer `record` / `sealed record` for data, `readonly record struct` for small value types (`CursorPage`).
- Access tokens are plain `string`s everywhere. Don't introduce a token wrapper type.
- Use `ValueTask` on hot pipeline paths (middleware), `Task` elsewhere.
- `ConfigureAwait(false)` on every `await` in library code.
- Guard public inputs with `ArgumentNullException.ThrowIfNull` / `ArgumentOutOfRangeException.ThrowIf…`.
- No exceptions for HTTP status. Return `EsiResult<T>` (see docs/architecture.md).
- `var` when the type is obvious from the right-hand side.
- Comments explain *why*, not *what*.
