# Roslynベースのコード品質分析: OSSプロジェクトと先行事例

調査日: 2026-03-12

## 目次

1. [Roslynを使ったコード品質分析のOSSプロジェクト](#1-roslynを使ったコード品質分析のossプロジェクト)
2. [コードクローン検出ツール](#2-コードクローン検出ツール)
3. [Roslyn SemanticModelを使った高度な分析](#3-roslyn-semanticmodelを使った高度な分析)
4. [コード品質の「ダッシュボード」的アプローチ](#4-コード品質のダッシュボード的アプローチ)
5. [Git履歴 x 静的解析の組み合わせ](#5-git履歴-x-静的解析の組み合わせ)
6. [unilyzeへの示唆](#6-unilyzeへの示唆)

---

## 1. Roslynを使ったコード品質分析のOSSプロジェクト

### 1.1 主要プロジェクト一覧

| プロジェクト | ルール数 | 焦点 | NuGet DL数 | 備考 |
|---|---|---|---|---|
| [Roslynator](https://github.com/dotnet/roslynator) | 500+ | スタイル・品質・リファクタリング | - | .NET Foundation管理。IDE拡張からNuGet移行中 |
| [StyleCop.Analyzers](https://github.com/DotNetAnalyzers/StyleCopAnalyzers) | SA系ルール | スタイル一貫性 | 21M+ | `stylecop.json`でカスタマイズ |
| [ErrorProne.NET](https://github.com/SergeyTeplyakov/ErrorProne.NET) | - | 正確性・正しさ | - | Google error-proneのC#版。Equalsの誤実装検出等 |
| [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) | MA0001~MA0173+ | ベストプラクティス全般 | - | StringComparison漏れ、ConfigureAwait等を網羅 |
| [SonarAnalyzer.CSharp](https://github.com/SonarSource/sonar-dotnet) | 380+ C# / 130+ VB | バグ・脆弱性・コードスメル | - | スタンドアロン利用可。認知的複雑度等メトリクス付き |
| [Microsoft.Unity.Analyzers](https://github.com/microsoft/Microsoft.Unity.Analyzers) | - | Unity固有の問題 | - | SerializeField誤警告の抑制、Unity APIの正しい使用 |
| [SecurityCodeScan](https://github.com/security-code-scan/security-code-scan) | - | 脆弱性パターン検出 | ~1M | SQLi, XSS等 |
| [AsyncFixer](https://github.com/semihokur/AsyncFixer) | - | Async/Await診断 | 400K+ | 非同期コードの典型的ミス |
| [BannedApiAnalyzers](https://github.com/dotnet/roslyn-analyzers) | - | 禁止API使用検出 | - | テキストファイルで禁止APIリスト定義 |

### 1.2 Roslynator の詳細

- dotnet/roslynator として .NET Foundation 配下で管理
- 500+ のアナライザ、リファクタリング、コードフィックスを提供
- 次期メジャーリリースでIDE拡張からアナライザを削除し、NuGet配布に一本化予定
- Testing Framework を提供しており、カスタムアナライザのユニットテストに利用可能
- Roslyn .NET API を拡張するAPIパッケージも別途提供
- 資金: .NET on AWS OSS Fund ($6,000)、Microsoft ($1,000) 等

### 1.3 ErrorProne.NET の特徴

- Google の error-prone（Java向け）にインスパイアされた正確性重視のアナライザ
- 著者: Sergey Teplyakov（Microsoft所属）
- 検出例:
  - `object.Equals(object)` の疑わしい実装（右辺パラメータ未使用）
  - struct の readonly 化推奨（`MakeStructReadOnlyAnalyzer`）
  - ValueTask の誤用検出（EPC14/15）

### 1.4 Meziantou.Analyzer の注目ルール

| ルールID | 内容 | 重大度 |
|---|---|---|
| MA0001 | `StringComparison` が欠落 | suggestion |
| MA0004 | `Task.ConfigureAwait` を使え | warning |
| MA0009 | 正規表現評価にタイムアウトを追加 | warning |
| MA0142 | パターンマッチング推奨（`== null` より `is null`） | suggestion |
| MA0155 | `async void` を避け `async Task` を使え | warning |
| MA0160 | 結果を捨てるなら `TryGetValue` より `ContainsKey` | warning |

`<MeziantouAnalysisMode>` MSBuild プロパティでデフォルト重大度を一括設定可能。他アナライザとの重複ルールを自動検出する機能あり。

### 1.5 Unity固有のアナライザ

| プロジェクト | 管理者 | 特徴 |
|---|---|---|
| [Microsoft.Unity.Analyzers](https://github.com/microsoft/Microsoft.Unity.Analyzers) | Microsoft | VS同梱。DiagnosticSuppressor APIでUnityに不適切なCA警告を抑制 |
| [UnityEngineAnalyzer](https://github.com/vad710/UnityEngineAnalyzer) | コミュニティ | AOTやパフォーマンス問題の検出。現在はMicrosoft版推奨 |

Unityでは `.dll` に `RoslynAnalyzer` ラベルを付与することでプロジェクト内にアナライザを組み込める。`BannedApiAnalyzers` を使って `GameObject.Find` やリフレクション系APIの使用を禁止する運用も有効。

### 1.6 VS 2026 での注意点

Visual Studio 2026 Community Edition では「Run Analyzers」メニューが完全に削除された（[GitHub Issue #7781](https://github.com/dotnet/roslyn-analyzers/issues/7781)）。Analyzeメニュー自体がメインメニューバーから消失。フルソリューション解析はライブ解析に依存する形となり、大規模ソリューションではパフォーマンス問題が発生しうる。CLI経由（`dotnet build` + アナライザ）での運用がより重要になった。

---

## 2. コードクローン検出ツール

### 2.1 ツール比較

| ツール | アルゴリズム | 検出タイプ | 言語対応 | スケーラビリティ | 特徴 |
|---|---|---|---|---|---|
| [similarity](https://github.com/mizchi/similarity) | APTED (木構造編集距離) | Type 1-3 + semantic | TS/JS (本命), Py, Rust, C# (実験的) | ~60KLoC/秒 | AI連携前提設計 |
| [jscpd](https://www.npmjs.com/package/jscpd) | Rabin-Karp | Type 1-2 | 150+ 形式 | 中小規模 | CI/CD統合向き。軽量 |
| PMD CPD | Karp-Rabin | Type 1-2 | Java, C++他 | 中小規模 | PMDスイートの一部 |
| CCFinderX | トークンベース比較 | Type 1-2 | C, C++, Java, COBOL | ~100MLOC | 大規模解析可能だがType-3非対応 |
| [SourcererCC](https://github.com/Mondego/SourcererCC) | prefix filtering + token比較 | Type 1-3 | 任意言語 | 100MLOC+ | CCFinderXの2倍速でType-3も検出 |
| Deckard | AST比較 (ベクトル化) | Type 1-3 (一部4) | 複数言語 | 中規模 | 高精度だがスケーラビリティに課題 |
| [CCStokener](https://www.sciencedirect.com/science/article/abs/pii/S0164121223000134) | セマンティックトークン | Type 1-4 | - | - | トークンにn-gramで構造情報付与。DL不要 |
| [Amain](https://github.com/CGCL-codes/Amain) | AST + マルコフ連鎖 | Type 1-4 | - | - | 精度・F1でトップクラス |
| [Philips DuplicateCodeAnalyzer](https://github.com/philips-software/roslyn-analyzers) | Roslyn SyntaxTree | - | C# | - | Roslyn Analyzerとしてビルド時に動作 |

### 2.2 クローンタイプの定義

| タイプ | 定義 | 検出難度 |
|---|---|---|
| Type 1 (Exact) | 空白・コメント以外は同一 | 低 |
| Type 2 (Parameterized) | 識別子・型・リテラルが異なる | 中 |
| Type 3 (Near-miss) | 文の追加・削除・変更あり | 高 |
| Type 4 (Semantic) | 構文は異なるが同じ機能 | 最高 |

### 2.3 アルゴリズム分類と特性

```
+------------------+------------------+------------------+
| Token-based      | AST-based        | Graph/DL-based   |
|                  |                  |                  |
| 高速             | 高精度           | セマンティック    |
| スケーラブル      | 構文理解         | 機能的等価検出    |
| Type 1-3         | Type 1-3 (一部4) | Type 3-4         |
| 誤検出あり        | パース負荷高い    | 学習データ必要    |
|                  |                  |                  |
| jscpd, CPD       | Deckard, Amain   | DeepSim, FCCA    |
| CCFinderX        | similarity       | CodeBERT         |
| SourcererCC      |                  | CCStokener       |
+------------------+------------------+------------------+
```

最近の研究トレンド: まずトークンベースで候補を高速に絞り込み、次にAST/セマンティック検証で精度を上げる二段階アプローチが計算効率と精度を両立する。LLM（CodeBERT等）を使ったアプローチも研究段階で進展しているが、ドメイン固有ライブラリで偽マッチが出る課題がある。

### 2.4 mizchi/similarity の詳細

- 元はTypeScript実装だったがV8ヒープエラーにより Rust で再実装（`oxc_parser` 使用）
- コアアルゴリズム: APTED（木構造編集距離）でASTの構造的類似度を計算し、コード量に基づくペナルティでスコア調整
- C# は `similarity-generic` として tree-sitter パーサ経由で実験的サポート（tree-sitter-c-sharp はRoslyn文法ベース）
- 高速化: Bloom filter導入で約5x、マルチスレッドで約4x、合計約50xの高速化
- AI連携を前提に設計: `similarity-ts . --threshold 0.8` の出力をClaude/GPT-4に渡してリファクタリング計画を策定
- 「ほぼすべての実装を Claude Code で行った」（mizchi氏）
- `--experimental-overlap`: 関数内の部分的コード重複も検出可能
- `--cross-file`: ファイル横断での類似度検出
- 出力形式: VSCode互換。`Similarity: 85.00%, Priority: 8.5` のようにインパクト順で表示

### 2.5 C# 向けコードクローン検出の現状

- Roslyn Analyzers に対するDuplicate Code Detection の feature request: [dotnet/roslyn-analyzers#4978](https://github.com/dotnet/roslyn-analyzers/issues/4978)
- Philips Software の Roslyn ベース重複コードアナライザ: ビルド時にIDEフィードバック可能
- Clone Detective for Visual Studio: 旧ツール（維持されていない）
- similarity-generic のC#サポートはまだ実験的だが、tree-sitter-c-sharp（C# 1~10サポート）を使用しており文法的には安定

---

## 3. Roslyn SemanticModelを使った高度な分析

### 3.1 現状の unilyze

現在のプロジェクトは SyntaxTree のみを使用し、SemanticModel は未使用（README L119に明記）。

SyntaxTreeのみで実現していること:
- 型の抽出（class, record, struct, interface, enum, delegate）
- メンバーの抽出（field, property, method, event, indexer）
- 依存関係の推論（継承, インターフェース実装, フィールド型, プロパティ型, メソッドパラメータ, 戻り値型等）
- 属性とジェネリック制約の解析
- 2パスによるインターフェース判定（knownInterfaces + ヒューリスティック）

SyntaxTreeのみに起因する制約:
- 型名の文字列マッチングに依存（同名の異なる名前空間の型を区別できない）
- 型推論（`var`）から実際の型情報を取得できない
- メソッド呼び出しのオーバーロード解決ができない
- 外部ライブラリ（UnityEngine等）への依存を正確にトレースできない
- 拡張メソッドの解決ができない

### 3.2 SyntaxTree と SemanticModel の比較

| 機能 | SyntaxTree | SemanticModel |
|---|---|---|
| コード構造（ノード・トークン） | 可能 | 可能 |
| シンボル解決（何を指しているか） | 不可 | `GetSymbolInfo()` |
| 型情報の取得 | 不可 | `GetTypeInfo()` |
| スコープ・名前束縛 | 不可 | 可能 |
| オーバーロード解決 | 不可 | 可能 |
| 定数値の評価 | 不可 | `GetConstantValue()` |
| データフロー解析 | 不可 | `AnalyzeDataFlow()` |
| 制御フロー解析 | 不可 | `AnalyzeControlFlow()` |
| コスト | 低（パースのみ） | 高（Compilationが必要） |

### 3.3 SemanticModel の取得方法

```
SyntaxTree (1ファイルのパース結果)
    |
    +-- 他のSyntaxTree群
    +-- 参照アセンブリ情報
    +-- コンパイラオプション
    |
    v
Compilation (プロジェクト全体のコンパイル情報)
    |
    v
SemanticModel (1つのSyntaxTreeに対する意味情報)
```

Compilation は「コンパイラが見ているのと同じ全体像」を表す。ソースファイル間の依存、外部アセンブリ参照、名前空間解決のすべてがここに集約される。`var` の実型解決やオーバーロード選択が可能になるのは、Compilationが全ての型情報を保持しているため。

### 3.4 SemanticModel で可能になる分析

#### データフロー解析

`SemanticModel.AnalyzeDataFlow(firstStatement, lastStatement)` が返す `DataFlowAnalysis`:

| プロパティ | 説明 |
|---|---|
| DataFlowsIn | 領域外で宣言され、領域内で読まれる変数 |
| DataFlowsOut | 領域内で書かれ、領域外で読まれる変数 |
| ReadInside | 領域内で読まれる変数 |
| WrittenInside | 領域内で書かれる変数 |
| AlwaysAssigned | 領域内で必ず代入される変数 |
| VariablesDeclared | 領域内で宣言される変数 |

用途: Extract Method の前提分析、static化可能なメソッドの検出、未使用変数の検出

#### 制御フロー解析

`ControlFlowAnalysis`:
- EntryPoints: 外部からジャンプで到達する文
- ExitPoints: 外部にジャンプする文
- EndPointIsReachable: 到達可能性
- ReturnStatements: return文の一覧

用途: 例外を飲み込むcatchブロックの検出、デッドコードの検出

#### IOperation ベースの CFG (Control Flow Graph)

`ControlFlowGraph.Create(syntaxNode, semanticModel)` で構築:

```
[Entry Block]
    |
    v
[Basic Block 1] -- operations + branch conditions
    |          \
    v           v
[Basic Block 2] [Basic Block 3]
    |          /
    v         v
[Exit Block]
```

roslyn-analyzers のデータフロー解析フレームワーク:

| コンポーネント | 役割 |
|---|---|
| DataflowAnalysis | CFG上のワークリストベース解析。不動点到達まで反復 |
| AbstractDataFlowAnalysisContext | 解析コンテキスト（入力データ） |
| DataFlowAnalysisResult | 全オペレーションの解析結果 |
| DataFlowOperationVisitor | transfer function の定義 |
| AbstractDomain | 値のマージ・比較を定義する抽象ドメイン |

### 3.5 SemanticModel 導入のトレードオフ

```
導入メリット                          導入コスト
+-----------------------------+    +-----------------------------+
| 正確な型解決                  |    | Compilation が必要           |
| var の実型取得                |    |   -> 全参照アセンブリが必要    |
| オーバーロード解決             |    |   -> Unity の DLL 群が必要    |
| 外部依存の正確なトレース       |    | メモリ使用量の大幅増加         |
| データフロー/制御フロー解析    |    | 解析速度の低下               |
| 定数折りたたみ                |    | セットアップの複雑化           |
| 拡張メソッドの解決             |    | エラー耐性の低下              |
+-----------------------------+    |   (参照不足でモデル不完全)     |
                                   +-----------------------------+
```

Unity プロジェクトでの SemanticModel 構築は、Unity の DLL 群（UnityEngine.dll, UnityEditor.dll 等）への参照解決が必要。現状の「.asmdef + .cs のみで動作」というシンプルさとは大きくトレードオフになる。

---

## 4. コード品質の「ダッシュボード」的アプローチ

### 4.1 SonarQube の Quality Gate

Quality Gate は「このコードはリリース可能か?」に答えるための品質基準。

デフォルト "Sonar Way" Quality Gate 条件:
- Reliability Rating が A より悪い -> 不合格
- Security Rating が A より悪い -> 不合格
- Maintainability Rating が A より悪い -> 不合格

レーティングスケール:

| ランク | 条件 |
|---|---|
| A | info以下のissueのみ |
| B | low issue が1つ以上 |
| C | medium issue が1つ以上 |
| D | high issue が1つ以上 |
| E | blocker issue が1つ以上 |

主要メトリクス:
- Technical Debt: 全保守性issueの修正コスト合計（分単位、1日=8時間換算）
- Technical Debt Ratio: `技術的負債 / (1行あたり開発コスト x 行数)`。デフォルト1行=30分
- Maintainability Rating: TDR <= 5% で A
- Cyclomatic Complexity / Cognitive Complexity
- 重複行・重複ブロック
- カバレッジ率

### 4.2 CodeClimate の Maintainability Rating

Technical Debt Ratio = 修正コスト / 再実装コスト。プロジェクトレベルではTDRに基づくレター評価（A~F）。

10項目の技術的負債チェック:
1. 引数の過多
2. 複雑なブール論理
3. ファイル長超過
4. メソッド長超過
5. メソッド複雑度
6. 戻り値文の過多
7. 重複コード
8. その他の構造的問題

Churn vs. Quality マトリクス:
- X軸: 変更頻度（git履歴から算出）
- Y軸: 品質グレード
- 右上象限 = 高頻度変更 + 低品質 = 最優先リファクタリング対象

このマトリクスはCodeSceneのホットスポット分析と概念的に同じだが、CodeClimate は静的解析寄り、CodeSceneは行動分析（git履歴）寄り。

### 4.3 CodeScene の Code Health

- 1~10のスケール（10 = 最も保守性が高い）
- 25+ の要因をスキャン
- LoC加重平均で集約
- 閾値: Healthy (>= 9), Warning (4~9), Alert (< 4)

検出するコードスメル（複合ルール）:

| スメル | 構成要素 |
|---|---|
| Brain Method | 行数 + 循環的複雑度 + ネスト深度 + 中心性スコア |
| Brain Class | 行数 + メソッド数 + 少なくとも1つの複雑な中心メソッド |
| Low Cohesion | LCOM4 (Lack of Cohesion of Methods) |
| Developer Congestion | 同一コードへの複数開発者のアクセスパターン |
| Complex Code by Former Contributors | 退職者が書いた低Code Healthコード |

研究結果:
- Alert コードは Healthy コードの 15倍の欠陥を含む
- Alert コードの問題解決には平均 124% 多い開発時間を要する
- Code Health は SonarQube の Maintainability Rating、Microsoft の Maintainability Index、人間の専門家の平均を上回る精度

アルゴリズムはプロプライエタリ。OSSでの再現は個別要因（複雑度、ネスト深度、LCOM4等）の組み合わせで近似可能。

### 4.4 コーディングエージェントへの組み込み

#### CodeScene MCP Server の事例

[codescene-mcp-server](https://github.com/codescene-oss/codescene-mcp-server) が3レベルのセーフガードを提供:

| レベル | ツール名 | タイミング |
|---|---|---|
| 継続的 | `code_health_review` | コード生成中にリアルタイム |
| コミット前 | `pre_commit_code_health_safeguard` | ステージングファイルに対して |
| PR前 | `analyze_change_set` | ブランチ全体 vs base ref |

エージェントループの動作:
1. コードベースの健全性マップを把握
2. 変更ごとに Code Health シグナルでチェック
3. 品質低下 -> フィードバック -> エージェントが自動改善
4. テスト通過 + Code Health 改善まで反復

AGENTS.md パターン: エージェントの行動規範をファイルとして定義。ツール呼び出し順序・判断ロジックをエンコード。CodeScene + MCP + AGENTS.md の3層構造で「抽象的なエンジニアリング原則を実行可能なガイダンスに変換」。

6つの運用パターン（CodeScene公式ブログより）:

| # | パターン | 説明 |
|---|---|---|
| 1 | Pull Risk Forward | Code Health >= 9.5 の部分のみにAI生成を適用 |
| 2 | Safeguard Generated Code | 3レベルのセーフガードで品質を自動チェック |
| 3 | Refactor to Expand AI-Ready Surface | Code Health 改善でAI適用可能領域を拡大 |
| 4 | Encode Principles in AGENTS.md | エージェントの行動規範をファイルで定義 |
| 5 | Use Code Coverage as Behavioral Guardrail | カバレッジ低下をエージェントの暴走検知に利用 |
| 6 | Automate Checks End-to-End | ユニットテスト + E2Eテストで検証を自動化 |

実績:
- CodeScene社内: 2~3xのタスク速度向上（完全エージェント化から4ヶ月）
- loveholidays社: セーフガード導入後5ヶ月で50%のコードをエージェント支援で作成
- 研究結果: AIコーディングアシスタントは不健全なコードベースで欠陥リスクを30%以上増加

---

## 5. Git履歴 x 静的解析の組み合わせ

### 5.1 Adam Tornhill と CodeScene

書籍:
- "Your Code as a Crime Scene" (2014, [第2版](https://pragprog.com/titles/atcrime2/your-code-as-a-crime-scene-second-edition/)) — 犯罪捜査のプロファイリング技法をコード分析に応用
- "Software Design X-Rays" — 行動コード分析によるアーキテクチャ改善

核心的な知見: コードベースの 1~2% が開発作業の 70% を占める。この「ホットスポット」に集中することでチームは 2倍速く、10倍予測可能になる。

Adam Tornhill（2025年11月のポッドキャストインタビューより）:
> "Change coupling is important because it shows you the change patterns in your codebase. And it has so many different use cases. The most obvious one is to be able to reason about the cost of change."

### 5.2 Code Maat (OSS)

[code-maat](https://github.com/adamtornhill/code-maat) — Clojure製のVCSログ解析ツール。

対応VCS: Git, SVN, Mercurial, Perforce, TFS

サポートする分析:

| 分析 | 説明 |
|---|---|
| revisions | ファイルごとの変更回数 |
| coupling | 論理的結合（同時変更パターン） |
| soc (sum of coupling) | 結合度の総和 |
| authors | ファイルごとの開発者数 |
| entity-ownership | 主担当開発者 |
| entity-effort | ファイルごとの開発工数 |
| fragmentation | 知識の分散度 |
| age | 最終変更からの経過月数 |
| communication | チーム間コミュニケーション必要性 |
| abs-churn / entity-churn / author-churn | 絶対/相対チャーン |

Change Coupling のパラメータ:
- 最小リビジョン数: 5（デフォルト）
- 最小共有リビジョン数: 5
- 最小結合パーセント: 30%
- 最大チェンジセットサイズ: 30モジュール

### 5.3 Hotspot 分析

ホットスポット = 高い変更頻度(churn) + 高い複雑度(complexity)

```
     複雑度 (高)
       ^
       |   x  <-- ホットスポット: 最優先リファクタリング対象
       |  x x
       | x   x
       |x  o  o  <-- 安定した複雑コード: 監視のみ
       +-----------> 変更頻度 (高)
         o  o  o  <-- 頻繁に変更されるが単純: 低リスク
```

DIYホットスポット分析:
```bash
# 過去12ヶ月で最も変更されたファイルTop20
git log --format=format: --name-only --since=12.month \
  | egrep -v '^$' \
  | sort | uniq -c | sort -nr | head -20
```

このリストと静的解析の複雑度メトリクスを組み合わせることで、ツールなしでもホットスポットを特定できる。

### 5.4 Change Coupling（論理的結合）

物理的な依存関係がなくても、同じコミットで変更されるファイルペアを検出。

検出方法:
- 最も基本的: 同一コミット内のファイルペアをカウント
- 窓付き: 一定期間内のコミットを結合窓として扱う

意義:
- 隠れたアーキテクチャ上の依存を発見
- リファクタリング対象の特定（結合を断ち切るべき箇所）
- 変更コストの見積もり（A を変えたら B も変える必要がある確率）

### 5.5 Social Complexity（社会的複雑度）

| 指標 | 説明 | 影響 |
|---|---|---|
| 開発者数 / ファイル | そのファイルに触った開発者の数 | 多いほどコミュニケーションコスト増、欠陥予測因子 |
| 主担当開発者 | 最も多くのコミットを持つ開発者 | 離脱時のリスク評価 |
| 知識の分散度 | 変更がチーム間にどれだけ分散しているか | 高いほど責任の所在が曖昧 |
| Bus Factor | 何人離脱したらプロジェクトが停止するか | 低いほど高リスク |

code-maat の `authors` 分析はこれらの指標の基礎データを提供する。

### 5.6 最近のトレンド（2025-2026）

- 学術研究: LLM + ホットスポット分析のハイブリッド手法が発表（Springer, 2025）
- CodeScene MCP Server (2025年12月リリース): エージェントのワークフローにCode Healthを統合
- GitHub Actions: `git log` 解析 + ML予測で品質リスクを自動判定するアクション
- "Temporal Code Intelligence": git履歴パターンからコード品質の進化を予測するプラットフォーム概念

---

## 6. unilyzeへの示唆

### 6.1 短期的に取り組めること（SyntaxTree のみ）

| 施策 | 説明 | 参考 |
|---|---|---|
| メトリクス拡充 | Cyclomatic Complexity, ネスト深度, メソッド/クラス行数をJSON出力に追加 | SonarQube, CodeScene |
| コードスメル検出 | God Class (行数 + メソッド数), Long Method (行数 + 複雑度) | CodeScene Code Health |
| Quality Score | 複数メトリクスの加重スコア（1-10） | CodeScene Code Health |
| HTML ダッシュボード | 既存のHTMLビューアに品質メトリクスを統合表示 | CodeClimate |
| 重複コード検出 | SyntaxTreeの部分木比較 or トークンベース | Philips DuplicateCodeAnalyzer |

### 6.2 中期的な拡張（Git履歴統合）

| 施策 | 説明 | 実装方法 |
|---|---|---|
| Hotspot 分析 | 変更頻度 x 複雑度のマトリクス | `git log --numstat` + 既存メトリクス |
| Change Coupling | 同時変更ファイルペアの検出 | `git log` コミットごとのファイルリスト |
| Code Age | ファイルごとの最終変更日 | `git log -1 --format=%ci` |
| Author Map | ファイルごとの開発者数・主担当者 | `git shortlog -sn` |
| Churn vs Complexity | ホットスポット可視化 | HTMLビューアにバブルチャート追加 |

### 6.3 SemanticModel 導入（必要に応じて段階的に）

段階的導入の案:

```
Step 1: 限定的なSemanticModel（参照なし）
  -> Compilationを作るが外部参照は空
  -> 同一プロジェクト内の型解決のみ
  -> var の一部解決、同名型の区別

Step 2: Unity DLL参照付きSemanticModel
  -> UnityEngine.dll等を参照に追加
  -> 完全な型解決、API使用パターン分析
  -> セットアップコスト大

Step 3: データフロー/制御フロー解析
  -> CFGベースの高度な分析
  -> null参照リスク、未使用変数の検出
```

Step 1 は比較的低コストで実現可能。外部型は `Unknown` として扱い、プロジェクト内の型のみ正確に解決する形。

### 6.4 エージェント統合の方向性

CodeScene MCP Server のパターンに倣った統合:

```
unilyze の JSON 出力
    |
    +-- 品質スコア (1-10)
    +-- ホットスポット情報
    +-- 依存グラフ
    +-- コードスメル一覧
    |
    v
エージェント (Claude Code / Copilot)
    |
    +-- コンテキストとして品質情報を参照
    +-- リファクタリング対象の優先順位付け
    +-- 変更前後のスコア比較
    +-- AGENTS.md でワークフロー定義
```

### 6.5 推奨される段階的アプローチ

```
Phase 1: メトリクス拡充（SyntaxTreeのみ）
  +-- Cyclomatic Complexity
  +-- ネスト深度
  +-- クラス/メソッド行数
  +-- 品質スコア (1-10)
  |
Phase 2: Git履歴統合
  +-- Hotspot分析
  +-- Change Coupling
  +-- ファイル年齢・開発者情報
  |
Phase 3: ダッシュボード化
  +-- HTMLビューアにメトリクス統合
  +-- Churn vs Complexity マトリクス
  +-- トレンド表示（複数回の分析結果比較）
  |
Phase 4: SemanticModel（必要に応じて）
  +-- 限定的な型解決（Step 1）
  +-- Unity DLL参照（Step 2）
  +-- データフロー解析（Step 3）
```

---

## Sources

### Roslyn Analyzers
- [Roslynator (GitHub)](https://github.com/dotnet/roslynator)
- [Roslynator Documentation](https://josefpihrt.github.io/docs/roslynator/)
- [StyleCop Analyzers (GitHub)](https://github.com/DotNetAnalyzers/StyleCopAnalyzers)
- [ErrorProne.NET (GitHub)](https://github.com/SergeyTeplyakov/ErrorProne.NET)
- [Meziantou.Analyzer (GitHub)](https://github.com/meziantou/Meziantou.Analyzer)
- [Meziantou.Analyzer Rules](https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/README.md)
- [SonarAnalyzer.CSharp (NuGet)](https://www.nuget.org/packages/SonarAnalyzer.CSharp/)
- [SonarSource/sonar-dotnet (GitHub)](https://github.com/SonarSource/sonar-dotnet)
- [Microsoft.Unity.Analyzers (GitHub)](https://github.com/microsoft/Microsoft.Unity.Analyzers)
- [UnityEngineAnalyzer (GitHub)](https://github.com/vad710/UnityEngineAnalyzer)
- [awesome-analyzers (GitHub)](https://github.com/cybermaxs/awesome-analyzers)
- [Roslyn Analyzers Overview (Microsoft Learn)](https://learn.microsoft.com/en-us/visualstudio/code-quality/roslyn-analyzers-overview)
- [Unity Manual - Roslyn Analyzers](https://docs.unity3d.com/6000.3/Documentation/Manual/roslyn-analyzers.html)
- [VS 2026 Run Analyzers missing (GitHub Issue)](https://github.com/dotnet/roslyn-analyzers/issues/7781)

### Code Clone Detection
- [mizchi/similarity (GitHub)](https://github.com/mizchi/similarity)
- [similarity-ts 紹介記事 (Zenn)](https://zenn.dev/mizchi/articles/introduce-ts-similarity?locale=en)
- [jscpd (npm)](https://www.npmjs.com/package/jscpd)
- [SourcererCC (GitHub)](https://github.com/Mondego/SourcererCC)
- [SourcererCC Paper (ICSE 2016)](https://clones.usask.ca/pubfiles/articles/SajnaniSourcererCCICSE2016.pdf)
- [Code Clone Detection Literature & Tools](https://clones.usask.ca/clones/tools/)
- [CCStokener (ScienceDirect)](https://www.sciencedirect.com/science/article/abs/pii/S0164121223000134)
- [Amain (GitHub)](https://github.com/CGCL-codes/Amain)
- [Philips Roslyn Analyzers (GitHub)](https://github.com/philips-software/roslyn-analyzers)
- [dotnet/roslyn-analyzers#4978 - Duplicate Code Detection](https://github.com/dotnet/roslyn-analyzers/issues/4978)
- [tree-sitter-c-sharp (GitHub)](https://github.com/tree-sitter/tree-sitter-c-sharp)
- [Clone Detection Comparison (ICSE 2023)](https://wu-yueming.github.io/Files/ICSE2023_TACC.pdf)

### Roslyn SemanticModel
- [Learn Roslyn Now Part 7: Semantic Model](https://joshvarty.com/2014/10/30/learn-roslyn-now-part-7-introducing-the-semantic-model/)
- [Learn Roslyn Now Part 8: Data Flow Analysis](https://joshvarty.com/2015/02/05/learn-roslyn-now-part-8-data-flow-analysis/)
- [Learn Roslyn Now Part 9: Control Flow Analysis](https://joshvarty.com/2015/03/24/learn-roslyn-now-control-flow-analysis/)
- [Get started with semantic analysis (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/semantic-analysis)
- [Analyzing Control Flow with Roslyn (Atmosera)](https://www.atmosera.com/blog/analyzing-control-flow-with-roslyn/)
- [Writing dataflow analysis based analyzers (roslyn-analyzers)](https://github.com/dotnet/roslyn-analyzers/blob/main/docs/Writing%20dataflow%20analysis%20based%20analyzers.md)
- [DataFlowAnalysis Class (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.dataflowanalysis)
- [ControlFlowGraph (Roslyn source)](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/Operations/ControlFlowGraph.cs)

### Code Quality Dashboards
- [SonarQube Metrics Definition](https://docs.sonarsource.com/sonarqube-server/10.8/user-guide/code-metrics/metrics-definition)
- [SonarQube Quality Gates](https://docs.sonarsource.com/sonarqube-server/10.8/instance-administration/analysis-functions/quality-gates)
- [CodeClimate Maintainability](https://docs.codeclimate.com/docs/maintainability)
- [CodeClimate Trends](https://docs.codeclimate.com/docs/trends)
- [Qlty Maintainability Metrics](https://docs.qlty.sh/cloud/maintainability/metrics)
- [CodeScene Code Health](https://codescene.com/product/code-health)
- [How is Code Health Calculated (CodeScene)](https://supporthub.codescene.com/how-is-code-health-calculated)
- [Code Health KPIs (CodeScene Blog)](https://codescene.com/blog/3-code-health-kpis/)

### Git History x Static Analysis
- [Adam Tornhill - Code as a Crime Scene](https://adamtornhill.com/articles/crimescene/codeascrimescene.htm)
- [Your Code as a Crime Scene, 2nd Edition (Pragmatic Programmers)](https://pragprog.com/titles/atcrime2/your-code-as-a-crime-scene-second-edition/)
- [code-maat (GitHub)](https://github.com/adamtornhill/code-maat)
- [CodeScene MCP Server (GitHub)](https://github.com/codescene-oss/codescene-mcp-server)
- [Agentic AI Coding Best Practice Patterns (CodeScene Blog)](https://codescene.com/blog/agentic-ai-coding-best-practice-patterns-for-speed-with-quality)
- [CodeScene MCP Server Documentation](https://codescene.io/docs/developer-tools/mcp/codescene-mcp-server.html)
- [AI Coding Assistants Raise Defect Risk 30%+](https://techintelpro.com/news/ai/agentic-ai/ai-coding-assistants-raise-defect-risk-30-in-unhealthy-code)
- [Focus Refactoring with Hotspots Analysis](https://understandlegacycode.com/blog/focus-refactoring-with-hotspots-analysis/)
- [Enhancing Hotspot Detection with AI (Springer, 2025)](https://link.springer.com/chapter/10.1007/978-3-032-09318-9_25)
- [Tech Lead Journal #241 - Adam Tornhill](https://techleadjournal.dev/episodes/241/)
