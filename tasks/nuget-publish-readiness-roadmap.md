# unilyze NuGet公開可能状態までのロードマップ

初版: 2026-03-14
最終更新: 2026-06-13
対象: `unilyze` を OSS / NuGet で一般公開できる状態まで引き上げるための実装・検証・運用計画

---

## 目次

1. [現状サマリー](#1-現状サマリー)
2. [公開可能の定義](#2-公開可能の定義)
3. [解決済みブロッカー](#3-解決済みブロッカー)
4. [残存する課題](#4-残存する課題)
5. [ロードマップ進捗](#5-ロードマップ進捗)
6. [段階的リリース戦略](#6-段階的リリース戦略)
7. [公開判定チェックリスト](#7-公開判定チェックリスト)

---

## 1. 現状サマリー

`unilyze` は全 6 フェーズが完了し、NuGet 公開可能な状態にある。v0.1.x〜v0.3.0 のタグベースリリースが運用中。

### 1.1 テスト実行状況

テストプロジェクトは `net8.0;net9.0;net10.0` のマルチターゲット化済み。

| TFM | 結果 | 備考 |
|---|---|---|
| `net9.0` | green | ローカル確認済み |
| `net10.0` | green | ローカル確認済み |
| `net8.0` | CI で確認 | ローカル環境に .NET 8 runtime なし |

`CogCCCrossValidationTests` は green。Spearman 1.000 / 完全一致 100%。

### 1.2 総合評価

- `preview` / `stable` の公開条件をすべて満たしている
- `DitCalculator` の `I[A-Z]` ヒューリスティックは撤去済み（構文木内の実 interface 宣言を参照する方式に置換）
- Cytoscape は埋め込みリソースとして同梱され、CDN 依存なし
- タグ (`v*`) ベースの NuGet publish workflow が稼働中

---

## 2. 公開可能の定義

| 観点 | 公開基準 | 判定方法 | 現状 |
|---|---|---|---|
| 解析正確性 | 同名型、nested type、partial type、別 assembly の型衝突で結果が壊れない | ユニットテスト | 達成 |
| Unity 対応 | `.asmdef` の GUID 参照を落とさず扱える | asmdef fixture テスト | 達成 |
| 型判定の妥当性 | interface / base type を推測で誤判定しない | SemanticModel 利用時テスト、syntax-only fallback テスト | 達成 |
| メトリクス主張の整合性 | Sonar 準拠を言うなら cross validation が通る | Cross validation テスト、README/NuGet 説明確認 | 達成 |
| CLI 安定性 | 壊れた入力や headless 環境でも未処理例外で落ちない | E2E テスト、異常系テスト | 達成 |
| レポート配布性 | オフライン環境でも生成 HTML を開ける | 生成 HTML テスト | 達成 |
| OSS 運用性 | CI で build/test/pack/install が再現できる | GitHub Actions | 達成 |

---

## 3. 解決済みブロッカー

### 3.1 解析正確性 (全て解決済み)

| 論点 | 旧状態 | 対応内容 |
|---|---|---|
| 型識別子が弱い | `Name` / `Namespace + Name` ベース | `TypeId` (`Assembly::Namespace.Type+Nested`) を導入。全メトリクス・依存・HTML で統一 |
| interface 判定がヒューリスティック | `I[A-Z]` 命名推測が各所に残存 | TypeInfo / AnalysisPipeline で撤去。SemanticModel 時は `TypeKind.Interface` で判定 |
| `.asmdef` GUID 参照を捨てる | `GUID:...` を無視 | `.asmdef.meta` から GUID lookup を構築。未解決は `UnresolvedReferences` で保持 |

### 3.2 公開品質 (全て解決済み)

| 論点 | 旧状態 | 対応内容 |
|---|---|---|
| Cross validation と説明の同期 | README / docs の整合が未確認 | Spearman 1.000 / 完全一致 100%。README・docs/metrics.md・実装間で矛盾なし |
| 例外処理が薄い | 未処理例外でスタックトレースが表示される | JsonException, IOException, DirectoryNotFoundException 等をユーザー向けメッセージに変換 |
| HTML の配布性 | CDN なしでは表示不可 | Cytoscape / dagre を埋め込みリソースとして同梱し、完全オフラインで表示可能 |

---

## 4. 残存する課題

なし。初版時点の残存課題はすべて解消済み。

### 4.1 解消済みの旧課題 (2026-06-13 時点)

| 項目 | 対応内容 |
|---|---|
| DitCalculator の `I[A-Z]` ヒューリスティック | 撤去済み。syntax-only fallback は同一構文木内の `InterfaceDeclarationSyntax` 宣言を探して判定。外部参照 (`QualifiedNameSyntax`) は namespace 衝突回避のため interface 判定をスキップし DIT=1 |
| Cytoscape の self-contained 化 | `cytoscape.min.js` / `dagre.min.js` / `cytoscape-dagre.js` を埋め込みリソースとして同梱（SHA256 を csproj に記録、THIRD-PARTY-NOTICES.txt 同梱）。CDN 依存なし |
| preview / stable リリースゲートの分離 | `.github/workflows/publish.yml` で `v*` タグ push 時に `dotnet nuget push`。MinVer によるタグベースバージョニング。v0.3.0 までリリース済み |
| PackageIcon | `assets/icon.png` (128x128) を追加し `<PackageIcon>` を設定。pack で nuspec に `<icon>` が入ることを確認済み |

---

## 5. ロードマップ進捗

### 5.1 フェーズ1: 型識別の再設計 --- 完了

- `TypeIdentity.cs` で `TypeId` (`Assembly::Namespace.Type+Nested`arity) を定義
- TypeInfo, AnalysisPipeline, CouplingMetricsCalculator, DiffCalculator, HtmlTemplate を TypeId ベースに統一
- partial type のマージキーも TypeId ベース
- テスト: NestedTypes_WithSameSimpleName_GetDistinctTypeIds, TypeIdMatching_AvoidsAssemblyCollision 等

### 5.2 フェーズ2: 型判定の正確化 --- 完了

- TypeInfo / AnalysisPipeline: `LooksLikeInterface` 撤去済み。SemanticModel 時は `TypeKind.Interface` で判定
- syntax-only fallback: TypeInfo は `knownInterfaces` HashSet で実型定義ベースの判定
- DitCalculator: `I[A-Z]` パターン撤去済み。構文木内の実 interface 宣言を参照して判定
- テスト: ClassNamedLikeInterface_RemainsBaseType 等

### 5.3 フェーズ3: Unity asmdef 解決の改善 --- 完了

- `BuildGuidLookup()` で GUID → AssemblyName マッピングを構築
- `TryReadGuid()` で `.asmdef.meta` から GUID 読み取り
- 未解決 GUID は `UnresolvedReferences` で保持（silent drop なし）
- テスト: Discover_GuidReferences_AreResolvedWhenMetaExists, Discover_MixedReferences_KeepNamedAndResolvedRefs 等

### 5.4 フェーズ4: メトリクス主張と検証結果の整合化 --- 完了

- `CogCCCrossValidationTests` は green (Spearman 1.000, 完全一致 100%, 70 メソッド)
- docs/metrics.md に SonarAnalyzer.CSharp 10.20.0 との検証結果を明記
- README は「SonarSource 仕様準拠」と記述、詳細は docs に委譲
- 既知差異（static ローカル関数の扱い）を docs で明記

### 5.5 フェーズ5: CLI とレポート出力の堅牢化 --- 完了

- `--no-open` オプション実装済み
- 例外処理: JsonException, IOException, UnauthorizedAccessException, DirectoryNotFoundException をキャッチ
- ブラウザ起動失敗は警告のみ（プロセスは正常終了）
- HTML offline fallback report 実装済み（CDN 不要で types, deps, hotspots, cycles, coupling を表示）
- テスト: InvalidFormat_ExitsNonZero, NonExistentPath_ExitsNonZero, InvalidJsonInput_ExitsNonZeroWithFriendlyMessage, Generate_EmbedsOfflineFallbackReport 等

### 5.6 フェーズ6: リリースエンジニアリング整備 --- 完了

- GitHub Actions CI: net8.0/9.0/10.0 test matrix + pack-smoke ジョブ
- `dotnet pack` → `dotnet tool install` → `unilyze --version` の smoke test
- package metadata: Authors, Description, RepositoryUrl, PackageLicenseExpression, PackageReadmeFile, PackageTags 設定済み
- LICENSE (MIT) + README.md を NuGet パッケージに同梱

---

## 6. 段階的リリース戦略

### 6.1 現在の到達点

| 段階 | 条件 | 判定 |
|---|---|---|
| `preview` | フェーズ1-4完了、フェーズ5の CLI 異常系が概ね整備、CI あり | 達成 |
| `stable` | フェーズ1-6完了、README と実装の主張整合、主要テスト green、pack/install ゲートあり | 達成 |

タグベースリリースが運用中（v0.3.0 まで公開済み）。stable のブロッカーは残っていない。

### 6.2 preview 公開の条件 (全て満たしている)

- [x] 型識別の衝突問題が解消済み
- [x] `.asmdef` GUID 参照が解決済み
- [x] interface 判定ヒューリスティックが撤去済み
- [x] README に既知制限が明記されている
- [x] package description で過剰な互換性主張をしていない

### 6.3 stable 公開の条件

- [x] Cross validation が green
- [x] CI matrix が常時 green
- [x] offline HTML / `--no-open` / 異常系 CLI が揃っている
- [x] DitCalculator の `I[A-Z]` ヒューリスティックを撤去（構文木ベース判定に置換）

---

## 7. 公開判定チェックリスト

### 7.1 実装ゲート

- [x] `TypeId` ベースで全メトリクス・依存・HTML が統一されている
- [x] `.asmdef` GUID 参照を扱える
- [x] unresolved 参照が silent drop されない
- [x] base/interface 判定に命名ヒューリスティックが残っていない

### 7.2 テストゲート

- [x] `dotnet test` が `net8.0`, `net9.0`, `net10.0` で green
- [x] 同名型、nested type、partial type、GUID asmdef、CLI 異常系のテストがある
- [x] Cross validation の扱いが README の主張と一致している

### 7.3 配布ゲート

- [x] `dotnet pack` と `dotnet tool install` の smoke test が CI で通る
- [x] ローカル HTML が CDN なしで見える (vendor JS 同梱)
- [x] `--no-open` で headless 実行できる
- [x] README、LICENSE、NuGet メタデータが同期している
- [x] PackageIcon が設定されている (`assets/icon.png`)
