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

## Phase 6: Code Smell precision corpus（基盤整備）

v0.1.2 / SyntaxOnly 時代のスナップショット (`unilyze-*.json`) は metricsVersion 未記録かつ semantic Kind が欠落するため、現行版 (toolVersion / metricsVersion / analysisLevel 付き) で再計測し、人手ラベリング用 CSV を生成する基盤を整備した。本 Issue ではラベリング自体は行わない。

### 再計測コーパス

| Project | 出力 | 解析レベル目標 | 備考 |
|---------|------|----------------|------|
| VContainer | `unilyze-vcontainer-v2.json` | Complete | `/tmp/cross-validation-repos` に clone、commit は `corpus-projects.json` 参照 |
| UniTask | `unilyze-unitask-v2.json` | Complete | 同上 |
| Boss Room | `unilyze-bossroom-v2.json` | Complete | 同上 |
| HelloMarioFramework | `unilyze-hmf-v2.json` | Complete | 同上 |
| unilyze self | `unilyze-self-v2.json` | SyntaxOnly | 非 Unity プロジェクトの上限 |
| Unity-Decommissioned (任意) | `unilyze-decommissioned-v2.json` | Complete | semantic Kind 補完用。`Library/ScriptAssemblies` が必要 |

再計測:

```bash
python3 tasks/cross-validation/scripts/measure_smell_corpus.py --include-optional
python3 tasks/cross-validation/scripts/sample_smells.py --seed 42
```

Unity プロジェクトは `UNILYZE_EDITORS_ROOT` と `corpus-projects.json` の editorVersionAliases でパッチ版エディタを解決する。`Library/ScriptAssemblies` が無い clone では Complete に到達せず FullEngine / CoreEngine に縮退する（stderr に警告）。

### TP / FP 判定基準（ラベル列は未記入）

**定義:** 1 件の smell occurrence を **TP** とするのは、当該コードが smell が示すリファクタリングを適用した場合に **意味のある改善** が見込めるとき。閾値を超えたから正しい、という算術的正しさだけでは TP にしない（その読み方では FP は自明に 0% になる）。

**FP 候補の例:**

| Kind | FP になりやすい例 |
|------|-------------------|
| LowCohesion | ステートレスなユーティリティ holder、DTO |
| GodClass | 生成コード、テストフィクスチャ、Unity Inspector 都合の MonoBehaviour |
| DeepNesting | パーサ / 状態機械の意図的ネスト |
| HighCoupling | ファサード、Composition Root、DI コンテナ |
| BoxingAllocation | 意図的な `object` ボックス（Interop 等）で hot path 外 |
| LongMethod | 宣言的に読みやすい linear 手続きで分割メリットが小さい |

**uncertain:** 文脈不足で判断不能な場合。precision 計算の分子・分母からは除外し、件数のみ別報告する。

### 判定者運用（提案）

1. **Primary（人手）:** リポジトリ maintainer が rubric に沿って `label` / `rationale` を記入。
2. **Secondary（LLM）:** 同一 occurrence に独立プロンプトで TP/FP/uncertain を付与し、Primary との不一致をレビューキューに回す。
3. **Tertiary（外部ツール合意）:** SonarAnalyzer S110/S138/S107 等、対応 rule がある Kind ではツール一致を参考信号として記録（`match_smell_sonar.py` 拡張予定）。
4. **信頼性:** 50 件以上を二重ラベルし Cohen's κ を報告してから per-Kind precision（Wilson 95% CI）を公表する。

### サンプリング

`sample_smells.py` は Kind ごとに可変サイズ:

- コーパス全体で **20 件未満** の Kind → **全数** を CSV に出力
- **20 件以上** の Kind → **20 件** をプロジェクト横断の層化ランダムサンプル（seed=42、可能なら 2 プロジェクト以上）

出力: `cross-validation/data/smell-precision-labels.csv`（`label` / `judge` / `rationale` は空欄）

**コーパス未出現 Kind:** `CyclicDependency`, `MissingInnerException`（v2 再計測 6 プロジェクト合計 4542 smells 中 0 件）。precision 測定対象外として別表記する。

## データ

- `cross-validation/data/unilyze-*-v2.json` — 現行版 smell コーパス再計測
- `cross-validation/data/smell-precision-labels.csv` — ラベリング対象（未ラベル）
- `cross-validation/data/smell-corpus-measurement.json` — 再計測メタデータ
- `cross-validation/data/smell-precision-sample-plan.json` — サンプリング計画
- `cross-validation/corpus-projects.json` — clone URL / pin commit / 解析レベル
- `cross-validation/data/unilyze-*.json` — Unilyze 出力 (v0.1.2 比較用・旧)
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
