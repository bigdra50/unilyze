---
name: quality-audit
description: |
  C#プロジェクトの統合品質監査。unilyze CLIの定量メトリクスとAIコードレビューを組み合わせ、
  数値根拠付きの改善提案を生成する。メトリクスの盲点(トップレベルステートメント等)もAIが補完する。
  Use for: "品質監査", "quality audit", "統合分析", "メトリクス+レビュー", "改善ポイント洗い出し"
---

# Quality Audit

unilyze の定量メトリクスと AI コードレビューを統合し、数値根拠付きの改善提案を出力する。

## Usage

```
/quality-audit [path] [--top N] [--threshold <score>]
```

- `path`: プロジェクトルート (省略時: カレントディレクトリ)
- `--top`: 詳細分析する型の数 (省略時: 5)
- `--threshold`: CodeHealth 閾値 (省略時: 7.0)

## Quick Reference

メトリクスの定義・閾値と JSON フィールドは CLI から直接確認できる:

```bash
unilyze metrics   # メトリクス定義、CodeSmell 閾値一覧
unilyze schema    # JSON 出力の全フィールドリファレンス
```

## Shell の注意事項

zsh は `!` をヒストリ展開する。jq フィルタ内の `!=` が `\!=` に変換されてパースエラーになる。

回避策:
- `!=` を使わない。jq では `select(.field)` で null/false を除外できる (`null` は falsy)
- `!= null` の代わりに `select(.field)` を使う
- 否定が必要なら `| not` を使う (例: `select(.x == 0 | not)`)
- どうしても `!=` が必要な場合は heredoc 経由: `cat <<'EOF' | jq -f /dev/stdin file.json`

## Workflow

### Phase 1: 定量メトリクス取得

unilyze CLI で JSON を取得する。スナップショットはリポジトリルートの `.unilyze/` に保存する。

```bash
command -v unilyze || echo "NOT_FOUND"
# 見つからない場合: dotnet run --project <repo>/src/Unilyze -- で代用

UNILYZE_DIR="$(git rev-parse --show-toplevel 2>/dev/null || pwd)/.unilyze"
mkdir -p "$UNILYZE_DIR"

unilyze -p <path> -f json -o "$UNILYZE_DIR/quality-audit.json"
```

自前コードに絞る場合は `--prefix` または `-a` を使う:

```bash
# プレフィックスで絞り込み (推奨: 自前 asmdef の共通接頭辞)
unilyze -p <path> --prefix "App." -f json -o "$UNILYZE_DIR/quality-audit.json"

# アセンブリ名で指定
unilyze -p <path> -a App.Domain -f json -o "$UNILYZE_DIR/quality-audit.json"
```

サードパーティ (UniRx, MessagePack, Mirror 等) を含めると外部コードのワースト型がノイズになる。
自前アセンブリのみを計測対象にすることを推奨する。

JSON からワースト箇所を抽出:

```bash
# CodeHealth ワースト N 件 (.codeHealth が null の型を除外)
jq '[.typeMetrics[] | select(.codeHealth)] | sort_by(.codeHealth) | .[:5]' "$UNILYZE_DIR/quality-audit.json"

# Critical CodeSmell
jq '[.typeMetrics[] | select(.codeSmells) | {typeName, namespace, codeSmells: [.codeSmells[] | select(.severity == "Critical")]}] | map(select(.codeSmells | length > 0))' "$UNILYZE_DIR/quality-audit.json"

# CBO 閾値超過 (.cbo > 14 は null を自動除外)
jq '[.typeMetrics[] | select(.cbo > 14) | {typeName, namespace, cbo}] | sort_by(.cbo) | reverse' "$UNILYZE_DIR/quality-audit.json"

# Boxing ホットスポット (GC 圧力)
jq '[.typeMetrics[] | select(.boxingCount) | {typeName, boxingCount, closureCaptureCount, paramsAllocationCount}] | sort_by(-.boxingCount)' "$UNILYZE_DIR/quality-audit.json"

# 例外フロー問題
jq '[.typeMetrics[].codeSmells[]? | select(.kind == "CatchAllException" or .kind == "MissingInnerException" or .kind == "ThrowingSystemException")] | group_by(.kind) | .[] | {kind: .[0].kind, count: length}' "$UNILYZE_DIR/quality-audit.json"

# DI 依存グラフ
jq '[.dependencies[] | select(.kind == "DIRegistration")] | .[] | {service: .fromType, impl: .toType}' "$UNILYZE_DIR/quality-audit.json"

# サマリー統計
jq '{totalTypes: (.typeMetrics | length), belowThreshold: [.typeMetrics[] | select(.codeHealth) | select(.codeHealth < 7.0)] | length, criticalSmells: [.typeMetrics[] | select(.codeSmells) | .codeSmells[] | select(.severity == "Critical")] | length, allSmells: [.typeMetrics[] | select(.codeSmells) | .codeSmells[]] | length, boxingTypes: [.typeMetrics[] | select(.boxingCount)] | length, closureTypes: [.typeMetrics[] | select(.closureCaptureCount)] | length, diRegistrations: [.dependencies[] | select(.kind == "DIRegistration")] | length}' "$UNILYZE_DIR/quality-audit.json"
```

### Phase 2: AI コードレビュー (ワースト箇所)

Phase 1 で特定したワースト型のソースファイルを読み、分析する:

- メトリクスが悪い根本原因
- 具体的な改善案 (メソッド抽出、責務分離等)
- メトリクスでは検出できないランタイムリスク

全ファイルを読む必要はない。メトリクスが悪い箇所に集中する。

CycCC と CogCC の使い分け:
- CycCC が高い → テストケース数の下限見積もり。テスタビリティの問題
- CogCC が高い → 人間にとっての理解困難さ。可読性・保守性の問題
- 両方を併用して判断する。片方だけで改善方針を決めない

### Phase 3: 盲点補完

unilyze の計測対象外を AI が確認する。詳細は [references/blind-spots.md](references/blind-spots.md) を参照。

主な確認項目:
- トップレベルステートメント (Program.cs 等) の行数・複雑度
- IDisposable の Dispose 漏れ
- Process.Start のデッドロックパターン

> catch (Exception) の握り潰しは CatchAllException、inner exception 未設定は MissingInnerException として自動検出されるようになった。盲点から除外。

対象ファイルが存在しない場合はスキップする。

### Goodhart's Law への対処

> "When a measure becomes a target, it ceases to be a good measure."
> （指標が目標になると、良い指標ではなくなる）

メトリクス改善の提案時、以下のアンチパターンに注意する:

| メトリクス | ゲーミングの例 | 正しい対処 |
|-----------|---------------|-----------|
| CycCC / CogCC | 関数を過度に分割して数値を下げるが全体の可読性は低下 | ローカルとグローバルの可読性を両方確認 |
| テストカバレッジ | assertionなしのテストで100%達成 | mutation testing で検証 |
| LOC | 冗長なコードを書いて行数を稼ぐ | LOCは参考値、アウトカムで判断 |
| BoxingCount | boxing回避のために可読性を犠牲にした最適化 | ホットパスのみ最適化、プロファイラで確認 |

対策原則:
1. 複数メトリクスをバランスよく評価する（単一指標を目標にしない）
2. 定量指標（unilyze計測値）と定性判断（AIレビュー）を組み合わせる
3. アウトプット（LOC、コミット数）ではなくアウトカム（障害率、保守コスト）に注目
4. メトリクス値だけでなく「変更の妥当性」を問う（数値を下げるためだけの変更は棄却）

出典: [Goodhart's Law in Software Engineering](https://jellyfish.co/blog/goodharts-law-in-software-engineering-and-how-to-avoid-gaming-your-metrics/), [SPACE Framework](https://queue.acm.org/detail.cfm?id=3454124)

### Phase 4: 統合レポート

```
## Quality Audit Report

### Summary

| Metric | Value |
|--------|-------|
| Total types | N |
| Below threshold (CodeHealth < X) | N |
| Critical CodeSmells | N |
| Blind spot issues | N |

### Findings (優先度順)

#### 1. [High] TypeName (CodeHealth: X.X)

| Metric | Value | Rating |
|--------|-------|--------|
| CogCC max | X | Poor |
| CBO | X | Warning |

Root cause: {根本原因}
Recommendation: {改善案}

#### 2. [High] Program.cs (blind spot)

Lines: N | Detected by: AI review (not measured by unilyze)

Root cause: {説明}
Recommendation: {改善案}

### Action Plan

1. {効果が高い順}
2. ...
```

各 Finding には「メトリクス値」か「blind spot」のいずれかの根拠を必ず付ける。
メトリクス閾値は `unilyze metrics` または [references/metrics-thresholds.md](references/metrics-thresholds.md) を参照。

### Phase 5: スナップショット保持

`$UNILYZE_DIR/quality-audit.json` を残す。`/refactor-loop` の初期スナップショットとして使用可能。
trend 用に日付付きコピーも保存する:

```bash
cp "$UNILYZE_DIR/quality-audit.json" "$UNILYZE_DIR/snapshots/$(date +%Y-%m-%d).json"
```
