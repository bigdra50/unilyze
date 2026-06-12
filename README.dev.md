# unilyze Developer Guide

Maintenance, implementation, validation, and release notes for `unilyze`.
For installation and usage, see [README.md](README.md).

## Requirements

- unilyze supports `.NET 8.0 or later`
- A single latest SDK is sufficient for daily development. Current standard: `.NET SDK 10.0.103`
- Install `net8.0;net10.0` runtimes only when running the full local test matrix

CI matrix: `net8.0;net10.0`.

### .NET version support policy

Supported runtimes are the **current LTS** and the **previous LTS** (until its EOL). EOL'd STS releases are dropped in the next minor release. As of 2026-06, supported TFMs are `net8.0` and `net10.0`; `net9.0` (STS, EOL) has been removed.

### TFM and Roslyn maintenance

**Adding or removing a TFM**

1. Edit `<TargetFrameworks>` in [src/Unilyze/Unilyze.csproj](src/Unilyze/Unilyze.csproj) and [tests/Unilyze.Tests/Unilyze.Tests.csproj](tests/Unilyze.Tests/Unilyze.Tests.csproj).
2. Update the `test` job matrix in [.github/workflows/ci.yml](.github/workflows/ci.yml) (add or remove the matching `dotnet-version` / `framework` pair).
3. Update the inline comment in `ci.yml` that documents the multi-target TFMs (near the `quality-gate` job).
4. Update [README.md](README.md) policy note, this section, and the [Release Checklist](#release-checklist) below.
5. Add a [CHANGELOG.md](CHANGELOG.md) entry under `[Unreleased]` → `### Changed`, including global-tool impact when dropping a runtime.

**Roslyn / `Microsoft.CodeAnalysis` packages**

- Track the latest stable release on NuGet; perform major-version bumps only after reviewing Roslyn release notes and API breaking changes.
- Current pin: `Microsoft.CodeAnalysis.CSharp` **4.12.0**. Latest stable: **5.3.0**. Bumping the package version is separate work from TFM policy changes.

## Repository Map

- [src/Unilyze](src/Unilyze): CLI main project
- [scripts/release-smoke.sh](scripts/release-smoke.sh): Release smoke test for standard `.NET tool` workflow
- [packaging](packaging): Homebrew formula and Scoop manifest templates rendered by the release workflow
- [tests/Unilyze.Tests](tests/Unilyze.Tests): xUnit tests
- [docs/metrics.md](docs/metrics.md): Metric definitions
- [.github/workflows/ci.yml](.github/workflows/ci.yml): CI / pack smoke / CodeHealth gate
- [action.yml](action.yml): Official composite GitHub Action (Marketplace candidate)

## GitHub Action

The composite action lives at the repository root ([action.yml](action.yml)) so the CLI and action ship from one repo. Consumers reference `bigdra50/unilyze@v1` after the first action release.

### Tag scheme (NuGet vs Marketplace)

NuGet publish ([`.github/workflows/publish.yml`](.github/workflows/publish.yml)) triggers only on semver tags matching `v[0-9]+.[0-9]+.[0-9]+` (for example `v0.3.0`). This avoids accidental NuGet releases when pushing action-only tags.

| Tag kind | Example | Purpose |
|----------|---------|---------|
| NuGet release | `v0.3.0` | Immutable semver tag; package version is derived from the tag via MinVer |
| Action release (immutable) | `action-v1.0.0` | Pin a specific action revision for Marketplace |
| Action major (floating) | `v1` | Consumer default (`uses: bigdra50/unilyze@v1`); moved on each action release |

**Release flow (action, not yet on Marketplace):**

1. Merge action changes to `main`.
2. Tag `action-v1.0.0` (or the next patch) pointing at the release commit.
3. Force-move the floating `v1` tag to the same commit: `git tag -f v1 && git push -f origin v1`.
4. Confirm `publish.yml` did **not** run for the `v1` push (`gh run list --workflow publish.yml --limit 3`).
5. When ready for Marketplace: create a GitHub Release from `action-v1.x.y` and check "Publish this Action to the GitHub Marketplace".

Self-test workflow: [`.github/workflows/action-selftest.yml`](.github/workflows/action-selftest.yml) runs `actionlint` and dogfoods `uses: ./` with a locally packed NuGet tool.

Third-party `uses:` in the action are limited to `actions/setup-dotnet@v4` (SARIF upload stays in the consumer workflow). Sticky PR comments use `gh api`, not a third-party comment action.

## CI CodeHealth Gate

The `quality-gate` job in [`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs `unilyze badge --metric codehealth --fail-under` against `src/Unilyze` (SyntaxOnly analysis, matching [`.github/workflows/badges.yml`](.github/workflows/badges.yml)). The threshold is set to the measured floor of min CodeHealth so current main passes while a drop in the worst type fails CI. Re-measure and adjust `--fail-under` when refactoring intentionally improves or reshapes the metric baseline.

## Local Validation

### Tests

Running the following is normally sufficient:

```bash
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net10.0 --no-restore -v minimal
```

Run `net8.0` additionally only for local compatibility checks:

```bash
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net8.0 --no-restore -v minimal
```

To run with restore:

```bash
dotnet restore tests/Unilyze.Tests/Unilyze.Tests.csproj
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net10.0 -v minimal
```

### Golden corpus (metrics compatibility)

`tests/Unilyze.Tests/GoldenCorpusTests.cs` analyzes the fixed fixture at `tests/fixtures/golden/` and compares normalized JSON output against `tests/fixtures/golden/expected.json`. CI fails on any unintended metric drift.

The test writes `Golden.csproj` at runtime (Reference + HintPath) so analysis elevates to CoreEngine and semantic metrics (boxing, CBO, DIT) are pinned even without Unity installed.

If metric values move intentionally, confirm the change, then regenerate the baseline and update the compatibility artifacts together:

```bash
UNILYZE_GOLDEN_UPDATE=1 dotnet test tests/Unilyze.Tests -f net10.0 --filter GoldenCorpus
```

Review the `expected.json` diff in your PR. When the change alters measured values, also bump `AnalysisResult.CurrentMetricsVersion` and add a `[metrics]` entry to `CHANGELOG.md` per the [Metric Compatibility Policy](docs/metrics.md#メトリクス互換性ポリシー). Do not auto-regenerate in CI.

### Pack / Install Smoke

Release readiness is determined by [scripts/release-smoke.sh](scripts/release-smoke.sh), which validates the standard `.NET tool` workflow.

This script does not override `DOTNET_ROOT`. It verifies `dotnet tool install --tool-path ...` and generated shim execution in the calling shell environment.

On local macOS, the default `dotnet pack` may hang on the `PackAsTool` parallel pack path. If this occurs, disable parallelism:

```bash
dotnet restore src/Unilyze/Unilyze.csproj
dotnet build src/Unilyze/Unilyze.csproj -c Release --no-restore
dotnet msbuild src/Unilyze/Unilyze.csproj -t:Pack -p:Configuration=Release -p:NoBuild=true -p:PackageOutputPath="$PWD/artifacts/nupkg" -m:1 -p:BuildInParallel=false
bash scripts/release-smoke.sh --package-source ./artifacts/nupkg --version 0.1.0
```

Using `dotnet pack` via the normal path:

```bash
dotnet restore src/Unilyze/Unilyze.csproj
dotnet pack src/Unilyze/Unilyze.csproj -c Release -o ./artifacts/nupkg
bash scripts/release-smoke.sh --package-source ./artifacts/nupkg --version 0.1.0
```

### Self-contained binary publish

The semver-tag release workflow publishes untrimmed, self-contained, compressed single-file binaries for `osx-arm64`, `osx-x64`, `linux-x64`, and `win-x64`.
These binaries use `net10.0`, but users do not need a .NET SDK or runtime because the runtime is bundled.

Roslyn is not trimming-safe, so trimming is intentionally disabled.
Framework-dependent single-file binaries were rejected because they retain a runtime prerequisite.
An in-memory metadata-reference fallback was also rejected because it would add a second runtime reference-resolution path with metric compatibility risk.

`IncludeAllContentForSelfExtract=true` is required.
Without it, bundled framework assemblies have no stable on-disk locations and `DotnetRuntimeReferenceResolver` can silently degrade non-Unity analysis from Complete to Syntax.
The runtime extracts bundle content under `$HOME/.net` on Unix-like systems and `%TEMP%/.net` on Windows on first use.
Set `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to override the extraction root when the default home directory is not writable.

Local host-RID verification:

```bash
dotnet publish src/Unilyze/Unilyze.csproj -c Release -f net10.0 -r osx-arm64 \
  --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeAllContentForSelfExtract=true -o artifacts/publish/osx-arm64
./artifacts/publish/osx-arm64/Unilyze --version
```

Each RID job writes the uncompressed binary size and archive size to the GitHub Actions job summary.
Record the first tagged release measurements here:

| RID | Binary size | Archive size |
| --- | ---: | ---: |
| `osx-arm64` | Pending a network-enabled publish | Recorded by release job |
| `osx-x64` | Recorded by release job | Recorded by release job |
| `linux-x64` | Recorded by release job | Recorded by release job |
| `win-x64` | Recorded by release job | Recorded by release job |

The accepted tradeoff is a larger download in exchange for an installation path with no .NET prerequisite.

### Homebrew tap and Scoop manifest

The tracked files [packaging/unilyze.rb](packaging/unilyze.rb) and [packaging/unilyze.json](packaging/unilyze.json) contain release placeholders.
The release workflow replaces the version and SHA256 placeholders, validates the rendered files, and attaches `unilyze.rb` and `unilyze.json` to the GitHub Release.

Create the Homebrew tap once:

1. Create the public repository `bigdra50/homebrew-tap`.
2. Create a `Formula/` directory in that repository.
3. Copy the rendered release asset to `Formula/unilyze.rb`.
4. Commit and push the formula in the tap repository.

For every release:

1. Download the rendered `unilyze.rb` and `unilyze.json` assets from the GitHub Release.
2. Replace `Formula/unilyze.rb` in `bigdra50/homebrew-tap`.
3. Replace `packaging/unilyze.json` in this repository with the rendered manifest so its raw GitHub URL is installable by Scoop.
4. Verify `brew install bigdra50/tap/unilyze` and the documented Scoop install command on machines without .NET.

Automating the tap update with a cross-repository personal access token is intentionally deferred.

## Current Implementation Notes

### Type Identity

Internal references use `TypeId` rather than simple names.

- Format: `Assembly::Namespace.Outer+Inner`
- `QualifiedName` is used for display purposes
- Dependencies, coupling, diff, HTML nodes, and partial merge are `TypeId`-based

Related files:

- [src/Unilyze/TypeIdentity.cs](src/Unilyze/TypeIdentity.cs)
- [src/Unilyze/TypeInfo.cs](src/Unilyze/TypeInfo.cs)
- [src/Unilyze/AnalysisPipeline.cs](src/Unilyze/AnalysisPipeline.cs)

### Type Relationship Resolution

No `I[A-Z]` naming heuristics are used.

- Treated conservatively in syntax-only mode
- When SemanticModel is available, `INamedTypeSymbol.TypeKind` distinguishes base types from interfaces

Related tests:

- [tests/Unilyze.Tests/AnalysisPipelineTests.cs](tests/Unilyze.Tests/AnalysisPipelineTests.cs)
- [tests/Unilyze.Tests/TypeAnalyzerTests.cs](tests/Unilyze.Tests/TypeAnalyzerTests.cs)

### asmdef GUID Resolution

GUIDs are extracted from `.asmdef.meta` files to resolve `references: ["GUID:..."]`. Unresolvable GUIDs are retained, not discarded.

### Implicit Assembly-CSharp Detection

When `.asmdef` files exist but some `.cs` files under Assets are not covered by any asmdef directory, those files are automatically collected as the implicit `Assembly-CSharp` assembly. This matches Unity's default behavior where loose scripts belong to `Assembly-CSharp`. The detection uses directory-based exclusion to avoid double-counting files already covered by an asmdef.

Related files:

- [src/Unilyze/AsmdefInfo.cs](src/Unilyze/AsmdefInfo.cs)
- [tests/Unilyze.Tests/AsmdefInfoTests.cs](tests/Unilyze.Tests/AsmdefInfoTests.cs)

### HTML Viewer

Normally outputs a Cytoscape-based interactive graph. Falls back to a built-in offline report when external assets cannot be loaded.

- `--no-open` suppresses automatic browser launch
- The offline fallback still shows types, dependencies, hotspots, cycles, and assembly coupling
- Graph assets are not yet fully self-contained. This limitation is documented in the README

Related files:

- [src/Unilyze/Program.cs](src/Unilyze/Program.cs)
- [src/Unilyze/HtmlTemplate.cs](src/Unilyze/HtmlTemplate.cs)
- [tests/Unilyze.Tests/CliE2eTests.cs](tests/Unilyze.Tests/CliE2eTests.cs)

## Release Checklist

1. Green `dotnet test` on `net8.0` / `net10.0`
2. Green CI matrix on `net8.0` / `net10.0`
3. Pass pack/install smoke
4. Confirm README / docs / package metadata match the implementation
5. Confirm HTML fallback and `--no-open` are not broken
6. Apply the [Metric Compatibility Policy](docs/metrics.md#メトリクス互換性ポリシー): a patch release must not change metric values; a change to any metric definition requires at least a minor bump, a release note describing which metrics move in which direction, and a refreshed `scripts/crossval` validation in `docs/metrics.md`
7. If you changed a metric-calculation file, verify the `metricsVersion` bump (`AnalysisResult.CurrentMetricsVersion`) and CHANGELOG `[metrics]` entry
8. Update [CHANGELOG.md](CHANGELOG.md) before tagging (move `[Unreleased]` entries into a new `## [X.Y.Z]` section and set the release date). The publish workflow fails if this section is missing for the tagged version.
9. After the release workflow completes, copy the rendered `unilyze.rb` to `bigdra50/homebrew-tap/Formula/unilyze.rb`
10. Replace `packaging/unilyze.json` with the rendered release asset and verify Homebrew and Scoop installation without .NET

## NuGet Publish

Publishing is automated on semver tag push. Local API key storage is not assumed.

The current workflow uses the repository secret `NUGET_API_KEY`.
The release operator should migrate it to [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) as follows:

1. Sign in to nuget.org and open the account's **Trusted Publishing** page.
2. Add a GitHub policy for owner `bigdra50`, repository `unilyze`, and workflow file `publish.yml`. Enter only the file name, not `.github/workflows/`.
3. Optionally create a protected GitHub environment such as `release`, add required reviewers, set `environment: release` on the publish job, and enter the same environment in the NuGet policy.
4. Add `id-token: write` to the publish job permissions. Keep `contents: write` for GitHub Release creation.
5. Immediately before `dotnet nuget push`, add `NuGet/login@v1` with the nuget.org profile name and use its `NUGET_API_KEY` output for the push. Request the temporary key late because it is valid for one hour and each OIDC token can be exchanged only once.
6. Run `workflow_dispatch` to validate build, test, pack, and release smoke without publishing.
7. Push a release tag and verify package publication and GitHub Release creation.
8. After a successful Trusted Publishing release, delete the long-lived `NUGET_API_KEY` repository secret and remove the API-key preflight step from `.github/workflows/publish.yml`.

This issue documents the migration only.
The release operator must perform the nuget.org policy creation, workflow switch, first trusted publish, and secret removal.

Publish procedure:

1. Ensure the `CI` workflow is green on the target commit
2. Update `CHANGELOG.md` (Release Checklist step 8)
3. Create and push a semver tag: `git tag vX.Y.Z && git push origin vX.Y.Z`
4. The `Publish NuGet` workflow runs `net10.0` test, pack, release smoke, `dotnet nuget push`, and creates a GitHub Release whose body is the matching `CHANGELOG.md` section
5. Four matrix jobs publish and archive self-contained binaries, recording binary and archive sizes in their job summaries
6. The release-assets job creates `SHA256SUMS`, renders the Homebrew formula and Scoop manifest, and uploads all assets to the GitHub Release

Dry-run (no NuGet push, no GitHub Release upload): manually trigger the `Publish NuGet` workflow from Actions (`workflow_dispatch`).
The dry-run still builds all four binaries, runs the Linux bundle smoke, generates checksums, and validates the rendered package files.

Publish workflow:

- [`.github/workflows/publish.yml`](.github/workflows/publish.yml)
- Current secret name until migration: `NUGET_API_KEY`

## Known Local Caveats

- On macOS, the default parallel path of `dotnet pack` may hang
- `dotnet msbuild ... -t:Pack -m:1 -p:BuildInParallel=false` works around this
- `GenerateNuspec` alone and `dotnet tool install` work fine, so this is treated as a pack execution path issue, not package corruption
- If some runtimes are not installed locally, defer final verification for those TFMs to CI
- CLI E2E tests use `dotnet <Unilyze.dll>` instead of the apphost directly, to align runtime resolution with `dotnet test`
- Environments with multiple `dotnet` install roots may expose shim execution issues in the release smoke. The script does not work around this
