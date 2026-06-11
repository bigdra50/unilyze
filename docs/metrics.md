# Unilyze メトリクス定義

Unilyze が計算する各メトリクスの定義・準拠仕様・既知の差異をまとめる。

## Cognitive Complexity (CogCC)

準拠仕様: [SonarSource Cognitive Complexity Whitepaper](https://www.sonarsource.com/docs/CognitiveComplexity.pdf)

### ルール

| カテゴリ | 対象 | インクリメント |
|---------|------|-------------|
| 構造的 | `if`, `else if`, `else`, `switch`, `for`, `foreach`, `while`, `do`, `catch` | +1 + nesting |
| 基本的 | `goto`, 直接再帰 | +1 |
| 論理演算子 | `&&`, `||`, `or`, `and` | +1（種類変更時のみ。同種の連続は +1 のまま。`or`は`||`、`and`は`&&`と同種扱い） |
| ネスト増加 | lambda, anonymous method | nesting +1 (構造的インクリメントなし) |
| ショートハンド | `??`, `?.` | 0 (インクリメントなし) |

### SonarAnalyzer.CSharp (S3776) との差異

SonarAnalyzer.CSharp 10.20.0 との突合結果（Unilyze 自身のソースコード 70 メソッド）:

| 指標 | 値 |
|------|-----|
| Spearman 順位相関 | 1.000 |
| 完全一致率 | 100.0% (70/70) |
| ±1 以内率 | 100.0% (70/70) |

| 構文 | SonarAnalyzer | Unilyze | 備考 |
|------|-------------|---------|------|
| `or` パターン結合子 | +1 | +1 | 対応済み (`||`と同種扱い) |
| `and` パターン結合子 | +1 | +1 | 対応済み (`&&`と同種扱い) |
| 直接再帰 | +1 | +1 | 対応済み (メソッド名ベースの検出) |
| static ローカル関数 | 独立計算 | メソッドに含む | 仕様違い |
| `??` (null coalesce) | 0 | 0 | 一致 (v0.2.0 で修正済み) |
| `switch` expression | +1 + nesting | +1 + nesting | 一致 |

## Cyclomatic Complexity (CycCC)

準拠仕様: McCabe, T.J. (1976) "A Complexity Measure"

各述語ノード（分岐点）を +1 カウントする。ベースパスは 1。

### カウント対象

| ノード | インクリメント |
|--------|-------------|
| `if` | +1 |
| `case` label / `case` pattern | +1 |
| `for`, `foreach`（分解 foreach 含む）, `while`, `do` | +1 |
| `catch` | +1 |
| `? :` (三項演算子) | +1 |
| `?.` (null 条件) | +1 |
| `??` (null 合体) | +1 |
| `&&`, `||` | 各 +1 |
| bool オペランドの `&`, `|` | 各 +1（semantic model がある解析レベルのみ。SyntaxOnly では型解決できず非カウント） |
| `goto` | +1 |
| `switch` expression arm | +1 |

`??=`、switch expression 自体、catch の `when` フィルタ、`and` / `or` パターンはカウントしない。

### 公式 Roslyn エンジン (CodeAnalysisMetricData / Metrics.exe / CA1502) との規約差

unilyze 自身の src/Unilyze 全 339 メソッドを公式エンジンと突合し、両者の規約差を実証した確定表（97/100 型で残差ゼロ、残り 3 型は ±1 まで分解済み）
（再現手順は [scripts/crossval](../scripts/crossval/)、検証データは「バリデーション (検証)」セクション）:

| 構文 | 公式エンジン | Unilyze |
|------|------------|---------|
| `if` / `? :` / ループ / `case` label・pattern / `?.` / `??` / `&&` / `\|\|` | +1 | +1 |
| `default` label | +1 | カウントしない |
| `catch` | カウントしない | +1 |
| `switch` expression arm | カウントしない | +1 |
| `goto` | カウントしない | +1 |
| `??=` | カウントしない | カウントしない |
| bool の `&` / `\|` | +1 | semantic 時のみ +1 |
| 型集計 | 全メンバーシンボル各 base 1（暗黙 ctor・accessor・operator 含む） | 宣言メソッドのみ各 base 1 |

注意:

- かつて本ドキュメントは「公式は `?.` `??` をカウントしない」と記載していたが、これは誤り（実装突合で反証済み）。両エンジンともカウントする
- CA1502 の既定しきい値 25 を unilyze の CycCC に直接適用してはならない。switch expression arm 等の加算により unilyze 値は系統的に高くなり、概算換算式も提供しない（正確な比較には公式エンジンでの再解析が必要）
- 設計判断: unilyze は拡張解釈（catch / arm / goto は実分岐としてカウント）を意図的に維持する。`catch` と switch arm は McCabe の分岐点定義に忠実であり、既存ベースライン（refactor loop・トレンド・バッジ）の互換も保つ。公式互換値が必要な場合は CA1502 / Metrics.exe を直接使うこと。モダンな複雑度ゲートには SonarAnalyzer S3776 と 100% 整合済みの CogCC を推奨する

## LCOM-HS (Henderson-Sellers)

準拠仕様: Henderson-Sellers, B. (1996) "Object-Oriented Metrics: Measures of Complexity"

### 公式

```
LCOM-HS = (avg(mA) - M) / (1 - M)

mA(f) = フィールド f にアクセスするメソッド数
avg(mA) = 全フィールドの mA の平均
M = インスタンスメソッド数（コンストラクタ含む）
```

### 解釈

| 値 | 意味 |
|-----|------|
| 0.0 | 完全凝集（全メソッドが全フィールドにアクセス） |
| 1.0 | 完全分離（各メソッドが異なるフィールドにのみアクセス） |
| null | 計算不能（フィールド 0 個、またはメソッド 0-1 個） |

### NDepend / CK との差異

| 項目 | NDepend (最新) | CK | Unilyze |
|------|--------------|-----|---------|
| auto-property | F から除外 | F に含む | F から除外 (v0.2.0 で修正済み) |
| コンストラクタ | M に含む | M に含む | M に含む (v0.2.0 で修正済み) |
| static メンバー | 除外 | 除外 | 除外 |

## WMC (Weighted Methods per Class)

準拠仕様: Chidamber, S.R. & Kemerer, C.F. (1994) "A Metrics Suite for Object Oriented Design"

### 公式

```
WMC = Σ CycCC(method_i)  for all methods in class
```

クラス内の全メソッドの Cyclomatic Complexity の合計。重み付けは CycCC を使用。

### 解釈

| 値 | 意味 |
|-----|------|
| 0 | メソッドなし（データクラス、enum等） |
| 1-20 | 一般的な範囲 |
| > 20 | リファクタリング候補 |

## NOC (Number of Children)

準拠仕様: Chidamber & Kemerer (1994)

直接のサブクラス数。DependencyBuilder の Inheritance 依存から逆引きで算出。

### 解釈

| 値 | 意味 |
|-----|------|
| 0 | 継承されていない |
| 高い | 再利用度が高い基底クラス。変更時の影響範囲が大きい |

## RFC (Response For a Class)

### 公式

```
RFC = M + R

M = クラス内のメソッド数（コンストラクタ含む）
R = M 内から呼び出されるユニークな外部メソッド数
```

### Semantic / Syntactic パス

| パス | 解決方法 |
|------|---------|
| Semantic | SemanticModel で InvocationExpression のシンボルを解決。正確 |
| Syntactic (fallback) | InvocationExpression のメソッド名文字列で近似。オーバーロード区別不可 |

### 解釈

| 値 | 意味 |
|-----|------|
| <= 50 | 一般的な範囲 |
| > 50 | テスト・理解が困難になる傾向 |

## CBO (Coupling Between Objects)

準拠仕様: Chidamber & Kemerer (1994)

### 公式

```
CBO = 型 T が結合するユニークな外部型の数
```

型 T の宣言・メンバー・メソッド本体から参照される型の集合から、自身と除外型を除いた件数。

### カウント規約

実装: `CboCalculator.cs`

| パス | 解決方法 |
|------|---------|
| Semantic | 型宣言の descendant から `TypeSyntax` / `ObjectCreationExpression` / `CastExpression` を走査し、`SemanticModel` で `ITypeSymbol` を解決。`INamedTypeSymbol.OriginalDefinition` を集合に追加（ジェネリック型引数・配列要素型も再帰収集） |
| Syntactic (fallback) | base list、フィールド/プロパティ型、メソッド/コンストラクタのシグネチャと本体（局所変数宣言・`new`・cast・`typeof`）から型名文字列を収集 |

共通の除外:

- 自身の型
- Semantic 時: `SpecialType` が `None` 以外の組み込み型、`System.ValueType` / `System.Enum` / `System.Delegate` / `System.MulticastDelegate` / `System.Attribute` / `System.Void`
- Syntactic 時: C# プリミティブ名（`int`, `string`, `object` 等）

CBO は `TypeDependency` グラフとは独立に、型宣言 AST から直接算出する。DI 登録エッジは CBO には含まれない。

### しきい値（コードスメル）

| レベル | 条件 |
|--------|------|
| Warning (`HighCoupling`) | CBO >= 15 |
| Critical (`HighCoupling`) | CBO >= 25 |

定数: `SmellThresholds.HighCouplingCboWarning` / `HighCouplingCboCritical`

### 注意点

- SemanticModel が利用できない `SyntaxOnly` 解析では過小評価される（外部エンジン型への結合が不可視）
- 公式 Metrics エンジンの ClassCoupling とはカウント対象・粒度が異なる（「バリデーション (検証)」参照）

## DIT (Depth of Inheritance)

準拠仕様: Chidamber & Kemerer (1994)

### 公式

```
DIT = 型 T から `System.Object` 手前までの継承チェーン長
```

interface / struct は 0。class / record は直接・間接の基底 class を数える。

### カウント規約

実装: `DitCalculator.cs`

| パス | 規約 |
|------|------|
| Semantic | interface → 0。struct → 0。それ以外は `INamedTypeSymbol.BaseType` を `System.Object` に到達するまで辿り、段数をカウント（`System.Object` 自身は数えない） |
| Syntactic (fallback) | interface / struct / record struct → 0。base list なし → 0。先頭 base が `QualifiedNameSyntax`（外部型）→ 1。同一 syntax tree 内で同名 interface 宣言があれば 0、それ以外 → 1 |

Semantic 計算が失敗した場合、`SemanticEnricher` は syntactic fallback または `TypeNodeInfo.BaseType` の有無（0/1）に縮退する。

### しきい値（コードスメル）

| レベル | 条件 |
|--------|------|
| Warning (`DeepInheritance`) | DIT >= 5 |

定数: `SmellThresholds.DeepInheritanceDitWarning`

### 注意点

- 公式 Metrics エンジンは `object` 継承を 1 として数える規約差があり、全件でオフセットが生じる（「バリデーション (検証)」参照）
- エンジン型（`UnityEngine.MonoBehaviour` 等）を跨ぐ継承は Semantic 解析が必須。`SyntaxOnly` では過小評価される

## Ca / Ce (Afferent / Efferent Coupling)

Martin の安定度分析に基づく型単位の結合度。

### 公式

```
Ca(T) = T を依存先 (To) とするユニーク有向エッジ数
Ce(T) = T を依存元 (From) とするユニーク有向エッジ数
```

入力は `DependencyBuilder.Build` が生成する `TypeDependency` リスト（継承・interface 実装・メンバー型・コンストラクタ/メソッド引数・ジェネリック制約）に加え、解決済みの DI 登録エッジ（VContainer / Zenject）。

### カウント規約

実装: `CouplingMetricsCalculator.cs`

- 解析対象型集合（`allTypes`）に含まれる `FromTypeId` / `ToTypeId` のみカウント
- 自己参照 (`From == To`) は除外
- 同一 `(From, To)` ペアは 1 回のみ（`DependencyKind` が複数あっても重複しない）
- `FromTypeId` または `ToTypeId` が null（解析対象外への未解決エッジ）は除外

Ca / Ce にしきい値ベースのコードスメル判定はない。

### 注意点

- Ca / Ce は依存グラフ上のエッジ数であり、CBO（型宣言 AST からの型参照集合）とは定義が異なる
- 解析対象外の型への DI 登録はエッジとして接続されず、Ca / Ce に寄与しない

## Instability (I)

Martin の Instability。型単位とアセンブリ単位で算出粒度が異なる。

### 公式（型単位）

```
I(T) = Ce(T) / (Ca(T) + Ce(T))     ※ Ca + Ce > 0 の場合
I(T) = null                        ※ Ca + Ce = 0 の場合
```

実装: `CouplingMetricsCalculator.cs`（型ごと）。JSON 出力では小数第 2 位に丸める。

### 公式（アセンブリ単位）

```
I(assembly) = Σ Ce / (Σ Ca + Σ Ce)
```

アセンブリ内全型の Ca / Ce をそれぞれ合算。実装: `AssemblyMetrics.ComputeAssemblyInstability`。`Distance from Main Sequence` の I はこちらを使用する。

### 解釈

| 値 | 意味 |
|-----|------|
| 0.0 | 完全に安定（他型からのみ依存される） |
| 1.0 | 完全に不安定（他型へのみ依存する） |
| null（型のみ） | 入出力結合がゼロ |

Ca / Ce / Instability にコードスメルしきい値はない。

## Halstead Complexity Measures

準拠仕様: Halstead, M.H. (1977) "Elements of Software Science"

### 基本測定値

| 記号 | 意味 |
|------|------|
| n1 (UniqueOperators) | ユニークなオペレータ数 |
| n2 (UniqueOperands) | ユニークなオペランド数 |
| N1 (TotalOperators) | 総オペレータ数 |
| N2 (TotalOperands) | 総オペランド数 |

### 導出メトリクス

| メトリクス | 公式 | 説明 |
|-----------|------|------|
| Volume (V) | `(N1 + N2) * log2(n1 + n2)` | 実装サイズ |
| Difficulty (D) | `(n1 / 2) * (N2 / n2)` | 理解の困難さ。n2=0 の場合は 0 |
| Effort (E) | `D * V` | 実装に必要な精神的労力 |
| EstimatedBugs (B) | `E^(2/3) / 3000` | 推定バグ数 |

## Maintainability Index (MI)

準拠仕様: Oman & Hagemeister (1992) — Visual Studio / Microsoft Code Metrics 系の正規化 MI

### 公式（メソッド単位）

```
loc = max(1, メソッド宣言の行数)
V   = Halstead Volume（HalsteadCalculator.cs）

raw = 171 - 5.2 × ln(V) - 0.23 × CycCC - 16.2 × ln(loc)
MI  = max(0, raw × 100 / 171)        ※ V > 0
MI  = 100                            ※ V <= 0
```

`ln` は自然対数（`Math.Log`）。CycCC は unilyze の Cyclomatic Complexity（McCabe 拡張解釈）。MI は初回の syntactic 解析時点の CycCC で計算され、Semantic enrich で CycCC が更新されても MI 自体は再計算されない。

### 型単位の集約

実装: `CodeHealthCalculator.cs`

```
AverageMaintainabilityIndex = 型内メソッド MI の算術平均（小数第 1 位）
MinMaintainabilityIndex     = 型内メソッド MI の最小（小数第 1 位）
```

メソッドを持たない型は MI 非対象。プロジェクト平均（statusline / badge）はメソッドを持つ型のみを分母とする。

### しきい値（コードスメル）

| レベル | 条件 |
|--------|------|
| Warning (`LowMaintainability`) | メソッド MI < 60 |

定数: `SmellThresholds.LowMaintainabilityMiWarning`

badge / statusline の色分け（参考）: green >= 80, yellow >= 60, red < 60（`BadgeFormatter.cs` / `StatuslineFormatter.cs`）

### 注意点

- 行数はメソッド本体だけでなく、シグネチャを含む宣言全体の行スパン（`MemberExtractor.cs`）
- 公式 Metrics エンジンは型単位で集約するため、メソッド平均との規約差で相関は高いが一致しない（「バリデーション (検証)」参照）
- SyntaxOnly でも CodeHealth と同様おおむね安定

### 妥当性の限界

MI は 1992 年の Visual Basic コードに対する回帰分析から得られた固定係数（171, 5.2, 0.23, 16.2）に依存しており、現代の C# / Unity コードベースへの当てはまりには限界がある。

- Arie van Deursen "Think Twice Before Using the Maintainability Index" (https://avandeursen.com/2014/08/29/think-twice-before-using-the-maintainability-index/)
- Borg et al. "Ghost Echoes Revealed: Benchmarking Maintainability Metrics and Machine Learning Predictions Against Human Assessments" (ICSME 2024, arXiv:2408.10754)

後者を含む近年の評価では、MI を含む古典的メトリクスは人間の保守性評価との一致が弱いことが示されている。unilyze では MI を参考値として出力するが、単独の品質ゲート指標としては推奨しない。Phase 3 では CodeHealth を主指標に一本化し、MI は後方互換または補助表示に縮退する方針である。

## TypeRank

NDepend の TypeRank に相当する、PageRank ベースの型重要度スコア。

解決済みの DI 登録エッジ（VContainer / Zenject）も `TypeDependency` として依存グラフに含まれ、CBO（Ca/Ce）・循環検出・TypeRank にカウントされる。解析対象外の型へ向かう未解決エッジは除外される。

### アルゴリズム

- 入力: DependencyBuilder の TypeDependency リスト → 隣接リスト
- damping factor: 0.85
- 収束閾値: 1e-6 (L1 ノルム)
- 最大反復回数: 100
- Dangling node（出次数 0）のランクは全ノードに均等分配
- 結果は正規化（合計 = 1.0）

### 解釈

高いほど多くの型から依存されている重要な型。値オブジェクトやインフラ型が上位に来る傾向がある。

## Abstractness (A)

準拠仕様: Martin, R.C. "Agile Software Development" (Stable Abstractions Principle)

### 公式

```
A = (abstract class 数 + interface 数) / 全型数
```

アセンブリ粒度で算出。0.0 = 全て具象、1.0 = 全て抽象。

## Distance from Main Sequence (DfMS)

### 公式

```
D = |A + I - 1|

A = Abstractness
I = Instability (アセンブリ粒度: 全型の Ce 合計 / (Ca 合計 + Ce 合計))
```

Main Sequence（A + I = 1 の直線）からの距離。0.0 が理想。

| 位置 | 意味 |
|------|------|
| D ≈ 0 | 安定度と抽象度のバランスが良い |
| A=0, I=0 (D=1) | 安定かつ具象 → Zone of Pain（変更困難） |
| A=1, I=1 (D=1) | 不安定かつ抽象 → Zone of Uselessness |

## Relational Cohesion (H)

準拠仕様: NDepend - Relational Cohesion

### 公式

```
H = (R + 1) / N

R = アセンブリ内の型間依存エッジ数（重複除外、自己参照除外）
N = アセンブリ内の型数
```

N <= 1 の場合は null。値が高いほどアセンブリ内の型が密に連携している。1.5-4.0 が推奨範囲。

## Code Health

独自メトリクス。型単位のスコア (1.0 - 10.0)。

### 重み付け

| 要素 | 重み |
|------|------|
| 平均 CogCC | 25% |
| 最大 CogCC | 20% |
| 行数 | 15% |
| メソッド数 | 10% |
| 最大ネスト深度 | 15% |
| 過剰パラメータ数 | 15% |

## Code Smell

既知のコードスメルをルールベースで検出する。

スメル検出はしきい値依存のヒューリスティックであり、ground truth ではない。
Paiva, Damasceno, Figueiredo & Sant'Anna (2017) "On the evaluation of code smells and detection tools" (JSERD) によると、ツール間一致率は 67-100%、recall は 0-58%、precision は 0-100% であり、しきい値の差だけで結果が割れる。
しきい値は下表に記載されている。
計測値の互換性は [メトリクス互換性ポリシー](#メトリクス互換性ポリシー) を参照する。

<!-- smell-thresholds:start -->
| スメル | 判定条件 (Warning) | 判定条件 (Critical) |
|--------|-------------------|-------------------|
| GodClass | 行数 >= 500 or メソッド数 >= 20 | 行数 >= 1000 |
| LongMethod | 行数 >= 80 or CogCC >= 25 | 行数 >= 150 or CogCC >= 40 |
| ExcessiveParameters | パラメータ数 > 5 | — |
| HighComplexity | CycCC >= 15 or CogCC >= 15 | — |
| DeepNesting | ネスト深度 >= 4 | ネスト深度 >= 6 |
| LowCohesion | LCOM >= 0.8 | — |
| HighCoupling | CBO >= 15 | CBO >= 25 |
| LowMaintainability | MI < 60 | — |
| DeepInheritance | DIT >= 5 | — |
| CatchAllException | `catch (Exception)` without rethrow (excluding `when` filtered catches) | — |
<!-- smell-thresholds:end -->

### Unity hot-path severity escalation

`BoxingAllocation`, `ClosureCapture`, and `ParamsArrayAllocation` are normally Warning-level smells. When the enclosing type derives from `UnityEngine.MonoBehaviour` and the smell occurs inside a Unity hot-path method, severity escalates to Critical.

Hot-path methods are:

- `Update`, `FixedUpdate`, `LateUpdate`, `OnGUI`
- Coroutines: methods whose return type is `System.Collections.IEnumerator`

Lifecycle methods such as `Awake`, `Start`, `OnEnable`, `OnDisable`, and `OnDestroy` are **not** hot paths and keep Warning severity.

Escalation rewrites only `Severity` (Warning → Critical); `Kind` is unchanged so boxing/closure/params counts and CodeHealth are unaffected.

#### SyntaxOnly caveats

Under `SyntaxOnly` analysis:

- **ClosureCapture only:** Boxing and Params require a `SemanticModel` and emit nothing under SyntaxOnly, so hot-path escalation applies to ClosureCapture only.
- **MonoBehaviour detection:** the syntactic fallback matches the direct base-list type name against `MonoBehaviour` and cannot see through intermediate project base classes (e.g. `Player : BaseView` where `BaseView : MonoBehaviour` is not recognized without semantic resolution).

### 検出責務ルーティング

各スメルの検出責務を、決定的ルール検出（構造系・グラフ系・セマンティック系）と LLM 委譲（セマンティックな意図判断）に分ける。
Souza et al. (arXiv:2601.09873) はスメル種別ごとに最適な検出器が異なり、構造系は決定的ルール、セマンティック系は LLM が有利と報告している。
Wu, Mu et al. (iSMELL, ASE 2024) はメトリクスツールと LLM の組み合わせが LLM 単体を上回ると報告しており、決定的検出と LLM 解釈の分担を支持する。
LLM 委譲項目の詳細は [quality-audit blind-spots](../src/Unilyze/Skills/quality-audit/references/blind-spots.md) を参照し、Phase 3 チェックリストで確認する。

| スメル | SARIF ルール | 検出責務 | 根拠 |
|--------|-------------|---------|------|
| GodClass | UNI001 | ルール検出（メトリクスしきい値） | 構造系。しきい値で安定検出 |
| LongMethod | UNI002 | ルール検出（メトリクスしきい値） | 構造系。しきい値で安定検出 |
| ExcessiveParameters | UNI003 | ルール検出（メトリクスしきい値） | 構造系。しきい値で安定検出 |
| HighComplexity | UNI004 | ルール検出（メトリクスしきい値） | 構造系。しきい値で安定検出 |
| DeepNesting | UNI005 | ルール検出（メトリクスしきい値） | 構造系。しきい値で安定検出 |
| LowCohesion | UNI006 | ルール検出（メトリクスしきい値） | 構造系。しきい値で安定検出 |
| HighCoupling | UNI007 | ルール検出（メトリクスしきい値） | 構造系。しきい値で安定検出 |
| LowMaintainability | UNI008 | ルール検出（メトリクスしきい値） | 構造系。しきい値で安定検出 |
| CyclicDependency | UNI009 | ルール検出（グラフ解析） | 依存グラフ解析必須 |
| DeepInheritance | UNI010 | ルール検出（メトリクスしきい値） | 構造系。しきい値で安定検出 |
| BoxingAllocation | UNI011 | ルール検出（セマンティック解析） | SemanticModel 必須 |
| ClosureCapture | UNI012 | ルール検出（セマンティック解析） | SemanticModel 必須 |
| ParamsArrayAllocation | UNI013 | ルール検出（セマンティック解析） | SemanticModel 必須 |
| CatchAllException | UNI014 | ルール検出（セマンティック解析） | SemanticModel 必須 |
| MissingInnerException | UNI015 | ルール検出（セマンティック解析） | SemanticModel 必須 |
| ThrowingSystemException | UNI016 | ルール検出（セマンティック解析） | SemanticModel 必須 |
| WeakTemporization | UNI021 | ルール検出（構文解析、セマンティック補強） | SyntaxOnly 可 |
| ExpensiveUnityApiInHotPath | UNI017 | ルール検出（Unity ホットパス構文解析） | Unity 固有。MonoBehaviour の毎フレームメソッド内のみ |
| LinqInHotPath | UNI018 | ルール検出（Unity ホットパス構文解析） | Unity 固有。MonoBehaviour の毎フレームメソッド内のみ |
| CollectionAllocationInHotPath | UNI019 | ルール検出（Unity ホットパス構文解析） | Unity 固有。MonoBehaviour の毎フレームメソッド内のみ |
| StringConcatenationInHotPath | UNI020 | ルール検出（Unity ホットパス構文解析） | Unity 固有。MonoBehaviour の毎フレームメソッド内のみ |
| Feature Envy | — | LLM 委譲 | 意図・文脈判断が必要でしきい値化できない |
| 命名品質 | — | LLM 委譲 | 意図・文脈判断が必要でしきい値化できない |
| 意図とコードの乖離 | — | LLM 委譲 | 意図・文脈判断が必要でしきい値化できない |
| コメントとコードの不整合 | — | LLM 委譲 | 意図・文脈判断が必要でしきい値化できない |
| トップレベルステートメント | — | LLM 委譲 | 意図・文脈判断が必要でしきい値化できない |
| ランタイムリスク (Dispose 漏れ / デッドロック) | — | LLM 委譲 | 意図・文脈判断が必要でしきい値化できない |

## バリデーション (検証)

### Complete vs SyntaxOnly 解析の差分

Unity DLL を解決できない環境（CI 等）では SyntaxOnly に縮退する。
同一の実プロジェクト（oculus-samples/Unity-Decommissioned、283 型）を Complete と SyntaxOnly で解析した実測差分:

| 指標 | Complete | SyntaxOnly | 備考 |
|------|----------|------------|------|
| CodeHealth avg | 9.6 | 9.6 | 同一（構文情報のみで算出されるため） |
| CodeHealth min | 4.8 | 5.0 | 差分は `#if UNITY_EDITOR` の define 有無起因 |
| 依存関係数 | 452 | 429 | -5% |
| 循環依存 | 6 | 6 | 同一 |
| smells 総数 | 885 | 289 | 内訳は下表 |
| DIT max | 7 | 1 | エンジン型を跨ぐ継承はセマンティック必須 |
| CBO avg | 13.7 | 5.7 | UnityEngine 型への結合が不可視 |
| 解析時間 | 4.6s | 0.6s | |

smells の内訳差分:

| スメル | Complete | SyntaxOnly |
|--------|----------|------------|
| BoxingAllocation | 312 | 0 |
| ClosureCapture | 181 | 81 |
| ParamsArrayAllocation | 30 | 0 |
| DeepInheritance | 38 | 0 |
| HighCoupling | 111 | 19 |

SyntaxOnly では SemanticModel 依存の検出（Boxing / Params / DIT / CBO）が過小になる。
このため `unilyze badge` の対象は、レベル間で安定する CodeHealth / MI と、構文レベルのサブセットに縮退する smells に限定している。
上表のとおり smells の総数自体はレベル間で大きく変わる（885 → 289）ため、smells バッジはレベルをまたいだ比較に使えない旨をドキュメントに明記している。

### Microsoft.CodeAnalysis Metrics (公式エンジン) との突合

公式 Metrics ツールと同一実装の `CodeAnalysisMetricData`（Microsoft.CodeAnalysis.AnalyzerUtilities）で unilyze 自身の src/Unilyze を計測し、unilyze の SyntaxOnly 解析と突合した
（100 型マッチ、source generator 由来の JsonSerializerContext 2 型を除外。再現: [scripts/crossval](../scripts/crossval/)）:

| 指標 | Pearson 相関 | 平均絶対差 | 備考 |
|------|-------------|-----------|------|
| CycCC | 0.983 | 2.0 | 型単位合計で比較。乖離は規約差で 97/100 型が厳密に説明可能（下記） |
| MI | 0.870 | 5.4 | 公式は型集約、unilyze はメソッド平均（メソッド無し 43 型は unilyze 非対象。statusline / badge の MI 平均もメソッドを持つ型のみを分母とする） |
| 結合度 | 0.817 (順位) | — | 公式 ClassCoupling 平均 14.0 vs unilyze CBO 3.6（SyntaxOnly では過小） |
| DIT | — | — | 公式は object 継承を 1 と数える規約差で全件オフセット |

CycCC の乖離の構造（全 339 メソッドの突合で実証済み・issue #4 で調査完了）:

乖離 Δ = unilyze − 公式 は、規約差の構文出現数で厳密に分解できる（97/100 型で Δ = arm + catch + goto − default − メンバー base 差が完全一致）。
乖離上位の型と分解:

| 型 | 公式 | Unilyze | Δ | 内訳 |
|----|------|---------|---|------|
| BadgeFormatter | 14 | 28 | +14 | switch arm ×14 |
| HalsteadCalculator | 16 | 30 | +14 | switch arm ×14 |
| DIContainerAnalyzer | 42 | 55 | +13 | switch arm ×14 − default ×1 |
| BadgeSvgRenderer | 12 | 22 | +10 | switch arm ×10 |
| ClosureDetector | 30 | 40 | +10 | switch arm ×10 |
| BloomFilter128 | 26 | 17 | −9 | 公式のメンバー base（ctor / accessor 各 1）×9 |

メソッドを持たない record / DTO 型は一律 Δ = −1（公式が暗黙 ctor を base 1 で数えるため）。
残差が残る 3 型（HalsteadWalker / State / Walker）は、SyntaxOnly で型解決できない bool `&` `|` と、ネスト型のメンバー名照合に起因する ±1。

なお、調査前の仮説「`?.` `??` が unilyze 固有の加算」は反証された（公式エンジンも両方カウントする）。
実際の乖離要因は switch expression arm（本コードベースで支配的）、catch、goto、default、メンバー base 差である。
この調査の副産物として、分解 foreach（`foreach (var (a, b) in ...)`）が CycCC / CogCC / ネスト深度の全 walker でカウント漏れしていたバグを発見・修正した。

## メトリクス互換性ポリシー

計測値を変える bugfix が patch リリースで複数回入った経緯がある（分解 foreach のカウント漏れ修正、MI 平均の分母から無メソッド型を除外、DIT の `I[A-Z]` ヒューリスティック廃止など）。
これらはツール側の都合による計測値の変動であり、`diff` / `trend` / `badge` をバージョンを跨いで使う利用者にとってはノイズになる。
以下のポリシーで、ツール起因のメトリクス変動がどのリリース種別で発生しうるかを明確にする。

メトリクス定義の変更は [CHANGELOG.md](../CHANGELOG.md) に `[metrics]` プレフィックス付きで記載すること（同ファイルの [metrics] tag convention を参照）。

### リリース種別ごとの計測値の扱い

| リリース種別 | 計測値の変更 | 許容される変更 |
|------------|------------|--------------|
| patch | しない | クラッシュ修正、出力形式の追加（既存値を変えない範囲の項目追加・新フォーマット） |
| minor 以上 | しうる | メトリクス定義の変更（後述） |

メトリクス定義の変更とは、以下のいずれかを指す。

- カウント規約（どの構文を加算するか。例: 分解 foreach、switch expression arm）
- 分母・分子の構成（例: MI 平均の対象とする型集合）
- しきい値（コードスメル判定の Warning / Critical 境界など）
- 複合スコアの重み（Code Health の各要素の重み配分など）

これらの変更は最低でも minor バンプを要する。
patch リリースで計測値を変えてはならない。

### 定義変更時の手順

メトリクス定義を変更するリリースでは、以下を必須とする。

1. リリースノートに計測影響を記載する。どのメトリクスがどちらの方向に動くか（増加 / 減少 / 値域変化）を明示する
2. [scripts/crossval](../scripts/crossval/) のクロスバリデーションを再実行し、本ドキュメントの「バリデーション (検証)」セクションの検証データを更新する
3. 「公式エンジンとの規約差」など定義差を記述している箇所があれば、変更内容に合わせて更新する

### 利用者向けの注意

`diff` / `trend` は本来、解析対象コードの変化を追うための機能である。
ただし比較する 2 点が異なる unilyze バージョンで計測されている場合、メトリクス定義の変更（minor 以上で発生しうる）の影響が混入する。
コードを変えていないのに値が動いた場合、unilyze のバージョン差を疑うこと。
バージョンを固定して計測すれば、この影響は発生しない。

### metricsVersion による機械的検出

JSON 出力のルートに `metricsVersion`（int）と `toolVersion`（string）を含める。
`metricsVersion` は計測定義の互換性を表す整数で、計測値を変える変更のたびにインクリメントする。
`toolVersion` はスナップショット生成時の unilyze アセンブリバージョンである。

`diff` と `trend` は入力間で `metricsVersion` が異なる場合、stderr に 1 行警告を出す。
`diff --fail-on-version-mismatch` を指定すると、バージョン不一致時に exit code 2 で終了する（CI ゲート用）。

**metricsVersion のインクリメント規則:** 計測値を変える任意の変更（カウント規約・分母/分子・しきい値・重みの変更）では、
(1) `AnalysisResult.CurrentMetricsVersion` をインクリメント、(2) 最低 minor バンプ、(3) CHANGELOG に `[metrics]` エントリを追加、
の 3 点をセットで行う。patch リリースで metricsVersion を上げてはならない。

同一リリースウィンドウ（`[Unreleased]` 期間）では `metricsVersion` のバンプは 1 回にまとめる。
未リリースのバージョン番号に対応する定義は流動的であり、タグ付け前に複数の `[metrics]` 変更が入っても番号の再割り当ては行わない。

### 将来課題

（解決済み: issue #30 で `metricsVersion` / `toolVersion` を実装。上記「metricsVersion による機械的検出」を参照。）
