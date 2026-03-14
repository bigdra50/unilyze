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

## Shell の注意事項

zsh は `!` をヒストリ展開する。jq フィルタ内の `!=` が `\!=` に変換されてパースエラーになる。

回避策:
- `!=` を使わない。jq では `select(.field)` で null/false を除外できる (`null` は falsy)
- `!= null` の代わりに `select(.field)` を使う
- 否定が必要なら `| not` を使う (例: `select(.x == 0 | not)`)
- どうしても `!=` が必要な場合は heredoc 経由: `cat <<'EOF' | jq -f /dev/stdin file.json`

## Workflow

### Phase 1: 定量メトリクス取得

unilyze CLI で JSON を取得する。

```bash
command -v unilyze || echo "NOT_FOUND"
# 見つからない場合: dotnet run --project <repo>/src/Unilyze -- で代用

unilyze -p <path> -f json -o /tmp/quality-audit.json
```

JSON からワースト箇所を抽出:

```bash
# CodeHealth ワースト N 件 (.codeHealth が null の型を除外)
jq '[.typeMetrics[] | select(.codeHealth)] | sort_by(.codeHealth) | .[:5]' /tmp/quality-audit.json

# Critical CodeSmell
jq '[.typeMetrics[] | select(.codeSmells) | {typeName, namespace, codeSmells: [.codeSmells[] | select(.severity == "Critical")]}] | map(select(.codeSmells | length > 0))' /tmp/quality-audit.json

# CBO 閾値超過 (.cbo > 14 は null を自動除外)
jq '[.typeMetrics[] | select(.cbo > 14) | {typeName, namespace, cbo}] | sort_by(.cbo) | reverse' /tmp/quality-audit.json

# サマリー統計
jq '{totalTypes: (.typeMetrics | length), belowThreshold: [.typeMetrics[] | select(.codeHealth) | select(.codeHealth < 7.0)] | length, criticalSmells: [.typeMetrics[] | select(.codeSmells) | .codeSmells[] | select(.severity == "Critical")] | length, allSmells: [.typeMetrics[] | select(.codeSmells) | .codeSmells[]] | length}' /tmp/quality-audit.json
```

### Phase 2: AI コードレビュー (ワースト箇所)

Phase 1 で特定したワースト型のソースファイルを読み、分析する:

- メトリクスが悪い根本原因
- 具体的な改善案 (メソッド抽出、責務分離等)
- メトリクスでは検出できないランタイムリスク

全ファイルを読む必要はない。メトリクスが悪い箇所に集中する。

### Phase 3: 盲点補完

unilyze の計測対象外を AI が確認する。詳細は [references/blind-spots.md](references/blind-spots.md) を参照。

主な確認項目:
- トップレベルステートメント (Program.cs 等) の行数・複雑度
- IDisposable の Dispose 漏れ
- bare catch / 広すぎる例外キャッチ
- Process.Start のデッドロックパターン

対象ファイルが存在しない場合はスキップする。

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
メトリクス閾値は [references/metrics-thresholds.md](references/metrics-thresholds.md) を参照。

### Phase 5: スナップショット保持

`/tmp/quality-audit.json` を残す。`/refactor-loop` の初期スナップショットとして使用可能。
