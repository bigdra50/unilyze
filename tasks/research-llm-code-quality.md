# arXiv論文調査: コーディングエージェント/LLMを使った自動コード品質分析・改善

調査日: 2026-03-12
対象期間: 2024年〜2026年（基礎的重要論文含む）

---

## 調査サマリー

7つのテーマに沿って計16本の論文を調査した。以下に各論文の詳細と、テーマ横断的な知見をまとめる。

---

## 1. LLMによる自動コードレビュー

### 1-1. A Survey of Code Review Benchmarks and Evaluation Practices in Pre-LLM and LLM Era

| 項目 | 内容 |
|------|------|
| arXiv ID | [2602.13377](https://arxiv.org/abs/2602.13377) |
| 発表年 | 2026年2月 |
| 著者 | Taufiqul Islam Khan, Shaowei Wang, Haoxiang Zhang, Tse-Hsun Chen |

- 手法: 2015〜2025年の99本の論文（Pre-LLM期58本、LLM期41本）を体系的に分析したサーベイ論文。コードレビュー研究を5ドメイン・18タスクに分類
- 主要知見: LLM時代に入り、エンドツーエンド生成型レビュー、多言語対応の拡大、変更理解タスク単体の減少といったシフトが発生。現行ベンチマークはタスク多様性、動的ランタイム評価、きめ細かい評価手法が不足
- 実用性: コーディングエージェントにコードレビュー機能を組み込む際の評価フレームワーク設計に直接参照できる
- 制限: サーベイ論文のため新手法の提案はない

### 1-2. Automated Code Review In Practice

| 項目 | 内容 |
|------|------|
| arXiv ID | [2412.18531](https://arxiv.org/abs/2412.18531) |
| 発表年 | 2024年12月（ICSE 2025採択） |
| 著者 | Umut Cihan, Vahid Haratian ほか |

- 手法: Qodo PR Agentベースの自動コードレビューツールを10プロジェクト・4,335 PR（うち1,568件が自動レビュー対象）で産業評価
- 主要知見: 自動レビューコメントの73.8%が「resolved」とマーク。一方でPRクローズ時間は5h52m→8h20mに増加。バグ検出向上・品質意識向上の効果あり。誤レビュー・不要な修正提案・無関係コメントという欠点も確認
- 実用性: 高い。既にQodo PR Agentとして実用化されている。エージェントに組み込む際のUX設計（ノイズフィルタリング）の知見が得られる
- 制限: PR処理時間の増加。誤検知によるレビュー疲れのリスク

### 1-3. Combining Large Language Models with Static Analyzers for Code Review Generation

| 項目 | 内容 |
|------|------|
| arXiv ID | [2502.06633](https://arxiv.org/abs/2502.06633) |
| 発表年 | 2025年2月 |
| 著者 | Imen Jaoua, Oussama Ben Sghaier, Houari Sahraoui |

- 手法: 知識ベースシステム（KBS: 静的解析ツール）とLLMを3段階で統合するハイブリッド手法。Data-Augmented Training（DAT）、RAG、Naive Concatenation of Outputs（NCO）の3戦略を比較
- 主要知見: RAGが最も効果的で、レビューコメントの精度とカバレッジの両方を向上。ただし精度面では静的解析単体にはまだ及ばない
- 実用性: 高い。静的解析ツール既存のプロジェクトにLLMを重ねるパイプラインとして直接適用可能
- 制限: 静的解析の精度を超えられない場面がある。RAGのチャンク戦略・インデックス構築のコスト

---

## 2. 静的解析とLLMのハイブリッドアプローチ

### 2-1. Static Analysis as a Feedback Loop: Enhancing LLM-Generated Code Beyond Correctness

| 項目 | 内容 |
|------|------|
| arXiv ID | [2508.14419](https://arxiv.org/abs/2508.14419) |
| 発表年 | 2025年8月 |
| 著者 | Scott Blyth, Sherlock A. Licorish, Christoph Treude, Markus Wagner |

- 手法: Bandit（セキュリティ）とPylint（品質）の静的解析結果をLLMにフィードバックする反復ループ。最大10イテレーションでコード品質を段階的に改善
- 主要知見: GPT-4oでセキュリティ問題を40%超→13%、可読性違反を80%超→11%、信頼性警告を50%超→11%に削減
- 実用性: 極めて高い。コーディングエージェントの「生成→検証→修正」ループに直接組み込める設計パターン
- 制限: Python限定の評価。イテレーション回数増加に伴うAPIコスト。10回でも解消されない問題がある

### 2-2. Augmenting Large Language Models with Static Code Analysis for Automated Code Quality Improvements

| 項目 | 内容 |
|------|------|
| arXiv ID | [2506.10330](https://arxiv.org/abs/2506.10330) |
| 発表年 | 2025年6月 |
| 著者 | Seyed Moein Abtahi, Akramul Azim |

- 手法: 静的解析フレームワークでバグ・脆弱性・コードスメルを検出し、RAGを組み合わせたLLM（GPT-3.5 Turbo / GPT-4o）で自動修正。「Code Comparison App」でハルシネーション対策
- 主要知見: 修正適用後の再スキャンでコード問題の大幅削減を確認。ハルシネーション対策として差分比較による検証が有効
- 実用性: 高い。大規模プロジェクトでの実証あり。差分検証によるガードレールはエージェント設計の参考になる
- 制限: LLMのコスト。複雑な修正での精度低下

---

## 3. AIによる自動リファクタリング

### 3-1. An Empirical Study on the Potential of LLMs in Automated Software Refactoring

| 項目 | 内容 |
|------|------|
| arXiv ID | [2411.04444](https://arxiv.org/abs/2411.04444) |
| 発表年 | 2024年11月 |
| 著者 | Bo Liu, Yanjie Jiang, Yuxia Zhang, Nan Niu, Guangjie Li, Hui Liu |

- 手法: 20プロジェクトの180件の実リファクタリング事例でChatGPTとGeminiを評価。リファクタリング機会の特定と解法の推薦を分離して分析
- 主要知見: 詳細プロンプトにより検出率が15.6%→86.7%に向上。解法の63.6%が人間の専門家と同等以上。ただし176件中13件が安全でない変更（機能変更・構文エラー）。RefactoringMirrorという安全策を提案
- 実用性: 中〜高。プロンプト設計次第で大幅に性能向上するが、安全性検証が必須
- 制限: 安全でないリファクタリングの存在。Java限定。プロンプトへの依存度が高い

### 3-2. ECO: An LLM-Driven Efficient Code Optimizer for Warehouse Scale Computers

| 項目 | 内容 |
|------|------|
| arXiv ID | [2503.15669](https://arxiv.org/abs/2503.15669) |
| 発表年 | 2025年3月 |
| 著者 | Hannah Lin, Martin Maas ほか（Google） |

- 手法: 歴史的コミットからパフォーマンスアンチパターンの辞書を構築し、ファインチューンしたLLMで類似パターンを自動検出・リファクタリング。変更後のコードを検証し、コードレビューに提出、本番環境で効果を測定
- 主要知見: Googleの本番環境にデプロイ済み。25,000行超の変更、6,400コミット超、99.5%超の本番成功率。四半期あたり平均50万正規化CPUコアの節約
- 実用性: 極めて高い。世界最大規模の本番デプロイ実績。パターン辞書＋LLMという設計はスケーラブル
- 制限: Googleの内部インフラに依存した部分が大きい。オープンソースではない

### 3-3. ACE: Automated Technical Debt Remediation with Validated LLM Refactorings

| 項目 | 内容 |
|------|------|
| arXiv ID | [2507.03536](https://arxiv.org/abs/2507.03536) |
| 発表年 | 2025年7月 |
| 著者 | Adam Tornhill, Markus Borg, Nadim Hagatulah, Emma Söderberg |

- 手法: LLMによるリファクタリング提案に対して、コード品質の客観的改善と動作の正しさの両面でバリデーションを行うガードレール付きパイプライン
- 主要知見: バリデーションにより精度が37%→98%に向上。リコール52%で、検出されたコードスメルの半数以上を自信を持ってリファクタリング可能
- 実用性: 高い。「ハルシネーション対策としてのガードレール」というアーキテクチャパターンはエージェント設計の核心
- 制限: リコール52%にとどまる（残り半分は自動修正できない）。ガードレールの設計・維持コスト

---

## 4. 機械学習によるコードスメル検出

### 4-1. Prompt Learning for Multi-Label Code Smell Detection: A Promising Approach

| 項目 | 内容 |
|------|------|
| arXiv ID | [2402.10398](https://arxiv.org/abs/2402.10398) |
| 発表年 | 2024年2月 |
| 著者 | Haiyang Liu, Yang Zhang, Vidya Saikrishna, Quanquan Tian, Kun Zheng |

- 手法: ASTからコード断片を抽出し、自然言語プロンプトとマスクトークンを組み合わせてLLMに入力する「PromptSmell」を提案。多ラベル問題を多クラス分類に変換
- 主要知見: 精度+11.17%、F1+7.4%の改善。プロンプト学習によるコードスメル検出の有効性を実証
- 実用性: 中〜高。AST解析をLLMに組み合わせるパイプラインはRoslyn等のAST解析ツールと連携可能
- 制限: 特定のスメルタイプに限定。学習データの質への依存

### 4-2. A Comprehensive Evaluation of Parameter-Efficient Fine-Tuning on Code Smell Detection

| 項目 | 内容 |
|------|------|
| arXiv ID | [2412.13801](https://arxiv.org/abs/2412.13801) |
| 発表年 | 2024年12月 |
| 著者 | Beiqi Zhang, Peng Liang, Xin Zhou ほか |

- 手法: 4種のPEFT手法を9つの言語モデルで評価。Complex Conditional、Complex Method、Feature Envy、Data Classの4スメルタイプをベンチマーク
- 主要知見: PEFTによるファインチューニングは完全ファインチューニングと同等以上の性能で、GPUメモリ使用量を大幅削減。MCC改善は0.33%〜13.69%
- 実用性: 高い。PEFT手法により軽量なモデルでもコードスメル検出が可能。エッジデプロイやCI/CD統合に向く
- 制限: 4タイプのスメルに限定。高品質なラベル付きデータセットの構築コスト

---

## 5. ASTベースのコード品質メトリクス

### 5-1. AutoCodeRover: Autonomous Program Improvement

| 項目 | 内容 |
|------|------|
| arXiv ID | [2404.05427](https://arxiv.org/abs/2404.05427) |
| 発表年 | 2024年4月 |
| 著者 | Yuntong Zhang, Haifeng Ruan, Zhiyu Fan, Abhik Roychoudhury |

- 手法: AST構造（クラス・メソッドレベル）を活用したコード検索と、スペクトラムベースの障害局所化を組み合わせた自律プログラム改善エージェント
- 主要知見: SWE-bench-liteで19%の解決率を達成（当時のSWE-agentベースラインを上回る）。1件あたり平均$0.43のコストで、開発者の平均2.77日に対して約11.7分で処理
- 実用性: 極めて高い。AST構造によるコード理解はRoslyn Analyzerと共通するアプローチ。自律的なバグ修正エージェントの先駆的実装
- 制限: 解決率19%（当時）は限定的。複雑な問題への対応力に課題

---

## 6. 技術的負債の自動検出

### 6-1. Self-Admitted Technical Debt in LLM Software: An Empirical Comparison

| 項目 | 内容 |
|------|------|
| arXiv ID | [2601.06266](https://arxiv.org/abs/2601.06266) |
| 発表年 | 2026年1月 |
| 著者 | （論文ページ参照） |

- 手法: LLMソフトウェア、MLソフトウェア、非MLソフトウェアにおけるSelf-Admitted Technical Debt（SATD）を比較分析
- 主要知見: LLMプロジェクト特有の3つの新しい負債カテゴリを発見。SATD検出精度はパラダイムで異なる（LLM: 0.82、ML: 0.96、非ML: 0.74）。LLMプロジェクトはSATD出現まで中央値492日だが、出現後は中央値553日持続し除去率49.1%と最低
- 実用性: 中。LLMベースのコーディングエージェント自体が技術的負債を生みやすいという知見は、エージェント設計時の品質管理に直接関係
- 制限: GitHubコメントベースの分析で、コード構造自体の負債は対象外

---

## 7. トークンレベルを超えたコードクローン検出

### 7-1. HyClone: Bridging LLM Understanding and Dynamic Execution for Semantic Code Clone Detection

| 項目 | 内容 |
|------|------|
| arXiv ID | [2508.01357](https://arxiv.org/abs/2508.01357) |
| 発表年 | 2025年8月 |
| 著者 | Yunhao Liang, Ruixuan Ying, Takuya Taniguchi, Guwen Lyu, Zhe Cui |

- 手法: LLMによるセマンティックフィルタリング（第1段階）と、LLM生成テスト入力による動的実行検証（第2段階）を組み合わせた2段階フレームワーク
- 主要知見: 直接的なLLMベース検出と比較して精度・リコール・F1が大幅に向上。Type 4クローン（構文は異なるが機能が同一）の検出に特に有効
- 実用性: 高い。コードベースの重複除去や類似コード統合のエージェント機能として組み込み可能
- 制限: Python限定の評価。動的実行のコストとセキュリティリスク

### 7-2. A Self-Improving Coding Agent

| 項目 | 内容 |
|------|------|
| arXiv ID | [2504.15228](https://arxiv.org/abs/2504.15228) |
| 発表年 | 2025年4月 |
| 著者 | Maxime Robeyns, Martin Szummer, Laurence Aitchison |

- 手法: 基本的なコーディングツールを備えたエージェントが、自身のコードを自律的に編集し性能を改善する自己改善メカニズム。勾配ベースでない学習（LLMリフレクション＋コード更新）
- 主要知見: SWE Bench Verifiedのサブセットで17%→53%への性能向上。LiveCodeBenchでも追加的な改善
- 実用性: 高い。コーディングエージェント自体の自己改善という、メタレベルの品質向上アプローチ
- 制限: 自己改善の収束性・安定性の保証が不十分。暴走リスク

---

## テーマ横断的な知見

### アーキテクチャパターン

調査した論文群から、コーディングエージェントに組み込むべき主要パターンが浮かび上がる。

```
+--------------------------------------------------+
|            コーディングエージェント                 |
+--------------------------------------------------+
|                                                    |
|  [1] 静的解析 + LLM ハイブリッドパイプライン       |
|      静的解析ツール → 問題検出 → LLMで修正提案     |
|      → 差分検証（ガードレール）→ 適用              |
|                                                    |
|  [2] 反復フィードバックループ                      |
|      コード生成 → 静的解析 → 違反フィードバック     |
|      → LLM再生成 → ... (最大N回)                   |
|                                                    |
|  [3] RAGによるコンテキスト強化                      |
|      コードベース/ルール → ベクトルDB               |
|      → 関連コンテキスト取得 → LLMプロンプト注入     |
|                                                    |
|  [4] AST構造活用                                   |
|      AST解析 → クラス/メソッド/依存関係抽出         |
|      → 構造的コード理解 → 精度の高い修正            |
|                                                    |
|  [5] ガードレール + バリデーション                  |
|      LLM出力 → テスト実行 → 品質スコア比較          |
|      → 安全でない変更の自動破棄                     |
+--------------------------------------------------+
```

### 実用性の高い論文トップ5

| 順位 | 論文 | 理由 |
|------|------|------|
| 1 | ECO (2503.15669) | Google本番デプロイ済み。パターン辞書+LLMの設計が再現可能 |
| 2 | Static Analysis as a Feedback Loop (2508.14419) | 反復ループの具体的な効果を定量的に実証 |
| 3 | ACE (2507.03536) | ガードレール付きリファクタリングで精度98%達成 |
| 4 | AutoCodeRover (2404.05427) | AST活用の自律エージェントの先駆的実装 |
| 5 | Combining LLMs with Static Analyzers (2502.06633) | RAG統合の3戦略比較が実装の指針になる |

### 研究のギャップ・今後の課題

1. 言語横断的な評価が不足: 多くの研究がPythonまたはJavaに限定されており、C#/TypeScript等での評価が少ない
2. リアルタイム性: CI/CDパイプラインへの統合時のレイテンシ評価がほぼない
3. 長期的効果: 自動リファクタリングが長期的にコードベースの品質にどう影響するかの縦断研究が不足
4. コスト対効果: APIコストと品質改善の関係を定量的に分析した研究が少ない
5. 安全性保証: LLMによる変更の安全性を形式的に保証する手法がまだ発展途上

---

## 参考文献一覧

| # | タイトル | arXiv ID | 年 | テーマ |
|---|---------|----------|-----|--------|
| 1 | A Survey of Code Review Benchmarks and Evaluation Practices in Pre-LLM and LLM Era | 2602.13377 | 2026 | コードレビュー |
| 2 | Automated Code Review In Practice | 2412.18531 | 2024 | コードレビュー |
| 3 | Combining Large Language Models with Static Analyzers for Code Review Generation | 2502.06633 | 2025 | コードレビュー/ハイブリッド |
| 4 | Static Analysis as a Feedback Loop | 2508.14419 | 2025 | ハイブリッド |
| 5 | Augmenting LLMs with Static Code Analysis | 2506.10330 | 2025 | ハイブリッド |
| 6 | An Empirical Study on the Potential of LLMs in Automated Software Refactoring | 2411.04444 | 2024 | リファクタリング |
| 7 | ECO: An LLM-Driven Efficient Code Optimizer | 2503.15669 | 2025 | リファクタリング |
| 8 | ACE: Automated Technical Debt Remediation | 2507.03536 | 2025 | リファクタリング/技術的負債 |
| 9 | Prompt Learning for Multi-Label Code Smell Detection | 2402.10398 | 2024 | コードスメル |
| 10 | A Comprehensive Evaluation of PEFT on Code Smell Detection | 2412.13801 | 2024 | コードスメル |
| 11 | AutoCodeRover: Autonomous Program Improvement | 2404.05427 | 2024 | AST/エージェント |
| 12 | Self-Admitted Technical Debt in LLM Software | 2601.06266 | 2026 | 技術的負債 |
| 13 | HyClone: Bridging LLM Understanding and Dynamic Execution | 2508.01357 | 2025 | コードクローン |
| 14 | A Self-Improving Coding Agent | 2504.15228 | 2025 | エージェント |
| 15 | On the Use of Deep Learning Models for Semantic Clone Detection | 2412.14739 | 2024 | コードクローン |
| 16 | An Empirical Study on the Code Refactoring Capability of LLMs | 2411.02320 | 2024 | リファクタリング |
