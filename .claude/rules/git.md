# Git and releases

Full detail: [docs/contributing.md](../../docs/contributing.md), [docs/releasing.md](../../docs/releasing.md).

- Never commit or push to `main` directly. Create a branch: `feat/`, `fix/`, `spec/<compat-date>`, `docs/`, `chore/`.
- Commit or push only when the user asks.
- PR titles use Conventional Commits (`feat(scope): …`, `fix: …`, `!` for breaking). PRs are squash-merged.
- Never create, move or push `v*` tags unless the user explicitly asks. A tag is a NuGet release.
- Never put version numbers in `.csproj`/`.props`. MinVer takes them from tags.
- Any change to the public API must update `PublicAPI.Unshipped.txt` in the same PR.
- When a change is breaking (including ESI removing or changing an endpoint or field), say so in the PR title (`!`)
  and description. Before 1.0, breaking means a minor bump; from 1.0, a major bump. No `[Obsolete]` shims for ESI removals.
- Spec updates: spec + `generator.json` + regenerated code in one `spec/<compat-date>` PR, never mixed with other work.
