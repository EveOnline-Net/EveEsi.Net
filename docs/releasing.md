# Releasing

## Principles
- **One version for both packages.** `NewEdenKit.ESI` and `NewEdenKit.ESI.Abstractions` are always released
  together with the same version.
- **Releases are driven by tags.** Pushing a `vX.Y.Z` tag on `main` is the release. Nothing else publishes to nuget.org.
- **Versions come from git** via [MinVer](https://github.com/adamralph/minver). No version numbers are
  written in project files.
- **SemVer**, starting at `0.1.0`.

## Version numbers

### Before 1.0 (`0.x`)
The public API isn't stable yet.

| Change | Bump | Example |
|---|---|---|
| Breaking (incl. ESI removing/changing an endpoint or field) | minor | `0.3.2` → `0.4.0` |
| Additive or fix | patch | `0.3.2` → `0.3.3` |

### From 1.0 on
`1.0.0` is tagged once the public API is considered stable.

| Change | Bump |
|---|---|
| Breaking: endpoint removed, field removed/type changed, required parameter added, enum value removed, any breaking change to our own API | **major** |
| Additive: new endpoint, new optional field/parameter, new enum value, new middleware | minor |
| Fixes, docs, internal changes | patch |

**ESI changes count the same as our own changes.** If ESI removes or changes something, the generated API
changes and the version reflects that. There's no deprecation period.

### How breaking changes are detected
- `Microsoft.CodeAnalysis.PublicApiAnalyzers`: every public API change shows up in
  `PublicAPI.Unshipped.txt` in the PR diff.
- `EnablePackageValidation` with `PackageValidationBaselineVersion` set to the last released version:
  the build fails on breaking changes unless they're expected. From `1.0` on, this baseline is what
  stops a breaking change from shipping in a minor or patch.
- Before `0.1.0` is released there's no baseline, so package validation is off until then.

## Prereleases (on demand)
There are no automatic prereleases. When a preview is needed, tag `main` with a prerelease suffix:

```
git tag v0.4.0-preview.1
git push origin v0.4.0-preview.1
```
The same release workflow publishes it to nuget.org as a prerelease. A preview leaves the public API files as they
are: entries stay in `PublicAPI.Unshipped.txt` until a stable `vX.Y.Z` ships, since a preview's API may still change.
Projects that aren't implemented yet set `<IsPackable>false</IsPackable>` so a release never publishes an empty
package.

Untagged CI builds get MinVer's height-based versions (e.g. `0.4.1-alpha.0.3`). They're build artefacts only
and are never published.

## Release steps
1. Make sure `main` is green and contains everything for the release.
2. Decide the version using the tables above. Check the PublicAPI diffs since the last tag.
3. Move the entries from `PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt` in every project (a `chore: prepare vX.Y.Z` PR).
4. Tag and push (only the `maintainers` team can create `v*` tags; the "Protect release tags" ruleset blocks
   everyone else):
   ```
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```
5. The `release` GitHub Actions workflow, triggered by `v*` tags, runs in the `nuget` environment. It **waits for a
   maintainer to approve the run** (Actions → the run → Review deployments), then:
   - builds and tests every target
   - packs the two packages, `NewEdenKit.ESI` (with Models, Routes, Client and Middleware inside) and
     `NewEdenKit.ESI.Abstractions`, with the version from the tag
   - publishes `.nupkg` + `.snupkg` to nuget.org using **NuGet Trusted Publishing** (OIDC, no stored API key). The
     nuget.org trusted-publishing policy must name `NewEdenKit/NewEdenKit.ESI`, `release.yml` and the `nuget`
     environment.
   - creates a GitHub Release with generated notes (from squash-merged PR titles)
6. After the release, set `PackageValidationBaselineVersion` to the new version (in `Directory.Build.props`).

The release notes always state the ESI compatibility date the release targets.

## Package requirements
All packable projects have, set in `Directory.Build.props`:
- `PackageId` = project name, shared `Authors`
- `PackageLicenseExpression` = `GPL-3.0-or-later` (full text in `LICENSE`)
- `PackageProjectUrl` / `RepositoryUrl` = `https://github.com/NewEdenKit/NewEdenKit.ESI`, `RepositoryType` = `git`
- `PackageReadmeFile`, `PackageIcon`, `PackageTags` (`eve-online;esi;eve;api-client`)
- SourceLink, `ContinuousIntegrationBuild` in CI, deterministic builds
- `IncludeSymbols` + `SymbolPackageFormat=snupkg`
- `GenerateDocumentationFile`

`tools/` and `tests/` projects set `IsPackable=false`.

## Servicing older versions
Normally there's only one supported line: the latest. If a fix is needed for an older line after a newer
one has shipped, branch `release/X.Y` from that line's last tag, fix it there, and tag `vX.Y.Z+1` from that branch.
