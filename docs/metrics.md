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
