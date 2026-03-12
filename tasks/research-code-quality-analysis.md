# コーディングエージェントによるコード品質自動分析・修正の手段・ツール・アプローチ

調査日: 2026-03-12

---

## 目次

1. [静的解析アプローチ](#1-静的解析アプローチ)
2. [コードメトリクスの自動計測](#2-コードメトリクスの自動計測)
3. [LLM/AIを使った分析アプローチ](#3-llmaiを使った分析アプローチ)
4. [アーキテクチャ準拠チェック](#4-アーキテクチャ準拠チェック)
5. [コード変更のリスク分析](#5-コード変更のリスク分析)
6. [コーディングエージェント統合](#6-コーディングエージェント統合)
7. [統合戦略の提案](#7-統合戦略の提案)

---

## 1. 静的解析アプローチ

### 1.1 カスタムRoslyn Analyzer + CodeFixProvider

#### 概要

Roslyn (C#/VBコンパイラプラットフォーム) のAPI上に構築されるアナライザー。コンパイル時にソースコードの構文木・セマンティックモデルを解析し、診断（Diagnostic）を報告する。オプションで `CodeFixProvider` を関連付け、自動修正を提供できる。

#### アーキテクチャ

```
+------------------+     +-------------------+     +------------------+
| DiagnosticAnalyzer|---->| Diagnostic Report |---->| CodeFixProvider  |
| (ルール検出)      |     | (警告/エラー表示)  |     | (自動修正提案)    |
+------------------+     +-------------------+     +------------------+
        |                                                   |
        v                                                   v
  SyntaxTree /                                     SyntaxTree変換 /
  SemanticModel解析                                 CodeAction生成
```

#### 実装の要点

- `DiagnosticAnalyzer` を継承し、`Initialize()` で `SyntaxNode` / `Symbol` / `Operation` のコールバックを登録
- `CodeFixProvider` を継承し、`RegisterCodeFixesAsync()` で修正アクションを登録
- `FixableDiagnosticIds` で対応するDiagnostic IDを紐付け
- `GetFixAllProvider()` で `WellKnownFixAllProviders.BatchFixer` を返すと一括修正が可能
- `.editorconfig` / `.globalconfig` でルールの有効化・無効度・重大度を制御
- .NET 5.0以降、コードスタイルアナライザーがSDKに同梱されビルド時に警告/エラーとして強制可能

#### C#/Unity互換性

- Unity は Roslyn アナライザーをネイティブサポート（Assets フォルダに DLL を配置、または NuGet パッケージ参照）
- `Microsoft.Unity.Analyzers`（v1.26.0）: Unity専用のRoslynアナライザー群
  - `InitializeOnLoad` の不正シグネチャ検出
  - `MonoBehaviour` / `ScriptableObject` への不正な `new` 呼び出し検出
  - Unity暗黙使用フィールドの `DiagnosticSuppressor` による誤警告抑制
  - Visual Studio の Game Development with Unity ワークロードに同梱
- `Roslynator`: 500以上のアナライザー・リファクタリングを提供するオープンソースツール

#### コーディングエージェントとの統合可能性

- Roslyn Analyzerの出力はSARIF形式で標準化されており、エージェントが解析結果を消費しやすい
- CodeFixProviderの自動修正ロジックをエージェントが呼び出して適用可能
- MCP Server経由でRoslyn診断情報をAIエージェントに公開するパターンが出現（後述）

#### 長所

- コンパイラレベルの精度（偽陽性が少ない）
- IDE統合が優秀（リアルタイムフィードバック、電球アイコン）
- カスタムルール作成の自由度が高い
- ビルドパイプラインに組み込み可能
- Unity公式サポートあり

#### 短所

- Roslyn APIの学習コストが高い
- 構文木操作のボイラープレートが多い
- クロスファイル解析はAnalyzerの設計上制限がある
- Unity固有のランタイム挙動（シリアライゼーション等）の完全な解析は困難

### 1.2 ReSharper / Rider

#### 概要

JetBrains製の静的解析ツール。ReSharperはVisual Studio拡張、RiderはスタンドアロンIDE。2500以上のインスペクションルールを提供。

#### C#/Unity互換性

- `resharper-unity` プラグイン: Unity専用インスペクション・コード補完・デバッグ支援
- 2025.2.1以降: ReSharper Command Line Tools (CLT) で Unity プラグインが正式動作
  - `inspectcode` / `cleanupcode` コマンドでCI/CDパイプライン統合可能
  - 出力はSARIF形式
- インストール: `dotnet tool install -g JetBrains.ReSharper.GlobalTools`

#### コーディングエージェントとの統合可能性

- `inspectcode` のSARIF出力をエージェントに渡して分析・修正指示を生成可能
- `cleanupcode` でフォーマット・コードスタイルの自動適用
- CI/CDステップとして自動実行し、結果をPRコメントとして投稿可能

#### 長所

- 検出ルールの質・量が業界トップクラス
- Unity専用プラグインの成熟度が高い
- CLI版によりCI統合が容易
- リファクタリング・クイックフィックスが充実

#### 短所

- 商用ライセンス必須（CLTは無料だがIDE版は有料）
- 大規模プロジェクトでのパフォーマンスに課題
- カスタムルール作成はRoslyn Analyzerより複雑

### 1.3 SonarQube / SonarCloud

#### 概要

オープンソース（Community版）/ 商用の静的解析プラットフォーム。380以上のC#ルール、130以上のVB.NETルールを持つ。Cognitive Complexityの計測が特長。

#### C#/Unity互換性

- `SonarAnalyzer.CSharp` はRoslynベースで構築
- `sonarqube-roslyn-sdk` 経由でカスタムRoslynアナライザーをSonarQubeプラグインとして統合可能
- Unity専用のファーストパーティ対応はないが、`Microsoft.Unity.Analyzers` をSDK経由で取り込み可能
- Cognitive Complexity のしきい値はプロジェクトごとにカスタマイズ可能（デフォルト15）

#### コーディングエージェントとの統合可能性

- SonarQube Web APIからメトリクス・Issue情報を取得可能
- CI/CDパイプライン（GitHub Actions等）に統合してPR品質ゲートを実現
- SonarCloudはGitHub PR連携が標準装備

#### 長所

- Cognitive Complexity計測のデファクトスタンダード
- Community版が無料で利用可能
- 品質ゲートによるマージ前チェック
- 多言語対応（30言語以上）
- 技術的負債の可視化

#### 短所

- サーバーインスタンスの運用が必要（SonarQube Server）
- Unity固有の誤検出を手動で管理する必要がある
- Cognitive Complexity以外のメトリクスが限定的

---

## 2. コードメトリクスの自動計測

### 2.1 メトリクス一覧

| メトリクス | 説明 | 計測ツール | 推奨しきい値 |
|---|---|---|---|
| Cyclomatic Complexity | 独立パス数（McCabe, 1976） | VS Code Metrics, NDepend, SonarQube | メソッド当たり10以下（NIST235） |
| Cognitive Complexity | 人間の理解コスト（SonarSource独自） | SonarQube, NDepend | メソッド当たり15以下 |
| Maintainability Index | 保守性の総合指標（0-100） | VS Code Metrics | 20以上 |
| Afferent Coupling (Ca) | 入力結合度（被依存数） | NDepend | コンテキスト依存 |
| Efferent Coupling (Ce) | 出力結合度（依存数） | NDepend | TypeCe > 50 は要注意 |
| Relational Cohesion (H) | アセンブリ内の凝集度 | NDepend | 1.5 - 4.0 |
| LCOM (HS) | メソッドの凝集度欠如 | NDepend | 1.0超は警戒 |
| Class Coupling | ユニーク依存クラス数 | VS Code Metrics | コンテキスト依存 |
| Depth of Inheritance (DIT) | 継承階層の深さ | NDepend | 6以下 |
| Change Coupling | 同時変更頻度（git履歴ベース） | CodeScene, Code Maat | コンテキスト依存 |

### 2.2 Visual Studio Code Metrics（無料）

- メニュー: Analyze → Calculate Code Metrics for Solution
- 計測対象: Maintainability Index, Cyclomatic Complexity, Class Coupling, Depth of Inheritance, Lines of Code
- 注意: MSIL（コンパイル後のIL）ベースで計測するため、ソースコードと差異が出る場合がある
- NuGetパッケージ `Microsoft.CodeAnalysis.Metrics` でビルド時に計測可能

### 2.3 NDepend（商用、.NET特化）

- 100以上のコードメトリクスをサポート
- Dependency Structure Matrix (DSM) による結合度・凝集度の可視化
- CQLinq: LINQ構文でコードを問い合わせるDSL
- Visual Studio 2026, .NET 10.0, Unity, Blazor, Xamarin対応
- Azure DevOps, GitHub Actions統合
- MCP Serverを公開（後述）
- Trend分析: メトリクスの時系列変化を追跡
- 技術的負債の金額換算機能

#### エージェント統合における特筆点

NDependがオープンソースで公開したMCP Serverは14のツールを公開:
- コードメトリクス検索（Maintainability Index、Complexity、カバレッジ等）
- Issue検出（NDependルール、Roslynアナライザー、R#インスペクション統合）
- 依存関係グラフ生成（SVG出力）
- コードクエリ（CQLinq）生成
- コードdiff比較
- AI修正のためのの詳細診断情報提供

設計パターン:
- トークン最適化: 10-20のwell-designedなツールに限定
- ページネーションでプロンプトオーバーフロー防止
- Privacy-first: コードはローカル実行、LLMにはクエリ結果のみ送信

### 2.4 Cognitive Complexity vs Cyclomatic Complexity

| 観点 | Cyclomatic Complexity | Cognitive Complexity |
|---|---|---|
| 測定対象 | 構造的な分岐パス数 | 人間の認知的理解コスト |
| ネスト考慮 | なし | ネストが深いほどスコア増加 |
| switch/case | 各caseがカウント | switchは1カウント |
| 短絡評価 | 各論理演算子がカウント | 同種の連続は1カウント |
| テスト網羅性 | テストケース数の最小見積もりに有用 | 不向き |
| 可読性評価 | 弱い | 強い |
| 提唱者 | McCabe (1976) | SonarSource (2017) |

Cognitive Complexityはレビュー時の認知負荷軽減という目的に対してCyclomatic Complexityより適切な指標。

---

## 3. LLM/AIを使った分析アプローチ

### 3.1 ツール比較表

| ツール | C#対応 | 価格 | プラットフォーム | 特長 |
|---|---|---|---|---|
| GitHub Copilot Code Review | ○ | $19-39/user/月 | GitHub only | LLMネイティブ、CodeQL統合、2025年4月GA |
| CodeRabbit | ○ | $12-24/user/月 | GitHub, GitLab, Bitbucket | GitHubで最もインストールされたAIアプリ、200万リポ接続 |
| Qodo Merge | ○ | $15/user/月 | GitHub, GitLab, Bitbucket | Gartner Magic Quadrant Visionary (2025)、multi-agent review |
| DeepSource | ○ | $20/user/月 | GitHub, GitLab, Bitbucket | SAST+SCA統合、OWASP/CWE対応、20言語以上 |
| Codacy | ○ | $15/user/月 | GitHub, GitLab, Bitbucket | 40言語対応、22,000ルール、34統合ツール |
| Amazon CodeGuru | × | $0.50/100LOC | GitHub, Bitbucket, CodeCommit | Java/Pythonのみ。C#非対応 |

### 3.2 GitHub Copilot Code Review

- 2025年4月にGA、1ヶ月で100万ユーザー到達
- CopilotをレビュアーとしてPRにアサインする形式
- 2025年10月: コンテキスト収集強化（ソースファイル読み取り、ディレクトリ構造探索、CodeQL/ESLint統合）
- diff-basedのため、PRの変更差分のみ参照（アーキテクチャ全体は見えない）
- C#対応あり

### 3.3 CodeRabbit

- GitHubで最もインストールされたAIコードレビューアプリ（200万リポ、1300万PR）
- AST評価 + SAST + 生成AIフィードバックの多層分析
- 実世界のランタイムバグ検出精度46%
- 手動レビュー工数50%以上削減、レビューサイクル80%高速化
- 2026年新機能: コードグラフ分析（依存関係理解）、リアルタイムWebクエリ、LanceDBによるセマンティック検索
- OSS無料、有料$12-24/user/月
- C#対応あり

### 3.4 Qodo Merge（旧Codium）

- `/describe`, `/improve`, `/analyze`, `/implement`, `/compliance` コマンド
- multi-agent review: 単一AIではなく複数のレビューエージェントが異なる観点で分析
- Jira等のIssueトラッカー連携でPRが要件を満たすか検証
- 17%のPRで高重大度Issue（スコア9-10）を検出
- monday.comでは月800以上の潜在的問題を防止、73.8%の提案採用率

### 3.5 AI生成コードの品質に関する知見

CodeRabbitの2025年レポートから:
- AI生成PRは平均10.83個のIssueを含む（人間は6.45個）→ 1.7倍
- AI生成はロジック・正確性エラーが1.75倍多い
- セキュリティ問題1.57倍、パフォーマンス問題1.42倍
- クリティカルIssueは1.4倍、メジャーIssueは1.7倍

この結果は、AIエージェントが生成したコードにも品質チェックを適用する必要性を裏付ける。

---

## 4. アーキテクチャ準拠チェック

### 4.1 ArchUnitNET

#### 概要

Java の ArchUnit の C# 移植版。C# バイトコードを解析してアーキテクチャルールを自動テストするライブラリ。Apache License 2.0。

#### 対応テストフレームワーク

xUnit, xUnit V3, NUnit, MSTest V2/V3/V4, TUnit

#### 主要機能

1. Fluent APIによるルール定義

```csharp
// レイヤー間の依存制約
Types().That().Are(DomainLayer)
    .Should().NotDependOnAny(InfrastructureLayer)
    .Because("Domain must not depend on Infrastructure");

// 命名規約の強制
Classes().That().AreAssignableTo(typeof(IRepository<>))
    .Should().HaveNameEndingWith("Repository");

// メソッド呼び出し制限
Classes().That().Are(DomainClasses)
    .Should().NotCallAny(
        MethodMembers().That().AreDeclaredIn(UILayer));
```

2. PlantUMLダイアグラムからのルール導出

```csharp
Types().Should().AdhereToPlantUmlDiagram(myDiagram);
```

3. スライスルールと依存ダイアグラムの自動生成

#### C#/Unity互換性

- C#バイトコード解析のため、Unity（IL2CPP以前のMono/IL段階）で動作
- アセンブリ単位のロードで柔軟な対象指定が可能
- Unity固有のasmdef境界をArchUnitNETのレイヤーにマッピング可能

#### コーディングエージェントとの統合可能性

- ユニットテストとして実行されるため、CI/CDパイプラインで自動チェック可能
- テスト失敗時のメッセージをエージェントに渡してアーキテクチャ違反の修正を指示可能
- PlantUMLダイアグラムとの連携でアーキテクチャ意図の文書化・検証を一体化

#### 長所

- 宣言的で読みやすいルール定義
- 既存テストフレームワークとの統合
- PlantUMLダイアグラム検証
- アーキテクチャ違反の自動検出

#### 短所

- バイトコード解析のため、ソースレベルの詳細情報が限られる
- ルール定義には一定のアーキテクチャ知識が必要
- Unity IL2CPPビルドとの互換性は未検証

### 4.2 NDepend による依存関係ルール検証

- CQLinqでカスタム依存関係ルールを定義

```csharp
// CQLinqクエリ例
warnif count > 0
from t in Types
where t.IsUsing("UnityEngine.UI")
   && !t.ParentNamespace.Name.Contains("UI")
select t
```

- DSM (Dependency Structure Matrix) で結合度を視覚的に把握
- アーキテクチャ違反をビルド時に検出し品質ゲートとして機能

---

## 5. コード変更のリスク分析

### 5.1 ツール比較

| ツール | タイプ | 主要機能 | C#対応 | 価格 |
|---|---|---|---|---|
| CodeScene | 商用SaaS | Hotspot、Code Health、知識分布、Change Coupling | ○ (30言語以上) | 商用ライセンス |
| Code Maat | OSS CLI (Java) | Coupling、リビジョン分析、コード年齢、知識メトリクス | ○ (言語非依存) | 無料 |
| CodeCohesion | OSS 3D可視化 | 3Dカップリング可視化、DDD分析、進化タイムライン | ○ | 無料 |
| Git History Analyzer | GitHub Action | ML予測、コードチャーン、コミットパターン分析 | ○ | 無料 |

### 5.2 CodeScene（Behavioral Code Analysis）

#### 概要

Adam Tornhill（"Your Code As A Crime Scene" 著者）が創設。静的解析を超えて、git履歴データと機械学習を組み合わせた「行動的コード分析」を行う。

#### Hotspot分析

- git活動量（変更頻度）でコードのHotspotを特定
- 大半のコードベースで、全体の少数のファイルに開発活動が集中するパターンを利用
- Hotspotと欠陥密度に強い相関: Hotspotはコードの一部だが、報告・解決された全欠陥の25-70%を占める

#### Code Health（1-10スケール）

- 9以上: 健全
- 4-9: 警告
- 4未満: アラート
- 25以上のコードスメルを検出: God Class, God Methods, Duplicated Code等
- Hotspot の Code Health が8未満なら詳細調査推奨

#### 知識分布分析

- Key Person Risk: 特定の開発者への依存度
- Bus Factor シミュレーション: 離脱リスクの定量化
- Change Coupling: 隠れた依存関係の可視化

#### CI/CD統合

- GitHub, GitLab, Bitbucket, Azure DevOps でPR/MR自動レビュー
- IDE拡張（VS Code, IntelliJ, Cursor, Visual Studio）
- 品質ゲートとしてビルドパイプラインに組み込み可能
- CodeScene ACE: AI駆動のIDE内リファクタリングエージェント

### 5.3 Code Maat（OSS）

#### DIYホットスポット分析

```bash
# 過去12ヶ月で最も変更されたファイルTop50
git log --format=format: --name-only --since=12.month \
  | egrep -v '^$' \
  | sort \
  | uniq -c \
  | sort -nr \
  | head -50
```

#### 分析可能なメトリクス

- Logical Coupling（論理的結合度）: 同時変更される傾向のあるモジュール
- Code Age: ファイルの最終変更からの月数（安定性の指標）
- Author Analysis: モジュールごとの著者数・リビジョン数（欠陥予測因子）
- サブシステムレベルへのスケーリング

### 5.4 バグ予測モデル

学術研究の知見:
- Change Coupling は構造的結合度よりも変更伝播の予測に有効（Hassan & Holt）
- LightGBMベースのLearning-to-Rankアプローチでバグ発生確率の高いファイルをランキング
- Churn × Complexity の2軸マトリクスで技術的負債の優先順位付け

---

## 6. コーディングエージェント統合

### 6.1 MCP (Model Context Protocol) による統合パターン

#### 概要

MCPはAIエージェント（Claude Code, GitHub Copilot等）が外部ツールのデータや機能にアクセスするための標準プロトコル。stdioモード（ローカルサブプロセス）とHTTP SSEモード（リモート）の2種類の通信方式をサポート。

#### 既存のコード分析MCP Server

```
+---------------------------------------------+
|           AIエージェント (Claude Code等)       |
+---------------------------------------------+
          |              |              |
   +------+------+ +----+----+ +-------+-------+
   | NDepend MCP | | Codacy  | | CodePathfinder|
   | Server      | | MCP     | | MCP Server    |
   +------+------+ +----+----+ +-------+-------+
          |              |              |
   +------+------+ +----+----+ +-------+-------+
   | C#静的解析   | | 品質    | | セマンティック |
   | メトリクス   | | ルール  | | コード検索    |
   | 依存グラフ   | | SCA     | | 呼び出しグラフ |
   +-------------+ +---------+ +---------------+
```

| MCP Server | 提供元 | 主要機能 | C#対応 |
|---|---|---|---|
| NDepend MCP | NDepend (OSS) | メトリクス検索、Issue検出、依存グラフ、CQLinq | ○ |
| Codacy MCP | Codacy (公式) | 品質ルール、SCA、セキュリティ | ○ |
| CodePathfinder | 商用 | セマンティック検索、呼び出しグラフ、データフロー分析 | △ (Python中心) |
| Joern MCP | OSS | Code Property Graph、脆弱性検出 | △ |
| Parasoft MCP | 商用 | SAST、コーディング規約、カバレッジ | × (C/C++中心) |
| Code Analysis MCP | OSS | 静的解析、依存分析、コード複雑度 | △ |

#### NDepend MCP Server の設計パターン（参考アーキテクチャ）

```
User Request
    |
    v
AI Agent (Claude Code / Copilot)
    |
    v
MCP Client ── Tool Definitions ──> LLM
    |                                |
    v                                v
MCP Server (NDepend)            Execution Plan
    |
    +-- Initialize (分析スナップショット読み込み)
    +-- Search Metrics (メトリクス検索)
    +-- List Issues (Issue一覧)
    +-- Get Dependency Graph (SVG生成)
    +-- Generate Query (CQLinq生成)
    +-- Compare Diff (ベースライン比較)
    |
    v
Results → LLM → Final Response
```

設計原則:
- ツール数は10-20に限定（トークン効率）
- ページネーション対応
- 初期化ステップで分析データをプリロード
- Progressive Disclosure（段階的情報開示）
- Privacy-first（コードはローカル実行、結果のみLLMに送信）

### 6.2 CI/CDパイプラインでの自動分析

#### 推奨パイプライン構成

```
PR Open / Push
    |
    v
+---+---+---+---+---+---+---+
|   並列実行ステップ           |
+---+---+---+---+---+---+---+
|                             |
| +-----------+ +-----------+|
| |Roslyn     | |ReSharper  ||
| |Analyzers  | |inspectcode||
| |(ビルド時)  | |(SARIF出力) ||
| +-----------+ +-----------+|
|                             |
| +-----------+ +-----------+|
| |SonarQube  | |ArchUnit   ||
| |Scanner    | |NET Tests  ||
| |(品質ゲート) | |(アーキ検証)||
| +-----------+ +-----------+|
|                             |
| +-----------+ +-----------+|
| |CodeRabbit | |Hotspot    ||
| |AI Review  | |Analysis   ||
| |(PRコメント) | |(git log)  ||
| +-----------+ +-----------+|
+-----------------------------+
    |
    v
品質ゲート判定
    |
    +-- Pass → Merge可能
    +-- Fail → PRブロック + 修正提案
```

#### GitHub Actionsの例

```yaml
# 概念的な構成例
jobs:
  roslyn-analysis:
    # Roslyn Analyzerをビルド時に実行
    # TreatWarningsAsErrors でゲーティング

  resharper-inspect:
    # inspectcode --output=results.sarif
    # SARIF結果をPRコメントとして投稿

  sonarqube-scan:
    # SonarScanner for .NET
    # 品質ゲート結果を取得

  archunit-test:
    # dotnet test --filter "Category=Architecture"
    # テスト失敗でPRブロック

  ai-review:
    # CodeRabbit / Qodo Merge
    # AIレビューコメント
```

### 6.3 Claude Code / GitHub Copilotでの統合

#### Claude Code

- MCP経由で300以上のインテグレーション（GitHub, Slack, PostgreSQL, Sentry, Linear等）
- 1Mトークンコンテキストウィンドウ（beta）で大規模なコードベース分析が可能
- Agent Teams（research preview）: 複数サブエージェントによる分散分析
- Skills / Hooks: カスタムスキルとして静的解析を組み込み可能
- `/review` コマンドでステージング済み/未ステージの変更をCLI上で分析

#### GitHub Copilot

- Coding Agent: GitHub Issueにアサインして非同期でコード生成・修正
- Agent Mode: IDE内のローカルエージェント
- MCP統合: GitHub MCP Serverが標準搭載
- 2026年2月: Claude / Codex がCopilot内のサードパーティエージェントとして利用可能
- Copilot CLI: MCP Server対応のターミナルエージェント

#### 統合アーキテクチャパターン

```
+--------------------------------------------------+
|  開発者のワークフロー                               |
+--------------------------------------------------+
|                                                    |
|  IDE (Rider/VS)           Terminal                 |
|  +----------------+       +-------------------+    |
|  | Copilot Agent  |       | Claude Code       |    |
|  | Mode           |       | (MCP Client)      |    |
|  +-------+--------+       +--------+----------+    |
|          |                          |               |
|          |    +---------------------+               |
|          |    |                                      |
|          v    v                                      |
|  +-------+----+----+                                |
|  | MCP Servers      |                               |
|  +------------------+                               |
|  | NDepend MCP      | ← メトリクス, 依存関係         |
|  | Roslyn MCP       | ← 診断, CodeFix               |
|  | Git Analysis MCP | ← Hotspot, Change Coupling    |
|  | SonarQube API    | ← Cognitive Complexity         |
|  +------------------+                               |
|          |                                           |
|          v                                           |
|  +------------------+                               |
|  | CI/CD Pipeline   |                               |
|  | (GitHub Actions)  |                               |
|  +------------------+                               |
+--------------------------------------------------+
```

---

## 7. 統合戦略の提案

### 7.1 既存資産との接続

現在の資産:
- Roslynベースの依存グラフ可視化ツール
- コード類似度検出機能

これらを活かした統合方針:

```
既存ツール (依存グラフ + 類似度検出)
    |
    +-- MCP Server化
    |     |
    |     +-- Tool: analyze_dependencies
    |     |     入力: namespace/class名
    |     |     出力: 依存グラフ(JSON/SVG)
    |     |
    |     +-- Tool: detect_similarity
    |     |     入力: ファイルパス or namespace
    |     |     出力: 類似コードペア + 類似度スコア
    |     |
    |     +-- Tool: get_metrics
    |           入力: class/method名
    |           出力: Complexity, Coupling等
    |
    +-- Roslyn Analyzer追加
    |     |
    |     +-- Unity固有ルール (Microsoft.Unity.Analyzers連携)
    |     +-- アーキテクチャ制約ルール
    |     +-- メトリクスしきい値チェック
    |
    +-- CI/CDパイプライン
          |
          +-- PR時に自動実行
          +-- 結果をPRコメントとして投稿
          +-- 品質ゲートでマージ制御
```

### 7.2 段階的導入ロードマップ

#### Phase 1: 基盤整備（低コスト・高効果）

- [ ] Roslyn Analyzer: `Microsoft.Unity.Analyzers` + `Roslynator` の導入
- [ ] `.editorconfig` でルールの有効化・重大度設定
- [ ] DIYホットスポット分析スクリプトの作成
- [ ] 既存ツールのSARIF出力対応

#### Phase 2: メトリクス計測と可視化

- [ ] Visual Studio Code Metricsまたは `Microsoft.CodeAnalysis.Metrics` でベースライン計測
- [ ] Cognitive Complexity計測の導入（SonarAnalyzer.CSharp or カスタムAnalyzer）
- [ ] 既存の依存グラフツールにCoupling/Cohesionメトリクスを追加

#### Phase 3: アーキテクチャ準拠チェック

- [ ] ArchUnitNET導入、レイヤー制約のテスト化
- [ ] Unity asmdef境界に基づくアーキテクチャルール定義
- [ ] PlantUMLダイアグラムとの連携

#### Phase 4: AI/エージェント統合

- [ ] 既存ツールのMCP Server化
- [ ] Claude Code / Copilot からMCP経由で分析結果を参照
- [ ] CodeRabbit or Qodo Merge によるAI PRレビュー導入
- [ ] CI/CDパイプラインでの自動実行

#### Phase 5: 高度な分析

- [ ] CodeScene導入（行動的コード分析）
- [ ] NDepend導入（メトリクス深掘り + MCP統合）
- [ ] バグ予測モデルの実験的導入

### 7.3 C#/Unity固有の考慮事項

1. Unity のスクリプトコンパイルパイプラインは標準の .NET とは異なる
   - `.csproj` は Unity が自動生成するため、Analyzer の参照方法に注意が必要
   - `Assets/` フォルダにAnalyzer DLLを配置するか、asmdef経由で参照
2. Unity のシリアライゼーション（`[SerializeField]` 等）は通常のC#分析では「未使用フィールド」として誤検出される
   - `Microsoft.Unity.Analyzers` の `DiagnosticSuppressor` で対応
3. `MonoBehaviour` のライフサイクルメソッド（`Start`, `Update` 等）は暗黙的に呼び出される
   - 通常の静的解析では「未使用メソッド」として誤検出される
4. IL2CPP ビルドではバイトコード解析ツール（ArchUnitNET等）の挙動が変わる可能性がある
5. Unity Package Manager (UPM) 経由のパッケージにはAnalyzerを直接適用しにくい

---

## Sources

### 静的解析
- [Roslyn Analyzers Overview - Microsoft Learn](https://learn.microsoft.com/en-us/visualstudio/code-quality/roslyn-analyzers-overview)
- [Write Your First Analyzer and Code Fix - Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
- [Getting Started Writing a Custom Analyzer & Code Fix - GitHub](https://github.com/dotnet/roslyn/blob/main/docs/wiki/Getting-Started-Writing-a-Custom-Analyzer-&-Code-Fix.md)
- [Microsoft.Unity.Analyzers - GitHub](https://github.com/microsoft/Microsoft.Unity.Analyzers)
- [Roslynator - GitHub](https://github.com/dotnet/roslynator)
- [Unity Manual: Roslyn analyzers and source generators](https://docs.unity3d.com/Manual/roslyn-analyzers.html)
- [ReSharper Unity Plugin - GitHub](https://github.com/JetBrains/resharper-unity)
- [ReSharper Command Line Tools](https://www.jetbrains.com/help/resharper/ReSharper_Command_Line_Tools.html)
- [InspectCode Command-Line Tool - JetBrains](https://www.jetbrains.com/help/rider/InspectCode.html)
- [SonarSource sonar-dotnet - GitHub](https://github.com/SonarSource/sonar-dotnet)
- [SonarQube Roslyn SDK - GitHub](https://github.com/SonarSource/sonarqube-roslyn-sdk)
- [How to integrate Roslyn analyzers with SonarQube](https://www.mytechramblings.com/posts/how-to-integrate-your-roslyn-analyzers-with-sonarqube/)

### コードメトリクス
- [Code metrics values - Microsoft Learn](https://learn.microsoft.com/en-us/visualstudio/code-quality/code-metrics-values)
- [Cyclomatic Complexity - Microsoft Learn](https://learn.microsoft.com/en-us/visualstudio/code-quality/code-metrics-cyclomatic-complexity)
- [NDepend Code Metrics](https://www.ndepend.com/docs/code-metrics)
- [NDepend Features - Code Quality](https://www.ndepend.com/features/code-quality)
- [NDepend DSM](https://www.ndepend.com/docs/dependency-structure-matrix-dsm)
- [Maintainability Index limitations - Sourcery](https://www.sourcery.ai/blog/maintainability-index)
- [Code Complexity Guide - CodeAnt AI](https://www.codeant.ai/blogs/reduce-code-complexity-guide)
- [Code Complexity Explained - Qodo](https://www.qodo.ai/blog/code-complexity/)

### AI コードレビュー
- [Best AI Code Review Tools 2026 - GitAutoReview](https://gitautoreview.com/blog/best-ai-code-review-tools-2026)
- [I Tested 5 AI Code Review Tools - DEV Community](https://dev.to/leejackson/i-tested-5-ai-code-review-tools-for-30-days-heres-what-actually-works-with-data-24i0)
- [Best AI Code Review Tools 2026 - DEV Community](https://dev.to/heraldofsolace/the-best-ai-code-review-tools-of-2026-2mb3)
- [Top 10 AI Code Review Tools 2026 - Apidog](https://apidog.com/blog/top-10-ai-code-review-tools-2/)
- [CodeRabbit - AI Code Reviews](https://www.coderabbit.ai/)
- [AI vs Human Code Generation Report - CodeRabbit](https://www.coderabbit.ai/blog/state-of-ai-vs-human-code-generation-report)
- [Qodo Merge](https://www.qodo.ai/products/qodo-merge/)
- [State of AI Code Review Tools 2025 - DevTools Academy](https://www.devtoolsacademy.com/blog/state-of-ai-code-review-tools-2025/)

### アーキテクチャ準拠チェック
- [ArchUnitNET - GitHub](https://github.com/TNG/ArchUnitNET)
- [ArchUnitNET Documentation](https://archunitnet.readthedocs.io/en/latest/guide/)
- [Writing ArchUnit style tests for .NET - Ben Morris](https://www.ben-morris.com/writing-archunit-style-tests-for-net-and-c-for-self-testing-architectures/)

### コード変更リスク分析
- [CodeScene Behavioral Code Analysis](https://codescene.com/product/behavioral-code-analysis)
- [Code Maat - GitHub](https://github.com/adamtornhill/code-maat)
- [CodeCohesion - GitHub](https://github.com/paulrayner/codecohesion)
- [Git History Analyzer GitHub Action](https://github.com/marketplace/actions/git-history-analyzer-and-code-quality-predictor)
- [Source Code Hotspots - arXiv](https://arxiv.org/html/2602.13170)
- [Focus Refactoring with Hotspots Analysis](https://understandlegacycode.com/blog/focus-refactoring-with-hotspots-analysis/)

### エージェント統合
- [NDepend MCP Server Guide](https://blog.ndepend.com/developing-an-mcp-server-with-c-a-complete-guide/)
- [Parasoft AI Agents & MCP Servers](https://www.parasoft.com/blog/ai-agents-mcp-servers-software-quality/)
- [Code Analysis MCP Server - PulseMCP](https://www.pulsemcp.com/servers/saiprashanths-code-analysis)
- [CodePathfinder MCP](https://codepathfinder.dev/mcp)
- [Codacy MCP Server - PulseMCP](https://www.pulsemcp.com/servers/codacy)
- [GitHub Copilot Coding Agent](https://github.com/newsroom/press-releases/coding-agent-for-github-copilot)
- [Claude and Codex in Copilot - GitHub Changelog](https://github.blog/changelog/2026-02-26-claude-and-codex-now-available-for-copilot-business-pro-users/)
- [GitHub Copilot CLI GA - GitHub Changelog](https://github.blog/changelog/2026-02-25-github-copilot-cli-is-now-generally-available/)
- [Copilot + Claude Code Multi-Agent - SmartScope](https://smartscope.blog/en/generative-ai/github-copilot/github-copilot-claude-code-multi-agent-2025/)
