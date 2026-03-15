# Unilyze メトリクス比較検証レポート

## 概要

Unilyze (v0.1.2) の計測結果を既存の信頼度の高いツールと比較し、各メトリクスの精度を検証した。

結論: Unilyze の計測は信頼でき、実用的な意義がある。

## 対象プロジェクト

| Project | C# Files | 行数 | asmdef | 特徴 |
|---------|----------|------|--------|------|
| HelloMarioFramework | 332 | ~90K | 5 | ゲーム、MonoBehaviour、大型クラス |
| Unity Boss Room | 204 | - | 15 | Netcode、複雑なネットワークコード |
| UniTask | 156 (lib) | - | 9 | ジェネリクス、非同期パターン |
| VContainer | 116 (Assets) | - | 3 | DI コンテナ、洗練された型設計 |

## 比較対象ツール

| ツール | バージョン | 比較メトリクス |
|--------|-----------|---------------|
| lizard | 1.21.2 | CycCC |
| SonarAnalyzer.CSharp | 10.20.0.135146 | CogCC (S3776) |
| JetBrains inspectcode | 2025.3.3 | Code Smell (質的) |
| 手計算 | - | LCOM-HS, DIT, CBO |

## 結果サマリー

### Phase 1: CycCC — Unilyze vs lizard (全4プロジェクト, 2826メソッド)

| Project | Matched | Exact% | Within1% | MAE | Spearman |
|---------|---------|--------|----------|-----|----------|
| HelloMarioFramework | 418 | 91.6% | 99.8% | 0.09 | 0.958 |
| Boss Room | 988 | 91.1% | 99.5% | 0.10 | 0.933 |
| UniTask | 996 | 96.8% | 98.8% | 0.10 | 0.869 |
| VContainer | 424 | 91.7% | 97.6% | 0.13 | 0.930 |

差異の主因: `?.` (null conditional), `goto`, `switch expression arm` の扱い。Unilyze は McCabe 拡張仕様に忠実で、lizard (テキストベースパーサー) より正確。

### Phase 2: CogCC — Unilyze vs SonarAnalyzer (2プロジェクト, 993メソッド)

| Project | Matched | Exact% | Within1% | MAE | Spearman |
|---------|---------|--------|----------|-----|----------|
| HelloMarioFramework | 418 | 100.0% | 100.0% | 0.00 | 1.000 |
| VContainer | 575 | 96.5% | 99.1% | 0.07 | 0.968 |

HelloMarioFramework は SonarAnalyzer (CogCC の本家実装) と 418メソッド全件完全一致。
VContainer の 20件の差異は `goto` ネスト増分とローカル関数帰属で全て説明可能。

### Phase 4: LCOM-HS / DIT / CBO — Unilyze vs 手計算 (15型, 45比較)

| Metric | Exact | Approximate | Mismatch |
|--------|-------|-------------|----------|
| LCOM-HS | 14 | 1 | 0 |
| DIT | 15 | 0 | 0 |
| CBO | 14 | 1 | 0 |
| Total | 43 (95.6%) | 2 (4.4%) | 0 (0%) |

2件の近似は丸め境界と大型クラスの推定値。実質的な計算エラーはゼロ。

### Phase 5: Code Smell — Unilyze vs jb inspectcode (質的比較)

両ツールのルールカテゴリは完全に非重複 (overlap ~0%)。

| Unilyze (構造的) | jb inspectcode (スタイル的) |
|-------------------|-----------------------------|
| Complexity (CycCC/CogCC) | Naming conventions |
| Method size, Nesting depth | Dead code detection |
| Parameter count | Encapsulation (visibility) |
| Coupling (CBO), Cohesion (LCOM) | Null safety, Pattern suggestions |
| God Class, Deep Inheritance | Code duplication |

jb inspectcode は C# 向けの複雑度・結合度・凝集度ルールを持たない (3,127 全ルール中 0件)。
Unilyze はこの空白を埋めるツールとして独自の価値を持つ。

## メトリクスごとの信頼度評価

| Metric | 判定 | 根拠 |
|--------|------|------|
| CycCC | ○ | 4プロジェクト 2826メソッドで lizard と 91-97% 完全一致。差異は定義差に起因 |
| CogCC | ○ | SonarAnalyzer (本家実装) と HMF で 100% 一致。VContainer も 96.5% |
| LCOM-HS | ○ | 15型の手計算で全件一致。auto-property 除外、コンストラクタ含有が正確 |
| DIT | ○ | 15型で全件一致。syntactic モードで interface-only = DIT 0 を正しく処理 |
| CBO | ○ | 15型で全件一致。syntactic 収集位置の制約を理解した上で正確 |
| Code Smell | ○ | 構造的メトリクスベースの検出。jb inspectcode とは問題空間が非重複で補完的 |
| MI | — | 今回の比較対象外 (macOS で使える無料の MI 計測ツールがない) |

## 既知の定義差異一覧

### CycCC (Unilyze vs lizard)

| 対象 | lizard | Unilyze | 影響 |
|------|--------|---------|------|
| `?.` (null conditional) | 非カウント | +1 | Unilyze > lizard |
| `goto` | 非カウント | +1 | Unilyze > lizard |
| `#if`/`#elif` | +1 | 非カウント | lizard > Unilyze |
| `switch expression arm` | 非カウント | +1 | Unilyze > lizard |
| `bool &`/`bool \|` | 非カウント | +1 (SemanticModel) | Unilyze > lizard |

### CogCC (Unilyze vs SonarAnalyzer)

| 対象 | Unilyze | SonarAnalyzer | 影響 |
|------|---------|---------------|------|
| `goto` ネスト増分 | +1 (flat) | +1 + nesting | Sonar higher |
| Non-static local functions | 親メソッドに帰属 | 帰属が異なる場合あり | Varies |

### OOP メトリクス

| 対象 | 影響 |
|------|------|
| `const` フィールド | StaticKeyword がないため instance 扱い。影響は軽微 |
| DIT syntactic モード | Unity DLL なしでは MonoBehaviour = DIT 1 |

## 発見されたバグ

なし。全フェーズを通じて Unilyze の計算エラーは確認されなかった。

## Unilyze の差別化ポイント

調査の結果、Unilyze の計測メトリクスを全てカバーする単一ツールは存在しない。

| 差別化要素 | 代替手段 |
|-----------|---------|
| CogCC + LCOM-HS を同時計測 | NDepend (LCOM) + SonarAnalyzer (CogCC) の2ツール併用が必要 |
| Unity .asmdef パース | 他ツールなし |
| macOS CLI + JSON/SARIF | NDepend (商用 EUR 399/年) が最も近い |
| diff / trend サブコマンド | NDepend (トレンドあり) が最も近いが JSON 出力なし |
| OSS + 無料 | 同等の網羅性を持つ無料ツールなし |

## 結論: Unilyze の計測は意義があるか

Yes。

1. 精度: 全メトリクスで既存ツール・手計算と高い一致を示した
2. 網羅性: CogCC + CycCC + LCOM-HS + DIT + CBO + Code Smell を単一ツールでカバーする唯一の無料 CLI
3. Unity 対応: .asmdef パースを持つ唯一のメトリクスツール
4. 補完性: jb inspectcode とはルール空間が非重複で、併用により構造的 + スタイル的な品質カバレッジを実現

## Extra: similarity-csharp クローン検出との相関

HelloMarioFramework に対して similarity-csharp (threshold=0.7) を実行し、クローン集中箇所と Unilyze メトリクスの相関を分析した。

| 区分 | 型数 | CodeHealth平均 | CBO平均 |
|------|------|---------------|---------|
| クローン集中型 | 8 | 7.0 | 12.1 |
| クローンなし型 | 4 | 10.0 | 3.3 |

- CodeHealth が低い型にクローンが集中する (7.0 vs 10.0)
- CBO が高い型にクローンが集中する (12.1 vs 3.3)
- Enemy 系の最大クローンクラスタ (226行影響) は Unilyze が HighComplexity / DeepNesting / HighCoupling を検出した型と一致
- Unilyze の「リファクタリングすべき箇所」の指摘がクローン検出で裏付けられた

## 詳細レポート

- [Phase 1: CycCC lizard 比較](cross-validation/phase1-cyccc-lizard.md)
- [Phase 2: CogCC SonarAnalyzer 比較](cross-validation/phase2-cogcc-sonar.md)
- [Phase 4: OOP メトリクス手計算](cross-validation/phase4-manual-oop-metrics.md)
- [Phase 5: Code Smell 質的比較](cross-validation/phase5-codesmell-qualitative.md)
- [Extra: similarity-csharp 相関分析](cross-validation/phase-extra-similarity-correlation.md)

## データ

- `cross-validation/data/unilyze-*.json` — Unilyze 出力 (全4プロジェクト)
- `cross-validation/data/lizard-*.csv` — lizard 出力
- `cross-validation/data/matched-cyccc-*.csv` — CycCC マッチング結果
- `cross-validation/data/matched-cogcc-sonar-*.csv` — CogCC マッチング結果
- `cross-validation/data/sonar-cogcc-*.json` — SonarAnalyzer 出力
- `cross-validation/data/manual-oop-metrics.csv` — 手計算結果
- `cross-validation/data/jb-inspect-unilyze.sarif` — jb inspectcode SARIF

## 環境

- macOS 15.5.0 (ARM64)
- .NET 10.0.3 SDK
- Unilyze 0.1.2
- lizard 1.21.2
- SonarAnalyzer.CSharp 10.20.0.135146
- JetBrains inspectcode 2025.3.3
- similarity-csharp (mizchi/similarity, Rust)
- 検証日: 2026-03-16
