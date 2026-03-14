# unilyze Developer Guide

`unilyze` の保守・実装・検証・リリース向けメモ。
利用者向けの導入と使い方は [README_JP.md](README_JP.md) を参照。

## Requirements

- unilyze のサポート対象は `.NET 8.0 or later`
- 日常開発は最新 SDK 1つでよい。現時点の標準は `.NET SDK 10.0.103`
- フルのローカル test matrix を回す場合だけ `net8.0;net9.0;net10.0` の runtime を入れる

CI matrix は `net8.0;net9.0;net10.0`。

## Repository Map

- [src/Unilyze](src/Unilyze): CLI 本体
- [scripts/release-smoke.sh](scripts/release-smoke.sh): 標準 `.NET tool` 導線の release smoke
- [tests/Unilyze.Tests](tests/Unilyze.Tests): xUnit テスト
- [docs/metrics.md](docs/metrics.md): メトリクス定義
- [.github/workflows/ci.yml](.github/workflows/ci.yml): CI / pack smoke

## Local Validation

### Tests

通常は以下を回せば十分。

```bash
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net10.0 --no-restore -v minimal
```

互換性確認をローカルでもやる場合だけ、追加で `net8.0` / `net9.0` を回す。

```bash
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net9.0 --no-restore -v minimal
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net8.0 --no-restore -v minimal
```

restore から行う場合:

```bash
dotnet restore tests/Unilyze.Tests/Unilyze.Tests.csproj
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net10.0 -v minimal
```

### Pack / Install Smoke

公開判定は、標準 `.NET tool` 導線を検証する [scripts/release-smoke.sh](scripts/release-smoke.sh) を基準にする。

この script は `DOTNET_ROOT` を上書きしない。呼び出し元の shell 環境のまま `dotnet tool install --tool-path ...` と生成 shim の実行を確認する。

ローカルの macOS では、既定の `dotnet pack` が `PackAsTool` の並列 pack 経路で停滞することがある。再現した場合は並列を切って実行する。

```bash
dotnet restore src/Unilyze/Unilyze.csproj
dotnet build src/Unilyze/Unilyze.csproj -c Release --no-restore
dotnet msbuild src/Unilyze/Unilyze.csproj -t:Pack -p:Configuration=Release -p:NoBuild=true -p:PackageOutputPath="$PWD/artifacts/nupkg" -m:1 -p:BuildInParallel=false
bash scripts/release-smoke.sh --package-source ./artifacts/nupkg --version 0.1.0
```

`dotnet pack` を通常経路で使う場合:

```bash
dotnet restore src/Unilyze/Unilyze.csproj
dotnet pack src/Unilyze/Unilyze.csproj -c Release -o ./artifacts/nupkg
bash scripts/release-smoke.sh --package-source ./artifacts/nupkg --version 0.1.0
```

## Current Implementation Notes

### Type Identity

内部参照は単純名ではなく `TypeId` を使う。

- 形式: `Assembly::Namespace.Outer+Inner`
- 表示用途は `QualifiedName`
- 依存、coupling、diff、HTML ノード、partial merge は `TypeId` ベース

関連ファイル:

- [src/Unilyze/TypeIdentity.cs](src/Unilyze/TypeIdentity.cs)
- [src/Unilyze/TypeInfo.cs](src/Unilyze/TypeInfo.cs)
- [src/Unilyze/AnalysisPipeline.cs](src/Unilyze/AnalysisPipeline.cs)

### Type Relationship Resolution

`I[A-Z]` の命名ヒューリスティックは使わない。

- syntax-only では保守的に扱う
- SemanticModel があるときは `INamedTypeSymbol.TypeKind` で base / interface を分ける

関連テスト:

- [tests/Unilyze.Tests/AnalysisPipelineTests.cs](tests/Unilyze.Tests/AnalysisPipelineTests.cs)
- [tests/Unilyze.Tests/TypeAnalyzerTests.cs](tests/Unilyze.Tests/TypeAnalyzerTests.cs)

### asmdef GUID Resolution

`.asmdef.meta` から GUID を引いて `references: ["GUID:..."]` を解決する。解決不能 GUID は捨てずに保持する。

関連ファイル:

- [src/Unilyze/AsmdefInfo.cs](src/Unilyze/AsmdefInfo.cs)
- [tests/Unilyze.Tests/AsmdefInfoTests.cs](tests/Unilyze.Tests/AsmdefInfoTests.cs)

### HTML Viewer

通常は Cytoscape ベースのインタラクティブ graph を出す。外部資産が読めない環境では built-in の offline report fallback に切り替わる。

- `--no-open` でブラウザ自動起動を抑止
- offline fallback でも types, dependencies, hotspots, cycles, assembly coupling は見える
- graph 資産自体はまだ完全 self-contained ではない。この制約は README に明記する

関連ファイル:

- [src/Unilyze/Program.cs](src/Unilyze/Program.cs)
- [src/Unilyze/HtmlTemplate.cs](src/Unilyze/HtmlTemplate.cs)
- [tests/Unilyze.Tests/CliE2eTests.cs](tests/Unilyze.Tests/CliE2eTests.cs)

## Release Checklist

1. `dotnet test` を `net9.0` / `net10.0` で green にする
2. CI matrix の `net8.0` / `net9.0` / `net10.0` を green にする
3. pack/install smoke を通す
4. README / docs / package metadata の説明が実装と一致していることを確認する
5. HTML fallback と `--no-open` を壊していないことを確認する

## NuGet Publish

GitHub Actions から publish する。ローカルの API key 保持は前提にしない。

事前に repository secret `NUGET_API_KEY` を設定する。

公開手順:

1. publish 対象 commit の `CI` workflow を green にする
2. Actions の `Publish NuGet` workflow を手動実行する
3. workflow 内で `net10.0` test、pack、release smoke、`dotnet nuget push` を順に実行する

publish workflow:

- [`.github/workflows/publish.yml`](.github/workflows/publish.yml)
- secret 名: `NUGET_API_KEY`

## Known Local Caveats

- macOS では `dotnet pack` の既定並列経路が停滞することがある
- `dotnet msbuild ... -t:Pack -m:1 -p:BuildInParallel=false` は通る
- `GenerateNuspec` 単体と `dotnet tool install` は問題なく通るので、パッケージ内容の破損ではなく pack 実行経路の問題として扱う
- 手元に一部 runtime が入っていない場合は、その TFM の最終確認を CI に委ねる
- CLI E2E は apphost 直叩きではなく `dotnet <Unilyze.dll>` で実行する。runtime 解決を `dotnet test` 側と揃えるため
- 複数の `dotnet` install root が混在している環境では、release smoke が shim 実行の問題を露出させる。script 側では回避しない
