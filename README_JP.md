# unilyze

Unity プロジェクトの型依存関係とコード品質を静的解析・可視化する CLI ツール。

開発者向けのビルド・テスト・リリース情報は [README.dev_JP.md](README.dev_JP.md) を参照。

### Requirements

- .NET 8.0 or later

## Quick Start

```
dotnet tool install --global Unilyze
```

Unity プロジェクトのディレクトリで実行するだけで、解析結果がブラウザで開く:

```bash
cd ~/MyUnityProject
unilyze
```

## Usage

```bash
# カレントディレクトリを解析してブラウザで開く
unilyze

# プロジェクトを指定
unilyze -p ~/MyUnityProject

# HTML + JSON をファイルに保存
unilyze -p ~/MyUnityProject -o graph.html

# HTML/JSON を生成するがブラウザは開かない
unilyze -p ~/MyUnityProject --no-open

# JSON だけ出力
unilyze -p ~/MyUnityProject -f json -o result.json

# SARIF 出力 (GitHub Code Scanning 連携)
unilyze -p ~/MyUnityProject -f sarif -o report.sarif

# 既存 JSON から HTML を再生成
unilyze -i result.json -o graph.html

# アセンブリを絞り込み
unilyze -p ~/MyUnityProject -a App.Domain

# プレフィックスでフィルタ
unilyze -p ~/MyUnityProject --prefix "App."
```

### Subcommands

```bash
# 2時点の分析結果を比較 (before/after)
unilyze diff <before.json> <after.json>
unilyze diff <before.json> <after.json> -o diff.json

# Hotspot 分析 (git churn x complexity)
unilyze hotspot -p ~/MyUnityProject
unilyze hotspot -p ~/MyUnityProject --since 6.month -n 10

# 品質トレンド (時系列比較)
unilyze trend <dir-of-jsons>
unilyze trend <dir-of-jsons> -o trend.json
```

`hotspot` は `git` コマンドが利用可能で、対象パスが Git リポジトリであることを前提とする。

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `-p, --path` | Unity プロジェクトルート | `.` |
| `-i, --input` | 既存の JSON を入力として使用 | - |
| `-o, --output` | 出力先（拡張子で形式を推定） | ブラウザで開く |
| `-f, --format` | 出力形式: `html`, `json`, `sarif` | `html` |
| `-a, --assembly` | 解析対象のアセンブリ名 | 全アセンブリ |
| `--prefix` | asmdef 名のフィルタプレフィックス | 自動検出 |
| `--no-open` | HTML 生成後にブラウザを自動起動しない | `false` |

## Metrics

| メトリクス | 説明 | 粒度 |
|-----------|------|------|
| Cognitive Complexity | SonarSource 仕様準拠の認知的複雑度 | メソッド |
| Cyclomatic Complexity | McCabe 1976 準拠の循環的複雑度 | メソッド |
| LCOM-HS | Henderson-Sellers 凝集度 (0.0-1.0+) | 型 |
| CBO | Coupling Between Objects (結合する型の数) | 型 |
| DIT | Depth of Inheritance (継承チェーンの深さ) | 型 |
| Ca / Ce | Afferent / Efferent Coupling | 型 |
| Instability | Ce / (Ca + Ce) (0.0: 安定 - 1.0: 不安定) | 型 |
| Maintainability Index | Halstead Volume, CycCC, LoC から算出 (0-100) | メソッド |
| Code Health | 複合スコア (1.0: 最悪 - 10.0: 最良) | 型 |

各メトリクスの詳細な定義と閾値は [docs/metrics.md](docs/metrics.md) を参照。

## Code Smell Detection

| Kind | 条件 |
|------|------|
| GodClass | 行数 > 500 かつ メソッド数 > 20 |
| LongMethod | 行数 > 60 |
| HighComplexity | CogCC > 25 |
| ExcessiveParameters | パラメータ数 > 4 |
| DeepNesting | ネスト深度 > 4 |
| LowCohesion | LCOM > 0.8 |
| HighCoupling | CBO >= 14 |
| LowMaintainability | MI < 20 |
| DeepInheritance | DIT >= 6 |
| CyclicDependency | 型/アセンブリ間の循環依存 (Tarjan SCC) |

## Output Formats

| Format | 用途 |
|--------|------|
| `html` | ブラウザで依存グラフとメトリクスを可視化 |
| `json` | エージェント連携、プログラマティック利用 |
| `sarif` | GitHub Code Scanning、IDE 統合 |

`html` 出力は通常はインタラクティブな依存グラフを表示する。外部グラフ資産をロードできない環境では、型一覧、依存関係、ホットスポット、循環依存、アセンブリ結合度を確認できる組み込みレポートビューへ自動フォールバックする。

## Known Limitations

- `html` のインタラクティブ graph は CDN から Cytoscape 系スクリプトを読む。取得できない環境では built-in offline report fallback に切り替わる。これは現行仕様。
- Windows は未確認。README に記載していない環境は、まだ動作保証しない。

## Analysis Levels

プロジェクトの構成に応じて、3段階のフォールバックで解析精度を最大化する:

| 優先度 | ソース | 取得情報 |
|--------|--------|---------|
| 1 | `.csproj` / `.sln` | DLL 参照パス, プリプロセッサシンボル, C# バージョン |
| 2 | `.asmdef` + Unity DLL | Unity Engine/Editor DLL, パッケージ DLL |
| 3 | SyntaxOnly | SyntaxTree のみ（SemanticModel なし） |

`.csproj` が存在する場合は参照情報を自動取得し、SemanticModel ベースの解析（LCOM, CBO, DIT, bool &/| の CycCC 等）の精度が向上する。`.csproj` がなくても `.asmdef` や Unity インストールから DLL を自動解決する。いずれも見つからない場合は SyntaxTree のみで動作する。

`.asmdef` ファイルがないプロジェクトにも対応。ディレクトリ全体を単一アセンブリとして解析する。

## Agent Workflow

コーディングエージェントと組み合わせて品質改善ループを回す設計:

```
unilyze (計測) → 問題特定 → 修正 → unilyze diff (検証) → 改善確認
```

```bash
# 1. 計測
unilyze -p ~/MyProject -f json -o /tmp/before.json

# 2. エージェントが問題箇所を修正
# ...

# 3. 再計測 & diff で改善確認
unilyze -p ~/MyProject -f json -o /tmp/after.json
unilyze diff /tmp/before.json /tmp/after.json
```

## License

MIT
