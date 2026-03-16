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
| `for`, `foreach`, `while`, `do` | +1 |
| `catch` | +1 |
| `? :` (三項演算子) | +1 |
| `?.` (null 条件) | +1 |
| `??` (null 合体) | +1 |
| `&&`, `||` | 各 +1 |
| `goto` | +1 |
| `switch` expression arm | +1 |

### Roslyn CA1502 との差異

| 項目 | CA1502 | Unilyze |
|------|--------|---------|
| `?.` | カウントしない | +1 |
| `??` | カウントしない | +1 |
| switch expression arm | 未対応 | +1 |

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

## TypeRank

NDepend の TypeRank に相当する、PageRank ベースの型重要度スコア。

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

| スメル | 判定条件 (Warning) | 判定条件 (Critical) |
|--------|-------------------|-------------------|
| GodClass | 行数 >= 500 or メソッド数 >= 20 | 行数 >= 1000 |
| LongMethod | 行数 >= 80 or CogCC >= 25 | 行数 >= 150 or CogCC >= 40 |
| ExcessiveParameters | パラメータ数 > 5 | — |
| HighComplexity | CycCC >= 15 or CogCC >= 15 | — |
| DeepNesting | ネスト深度 >= 4 | ネスト深度 >= 6 |
| LowCohesion | LCOM >= 0.8 | — |
