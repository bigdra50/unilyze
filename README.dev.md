# unilyze Developer Guide

Maintenance, implementation, validation, and release notes for `unilyze`.
For installation and usage, see [README.md](README.md).

[日本語版 / Japanese](README.dev_JP.md)

## Requirements

- unilyze supports `.NET 8.0 or later`
- A single latest SDK is sufficient for daily development. Current standard: `.NET SDK 10.0.103`
- Install `net8.0;net9.0;net10.0` runtimes only when running the full local test matrix

CI matrix: `net8.0;net9.0;net10.0`.

## Repository Map

- [src/Unilyze](src/Unilyze): CLI main project
- [scripts/release-smoke.sh](scripts/release-smoke.sh): Release smoke test for standard `.NET tool` workflow
- [tests/Unilyze.Tests](tests/Unilyze.Tests): xUnit tests
- [docs/metrics.md](docs/metrics.md): Metric definitions
- [.github/workflows/ci.yml](.github/workflows/ci.yml): CI / pack smoke

## Local Validation

### Tests

Running the following is normally sufficient:

```bash
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net10.0 --no-restore -v minimal
```

Run `net8.0` / `net9.0` additionally only for local compatibility checks:

```bash
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net9.0 --no-restore -v minimal
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net8.0 --no-restore -v minimal
```

To run with restore:

```bash
dotnet restore tests/Unilyze.Tests/Unilyze.Tests.csproj
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net10.0 -v minimal
```

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

1. Green `dotnet test` on `net9.0` / `net10.0`
2. Green CI matrix on `net8.0` / `net9.0` / `net10.0`
3. Pass pack/install smoke
4. Confirm README / docs / package metadata match the implementation
5. Confirm HTML fallback and `--no-open` are not broken

## NuGet Publish

Publishing is done via GitHub Actions. Local API key storage is not assumed.

Set the repository secret `NUGET_API_KEY` beforehand.

Publish procedure:

1. Ensure the `CI` workflow is green on the target commit
2. Manually trigger the `Publish NuGet` workflow from Actions
3. The workflow runs `net10.0` test, pack, release smoke, and `dotnet nuget push` in sequence

Publish workflow:

- [`.github/workflows/publish.yml`](.github/workflows/publish.yml)
- Secret name: `NUGET_API_KEY`

## Known Local Caveats

- On macOS, the default parallel path of `dotnet pack` may hang
- `dotnet msbuild ... -t:Pack -m:1 -p:BuildInParallel=false` works around this
- `GenerateNuspec` alone and `dotnet tool install` work fine, so this is treated as a pack execution path issue, not package corruption
- If some runtimes are not installed locally, defer final verification for those TFMs to CI
- CLI E2E tests use `dotnet <Unilyze.dll>` instead of the apphost directly, to align runtime resolution with `dotnet test`
- Environments with multiple `dotnet` install roots may expose shim execution issues in the release smoke. The script does not work around this
