# Extra: similarity-csharp クローン検出と Unilyze メトリクスの相関分析

## 概要

HelloMarioFramework に対して similarity-csharp (threshold=0.7) を実行し、検出されたクローン集中箇所と Unilyze のメトリクスの相関を分析した。

## similarity-csharp 結果

- 329 メソッド中 88 メソッド (27%) がクローングループに所属
- 32 クローングループ、172 類似ペア
- 合計重複影響: 1,648 行
- 最大グループ: Enemy.FixedUpdateStompable を代表とする 226 行影響のグループ (96.9% 類似)

## クローン集中型の Unilyze メトリクス

| Type | CloneGroups | CodeHealth | AvgCycCC | LCOM | CBO | Smells |
|------|-------------|------------|----------|------|-----|--------|
| Enemy | 6 | 7.9 | 3.9 | 0.65 | 12 | HighComplexity, DeepNesting |
| Chuck | 6 | 7.1 | 3.9 | 0.77 | 14 | HighComplexity x2, DeepNesting x2, HighCoupling |
| Player | 2 | 4.6 | 4.6 | 0.94 | 21 | GodClass, LongMethod x2, HighComplexity x2, DeepNesting x3, LowCohesion, HighCoupling |
| Bully | 3 | 6.5 | 4.1 | 0.66 | 12 | LongMethod, HighComplexity, DeepNesting x2 |
| DryBones | 3 | 9.2 | 2.0 | 0.73 | 12 | DeepNesting |
| QuestionBlock | 3 | 8.4 | 2.9 | 0.88 | 10 | DeepNesting x2, LowCohesion |
| OptionsMenu | 2 | 4.6 | 8.3 | 0.72 | 7 | LongMethod, HighComplexity, DeepNesting |
| IntroMenu | 2 | 7.2 | 8.0 | 0.50 | 8 | DeepNesting |

## クローンが少ない/無い型の Unilyze メトリクス

| Type | CloneGroups | CodeHealth | AvgCycCC | LCOM | CBO | Smells |
|------|-------------|------------|----------|------|-----|--------|
| ButtonHandler | 0 | 10.0 | 1.0 | 0.00 | 1 | none |
| Star | 0 | 10.0 | 2.7 | 0.75 | 5 | none |
| Coin | 0 | 10.0 | 2.0 | N/A | 4 | none |
| DestroyTimer | 0 | 10.0 | 1.0 | 1.00 | 3 | LowCohesion |

## 相関パターン

### CodeHealth とクローン密度

クローン集中型の CodeHealth 平均: 7.0
クローンなし型の CodeHealth 平均: 10.0

CodeHealth が低い型ほどクローンが集中する傾向がある。特に:
- Player (4.6) と OptionsMenu (4.6) は最低スコアかつクローン所有
- ButtonHandler, Star, Coin (全て 10.0) はクローンなし

### CBO (結合度) とクローン

クローン集中型はCBO が高い (平均 12.1)。クローンなし型は低い (平均 3.3)。
高結合な型は他の型と似た処理パターンを持ちやすく、クローンが生まれやすい。

### Enemy 系クローンの構造的原因

最大のクローンクラスタは Enemy / Bully / Chuck / DryBones の継承階層:
- `FixedUpdateStompable` (96.9% 類似, 226行影響) — テンプレートメソッドパターンの不在
- `WhenHurtPlayer` (100% 完全一致, 26行) — 基底クラスの振る舞いコピー
- `Cooldown` (70% 類似, 29行) — コルーチンの構造的重複
- `LateUpdate` (80% 類似, 38行) — 共通ロジックの未抽出

Unilyze はこれらの型に対して HighComplexity / DeepNesting / HighCoupling を検出しており、「リファクタリングすべき箇所」の指摘としてクローン検出と一致する。

### LCOM とクローン

QuestionBlock (LCOM=0.88, LowCohesion smell) はクローングループを3つ持つ。
凝集度が低い型は責務が分散しており、他の型と部分的に重複するコードを持ちやすい。

## 結論

similarity-csharp のクローン検出結果と Unilyze のメトリクスには明確な相関がある:

1. CodeHealth が低い型にクローンが集中 (平均 7.0 vs 10.0)
2. CBO が高い型にクローンが集中 (平均 12.1 vs 3.3)
3. Unilyze の Code Smell 検出箇所と similarity-csharp のクローン集中箇所が重複

Unilyze のメトリクスは「リファクタリングすべき箇所」を的確に示しており、similarity-csharp のクローン検出がそれを裏付ける形になっている。両ツールは相互補完的に使える。

## 環境

- similarity-csharp (threshold=0.7, 599ms で完了)
- 対象: HelloMarioFramework/Assets/HelloMarioFramework/Script/ (107 files, 329 methods)
