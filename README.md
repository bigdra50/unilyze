# unity-roslyn-graph

Unity プロジェクトの `.asmdef` とソースコードを Roslyn で静的解析し、依存関係グラフとコード品質メトリクスを生成する CLI ツール。

## Quick Start

```bash
# 1. ビルド（.NET 10 SDK が必要）
cd src/UnityRoslynGraph
dotnet build

# 2. Unity プロジェクトを解析 → JSON に保存
dotnet run -- analyze -p ~/MyUnityProject -o result.json

# 3. HTML インタラクティブビューアを生成して開く
dotnet run -- format -i result.json -f html -o graph.html
open graph.html
```

プレフィックスでアセンブリを絞り込む場合:

```bash
dotnet run -- analyze -p ~/MyUnityProject --prefix "App." -o result.json
dotnet run -- format -i result.json -f html -o graph.html
```

HTML ビューアでは型の依存グラフ、Code Health スコアバッジ、namespace の展開/折りたたみなどが操作できる。

## アーキテクチャ

```
.cs/.asmdef --> [analyze] --> JSON --> [format] --> csv/mermaid/dot/drawio/html
                               |
                               v
                         git管理・差分確認可能
```

分析（analyze）と可視化（format）を JSON 中間フォーマットで分離。
ショートカットコマンド（assembly/types/diagram）は内部で同じパイプラインを使用する。

## コマンド

### analyze

プロジェクトを分析し、JSON 中間フォーマットで出力する。

```bash
dotnet run -- analyze -p <path> [--prefix <prefix>] [-a <assembly>] [-o <file>]
```

### format

JSON 中間フォーマットを各種形式に変換する。

```bash
dotnet run -- format -i <json-file> [-f csv|mermaid|dot|drawio|html] [-s assembly|types|diagram] [-o <file>]
```

### assembly（ショートカット）

アセンブリ（asmdef）単位の依存関係を出力する。

```bash
dotnet run -- assembly -p <path> [-f csv|mermaid|dot|drawio] [--prefix <prefix>] [-o <file>]
```

### types（ショートカット）

型（クラス・インターフェース等）単位の依存関係を出力する。

```bash
dotnet run -- types -p <path> [-f csv|mermaid|dot|drawio] [-a <assembly>] [-o <file>]
```

### diagram（ショートカット）

マルチページ draw.io ファイルを生成する。

```bash
dotnet run -- diagram -p <path> [--prefix <prefix>] -o <file>
```

## オプション

| オプション | 説明 | デフォルト |
|-----------|------|-----------|
| `-p, --path` | Unity プロジェクトルートまたは Assets ディレクトリ | `.` |
| `-f, --format` | 出力形式: `csv`, `mermaid`, `dot`, `drawio`, `html` | `csv` |
| `-a, --assembly` | 解析対象のアセンブリ名 | 全アセンブリ |
| `--prefix` | asmdef 名のフィルタプレフィックス（省略時は自動検出） | - |
| `-o, --output` | 出力ファイルパス（drawio/html/diagram では必須） | stdout |
| `-i, --input` | 入力 JSON ファイル（format コマンド） | - |
| `-s, --scope` | format のスコープ: `assembly`, `types`, `diagram` | `types` |

## 出力形式

### CSV

型名・種類・アセンブリ・メンバー・依存先を一覧形式で出力。コード品質メトリクス（CC 平均/最大、Code Health）も含む。

### Mermaid

`classDiagram` 構文。ステレオタイプ付き（`<<interface>>`, `<<enum>>`, `<<record>>`）。
メンバーは型あたり最大 10 件表示。

### GraphViz Dot

record ノードで UML 風のコンパートメント表示。rankdir=BT で下から上へのレイアウト。

### draw.io

単一ページまたはマルチページの XML を生成。`adaptiveColors="auto"` でダーク/ライトモードに自動適応。

### HTML インタラクティブビューア

Cytoscape.js + dagre レイアウトによる単一 HTML ファイル。以下の機能を持つ。

## HTML ビューア機能

### 入れ子 namespace compound ノード

名前空間のドット区切り階層（例: `App.Editor.ScenarioEditor.Panels`）をツリー構造で表現。
ダブルクリックで展開/折りたたみを切り替える。

```
App (virtual)
├── Domain (19 types)
├── Composition (14) → ScreenContracts, ScreenFlows
├── Editor (2) → PlacementEditor, ScenarioEditor → Panels, Util
├── Infrastructure (virtual) → Calibration, Input, Scenario → Json
├── Presentation (2) → Extensions, Presenters, UI
└── Tests (virtual) → Common, EditMode, PlayMode
```

- 折りたたみ時: summary ノード（型数ラベル付き）
- 展開時: compound コンテナ内に子 namespace と直接型を表示
- virtual namespace（直接型を持たない中間ノード）は破線枠で表示
- meta-edge: 折りたたまれたノード間の依存をルーティング

### 型ノード

| 種類 | 形状 |
|------|------|
| class / record | 角丸四角形 |
| struct | 四角形 |
| interface | ひし形 |
| enum | 六角形 |
| delegate | 楕円 |

アセンブリごとに色分け（12 色パレット）。

### 依存エッジ

9 種類の依存を個別のスタイルで表示:

| 種類 | 線種 | 色 |
|------|------|-----|
| Inheritance | 実線 | 赤 |
| InterfaceImpl | 破線 | シアン |
| FieldType | 実線 | 緑 |
| PropertyType | 実線 | 紫 |
| MethodParam | 点線 | オレンジ |
| ReturnType | 実線 | 赤 |
| ConstructorParam | 点線 | 黄 |
| EventType | 破線 | ピンク |
| GenericConstraint | 点線 | 水色 |

### コード品質バッジ

型ノードに Code Health スコア（1-10）をオーバーレイ表示。
namespace にはスコア集計（min/max/avg + 警告/重大カウント）を表示。

ズームレベルに応じた LOD（Level of Detail）制御:

| ズーム | 表示対象 |
|--------|---------|
| 非常に遠い (< 0.15) | critical のみ (score < 4) |
| 遠い (0.15 - 0.30) | warning 以下 (score < 7) |
| 中間 (0.30 - 0.50) | 完璧以外 (score < 9) |
| 近い (> 0.50) | 全バッジ |

バッジサイズは `sqrt(zoom)` で緩やかにズームに追従し、0.45x〜1.6x にクランプ。
ビューポート外のバッジ生成スキップ、最小ピクセルサイズ閾値による非表示も行う。

### 操作

- Expand All / Collapse All: 全 namespace の展開/折りたたみ
- Fit: 表示可能ノードにズーム
- Re-layout: dagre レイアウトの再計算
- 検索: 型名・namespace 名でフィルタ（デバウンス付き）
- 型クリック: 詳細パネル（メンバー、CC バッジ、依存先/被依存、属性）

## draw.io ページ構成

`diagram` コマンドで生成されるマルチページファイル:

| ページ | 内容 |
|--------|------|
| Assembly Overview | asmdef ノード + メトリクス + Code Health + 依存エッジ |
| Types: {Assembly} | アセンブリごとの UML クラス図風型一覧（最大 12 メンバー表示） |
| Cross-Assembly Deps | アセンブリをまたぐ型依存のみ抽出 |
| Interface Map | インターフェースと実装クラスの対応図 |

## 分析内容

Roslyn の構文解析（SyntaxTree）のみで以下を抽出する。SemanticModel は使用しない。

- 型: class, record, struct, interface, enum, delegate
- メンバー: field, property, method, event, indexer, enum member
- 依存: 継承, インターフェース実装, フィールド型, プロパティ型, メソッドパラメータ, 戻り値型, コンストラクタパラメータ, イベント型, ジェネリック制約
- 属性: 名前と引数（named + positional）
- ジェネリック制約: `where T : IFoo`
- enum 基底型と値
- partial クラスの自動マージ
- 2 パスによるインターフェース判定（knownInterfaces + ヒューリスティック）
- ネストした型の検出

## コード品質メトリクス

### Cognitive Complexity (CC)

メソッド単位の認知的複雑度を計測:

- 構造的増分: if/else, switch, for/foreach/while/do, catch, goto, 三項演算子, ?? 演算子
- ネスト倍率: ネスト階層ごとに +1（例: if の中の if = 構造 +1 + ネスト +1 = 計 +2）
- 論理演算子: `&&`, `||` の種類変更ごとに +1（連続する同一演算子は重複カウントしない）
- ラムダ・匿名メソッド: ネスト階層のみ加算

### Code Health スコア (1-10)

型単位の品質スコア。6 つの要素の加重平均:

| 要素 | 重み | 良好 → 危険 |
|------|------|------------|
| CC 平均 | 25% | 5 → 25 |
| CC 最大 | 20% | 10 → 40 |
| 行数 | 15% | 200 → 800 |
| メソッド数 | 10% | 10 → 40 |
| 最大ネスト深度 | 15% | 3 → 7 |
| 多引数メソッド数 (>4) | 15% | 0 → 4 |

## JSON 中間フォーマット

`analyze` コマンドの出力。型情報・依存関係・メトリクスを構造化して保持する。

```jsonc
{
  "projectPath": "/path/to/unity-project",
  "analyzedAt": "2026-03-12T...",
  "assemblies": [
    {
      "name": "App.Domain",
      "directory": "...",
      "references": ["App.Application"],
      "metrics": { "typeCount": 20, "classCount": 15, ... },
      "healthMetrics": { "averageCodeHealth": 7.5, "minCodeHealth": 3.2, ... }
    }
  ],
  "types": [
    {
      "name": "TrainingSession",
      "namespace": "App.Application",
      "kind": "class",
      "modifiers": ["public", "sealed"],
      "interfaces": ["IDisposable"],
      "members": [
        {
          "name": "Execute",
          "type": "Task",
          "memberKind": "Method",
          "cognitiveComplexity": 5
        }
      ],
      "assembly": "App.Application",
      "filePath": "...",
      "lineCount": 120
    }
  ],
  "dependencies": [
    { "fromType": "TrainingSession", "toType": "ScenarioData", "kind": "FieldType" }
  ],
  "typeMetrics": [
    {
      "typeName": "TrainingSession",
      "codeHealth": 7.8,
      "averageCognitiveComplexity": 5.2,
      "maxCognitiveComplexity": 12,
      "lineCount": 120,
      "methodCount": 8,
      "maxNestingDepth": 3
    }
  ]
}
```

## 使用例

```bash
# JSON から各種フォーマットへ変換
dotnet run -- format -i result.json -f mermaid -s types
dotnet run -- format -i result.json -f drawio -s diagram -o diagram.drawio

# ショートカット: CSV でアセンブリ依存を確認
dotnet run -- assembly -p ~/MyUnityProject

# ショートカット: 特定アセンブリの型図を Mermaid で出力
dotnet run -- types -p ~/MyUnityProject -a App.Domain -f mermaid

# ショートカット: マルチページ draw.io ファイル生成
dotnet run -- diagram -p ~/MyUnityProject --prefix "App." -o architecture.drawio
```

## 設計判断

- SyntaxTree のみ使用（SemanticModel 不使用）: コンパイル不要で高速。不完全なプロジェクトでも動作
- JSON 中間形式: 分析（重い）と可視化（軽い）を分離。git diff で変更追跡が可能
- 2 ノード namespace システム: summary ノード（折りたたみ）+ compound ノード（展開）で DOM 操作なしに切り替え
- フォント読み込み待機: `document.fonts.ready` で Web フォント読み込み後に Cytoscape を初期化し、ラベル幅を正確に計測
- バッジの LOD 制御: 地図アプリと同様にズームレベルに応じてバッジの表示/非表示・サイズを制御。ズームアウト時は問題箇所のみ強調し、ズームイン時は全情報を表示
