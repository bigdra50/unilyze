# Phase 5: Code Smell 検出 — Unilyze vs JetBrains inspectcode 質的比較

## 概要

Unilyze 自身のソースコード (`src/Unilyze/`, 66型) を対象に、Unilyze の Code Smell 検出と JetBrains inspectcode (ReSharper CLI, v2025.3.3) の SARIF 出力を質的に比較した。

## 結論

両ツールは検出領域がほぼ完全に非重複。Precision/Recall の定量比較は成立しない。

JetBrains inspectcode は C# に対して循環的複雑度・認知的複雑度・メソッド行数・ネスト深度・結合度・凝集度のルールを持たない。3,127件の全ルールを精査した結果、複雑度系ルールは C++ 向け (`CppClangTidyReadabilityFunctionCognitiveComplexity` 等) のみ存在し、C# 向けにはゼロだった。

## ツール特性の違い

| 観点 | Unilyze | JetBrains inspectcode |
|------|---------|----------------------|
| 検出対象 | 構造的・計量的 Code Smell | コードスタイル・正確性・死コード |
| 複雑度 (CycCC/CogCC) | 検出する | C# 向けルールなし |
| メソッド行数 | LongMethod として検出 | ルールなし |
| ネスト深度 | DeepNesting として検出 | ルールなし |
| パラメータ数 | ExcessiveParameters として検出 | ルールなし |
| 結合度 (CBO) | HighCoupling として検出 | ルールなし |
| 凝集度 (LCOM) | LowCohesion として検出 | ルールなし |
| 継承深度 (DIT) | DeepInheritance として検出 | ルールなし |
| GodClass | 行数/メソッド数ベースで検出 | ルールなし |
| 命名規則 | 検出しない | InconsistentNaming (19件) |
| 死コード | 検出しない | UnusedMember/Type/Parameter 等 (29件) |
| コード重複 | 検出しない | DuplicatedStatements (1件) |
| カプセル化 | 検出しない | MemberCanBePrivate (13件) |
| Null 安全性 | 検出しない | 条件の常真/偽 (6件) |
| パターン提案 | 検出しない | MergeIntoPattern 等 (10件) |

## 検出結果サマリー

### Unilyze: 22 smells / 16 types

| Kind | 件数 |
|------|------|
| DeepNesting | 9 |
| HighComplexity | 7 |
| HighCoupling | 3 |
| GodClass | 2 |
| ExcessiveParameters | 1 |

### JetBrains inspectcode: 89 findings / 21 types (SUGGESTION 以上)

| カテゴリ | 件数 |
|---------|------|
| Code Style (命名・パターン・冗長等) | 39 |
| Dead Code (未使用メンバー・型・パラメータ) | 29 |
| Encapsulation (可視性縮小) | 13 |
| Correctness (null 条件) | 6 |
| Duplication | 1 |
| その他 | 1 |

## 型レベルの重複分析

16 types (Unilyze) と 18 types (JB, WARNING以上) の集合比較:

- 両方が指摘: 10 types
- Unilyze のみ: 6 types
- JB のみ: 8 types

ただし「両方が指摘」した 10 型でも、指摘内容は完全に異なる。

### 代表的な照合

#### AnalysisPipeline

| Unilyze | JB |
|---------|-----|
| HighComplexity: EnrichSingleType (CogCC 19) | InconsistentNaming: `RecalculateCycCC` 等 4件 |
| DeepNesting: EnrichSingleType (depth 6) | — |
| HighCoupling: CBO 19 | — |

Unilyze は構造的問題 (複雑度・ネスト・結合度) を指摘。JB は命名規則違反のみ。

#### CognitiveComplexity

| Unilyze | JB |
|---------|-----|
| HighComplexity: Walk (CycCC 20) | ConditionIsAlwaysTrue 4件 |
| DeepNesting: Walk (depth 4) | RedundantSuppressNullable 1件 |
| DeepNesting: HandleBinaryExpression (depth 4) | UnusedParameter.Local 1件 |

Unilyze は Walk メソッドの構造的複雑さを指摘。JB は null 安全性と未使用パラメータを指摘。

#### ProgramHelpers

| Unilyze | JB |
|---------|-----|
| DeepNesting: ParseOptions (depth 5) | DuplicatedStatements L90 |

唯一「間接的に関連する」ケース。ネストが深いコードは重複パターンを含みやすい。

#### CodeHealthCalculator

| Unilyze | JB |
|---------|-----|
| ExcessiveParameters: CalculateHealthScore (6 params) | InconsistentNaming: `avgMI` 等 3件 |

Unilyze はパラメータ数、JB は命名。同じ型だが別の観点。

## Unilyze のみが検出した型

| Type | Smell |
|------|-------|
| HtmlTemplate | GodClass (2201行) |
| TypeIdentity | GodClass (22メソッド) |
| CyclomaticComplexity | HighComplexity: Calculate (CycCC 22) |
| CouplingMetricsCalculator | HighComplexity: CountCouplings (CogCC 15) |
| SkillInstaller | DeepNesting: Install/List (depth 5) |
| TypeAnalyzer | HighCoupling: CBO 18 |

HtmlTemplate (2201行) は明らかな God Class だが、JB はこの型に対して一切の指摘を出さなかった。

## JB のみが検出した型 (WARNING以上)

| Type | Finding |
|------|---------|
| AnalysisResult | RedundantUsingDirective |
| CodeSmellDetector | InconsistentNaming (6件), UnusedParameter |
| CsprojParser | NotAccessedPositionalProperty |
| LcomCalculator | RedundantCast |
| MethodMetricsCalculator | InconsistentNaming (2件) |
| Program | ConditionalAccess (2件), MergeIntoPattern (6件), RedundantSuppressNullable |
| TrendAnalyzer | InconsistentNaming, RedundantUsingDirective |
| TypeInfo | InconsistentNaming (2件), NotAccessedField, UnusedMember |

すべてスタイル・死コード系。構造的品質問題はゼロ。

## Precision / Recall の考察

従来の Precision/Recall 計算には「共通ルール空間」が必要だが、Unilyze と JB inspectcode はルール空間が非重複のため、直接の数値比較は不適切。

間接的な近似として:

- Unilyze の ExcessiveParameters (パラメータ数 > 5) vs JB の UnusedParameter: 検出対象が異なる (多すぎる vs 使っていない)
- Unilyze の GodClass vs JB の MemberCanBePrivate: 観点が異なる (大きすぎる vs カプセル化不足)

これらは相関する可能性はあるが、同一の指摘とは言えない。

## 補完性の評価

両ツールは相補的。併用価値が高い。

```
+-----------------------------------+-----------------------------------+
|           Unilyze                 |       JetBrains inspectcode       |
|                                   |                                   |
|  Structural / Metric-based        |  Stylistic / Semantic             |
|  - Complexity (CycCC, CogCC)      |  - Naming conventions             |
|  - Method size (lines)            |  - Dead code detection            |
|  - Nesting depth                  |  - Encapsulation (visibility)     |
|  - Parameter count                |  - Null safety                    |
|  - Coupling (CBO, Ca, Ce)         |  - Pattern modernization          |
|  - Cohesion (LCOM)                |  - Code duplication               |
|  - Inheritance depth (DIT)        |  - Redundant code                 |
|  - God Class                      |                                   |
|                                   |                                   |
|        [overlap: ~0%]             |                                   |
+-----------------------------------+-----------------------------------+
```

## 環境

- Unilyze v0.1.2 (dotnet global tool)
- JetBrains inspectcode v2025.3.3 (ReSharper CLI)
- 対象: `src/Unilyze/Unilyze.csproj` (66型)
- 出力: SARIF, severity >= SUGGESTION
- JB 全ルール数: 3,127 (うち C# 複雑度系: 0)

## 所感

JetBrains inspectcode は「書き方の問題」を見つけるツール。Unilyze は「設計の問題」を見つけるツール。Code Smell という同じ語を使っていても、検出する問題空間が根本的に異なる。

C# 静的解析で構造的メトリクスベースの Code Smell 検出を行うツールは、SonarQube/SonarCloud (商用) を除くと選択肢が限られる。Unilyze がこの空白を埋めている形になっている。
