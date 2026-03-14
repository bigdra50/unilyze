# unilyze Developer Guide

`unilyze` の保守・実装・検証・リリース向けメモ。
利用者向けの導入と使い方は [README.md](README.md) を参照。

## Requirements

- unilyze のサポート対象は `.NET 8.0 or later`
- 開発時は現在の target frameworks (`net8.0;net9.0;net10.0`) をビルドできる SDK を使う
- フルのローカル test matrix を回す場合は `net8.0;net9.0;net10.0` の runtime を入れる

CI matrix は `net8.0;net9.0;net10.0`。

## Repository Map

- [src/Unilyze](src/Unilyze): CLI 本体
- [tests/Unilyze.Tests](tests/Unilyze.Tests): xUnit テスト
- [docs/metrics.md](docs/metrics.md): メトリクス定義
- [tasks/nuget-publish-readiness-roadmap.md](tasks/nuget-publish-readiness-roadmap.md): 公開 readiness の整理
- [.github/workflows/ci.yml](.github/workflows/ci.yml): CI / pack smoke

## Local Validation

### Tests

通常は以下を回せば十分。

```bash
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net9.0 --no-restore -v minimal
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net10.0 --no-restore -v minimal
```

全 runtime が入っている環境では `net8.0` も含めて回す。

```bash
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net8.0 --no-restore -v minimal
```

restore から行う場合:

```bash
dotnet restore tests/Unilyze.Tests/Unilyze.Tests.csproj
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net9.0 -v minimal
dotnet test tests/Unilyze.Tests/Unilyze.Tests.csproj -f net10.0 -v minimal
```

### Pack / Install Smoke

CI では `dotnet pack` 後に `dotnet tool install` して `unilyze --version` を確認する。

ローカルの macOS では、既定の `dotnet pack` が `PackAsTool` の並列 pack 経路で停滞することがある。再現した場合は並列を切って実行する。

```bash
dotnet msbuild src/Unilyze/Unilyze.csproj -t:Pack -p:NoBuild=true -m:1 -p:BuildInParallel=false
dotnet tool install --tool-path ./artifacts/tools-smoke Unilyze --add-source ./src/Unilyze/nupkg --version 0.1.0
./artifacts/tools-smoke/unilyze --version
```

`dotnet pack` を通常経路で使う場合:

```bash
dotnet restore src/Unilyze/Unilyze.csproj
dotnet pack src/Unilyze/Unilyze.csproj -c Release -o ./artifacts/nupkg
dotnet tool install --tool-path ./artifacts/tools-smoke Unilyze --add-source ./artifacts/nupkg --version 0.1.0
./artifacts/tools-smoke/unilyze --version
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
- graph 資産自体はまだ完全 self-contained ではない

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

公開 readiness の詳細は [tasks/nuget-publish-readiness-roadmap.md](tasks/nuget-publish-readiness-roadmap.md) を参照。

## Known Local Caveats

- macOS では `dotnet pack` の既定並列経路が停滞することがある
- `dotnet msbuild ... -t:Pack -m:1 -p:BuildInParallel=false` は通る
- `GenerateNuspec` 単体と `dotnet tool install` は問題なく通るので、パッケージ内容の破損ではなく pack 実行経路の問題として扱う
- 手元に一部 runtime が入っていない場合は、その TFM の最終確認を CI に委ねる
