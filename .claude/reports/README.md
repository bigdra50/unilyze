# .claude/reports/

HTML レポート 6 種 (decision / plan / brief / review / retrospective / architecture) のカタログ。
`html-reports` skill (`.claude/skills/html-reports/`) が配布元。

`index.html` から検索・フィルタ・関係性グラフ付き一覧でアクセスする。

## 使い方

| 操作 | mise (推奨) | slash command | 直接実行 |
|------|-------------|---------------|---------|
| 一覧を見る | `mise run report:open` | `/reports-open` | `open .claude/reports/index.html` |
| 新規作成 | `mise run report:new -- <type> <slug> [--<attr> <val>] [title]` | `/report-new` | `.claude/skills/html-reports/scripts/new-report.sh ...` |
| 初期化 (再) | `mise run report:init` | `/reports-init` | `.claude/skills/html-reports/scripts/init.sh` |
| アセット同期 | `mise run report:update-assets` | `/reports-update-assets` | `.claude/skills/html-reports/scripts/update-assets.sh` |
| 旧形式の移行支援 | `mise run report:migrate` | `/reports-migrate` | `.claude/skills/html-reports/scripts/migration-helper.sh` |

type と属性デフォルト:

| type | 属性 | デフォルト | 取りうる値 |
|------|------|-----------|-----------|
| `decision` | `--weight` | `light` | `light` / `heavy` |
| `plan` | — | — | — |
| `brief` | `--target` | `pr` | `issue` / `pr` / `handoff` |
| `review` | `--scope` | `pr` | `pr` / `module` / `periodic` |
| `retrospective` | `--base` | `plan` | `plan` / `incident` |
| `architecture` | `--detail` | `overview` | `overview` / `module` / `class` |

### 例

```bash
# 軽量判断記録
mise run report:new -- decision di-container

# 重量判断 (代替案比較あり)
mise run report:new -- decision shallow-class --weight heavy "浅いクラスの集約方針"

# 実装計画
mise run report:new -- plan title-provider-refactor

# PR description 用サマリ
mise run report:new -- brief feat-import-export --target pr

# モジュール監査 (旧 audit 相当の単発版)
mise run report:new -- review auth-module --scope module

# 定期品質計測 (旧 audit 相当)
mise run report:new -- review w21-quality --scope periodic "W21 品質計測"

# plan の振り返り
mise run report:new -- retrospective settings-refactor

# 不具合振り返り
mise run report:new -- retrospective sev2-redis-down --base incident

# コードベース全体のオンボーディング (1 枚絵レベル)
mise run report:new -- architecture codebase-overview

# モジュール単位のアーキテクチャ解説
mise run report:new -- architecture domain-layer --detail module "Domain 層の構成"

# ブラウザで一覧確認
mise run report:open
```

生成後の流れ:
1. 出力された HTML を編集 (テンプレートのプレースホルダを実コンテンツに置換)
2. `_index.js` の新規エントリの `tags`, `scent.one_line`, `scent.key_terms`, `related` を補完
3. 仕上げに `status` を `draft` → `done` に変更

## ディレクトリ構成

```
.claude/reports/
├── index.html              # トップページ (検索 + フィルタ + 関係性グラフ)
├── _index.js               # マニフェスト (new-report.sh が自動更新)
├── README.md               # 本ファイル
├── _assets/                # アセット (skill からコピー、update-assets.sh で同期)
│   ├── theme.css
│   ├── components.css
│   └── reports.js
├── decisions/              # 判断記録 (weight: light | heavy)
├── plans/                  # 実装計画
├── briefs/                 # PR / Issue / 引き継ぎ用サマリ
├── reviews/                # 指摘・監査 (scope: pr | module | periodic)
├── retrospectives/         # 完了後の振り返り (base: plan | incident)
├── architectures/          # コードベース解説 (detail: overview | module | class)
└── *.sarif, *.json, *.txt  # 解析・計測ツールの生データ (gitignore 推奨)
```

## レポート種別と表現要素

| 種別 | 用途 | 主な可視化要素 |
|------|------|----------------|
| decision | 判断記録 | 採用根拠 / (heavy) 代替案カード / 決定マトリクス / トレードオフレーダー |
| plan | 実装計画 | フェーズ別タスク / Gantt / 依存グラフ / 進捗バー |
| brief | PR/Issue/引き継ぎサマリ | TL;DR / Scope / レビュー観点 / `copy as ...` ボタン |
| review | 指摘・監査 | 指摘カード (priority) / (pr) Before/After diff / (module) 依存図 / (periodic) KPI・Heatmap |
| retrospective | 完了後振り返り | (plan) 計画 vs 実績 / (incident) タイムライン / 学び 3 種 (win/miss/surprise) |
| architecture | コードベース理解 | LOD slider (0-3) / Layer toggle (data/deps/domain) / モジュール一覧 / 用語集 / オンボーディングルート |

利用可能なコンポーネント一覧: `.claude/skills/html-reports/reference/visualization-catalog.md`

## brief の interactive export

brief レポートには 4 つの copy ボタンがある (日本語固定):

| ボタン | 内容 |
|--------|------|
| Copy as issue body | TL;DR + Why now + Scope + 既知の制約 |
| Copy as PR description | TL;DR + Scope + 設計判断 + レビュー観点 + 既知の制約 |
| Copy as handoff note | 全セクション + 関連レポート |
| Copy as JSON | 構造化 JSON (他ツール連携用) |

クリックで clipboard に Markdown / JSON が流れる。`gh pr create --body "$(pbpaste)"` などで使う。

## レポート間の関係性

`_index.js` の以下のフィールドでレポートを繋ぐ:

| フィールド | 意味 |
|-----------|------|
| `related[]` | 双方向の関連 (任意の type 同士) |
| `derived_from[]` | 派生元 (例: plan -> brief) |
| `supersedes[]` | 旧版を置き換える |
| `retrospective_of[]` | retrospective が振り返る対象 |

`index.html` の関係性グラフセクションが Mermaid で可視化する。

## scent (情報の匂い)

Pirolli の Information Foraging Theory に基づく検索ヒント。
`_index.js` の `scent` フィールドで指定すると、`index.html` がレポートカードに表示する。

```js
scent: {
  one_line: "1 行サマリ (検索カードの主表示)",
  key_terms: ["重要語1", "重要語2"],
  reading_minutes: 5,
  prereqs: ["事前に読むべきレポート id"]
}
```

## ライブラリ依存 (CDN)

`_assets/reports.js` が必要なときだけ動的に読み込む。

| 用途 | ライブラリ |
|------|-----------|
| 図解 (シーケンス/フロー/Gantt/クラス図) | Mermaid 11 |
| シンタックスハイライト | Prism 1.29 |
| チャート (line/radar/bar) | Chart.js 4.4 |

オフライン時は図表が表示されない。

## ステータス

| status | 意味 |
|--------|------|
| `done` | 完成・閲覧可能 |
| `draft` | 作成中 (new-report.sh のデフォルト) |
| `in-progress` | 作業中 (主に plan で進行中フェーズあり) |
| `archived` | 古い・参照用 |
| `template` | テンプレート参照用 |

## 旧 4 種からの移行

旧形式 (`audits/`, `adr-analysis/`) のレポートがある場合、`mise run report:migrate` で移行候補とコマンドを提示する。実行は人間が判断して行う。

マッピング:

| 旧 | 新 |
|----|-----|
| `audits/<file>.html` | `reviews/<file>.html` + `scope=periodic` |
| `adr-analysis/<file>.html` | `decisions/<file>.html` + `weight=heavy` |
