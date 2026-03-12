# unilyze

Unity プロジェクトの `.asmdef` と C# ソースコードを Roslyn で静的解析し、型の依存関係グラフとコード品質メトリクスをインタラクティブに可視化する CLI ツール。

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

# JSON だけ出力
unilyze -p ~/MyUnityProject -f json -o result.json

# 既存 JSON から HTML を再生成
unilyze -i result.json -o graph.html

# CSV を stdout に出力
unilyze -p ~/MyUnityProject -f csv

# アセンブリを絞り込み
unilyze -p ~/MyUnityProject -a App.Domain

# プレフィックスでフィルタ
unilyze -p ~/MyUnityProject --prefix "App."
```

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `-p, --path` | Unity プロジェクトルート | `.` |
| `-i, --input` | 既存の JSON を入力として使用 | - |
| `-o, --output` | 出力先（拡張子で形式を推定） | ブラウザで開く |
| `-f, --format` | 出力形式: `html`, `json`, `csv`, `mermaid`, `dot` | `html` |
| `-a, --assembly` | 解析対象のアセンブリ名 | 全アセンブリ |
| `--prefix` | asmdef 名のフィルタプレフィックス | 自動検出 |
| `-s, --scope` | スコープ: `types`, `assembly` | `types` |

## What it analyzes

Roslyn の SyntaxTree のみで解析（SemanticModel 不使用）。コンパイル不要で、不完全なプロジェクトでも動作する。

- 型: class, record, struct, interface, enum, delegate
- メンバー: field, property, method, event, indexer
- 依存: 継承, インターフェース実装, フィールド型, プロパティ型, メソッドパラメータ, 戻り値型, コンストラクタパラメータ, イベント型, ジェネリック制約
- Cognitive Complexity (メソッド単位)
- Code Health スコア (型単位, 1-10)
- partial クラスの自動マージ

## License

MIT
