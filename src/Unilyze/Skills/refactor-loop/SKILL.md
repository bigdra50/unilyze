---
name: refactor-loop
description: |
  unilyze メトリクスで収束判定するリファクタリングループ。
  ワースト型を特定→リファクタリング実施→unilyze diffで改善/悪化を定量判定→目標達成まで繰り返す。
  Use for: "refactor-loop", "定量リファクタリング", "CodeHealth上げて", "メトリクス改善ループ", "品質改善ループ"
---

# Refactor Loop

unilyze メトリクスの CodeHealth スコアを収束条件として、リファクタリングを反復実行する。

## Usage

```
/refactor-loop [path] [--target <score>] [--max-rounds N]
```

- `path`: プロジェクトルート (省略時: カレントディレクトリ)
- `--target`: 目標 CodeHealth (省略時: 8.0)
- `--max-rounds`: 最大ラウンド数 (省略時: 5)

## Workflow

```python
snapshot = get_or_create_baseline(path)
targets = identify_worst_types(snapshot, threshold=target)

for round in range(1, max_rounds + 1):
    type_to_fix = pick_worst(targets)
    refactor(type_to_fix)           # コード修正
    run_tests()                     # テスト通過を確認
    diff = unilyze_diff(snapshot)   # 定量比較
    report_round(round, diff)

    if all_above_target(diff):
        break
    if has_degradation(diff):
        fix_degradation()           # 悪化を修正してから次へ

    snapshot = update_snapshot()

print_final_summary()
```

### Step 1: ベースライン取得

```bash
# /quality-audit で作成済みなら再利用
if [ -f /tmp/quality-audit.json ]; then
  cp /tmp/quality-audit.json /tmp/refactor-before.json
else
  unilyze -p <path> -f json -o /tmp/refactor-before.json
fi
```

ワースト型を抽出:

```bash
jq --argjson t 8.0 '[.typeMetrics[] | select(.codeHealth != null and .codeHealth < $t)] | sort_by(.codeHealth) | .[0]' /tmp/refactor-before.json
```

### Step 2: リファクタリング実施

ワースト型のソースを読み、CodeSmell と メトリクスに基づいてリファクタリングする。

改善戦略の選択基準:

| CodeSmell / Metric | Strategy |
|---|---|
| GodClass (lines > 500) | 責務ごとにクラス分割 |
| LongMethod (lines > 60) | メソッド抽出 |
| HighComplexity (CogCC > 25) | 条件分岐の整理、早期 return、ストラテジーパターン |
| DeepNesting (depth > 4) | ガード節、メソッド抽出 |
| HighCoupling (CBO > 14) | インターフェース導入、依存逆転 |
| ExcessiveParameters (> 5) | パラメータオブジェクト導入 |
| LowCohesion (LCOM > 0.8) | 関連メソッド+フィールドを別クラスへ |

1つのラウンドで1つの型に集中する。複数の型を同時に変更しない。

### Step 3: テスト実行

リファクタリング後、テストを実行して既存動作を壊していないことを確認する。

```bash
dotnet test  # or project-specific test command
```

テストが失敗した場合、修正してからStep 4へ進む。

### Step 4: 定量比較

```bash
unilyze -p <path> -f json -o /tmp/refactor-after.json
unilyze diff /tmp/refactor-before.json /tmp/refactor-after.json 2>&1
```

判定ロジック:
- Degraded = 0 かつ対象型の CodeHealth >= target → 成功、次の型へ
- Degraded = 0 かつ CodeHealth < target → 改善不十分、同じ型で続行
- Degraded > 0 → 悪化を修正してから再計測

### Step 5: ラウンドレポート

```
## Round N

| Type | Before | After | Delta |
|------|--------|-------|-------|
| Namespace.TypeName | 5.2 | 7.8 | +2.6 |

Changes: {変更内容の要約}
Status: Improved / Degraded / Insufficient
```

### Step 6: 収束判定

以下のいずれかで終了:
- 全対象型が目標 CodeHealth に到達
- 最大ラウンド数に到達
- ユーザーが終了を指示

### Step 7: 最終サマリー

```
## Refactor Loop Summary

| Round | Target Type | Before | After | Status |
|-------|-------------|--------|-------|--------|
| 1 | TypeAnalyzer | 5.2 | 7.8 | Improved |
| 2 | CodeSmellDetector | 6.1 | 8.5 | Target reached |
| 3 | DiffCalculator | 6.5 | 8.2 | Target reached |

Overall: N types improved, M reached target, K remaining
```

スナップショットを更新:
```bash
cp /tmp/refactor-after.json /tmp/quality-audit.json
```

## Notes

- 1ラウンド1型に集中し、変更のスコープを限定する
- テスト通過を必ず確認してから次のラウンドへ
- `/quality-audit` のスナップショットをベースラインとして再利用可能
- 悪化が発生した場合は次のラウンドに進まず、まず悪化を修正する
