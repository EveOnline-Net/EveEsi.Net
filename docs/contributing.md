# Contributing

EveEsi.Net is hosted on GitHub at <https://github.com/EveOnline-Net/EveEsi.Net>, in the `EveOnline-Net` organization.
Contributions come in through pull requests against `main`.

**License:** the project is GPL-3.0-or-later (see `LICENSE`). By opening a PR, you agree that your
contribution is licensed under the same terms.

## Branching (trunk-based)

```
main ──●──●──●──●──●──●──▶   protected, always releasable
        \  /    \  /   \  /
    feat/…   fix/…   spec/2026-08-18      short-lived branches → PR → squash-merge
```

- `main` is protected by the "Protect main" ruleset. No direct pushes, force-pushes or deletion; every change goes
  through a PR that needs:
  - a green `build` check on a branch that is up to date with `main`;
  - **one approval from a code owner**, i.e. a member of `@EveOnline-Net/maintainers` (see `.github/CODEOWNERS`).
    The approval must come from someone other than whoever pushed last, and a new push dismisses it;
  - every review thread resolved.
- Maintainers can bypass the review requirement when merging a PR (never with a direct push). That exists so a
  maintainer's own PRs can be merged while there's only one maintainer; GitHub records each bypass on the PR.
- Branch from `main`, keep branches short-lived, and rebase or merge `main` in if they fall behind.
- Branch names:

| Prefix | Use |
|---|---|
| `feat/<short-name>` | new functionality |
| `fix/<short-name>` | bug fixes |
| `spec/<compat-date>` | ESI spec update (e.g. `spec/2026-08-18`) |
| `docs/<short-name>` | documentation only |
| `chore/<short-name>` | build, CI, tooling, dependencies |
| `release/<major>.<minor>` | **only** when an older line needs a fix after a newer one has shipped |

- PRs are **squash-merged**, so the PR title becomes the commit on `main`.

## Commit and PR titles (Conventional Commits)

The squash-merge PR title must follow Conventional Commits. Commits inside the branch can be anything.

```
feat(middleware): add ETag middleware
fix(client): send X-Tenant on every request
chore(spec): update to compatibility date 2026-08-18
feat(spec)!: update to 2026-11-01 (removes /characters/{id}/foo)
docs: describe pagination
```
- Types: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `perf`, `ci`.
- Common scopes: `client`, `middleware`, `routes`, `models`, `generator`, `spec`, `caching`, `di`.
- `!` after the type/scope marks a **breaking change**. The PR description must say what breaks.

## Pull request checklist

CI enforces these:
- [ ] `dotnet build` passes on all targets (`net8.0`, `net9.0`, `net10.0`) with no warnings.
- [ ] `dotnet test` passes.
- [ ] `dotnet format --verify-no-changes` passes.
- [ ] Public API changes are recorded in `PublicAPI.Unshipped.txt` (PublicApiAnalyzers).
- [ ] Package validation against the last released version passes, or the PR is marked breaking (`!`).

The reviewer also checks:
- [ ] No hand edits to `*.g.cs`.
- [ ] Tests added or updated for the change.
- [ ] Docs updated if behaviour or design changed (`docs/`, decision log for new decisions).

## Updating the ESI spec (community welcome)

The spec isn't updated automatically. When ESI changes, anyone can open a spec PR:

1. Branch `spec/<new-compat-date>` from `main`.
2. Replace `openapi.json` with the new spec from ESI.
3. Run the generator, then record the public API changes (see [generator.md](generator.md#running-it)):
   `dotnet run --project tools/EveEsi.Net.Generator -- --spec openapi.json --out src/`
4. If generation fails, fix it in `generator.json` (names, enum members, overrides). Never by editing output.
5. Build and test. Update `PublicAPI.Unshipped.txt` for the API changes.
6. Open the PR with the spec, `generator.json` and the regenerated code **together**. In the description, list:
   new endpoints, changed fields, removed endpoints, and whether it's breaking.

**We consume ESI; we don't shape it.** If ESI removes or changes an endpoint, the library follows straight away.
There's no deprecation period or `[Obsolete]` shim. The version bump says it instead (see [releasing.md](releasing.md)).
