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

`query` は jq 不要で evidence pack を直接出力する。スナップショット解析だけ jq を使う場合、zsh の `!` ヒストリ展開に注意 (`!=` は `select(.field)` や `| not` で回避)。

## Workflow

### Phase 1: 定量メトリクス取得

unilyze CLI で JSON を取得する。スナップショットはリポジトリルートの `.unilyze/` に保存する。

```bash
command -v unilyze || echo "NOT_FOUND"
# 見つからない場合: dotnet run --project <repo>/src/Unilyze -- で代用

UNILYZE_DIR="$(git rev-parse --show-toplevel 2>/dev/null || pwd)/.unilyze"
mkdir -p "$UNILYZE_DIR"

unilyze -p <path> -f json -o "$UNILYZE_DIR/quality-audit.json"
# 命名/意図/コメント整合レビュー用に API surface を含める場合:
unilyze -p <path> -f json --include-api-surface -o "$UNILYZE_DIR/quality-audit.json"
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

ワースト型とスメル・依存関係を evidence pack として取得:

```bash
# CodeHealth ワースト N 件 (Markdown, 既定)
unilyze query --worst 5 -i "$UNILYZE_DIR/quality-audit.json"

# API surface 付き (doc summary, public signatures, identifiers)
unilyze query --worst 5 -i "$UNILYZE_DIR/quality-audit.json" --include-api-surface

# 単一型 (JSON)
unilyze query --type GodClassTarget -i "$UNILYZE_DIR/quality-audit.json" -f json

# 直接解析 (スナップショット不要)
unilyze query --worst 5 -p <path> --include-api-surface
```

各 pack には型アンカー (`file:line`)、主要メトリクス、スメル (severity + line)、依存エッジ、CogCC 上位メソッドが含まれる。
`--include-api-surface` 指定時は doc summary、public signatures、identifiers、doc coverage も付与される。
Critical スメルだけ見る場合は pack 内の `[Critical]` 行を参照する。
CBO > 14、boxing ホットスポット、DI 登録、例外フロー問題も pack 内の metrics / smells / dependencies で確認できる。
サマリー統計は `unilyze statusline -p <path> --verbose` または JSON ルートの assembly health を参照。

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

unilyze の計測対象外を AI が確認する。
[docs/metrics.md Detection responsibility routing](../../../docs/metrics.md#detection-responsibility-routing) の LLM 委譲行に対応するチェックリスト。
詳細は [references/blind-spots.md](references/blind-spots.md) を参照。

| 確認項目 | 着目箇所 | メトリクスで漏れる理由 | 入力データ |
|---------|---------|---------------------|-----------|
| Feature Envy | メソッド内の他型フィールド参照・外部型への過度な委譲 | 責務配置は行数・複雑度では判定できない | query pack (smells, dependencies) |
| 命名品質 | 型名・メソッド名・パラメータ名と実装の対応 | 命名は静的メトリクスに現れない | `--include-api-surface` の identifiers / publicSignatures |
| 意図とコードの乖離 | コメント・テスト名・API 名と実装ロジックの不一致 | 意図はコード構造から推定できない | `--include-api-surface` の docSummary / identifiers |
| コメントとコードの不整合 | XML doc / インラインコメントと実装 | コメント内容は計測対象外 | `--include-api-surface` の docSummary / publicSignatures |
| トップレベルステートメントの行数・複雑度 | Program.cs 等のトップレベル本体 | 型に属さず TypeMetrics に含まれない | ソース直接読み |
| IDisposable の Dispose 漏れ | using 未使用の IDisposable 生成 | 所有権・ライフサイクルは静的解析困難 | ソース直接読み |
| Process.Start のデッドロックパターン | StandardOutput/Error の同期 ReadToEnd | 実行時デッドロックはメトリクス化不可 | ソース直接読み |

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

### Phase 4: Review coverage (CRScore-style)

Phase 2/3 の AI レビューが、決定的根拠 (Critical smells + ワースト型) を網羅したかを検証する。
CRScore (Naik et al., NAACL 2025) の comprehensiveness metric を quality-audit に適用したフェーズ。

**疑似リファレンス集合の構築:**

```bash
unilyze query --worst <N> -i "$UNILYZE_DIR/quality-audit.json" --include-api-surface
```

1. 上記 pack から **Critical** severity の全スメルを列挙する (type + kind + method + anchor)
2. ワースト型リスト (--worst N で選ばれた型) 自体も各 1 件のリファレンスとして含める
3. 各リファレンスについて Phase 2/3 の Findings に対応する記述があるか照合する

**判定ルール:**

- カバー済み: Finding に同型・同スメル種別 (または同等の blind-spot 指摘) が存在
- 未カバー: Finding に無く、かつ triage 理由も記録されていない → **必ず** Finding 追加または triage 理由を記載

**カバレッジ比率:** `Review coverage: covered/total` (例: `12/15`)

未カバー項目はレポートに `Uncovered pseudo-references` サブセクションとして列挙する。

### Phase 5: 統合レポート

```
## Quality Audit Report

### Summary

| Metric | Value |
|--------|-------|
| Total types | N |
| Below threshold (CodeHealth < X) | N |
| Critical CodeSmells | N |
| Blind spot issues | N |
| Review coverage | covered/total |

### Uncovered pseudo-references

- `{type}` / `{smellKind}` @ `{anchor}` — triage: {added as finding | reason for skip}

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

### Phase 6: スナップショット保持

`$UNILYZE_DIR/quality-audit.json` を残す。`/refactor-loop` の初期スナップショットとして使用可能。
trend 用に日付付きコピーも保存する:

```bash
cp "$UNILYZE_DIR/quality-audit.json" "$UNILYZE_DIR/snapshots/$(date +%Y-%m-%d).json"
```
