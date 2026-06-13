# unilyze リアルタイムコード可視化 強化ロードマップ

対象: 静的HTMLビューアを lazygit 的なライブ更新画面へ進化させ、ブラウザからソースへジャンプ、difit的diff表示、コードフロー図描画を実現する機能拡張計画

生成: マルチエージェントワークフロー（研究5 + 深掘り12 + 検証12 + 統合1 + 完全性批評1、計31エージェント）。各提案は検証エージェントが実コードと突合済み（valid 38 / needs-revision 34）。verdict が needs-revision のものは検証ノートの修正方針を織り込んで着手すること。

---

## 戦略テーマ

1. ライブ更新は「常駐+SSE+差分配信」を基盤とし、保存単位で状態をスナップショット更新する: ExplorViz(VISSOFT 2021)は更新頻度が高すぎると理解を損なうとし意図的に10秒間隔を選択、Kubelka et al.(ICSE 2018)はラグ許容閾値を約500msと報告する。両者を満たすには「保存イベント駆動で即時反映、ただし全再構築でなく変更型のみの差分パッチ」が要る。実コードは `HtmlFormatter.cs:13-28` が解析JSONを `__DATA_PLACEHOLDER__` へ生埋め込みするワンショット前提なので、`fetch`+SSEのランタイム注入経路を新設する。配信はSSEを第一候補とする(単方向push=本用途に十分、HttpListenerで自前実装可能、WebSocketのフレーミング/upgradeハンドシェイク不要、EventSourceが自動再接続を標準提供)。

2. 「発見→ソースへ飛ぶ」をUXの最優先動線にする: 開発者は時間の約70%をコード理解に費やし書く時間は5%前後(Minelli et al. ICPC 2015)、Krause-Glau(VISSOFT 2023)はスタンドアロンツールへのコンテキストスイッチを最大の障壁とし可視化→ソース直接ジャンプを「code proximity」として必須要件に挙げる。Mäder & Egyad(ASE 2011)はトレーサビリティ付与で完了+24%・正解+50%を実証する。`TypeMetrics.FilePath/StartLine`(CodeHealthCalculator.cs:49-50)と `MethodMetrics.StartLine` は既にJSONに乗っているため、バックエンド改修ゼロでまずエディタ起動リンクを出せる。ブラウザ内閲覧はserve前提。

3. diffはテキスト赤緑でなく「意味的に分類された変更」として見せる: ChangePrism(VISSOFT 2025)は行単位赤緑が全変更を同等扱いし重要度を区別できない問題を指摘、GumTree(ASE 2014)はAST編集スクリプトで19.4%のケースが評価者全員に「diffより理解しやすい」と判定された。unilyzeは既に `deltaScore`(変更メソッド×high/low risk: DiffCalculator.cs:95-133,150-153)を持つため、これを行レベルdiffのハイライト色へ接続するだけで差別化できる。ただしソーステキストdiffは `--base-ref` がworktreeを即破棄する(DiffRunner.cs:281-303)ため静的HTML単体では成立せず、serve常駐の `git show/diff` 経路が前提。

4. フロー図は全描画でなく「選択スコープのオンデマンド集約」に限定する: Yoghourdjian et al.(2018)はforce-directedが低直径グラフでヘアボール化すると実証、Kesavan et al.(2020)はnode-linkが深いコールスタックでスケールせずSankey的集約が有効とし、Okoe et al.(2018)は20ノード超で隣接行列がほぼ全タスクでnode-linkを上回るとする。よって全call graphの一括描画は避け、フォーカス中の型だけをオンデマンド解析・配信し、namespace/assemblyへロールアップする。現状の依存抽出は型間 `DependencyEdge` 止まりでメソッド呼び出しエッジを持たないため、`MethodCallEdge` 抽出コレクタの新設が必須。CFGはメソッド単体に限定し、不安定な `ControlFlowGraph` 内部APIでなくSyntaxWalkerの簡易フローに留める。

5. ローカルサーバとソース配信はゼロ依存・ローカル専用・allowlist境界を最初から設計する: serve化はソース本文・エディタ起動・任意パス読み取りという新たな攻撃面を開く。BCL内蔵の `HttpListener`/`FileSystemWatcher` のみで実装し(Kestrel/ASP.NET Coreはトリム非対応・バイナリ肥大化のため不採用、AOT/ゼロセットアップ思想を維持)、127.0.0.1固定バインド・ランダムポート・起動毎トークン・Origin検証を既定にする。ソースは「解析時に確定した絶対 `filePath` のallowlist」からのみ供給し、生パスをサーバに渡さずパストラバーサルを構造的に排除する。

## クイックウィン（effort S × impact medium 以上）

- ブラウザからソースへのジャンプ: `renderTypeDetail`(main.js:400-485)に `vscode://file/{path}:{line}`/`file://` リンクを追加。データはJSON既存、C#変更ゼロ(effort S, impact high)。
- 既存ビューアのリアルタイム化: 詳細パネルとメンバー行(main.js:462-464)に既存 `filePath/startLine` からソースリンクを生成。バックエンド改修不要(effort S, impact high)。
- フロー図: 型/メソッドノードのクリックで既存 `filePath/startLine` を使い `vscode://` リンクを出す。まずパネル描画(L460付近)に1行追加(effort S, impact high)。
- 配布: ELK CDN依存(elkjs 0.9.3, MIT)を `cytoscape/dagre` と同形の `EmbeddedResource`(Unilyze.csproj:33-38)で同梱しオフライン完全自己完結化。index.html:73のunpkgタグと `importScripts` を撤廃(effort S, impact medium)。
- セキュリティ: serveはループバック専用バインド+ランダムポート+起動毎トークンを既定化。`TryOpenInBrowser`(ProgramHelpers.cs:234)へ `?token=...` を載せる(effort S, impact medium)。
- セキュリティ: `projectPath/filePath` の絶対パス露出を最小化し、`Path.GetRelativePath`(SarifFormattingHelpers.cs:230で実績)で相対パス+不透明fileIdへ正規化(effort S, impact medium)。
- ライフサイクル: `CancellationTokenSource`+`Console.CancelKeyPress`+`AppDomain.ProcessExit` でCtrl-C時にListener停止・watcher破棄・exit 0するgraceful shutdownを `McpStdioServer.Run` と共有する `ServerLifetime` ヘルパに(effort S, impact high)。
- diff表示: ソース本文表示前にXSSサニタイズ層を確立。サーバ側 `WebUtility.HtmlEncode` か `escapeHtml`(main.js:192-198)を必ず通し、innerHTML直挿入(16箇所)にソース本文を混ぜない方針を固定(effort S, impact medium)。
- UX: 変更ハイライトのトランジェント・アニメーション。`applyDelta` 時に追加/変更/削除要素へ一時クラスを付与し1.5秒でフェード。色は既存diffバケット配色(styles.css:293-301)流用(effort S, impact medium)。

## Phase 1（短期）: ライブ配信基盤とゼロ改修ソースジャンプ

ゴール: `unilyze serve` を新設し常駐HTTP+SSE+FileSystemWatcherでライブ更新の土台を立てる。同時に既存JSONだけで成立するソースジャンプ(file://リンク)を投入し、静的HTML単体動作を完全維持する。破壊的変更なし(serve非利用時は現状動作)。セキュリティ境界をこのPhaseで先に固める。

- [ ] serve/ライフサイクル: `unilyze serve -p <path>` をProgram.cs:18-45のルーティングに1行追加し `ServeRunner.Run` へ。`ValidateTopLevelCommand`(Program.cs:13)にも登録。BCL `HttpListener` のみ使用、csprojは `Microsoft.NET.Sdk` のままnet8/net10両TFM維持。
- [ ] セキュリティ: 127.0.0.1固定バインド・ポート0でOS割当・起動毎セッショントークン生成・全エンドポイントでトークン検証(未一致404)。`0.0.0.0`/`--host` 公開は明示フラグ必須。
- [ ] ライフサイクル: graceful shutdown(Ctrl-C/ProcessExit→Listener停止・watcher破棄・cancel・exit 0)を `ServerLifetime` ヘルパで `McpStdioServer.Run` と共有。
- [ ] ライブ配信: `GET /` でviewer HTML、`GET /api/snapshot.json` で解析JSON(`AnalysisJsonContext.Default` 経由、Content-Type固定)、`GET /events` でtext/event-stream。`schema` ツール(McpToolHandlers.cs:181)と同じフィールド定義に必ず一致させ `GET /api/version` で `metricsVersion/toolVersion` を公開。
- [ ] ランタイム注入: main.js:1 `const DATA = __DATA_PLACEHOLDER__;` を「置換済みならインライン、未置換(serveモード)なら `await fetch('/api/snapshot.json')`」の二系統へ分岐。serveでないファイル単体HTMLでは `EventSource` を生成せず現状動作を完全維持。
- [ ] ファイル監視: `FileSystemWatcher(filter='*.cs', IncludeSubdireoctories=true)` をChannel/タイマーへ集約し200-400msのdebounce(連続保存・エディタ一時ファイルを1回に束ねる)。`.unilyze/cache` ディレクトリは除外。debounce発火で `AnalysisPipeline.Build(..., incremental:true)`(Program.cs:140-150と同形)を再実行しSSE push。多重起動禁止のセマフォ1でコアレッシング。
- [ ] ブラウザからソースへのジャンプ: 既存 `filePath/startLine` から `vscode://file/{path}:{line}`/`file://` リンクを `renderTypeDetail`(main.js:400-485)・メンバー行・codeSmells描画(同438-443、`smell.line??type.startLine`)に追加。editorScheme(vscode/idea/file)はlocalStorage切替。バックエンド改修ゼロ。
- [ ] 配布: ELK CDN依存を `EmbeddedResource` で同梱しオフライン化(SHA256/MIT表記追加)。

## Phase 2（中期）: 差分パッチ反映・ソース配信・diff表示

ゴール: ライブ更新を全再構築から差分パッチへ昇格し視点を据え置く。serve経由のソース閲覧・エディタ起動・行レベルdiffを、allowlist境界とサニタイズ層の上に実装する。CFG/call graphの素材となるソース位置をdiffへ伝播させる。破壊的変更: serve化に合わせ生埋め込みをやめ別エンドポイント+CSP配信へ移行する(ファイル単体HTMLは従来の生埋め込みを残す)。

- [ ] ビューアのリアルタイム化: パース時1回の派生インデックス構築(tl/tm/nsInfo/els等 main.js:44-879)を `buildDerivedState(data)` 純関数へ括り出し `applyDataset(newData)` を新設。新旧typeId集合を差分しCytoscapeノードをadd/remove/update、`cy.destroy()` 全再生成を排除。初回も `applyDataset(DATA)` で通す。
- [ ] インタラクションUX: `applyDataset` 前後で `captureViewState()`/`restoreViewState()`。保存対象はpan/zoom・選択ノードid・`expanded`(main.js:986)・`searchFilters`(main.js:1690)・`diffState`(main.js:61)・詳細パネルtypeId・リストスクロール位置。増分更新時のみ `fit:false`(main.js:1268の固定fit:trueを引数化)で画面が飛ばないようにする。Parnin & Rugaber(2011)のコンテキスト再構築コスト低減として根拠あり。
- [ ] セキュリティ(前提): ライブ配信移行に合わせ生埋め込み(HtmlFormatter.cs:13-28)をやめ `/api/snapshot.json` 配信へ。HTMLレスポンスにCSP(`default-src 'none'; script-src 'self'; connect-src 'self'` 等)を付与し同梱vendorを `'self'` 化(nonce付与か外部ファイル化)。生埋め込みXSS緩和の暗黙依存をCSPで補強。
- [ ] セキュリティ(前提): ソース配信は「解析時に確定したファイル集合のallowlist」からのみ供給。絶対 `filePath` を起動時に `Path.GetFullPath` で正規化し不変Set/Dictへ格納、クライアントには `fileId` のみ公開。返却前に `StartsWith(canonicalRoot)` で再検証しシンボリックリンク脱出も拒否。生パスをサーバに渡さない。
- [ ] ブラウザ内ソース閲覧: `GET /api/source?fileId=<id>&from=<line>&to=<line>` がallowlist実体のみ返す。ハイライトはRoslyn Classifier(既存Microsoft.CodeAnalysis依存)か軽量トークナイザでCDN非依存。viewerは詳細パネル/右ペインに表示し該当行へスクロール。`EndLine` 不在のため範囲は `StartLine+LineCount`(TypeMetrics.LineCount:22)で近似。
- [ ] エディタ起動: クライアントから `vscode://` 直叩きさせず `POST /api/open?fileId=...&line=...` をサーバへ。サーバが (a)fileIdをallowlist解決 (b)設定の固定allowlist(`--editor code|cursor|...`)で `ProcessStartInfo`+`UseShellExecute=false`+引数配列渡し(GitProcess.cs:15の既存パターン)で起動。path/lineは数値・allowlist検証後のみ渡す。
- [ ] difit的diff表示: `DiffResult` にソース位置を伝播。`TypeDiff` に `FilePath/StartLine/EndLine`、`MethodDiff` に `StartLine/EndLine` を追加し `DiffCalculator.ComputeTypeDiff`(DiffCalculator.cs:175)で after側 `TypeMetrics` から埋める。行範囲導出は `SarifFormattingHelpers.BuildRegion`(同161-188)を共通ヘルパへ抽出。FilePathは `GetRelativePath` でProjectPath相対化してから載せる。
- [ ] difit的diff表示: ソーステキストdiffは `GET /api/diff?file=<rel>&base=<ref>` で `git diff base -- relPath`/`git show base:relPath` をオンデマンド実行。relPath/baseは厳格バリデーション(リポジトリ外参照・`..`・引数注入遮断)。`--base-ref` がworktreeを即破棄(DiffRunner.cs:281-303)するためserve前提と明示。
- [ ] difit的diff表示: 行レベルdiffはvendor同梱の軽量unified→side-by-side変換JSで描画(CDN非依存)。`MethodDiff.StartLine..EndLine` でハンクを絞り大ファイルでも変更近傍のみ描画。`deltaScore` のhigh/low riskで色分けし既存 `deltaSpan/diffRowClass`(main.js:116-127)の配色とトーンを揃える。ChangePrism(2025)の意味的分類方針と整合。
- [ ] ライブdiff: serveのベースライン(直近スナップショット or git HEAD)に対し再解析ごとに `TypeDiff` を算出しSSEで snapshot+diff を同時push。viewerは `dl` 索引(main.js:64-77)と `diffBucket` underlay(main.js:953-961)を再適用。ライブ差分とコミット間diffを同じtypeId体系で統一表現にする。

## Phase 3（長期）: フロー図・段階的反映・UX洗練・性能ガード

ゴール: メソッド粒度の呼び出しエッジを抽出しオンデマンドのフロー図を導入する。構文即時→semantic確定の2フェーズ反映、lazygit流キーボード操作、大規模時の集約ガードで体験を仕上げる。常駐時のメモリ・並列度ガードでrc=134(SIGABRT)再燃を防ぐ。

- [ ] フロー図: `MethodCallEdge(FromMemberId, ToMemberId, FromTypeId, ToTypeId, Kind)` 抽出コレクタを新設。`RfcCalculator.CollectInvokedSymbols` と同型で `InvocationExpressionSyntax` を `model.GetSymbolInfo` で解決し、`AnalysisPipelineSemanticPhase.Run` のdeps生成直後で呼ぶ。プロジェクト外(BCL/Unity)呼び出しは `ToTypeId=null` で集約。memberIdは `TypeId+メソッド名+パラメータ数`(既存MethodDiffと整合)。AnalysisResultに `MethodCalls` 追加。
- [ ] フロー図: viewerに第3階層ノード `m:<memberId>`(parent=`t:<typeId>`)を追加。型ノード展開時に当該型のメソッドノードと `MethodCalls` 辺を遅延生成しcytoscapeへadd。既存compound-node・ELK layered(`hierarchyHandling:INCLUDE_CHILDREN` 設定済み)を再利用。デフォルトは現状の型グラフ、ダブルクリックで「呼び出しを展開」。
- [ ] フロー図: 大規模ガード。折りたたみ時は型ペアへ集約しエッジ太さ=呼び出し本数、namespace/assemblyへmeta-edgeロールアップ(既存 `DATA.dependencies` 集約ロジックL1146-1176を流用)、抽出側でエッジ数上限を設けトリミング。Kesavan et al.(2020)/Okoe et al.(2018)のnode-linkスケール限界に対応。
- [ ] フロー図(CFG): 選択した1メソッドのみオンデマンドでフローチャート化。`if/for/while/switch/try/return` をSyntaxWalkerで簡易フロー化(不安定な `ControlFlowGraph` 内部APIは不使用)。cytoscape preset/dagreで矩形ノード+菱形分岐、LR固定。VEIL(2025)/CFGExplorer(2018)が示す通り汎用dotレイアウトは実行順を破壊するため、関数・ループ単位のサブグラフ分割+ドリルダウンに留める。
- [ ] フロー図(diff連携): `MethodDiff`(Added/Removed/Modified)を呼び出しグラフのメソッドノードへ着色オーバーレイ。`MethodCalls` の逆辺をたどり「変更メソッドを呼ぶ側」をN階層ハイライト(blast radius)。既存 `diffBucket` 着色機構(main.js:838)流用。Fregnan et al.(JSS 2023)の変更起点呼び出しグラフ有効性と整合。
- [ ] 段階的反映UX: 再解析を2フェーズ配信。保存直後にincremental(syntax)結果を即push(`phase:'syntax'`、枠色フラッシュのみ)、続けてfull/semantic完走で `phase:'final'`(CBO/DIT等のsemantic指標を含む確定スナップショット)で上書き。重いsemanticでブロックせずlazygit的即時感を再現。Tanimoto Level 3-4相当。
- [ ] インクリメンタル: 常駐watch/serveで AnalysisResult・SyntaxTree・CSharpCompilation・manifestをin-memoryウォーム保持。変更ファイルだけ再パースし `Compilation.ReplaceSyntaxTree` で差し替え。`SyntaxIncrementalState` の `[ThreadStatic]`+finally null化(SyntaxIncrementalState.cs:5, AnalysisPipeline.cs:55)はワンショット前提なので、明示的ライフサイクルを持つ状態保持クラスへ置換する(破壊的変更)。
- [ ] インクリメンタル: semantic incrementalの土台として `UseSyntaxIncrementalCache` のゲートを `RequestedLevel==Syntax` 限定から外しComplete/Fullでもmanifestを読む。content-hash一致型は `CachedEnrichmentByTypeId.Metrics` を再利用し、変更ファイル所属型+逆依存閉包のみ再enrich。manifest fingerprintに参照DLLセットのハッシュを追加し参照変化時は全破棄。レイテンシ目標 p50<1s/p95<3s(1ファイル変更・中規模)を計測ゲート化。
- [ ] インクリメンタル: 変更検出を mtime+size プレフィルタ化。manifestに(mtime, length)併記し一致はSHA256スキップ、不一致のみ確定(現状は全.csを `HashFileContent` 全文SHA256: SyntaxIncrementalCollector.cs:158)。常駐watchはFileSystemWatcher通知ファイルのみハッシュし全走査を回避。
- [ ] UX: lazygit流キーボードナビゲーション。`installViewerKeyboard`(main.js:1887-1901)を拡張しj/kで上下移動・Enterで選択・Tabでフォーカス循環・数字キーでパネル切替・gでソースジャンプ・?でキーバインド一覧。`isEditableTarget` ガード(main.js:1841-1845)で入力中は無効化。
- [ ] 性能ガード: 常駐時 `maxParallelism` 既定を `Environment.ProcessorCount`(UnilyzeConfig.cs:43-44)から下げる(max(2, cores/2))か設定必須化。再解析ごとに前回 Compilation/SemanticModelキャッシュ(SemanticEnricher.cs:53)を明示破棄し世代蓄積を防ぐ。OOM検知で並列度を自動半減するバックオフを入れる。
- [ ] テスト容易性: `combine.py` を単純連結からimport解決のES-module簡易バンドルへ拡張。純粋ロジック(`buildDerivedState`・reconcile差分判定・diff索引)をCytoscape/DOM非依存モジュールへ切り出しVitestで回帰テスト。埋め込みviewer.html出力は従来と同一形式を維持。

## リスク

1. 配信方式の選択(SSE vs WebSocket vs ポーリング)はライブ差分の粒度に縛られる。SSEは単方向push・HttpListenerで自前実装可能・EventSource自動再接続という利点で本用途に十分だが、HTTP/1.1チャンク応答を自前で正しく書く必要があり、プロキシやブラウザのバッファリングでイベントが遅延する既知の罠がある。双方向(viewer→サーバのフィルタ要求等)が将来必要になればWebSocketへの移行コストが発生する。まずSSEで投入し、双方向要件が出るまでWebSocketライブラリ(追加依存)を導入しない方針を明示する。

2. ブラウザ内ソース表示とエディタ起動はトレードオフが逆。ブラウザ内閲覧はコンテキストスイッチ最小(Krause-Glau 2023のcode proximity)だが、ハイライト品質・編集不可・大ファイル描画コストの制約がある。エディタ起動は編集まで地続きだがURIスキーム/プロセス起動という攻撃面を開く。両睨みは実装量を倍化させ、ユーザーがどちらを主に使うかは未検証。Phase 1でfile://リンク(ゼロ改修)を出して実利用の偏りを観測し、serve版の作り込み投資配分を後から決める。

3. ライブ更新の頻度設計を誤ると認知負荷で逆効果になる。ExplorViz(2021)は高頻度更新が理解を損なうとして10秒間隔を選択、Sanderson et al.(ICER 2023)はライブ表示が速すぎると「追えない」という負の報告を確認した。一方Kubelka et al.(2018)の500ms閾値を超えると体験が損なわれる。「保存イベント=スナップショット単位」を更新粒度とし、変化したメトリクスのみ強調・差分ハイライトのトランジェント表示で注意を絞る設計が必要。全件再描画や連続flashは避ける。

4. 軽量・ゼロ依存思想とフロー図の表現力がトレードオフになる。`MethodCallEdge` 抽出はsemantic解析(SemanticModel)を要し常駐ウォーム前提でも重く、全call graphは描画も解析もスケールしない(Yoghourdjian 2018, Kesavan 2020)。CFGも `ControlFlowGraph` 内部APIは不安定でSyntaxWalker近似に妥協する。フォーカス型のオンデマンド解析+namespace集約+エッジ数上限で抑えるが、「正確な全体フロー」をユーザーが期待すると乖離する。フロー図は探索補助でありソース本体への導線、と訴求点を限定する(Bouraffa 2023: 2D可視化は速度改善を保証しない)。

5. serve化はXSS・パストラバーサル・任意コマンド起動・DNSリバインディングという新規攻撃面を一度に開く。現状のHTML安全性は `System.Text.Json` 既定エンコーダの `<` エスケープと `</script` 書換え(HtmlFormatter.cs:28)への暗黙依存に留まり、ソース本文(`</script>`/HTML混入可)・diff本文を載せると破綻する。allowlistベースのソース配信・127.0.0.1固定・トークン・Origin検証・CSP多層防御・サニタイズ層を「後付けでなくPhase 1/2の前提」として組み込む。`--input` で他者作成JSONを読む経路は絶対パスでホスト名/ユーザー名を暴露するため threat-model.md に「生成JSON/HTMLは共有注意artifact」と明記する。

---

## 計画の抜け漏れ（完全性批評より）

以下は批評エージェントが指摘した、上記計画でカバーされていない論点。計画への取り込み要否はユーザー判断。

1. THIRD-PARTY-NOTICES.txt とNuGetパッケージのライセンス整合: 計画はelkjs同梱を「SHA256/MIT表記追加」とだけ書くが、`THIRD-PARTY-NOTICES.txt` への追記(現状cytoscape/dagre等7件を列挙、`Unilyze.csproj:38` で `Pack="true"` 同梱)とNuGet配布パッケージへの反映が抜けている。さらにserveで配信する `git diff`/`git show` 出力やソース本文はユーザーのプロプライエタリコードであり、これは第三者ライセンスではなく「ツールが解析対象コードを配信する」という別カテゴリの責任。`elkjs` のWeb Worker(`importScripts`)同梱は、CSP `script-src 'self'; worker-src 'self'` と整合させないと Phase 2 のCSP方針(blob worker禁止)と衝突する点も未検討。

2. ゼロセットアップ思想とpython3ビルド依存・viewerバンドルの肥大化: クイックウィン「テスト容易性」が `combine.py` をES-moduleバンドラへ拡張するとあるが、`Unilyze.csproj:53-58` の `CombineViewerTemplate` ターゲットは既に `python3` を `Exec` 起動するビルド時依存。ライブ配信でmain.jsが分割・モジュール化されると、この埋め込み単一HTML生成パイプラインが破綻する。配信時はバンドルせず個別 `EmbeddedResource` を `/static/*` で出すのか、ビルド時に1つに固めるのかの分岐が未決。AOT/トリム非対応の主張に対し、`net8.0;net9.0;net10.0` の3TFM全てで `HttpListener`/`FileSystemWatcher`/`Process.Start` の挙動差(特にLinux containerでHttpListenerのHTTP.sys非依存実装、net8とnet10のSSE挙動差)を検証する計画がない。

3. MCPサーバ(stdio常駐)とserve(HTTP常駐)の併存・責務境界: `McpStdioServer`/`McpToolHandlers.cs` は既に常駐ループを持ちエージェント連携の主経路。計画は `ServerLifetime` ヘルパを「`McpStdioServer.Run` と共有」とするが、(a)エージェントがMCP経由で解析した結果をserveのライブviewerへ反映する経路、(b)MCPの `schema` ツール定義(`McpToolHandlers.cs:181`)とserveの `/api/snapshot.json` スキーマを「一致させる」としつつ単一ソース化する仕組み、(c)MCPとserveを同時起動した際のファイル監視・再解析の二重実行/競合、が未設計。エージェント連携との整合は観点一覧に挙がるが、serveがMCPを置換するのか共存するのかの意思決定が欠落。

3. `-f json`/stdout パイプラインとCI用途の後方互換: `Program.cs:79-82,183` の `-f json`/`-o` はstdout/ファイル出力でCIパイプ前提(`unilyze -p <path> -f json`)。serveは常駐・対話前提でこのバッチ/パイプ用途と根本的にモードが異なる。`--no-open`(`Program.cs:83`)・SARIF出力(`Program.cs:186`)・終了コード契約(CIゲート)に対しserveが与える影響(serveは終了しない=CIで誤用するとハング)、`--no-open` とserveの関係(serveでもブラウザを開かないモードが要る)、ヘッドレス/SSH/コンテナ環境でブラウザ起動不可時のURL標準出力フォールバックが未定義。

4. ライブ更新・SSE・差分パッチのテスト戦略(E2E): `tests/Unilyze.Tests/Cli/CliE2eTests.cs`・`McpE2eTests.cs` はプロセス起動E2Eの実績があるが、ライブ更新は (a)FileSystemWatcherのdebounce発火、(b)SSEイベント順序・再接続、(c)差分パッチ適用後のviewer状態(`captureViewState`/`restoreViewState`)、(d)`buildDerivedState` 純関数の回帰、という非決定的・タイミング依存・ブラウザDOM依存のテストを要する。計画はVitest(JS純ロジック)に触れるのみで、SSE/watcherのC#側E2E、ヘッドレスブラウザ(Playwright等=新規依存)でのライブ反映E2E、FileSystemWatcherのOS差(macOSのkqueue遅延・Linux inotify上限・Windowsバッファ溢れ)テストが無い。

5. FileSystemWatcherの運用上の既知の罠とスケール限界: 計画は `IncludeSubdirectories=true` でリポジトリ全体を監視するが、(a)Linux `inotify` のwatch上限(`fs.inotify.max_user_watches`)を大規模リポジトリで超過、(b)エディタの保存方式(atomic-rename vs in-place)による通知種別の差で `Changed` が来ずrenameで来るケース、(c)`.git`/`bin`/`obj`/`node_modules` の除外(計画は `.unilyze/cache` のみ除外)、(d)バッファ溢れ時の `InternalBufferSize`/`Error` イベントでの全再走査フォールバック、が未設計。`*.cs` filterだけでは `.csproj`/参照DLL変更(計画自身が「参照変化時は全破棄」と認める)を捕捉できない不整合もある。

6. ポート・多重起動・プロセスライフサイクルの運用: ポート0でOS割当とするが、(a)起動したURL/ポートをユーザー/エージェントへどう通知するか(stdout? `.unilyze/serve.json`?)、(b)同一プロジェクトで複数serveインスタンスが立った場合の検出・再利用(既存サーバへブラウザを向けるか新規起動か)、(c)孤児プロセス・ポートリーク(graceful shutdown失敗時)、(d)ブラウザを閉じてもサーバが残る問題(アイドルタイムアウトでの自動終了)、が未定義。`TryOpenInBrowser`(`ProgramHelpers.cs:234`)は `open`/`xdg-open`/`UseShellExecute` で固定URLを開くだけで、トークン付きURL・ポート動的決定・起動失敗時のフォールバック表示に対応していない。

7. Windows/クロスプラットフォーム固有の差異: `editorScheme`(vscode/idea/file)と `POST /api/open` のエディタ起動は、Windowsの `vscode://` URIハンドラ登録有無・パス区切り(`\` vs `/`)・`file://` のドライブレター・WSLパス変換で割れる。`TryOpenInBrowser` はOS分岐済みだが、エディタ起動(`code --goto path:line`)のクロスプラットフォーム引数・PATH解決・存在検証は新規。`vscode://file/{path}:{line}` のURLエンコード(空白・日本語パス・`#`)も未言及で、サニタイズ方針(クイックウィンのXSS層とは別の、URI injection)が抜けている。

8. 脅威モデルの具体性 — DNSリバインディング・CSRF・ローカルマルウェアの実害境界: リスク5でDNSリバインディングに言及するが、対策の具体(Host/Originヘッダのallowlist検証、`127.0.0.1` 以外のHostを拒否)がトークン・Origin検証と統合されていない。`POST /api/open` は「ローカルの任意プロセスがブラウザ経由で被害者のエディタ・任意ファイルを開かせる」CSRF/SSRF面で、トークンがlocalStorage/URLに乗る限り同一マシンの他プロセスから窃取可能(`?token=` がブラウザ履歴・プロセス一覧・ログに残る)。`threat-model.md` を作るとは書くが、脅威の列挙(STRIDE等)・各エンドポイントの認可マトリクス・「ローカル専用だから安全」の前提が崩れるケース(共有CI runner・リモートデスクトップ・マルチユーザーホスト)の分析が無い。

9. ソース範囲推定(`EndLine` 不在)に起因する表示・diffの正確性リスク: 計画自身が `TypeMetrics` に `EndLine` が無く `StartLine+LineCount`(`TypeMetrics.LineCount`)で近似、`MethodMetrics.StartLine` のみ、と認めている。partial class・属性・複数宣言・ネスト型・トップレベルステートメント(リスク5でホスト名露出に触れるが行範囲では未考慮)で `StartLine+LineCount` が実体とズレ、ブラウザ内ソース表示・行レベルdiffハンク・`vscode://...:line` ジャンプ先が誤る。`EndLine` をメトリクス抽出時に確定させる(`CodeHealthCalculator.cs` 近傍の改修)か、近似のままUXで許容するかの意思決定が未明示。

10. アクセシビリティ・大ファイル/巨大snapshot配信の性能契約: ブラウザ内ソース閲覧(Roslyn Classifier着色)・行レベルdiff・フロー図描画で、(a)数千行ファイルや巨大snapshot.jsonのSSE全量push時の帯域・パース時間、(b)差分パッチがガベージで肥大化する世代問題、(c)キーボードナビ(lazygit流 j/k)以外のスクリーンリーダ/ARIA・色覚多様性(diffの赤緑がリスク3配色と被る=`styles.css:293-301`)、が未検討。`/api/snapshot.json` をSSEで毎回全量送るのか差分のみかの帯域契約、Content-Encoding(gzip)の有無、`AnalysisJsonContext.Default` のSTJソースジェネレータがserveの動的配信で再シリアライズコストを生む点も未定義。

11. ドキュメント・移行・設定の一貫性: `Program.cs:225-267` のUSAGEテキスト・README・`schema.txt`(`Resources/schema.txt`)・MCPツールスキーマ・SKILL.md(`Skills/quality-audit`,`refactor-loop` 等がunilyze CLIを呼ぶ)に `serve` を反映する作業が計画に無い。Phase 2「生埋め込みをやめCSP配信へ移行」は破壊的変更で、既存の静的HTML成果物をCIアーティファクトやドキュメントに貼っているユーザーへの移行ガイド・非推奨告知・`metricsVersion`/`toolVersion`(計画の `/api/version`)とスキーマ互換ポリシーの明文化が欠落。設定キー(editorScheme・port・host・token・debounce・editor allowlist)を `UnilyzeConfig`(`UnilyzeConfig.cs`)・`.unilyze` 設定ファイルにどう載せるかも未設計。

---

## 研究調査サマリ

### ソフトウェア可視化研究

主要知見:

- CodeCity 3D都市隠喩の制御実験（Wettel, Lanza, Robbes、ICSE 2011）: 41名の参加者（学術21名・産業20名）を対象にした制御実験で、CodeCityを使ったグループはEclipse+Excelの対照群と比べて正答率+24%、完了時間-12%を達成した。静的メトリクス（メソッド数→建物高さ、属性数→底面積）の3Dマッピングが有効。ただし静的情報のみで動的情報は扱えない限界がある。
  - 出典: Wettel, Lanza & Robbes / ICSE 2011 / https://wettel.github.io/download/Wettel11a-icse.pdf
  - 含意: クラス・メソッド数などの静的メトリクスをビジュアル符号化することには実証的根拠がある。unilyzeのライブ画面でも、複雑度・依存数などをノードサイズやカラーに直接マッピングすることで、俯瞰把握速度を上げられる。ただし動的実行トレース（コールフロー）を見せる場合は別の可視化レイヤーが必要。
- Code Cities vs 表形式（Galperin, Koschke, Steinbeck、VISSOFT 2022）: 20名の参加者に6タスクを実施。コードスメルの俯瞰把握では Code Cities が4タスクで有意に速く、知覚努力も3タスクで低かった。一方、詳細分析タスクでは表形式が優位。「概観は City、詳細は表」という分業が最適と結論付けられた。
  - 出典: Galperin, Koschke & Steinbeck / VISSOFT 2022 / DOI: 10.1109/VISSOFT55257.2022.00014 / https://www.semanticscholar.org/paper/Visualizing-Code-Smells:-Tables-or-Code-Cities-A-Galperin-Koschke/6e9d0f414dc26dbf343c5391731f8e578ee46546
  - 含意: unilyzeのUI設計に直接応用できる知見。グラフ/city ビューは問題箇所の特定（どのクラスが臭いか）に向き、詳細メトリクスは別ペイン（表・リスト）で補完する二層設計にすべき。lazygit的な画面分割（左=グラフ俯瞰、右=詳細）はこの知見と合致する。
- 動的トレース + City 隠喩の制御実験（Dashuber, Philippsen、Information and Software Technology 2022）: DynaCityはランタイムトレースをアーク輝度・建物輝度で可視化。30名の開発者を対象とした制御実験で、全依存関係を描画する従来手法に比べ、完了速度+5.84%・正答率+11.7%・開発者の主観的な好みも優位。マイクロサービスにおける動的依存の把握に有効。
  - 出典: Dashuber & Philippsen / Information and Software Technology Vol.150, 2022 / DOI: 10.1016/j.infsof.2022.106989 / https://www.sciencedirect.com/science/article/pii/S0950584922001227
  - 含意: コールフロー図を設計する際、全エッジを描画すると視覚的雑音が増大する。呼び出し頻度や重要度でエッジを集約・輝度/太さで符号化するアプローチが実証的に有効。unilyzeのdiff表示でも、変化量に応じたヒートマップ的なオーバーレイが有効と考えられる。
- ExplorViz ライブトレース可視化（Hasselbring, Krause, Zirkelbach、Software Impacts 2020; Krause, Hansen, Hasselbring、VISSOFT 2021）: ランタイムトレースを3D City に重ねたライブヒートマップを実装。階層化可視化（フラット表示との比較）でタスク正答率が統計的に有意に向上した。ただし処理時間差には有意差なし。産業パートナー（PPI AG, Adesso SE）との協業でも実用性を確認。
  - 出典: Hasselbring, Krause & Zirkelbach / Software Impacts Vol.6, 2020 / DOI: 10.1016/j.simpa.2020.100034; Krause, Hansen & Hasselbring / VISSOFT 2021 / arXiv:2109.14217
  - 含意: ライブ更新機能（lazygit的画面）には実証的根拠がある。階層的な抽象化（パッケージ→クラス→メソッドのドリルダウン）を提供すれば正答率が向上する。一方、速度改善効果は限定的であり「見つかる量」の改善が主効果。unilyzeのライブ更新もファイル/クラス/メソッドの3階層ドリルダウンを優先すべき。
- コードエディタ近接可視化（Krause-Glau, Hasselbring、VISSOFT 2023）: VS Code 内に動的ソフトウェアcityを埋め込む Code-Proximal アプローチを提案。スタンドアロンツールへのコンテキストスイッチが開発者の最大の障壁と特定。14名の学生（7チーム）の評価で有用性・使いやすさを確認。可視化からソースコードへの直接ジャンプ機能が「code proximity」として必須要件と定義された。
  - 出典: Krause-Glau & Hasselbring / VISSOFT 2023 / arXiv:2308.15785
  - 含意: 「ブラウザからソースへ飛べる」というunilyzeの設計要件は、この研究が実証する最重要UX要件と一致する。可視化ノードをクリックしたらエディタ（VSCode/IDE）でそのファイル・行にジャンプできる機能は必須。スタンドアロンHTMLビューアの廃止方針も、この知見で支持される。
- グラフ可視化の認知スケーラビリティ限界（Yoghourdjian et al.、arXiv survey 2018）: 実証研究のサーベイにより、force-directed レイアウトは低直径グラフで「ヘアボール」化し認知不能になることが確認された。制約ベース手法は高品質レイアウトを生成するが計算複雑度でスケールしない。「大規模」の定義はデータ複雑性・視覚複雑性・技術によって相対的であり、万能なレイアウト手法は存在しない。
  - 出典: Yoghourdjian et al. / arXiv:1809.00270 [cs.HC] / https://arxiv.org/abs/1809.00270
  - 含意: unilyzeの依存関係グラフでノード数が増えると必ずスケーラビリティ問題が発生する。対策として: (1)階層的クラスタリングによるノード削減、(2)フィルタリング（選択クラスのみ表示）、(3)大規模時はSugiyama（階層レイアウト）に自動切替、を設計段階で組み込む必要がある。force-directedレイアウト単独は大規模C#コードベースには不適。
- ノードリンクのスケーラビリティ限界とコールグラフ（Kesavan et al.、arXiv:2007.01395、2020）: node-link レイアウトは大規模コールグラフで「スケーラビリティ不足」「深いコールスタックでのインタラクティビティ欠如」が実証されている。SankeyダイアグラムやCallFlow(SuperGraph集約)が代替として有効。diff call graph（2実行間の差分）をSankeyで色符号化する手法も提案されている。
  - 出典: Kesavan, Bhatia, Bhatele et al. / arXiv:2007.01395 / 2020 / https://arxiv.org/abs/2007.01395
  - 含意: unilyzeがコールフロー図を描く際、node-linkで全メソッドを並べる設計は避けるべき。SuperGraph的な集約（ホットパスのみ抽出）かSankey的なフロー表現が大規模コードベースには向く。diffモードでは変化した呼び出しパスを赤/緑で色符号化するSankey的アプローチが実績あり。

### ライブプログラミング/即時フィードバック研究

主要知見:

- Tanimoto (1990, 2013) がライブネスの6段階階層を定義。Level 1（意味的フィードバックなし）からLevel 4（編集に対して即座かつ自動的なフィードバック）、Level 5（予測的フィードバック）、Level 6（戦略的予測）まで。Level 4が最も実用的だが計算コストが高い。この分類は現在も live programming 研究の基礎フレームワークとして広く参照されている。
  - 出典: S.L. Tanimoto / LIVE@ICSE 2013, pp.31-34 / https://liveprogramming.github.io/2013/papers/liveness.pdf
  - 含意: unilyze のライブ更新画面は Tanimoto Level 3〜4 に相当する設計を目指せばよい。つまり「編集イベントごとに自動的に解析結果が更新される」(Level 3) を基本とし、ファイル保存やウォッチャーのイベント駆動更新 (Level 4) を実装することが理論的根拠として位置づけられる。Level 5/6 の予測機能は将来拡張として明示できる。
- Rauch et al. (2019) の Babylonian-style Programming は、汎用ソースコードにライブExampleをインライン埋め込みする手法を提案。実行時の変数値の変化をコードと並置して表示するProbeを採用し、非自明なプログラムに対してもランタイム挙動の可視化が可能なことを示した。一方でシステム応答時間の測定では、プログラム規模が増すと現実的な性能問題が生じることも明らかにした。
  - 出典: Rauch, Rein, Ramson, Lincke, Hirschfeld / The Art, Science, and Engineering of Programming Vol.3 No.3 2019 / https://doi.org/10.22152/programming-journal.org/2019/3/9
  - 含意: unilyze でのコードフロー図やProbe的な表示（変数値・メトリクスをコード行に対して付与するUI）は実現可能性が実証済みの手法。ただしファイル規模が大きくなると応答時間が問題になるため、インクリメンタル解析や差分更新の設計が必須。
- Begel & Myers (2021) らによる11名のプロ開発者・28時間の観察に基づくedit-runサイクルの初の実証研究 (arXiv:2109.02682)。デバッグ時の平均サイクル長は約1分、プログラミング時は約3分。ファイルナビゲーションや参照検索が入ると5分に延伸。live programmingが想定する「短く頻繁なedit-runサイクル」は一部支持されたが、開発者の実際の行動は環境の設計仮定と必ずしも一致しなかった。
  - 出典: arXiv:2109.02682 (2021) / https://arxiv.org/abs/2109.02682
  - 含意: lazygit風のライブ更新UIでは、ナビゲーションコスト（ファイル間移動、ソースジャンプ）を最小化することが体験改善に直結する。「ブラウザからソースへ飛べる」機能は、このedit-runサイクルの中断要因を削減する具体的な介入として根拠がある。
- Parnin & Rugaber (2011) は、86名の開発者の10,000セッション分析と414名のサーベイを実施。割り込み後にプログラミング活動が1分以内に再開されるセッションは10%にすぎない。また7%のセッションしか編集前にナビゲーションを行わない。タスク再開（resumption）は頻繁かつ長引く問題であり、コンテキスト再構築のコストが大きいことが示された。
  - 出典: Parnin & Rugaber / Software Quality Journal Vol.19 No.1 pp.5-34 (2011) / https://doi.org/10.1007/s11219-010-9104-9
  - 含意: unilyze がセッションをまたいでも前回の表示状態（選択ファイル、フィルタ条件、スクロール位置）を復元する設計は、この「コンテキスト再構築コスト」の低減として理論的に正当化できる。状態の永続化はライブ可視化と同等に重要な設計要素。
- Sanderson et al. (ICER 2023) の CS1 での準実験（ライブコーディング vs 静的コード例示）では、プログラミングプロセス指標（インクリメンタル開発、デバッグ、生産性）・試験成績・課題成績のいずれにも統計的有意差なし。また、ライブコーディンググループの学生の方が「講義が速すぎる」「メモを取れない」と報告する割合が高く、負の側面が確認された。
  - 出典: Sanderson et al. / ICER 2023 (ACM International Computing Education Research) / https://dl.acm.org/doi/10.1145/3568813.3600122
  - 含意: この結果は「ライブ表示が常に有効」という前提への警告。unilyze でのライブ更新は、情報の更新速度が速すぎると認知負荷を増大させる可能性がある。差分のハイライト表示（difft的UI）や変化したメトリクスのみの強調など、注意を要する変化を絞り込む設計が重要。
- McNutt et al. (arXiv:2306.09541) による17名参加のAIコード検証 × ライブプログラミングの対照実験。ライブプログラミングはAI生成コードの検証コストを下げ、過信・過疑の双方を緩和する効果が確認された。ただし、デバッグが主体のタスクではライブプログラミンググループの方が「パフォーマンス」カテゴリの認知負荷が高くなり、LP単体ではデバッグ支援として必ずしも有効でないことが示された。
  - 出典: McNutt et al. / arXiv:2306.09541 (2023) / https://arxiv.org/abs/2306.09541
  - 含意: unilyze でのライブ可視化は「探索・確認」フェーズ（メトリクスブラウジング、問題箇所の発見）で有効だが、「修正・デバッグ」フェーズへの遷移には別途サポートが必要。「問題箇所を発見してソースに飛ぶ」というワークフローの設計が特に重要で、発見フェーズと修正フェーズを明確に接続することが肝要。
- Lienhard et al. (arXiv:2403.02428) による live programming 環境へのクロスカッティング視点（コールツリーブラウザ）統合の研究。ローカル視点（個々の変数のインラインProbe）とクロスカッティング視点（実行全体の俯瞰）を組み合わせることで、デバッグ・コード理解・ナビゲーションの3用途で開発者が有用性を認めた。また、バイトコード書き換えによるインストルメンテーションの最悪ケースのオーバーヘッドは23倍、平均10倍という性能コストも定量化された。
  - 出典: Lienhard et al. / arXiv:2403.02428 (2024) / https://arxiv.org/abs/2403.02428
  - 含意: unilyze のUI設計に直接適用可能。「ファイル単位のメトリクス詳細（ローカル視点）」と「プロジェクト全体のコードフロー図・依存グラフ（クロスカッティング視点）」の両方を提供し、互いをナビゲート可能にする設計が効果的。性能面では、リアルタイム解析より差分更新・キャッシュ戦略を優先すべきことが示唆される。

### コードナビゲーション/プログラム理解研究

主要知見:

- 開発者は作業時間の約70%をコード理解（読む・ナビゲート）に費やし、実際にコードを書く時間はわずか5%前後。Minelli et al.がEclipse IDE上で18名・740セッションのインタラクションデータを分析した結果、理解70%・UI操作14%・編集5%・ナビゲーション4%という内訳が示された。別の研究では「コードナビゲーションの機械的操作だけで35%」「情報探索に50%」という数字も報告されており、値のブレはあるが「書く時間は圧倒的に少ない」点は一致する。
  - 出典: Minelli, Mocci, Lanza / ICPC 2015 / "I Know What You Did Last Summer: An Investigation of How Developers Spend Their Time" — https://www.semanticscholar.org/paper/I-know-what-you-did-last-summer:-an-investigation-Minelli-Mocci/f8d2d4e9a5d1e7ac5614afed0f9e4f97bde1c23b
  - 含意: unilyzeのライブ画面は「コードを書く支援」ではなく「読んで理解する支援」に最適化すべき。メトリクスをクリックしてソースへ飛ぶ導線、依存関係の即時表示など、ナビゲーション削減が最大の投資対効果を生む。
- トレーサビリティ（要件↔ソースコードのリンク）を与えられた開発者は、与えられなかったグループより平均24%速くタスクを完了し、50%多くの正解を生成した。Mäder & EgyadによるASE 2011の制御実験（71名、実際のOSSプロジェクト2件、traceabilityあり/なし半々）で実証。「トレーサビリティはナビゲーション経路を根本的に変える」と結論づけられている。
  - 出典: Mäder, P. & Egyed, A. / ASE 2011 / "Do software engineers benefit from source code navigation with traceability?" / IEEE Xplore: https://ieeexplore.ieee.org/document/6100095/
  - 含意: unilyzeがメトリクス違反箇所からソースへ直接リンクする機能は、研究上も最も有効と実証された介入に相当する。クリック→ソース→IDE連携（例: vscode:// スキーム）の実装は優先度が高い。
- ライブプログラミング環境（Pharo等）の実使用を17セッション分析したKubelka et al.（ICSE 2018）は、「一部のライブネス機能は頻繁に使用され、ナビゲーションの仕方に実際の影響を与える」と報告。190名のサーベイ回答者のうち60%が経験10年超、44%が産業界出身。ただし「どのラグが許容されるか」の閾値は~500msとされ、それを超えるとユーザーが二度見・ミスを起こす。
  - 出典: Kubelka, Robbes, Bergel / ICSE 2018 / "The Road to Live Programming: Insights From the Practice" / DOI: 10.1145/3180155.3180200 / https://dl.acm.org/doi/10.1145/3180155.3180200
  - 含意: unilyzeのライブ更新画面は500ms以内のレスポンスを設計目標にすべき。lazygit的なキー操作でメトリクスをリアルタイム更新する場合、表示遅延がこの閾値を超えると体験が損なわれる。
- Sharafi et al.（IEEE TSE, 2020/2022）は36名のアイトラッキング研究で開発者の3フェーズナビゲーションモデルを提案し、「thrashing（コード内を無目的に行き来する状態）」を定量化した。開発者は初期探索・焦点絞り込み・修正作業という段階を経るが、thrashingは主に初期フェーズで発生し、関連コード箇所が明示されないことが主因。
  - 出典: Sharafi, Bertram, Flanagan, Weimer / IEEE Transactions on Software Engineering 2020 / "Eyes on Code: A Study on Developers' Code Navigation Strategies" / IEEE Xplore: https://ieeexplore.ieee.org/document/9229106/
  - 含意: unilyzeの画面設計では「最初にどのファイル・クラスを見るべきか」を示すエントリポイント（問題ホットスポット一覧、重大度ランキング）を前面に出すことで、thrashingフェーズを短縮できる。
- Bouraffa, Fuhrmann, Maalej（ICSE 2023, n=20）は、空間的コードキャンバス（タブ型でなく2D配置）が「タスク完了性能には有意な差をもたらさない」ことを示した。一方で「ナビゲーション・アノテーション・UI操作に費やす時間の配分には有意差が出た」。つまり視覚的レイアウトの変化は作業スタイルは変えるが、総合的な理解速度の向上は保証されない。
  - 出典: Bouraffa, Fuhrmann, Maalej / ICSE 2023 / "Developers' Visuo-spatial Mental Model and Program Comprehension" / IEEE Xplore: https://ieeexplore.ieee.org/document/10172613/
  - 含意: コードフロー図や依存グラフなど2D空間的視覚化は「作業スタイルを変える」が「速くなる」とは限らない。unilyzeでこれらを実装する際は性能改善の根拠として過信せず、探索性・発見性の向上を訴求点にする方が研究知見と整合する。
- ソフトウェア可視化ツールの系統的文献レビュー（ScienceDirect 2018）によると、評価研究の70%は学生・研究者のみを対象とし、実際の開発者を含む産業ケーススタディは7%に過ぎない。ツール不採用の主因として「使い方の学習コスト」「認知モデルとのミスマッチ」「スケーラビリティ」が挙げられており、多くの可視化ツールが研究では有効でも実務で使われない現実が記録されている。
  - 出典: Systematic Literature Review of Software Visualization Evaluation / ScienceDirect 2018 / https://www.sciencedirect.com/science/article/abs/pii/S0164121218301237
  - 含意: unilyzeを「研究室では有効だが誰も使わないツール」にしないために、導入コストゼロ（CLI一発実行）・既存IDEとの統合（vscode リンク）・段階的採用（まずHTMLビューア、次にライブ）という設計方針は研究知見と合致している。
- ChangePrism（VISSOFT 2025, Chen, Lanza, Hayashi）は、通常のテキストdiffに加えてリファクタリング（青）とマイクロチェンジ（紫）を自動分類・色分け表示するツール。既存ツール（RefactoringMiner, RAID）はコミット全体の概観を欠くという批判のもと、General/CommitInsight/CodeDetailの3ビュー構成を採用。マイクロチェンジの可視化は本ツールが初とされる。
  - 出典: Chen, Lanza, Hayashi / VISSOFT 2025 / "ChangePrism: Visualizing the Essence of Code Changes" / arXiv:2508.12649 / https://arxiv.org/abs/2508.12649
  - 含意: unilyzeのdiff表示設計においてリファクタリング vs 実質的変更 vs マイクロチェンジを視覚的に分離することは最新研究とも一致する方向性。C#向けにRoslynのSyntaxTree差分を利用してセマンティックなdiff分類を実装することで差別化できる。

### 差分・変更可視化研究

主要知見:

- 【有効性実証】GumTree (Falleri et al., ASE 2014) は AST レベルの edit script (INS/DEL/UPD/MOV 4操作) を生成し、137/144 ファイルペア (95.1%) で correct、28/144 (19.4%) で評価者全員が「diff より変更を理解しやすい」と判定。平均実行時間は Jenkins で 20ms、jQuery で 74ms。テキスト diff が構文に無関係な行単位マッチをしてしまう欠点を根本的に解消した。一方 GumTree は大規模 AST でスケールしにくいという既知の限界があり、C# のある研究では 86 ペア中 27% が非最適マッチングと報告されている。
  - 出典: Falleri, Morandat, Blanc, Martinez, Monperrus / ASE 2014 / DOI:10.1145/2642937.2642982 / https://hal.science/hal-04855170v1/document
  - 含意: unilyze の diff 表示を行単位から AST 単位へ昇格させれば、リネーム・メソッド移動・引数追加などを正確に可視化できる。C# は Roslyn で AST が取得可能なため GumTree 相当のアルゴリズムを実装できる。ただし大規模ファイルでは近似ヒューリスティックへのフォールバックが必要。
- 【有効性実証】Bacchelli & Bird (ICSE 2013) は Microsoft の開発者を対象にインタビュー・観察・サーベイを実施し、「code change understanding がコードレビューの最大の課題として最も明確に浮かび上がった」と報告。開発者は変更理解のために多様な手段を用いるが、現行ツールはそのニーズの大部分を満たしていないと結論付けた。欠陥発見より知識共有・チーム認識向上が実際の主要利益だった。
  - 出典: Bacchelli, Bird / ICSE 2013 / DOI:10.1109/ICSE.2013.6606617 / https://2013.icse-conferences.org/content/expectations-outcomes-and-challenges-modern-code-review.html
  - 含意: ライブ可視化機能は「変更の理由 (rationale)」と「変更の影響範囲」を主軸に設計すべき。diff 行数を減らすだけでなく、変更の文脈 (なぜ変えたか、どこに影響するか) をワンクリックで確認できる動線が優先度高い。
- 【有効性実証・現場調査】Tao, Dang, Xie, Zhang, Kim (FSE 2012) が Microsoft の大規模定量+定性サーベイを実施。43% 以上のエンジニアが「コード変更の理解」をデイリーに行い 36% はそれ以上の頻度。現行ツールには「変更の品質・理解・分解を評価する適切な支援がない」と結論し、rationale (変更の理由) が最も重要な情報ニーズと判明。エンジニアは過去データより自身のコード知識に頼る傾向があった。
  - 出典: Tao, Dang, Xie, Zhang, Kim / FSE 2012 / DOI:10.1145/2393596.2393656 / https://taoxie.cs.illinois.edu/publications/fse12-study.pdf
  - 含意: unilyze のライブビューは diff だけでなく、git コミットメッセージ・PR 説明・影響ファイル一覧をその場で表示する「rationale パネル」を設けると実務ニーズに直結する。ソース・ジャンプ機能はエンジニアの「自分のコード知識」活用を支援する。
- 【有効性実証】Fregnan, Fröhlich, Spadini, Bacchelli (JSS 2023) は ReviewVis を開発し、コードレビュー対象のクラス・メソッドを依存グラフとして可視化。社内スタディ (9 名プロ開発者) + オンライン調査 (37名、次いで 31名) の 2 段階評価で、グラフベース可視化がレビュワーの変更セット理解を支援するという正の結果を得た。グラフ上のノードはクラス/メソッド、エッジはメソッド呼び出し依存を表す。
  - 出典: Fregnan, Fröhlich, Spadini, Bacchelli / Journal of Systems and Software Vol.195, 2023 / DOI:10.1016/j.jss.2022.111506 / https://www.zora.uzh.ch/entities/publication/7e55e778-46a1-4a5c-8aca-b776ce1523dd
  - 含意: unilyze の「コードフロー図」機能は単なるクラス図ではなく、変更されたメソッドを起点にした呼び出しグラフ (変更ノードをハイライト) として設計すると、先行研究の有効性と整合する。静的解析で依存関係を抽出し、変更箇所から影響波及を視覚化する設計が有望。
- 【有効性実証・限定的】Yoon & Myers (VL/HCC 2013) の AZURITE は Eclipse プラグインとして細粒度コード変更履歴をタイムライン + コード差分ビューで可視化。2名へのインタビューベースの予備的研究で「タイムラインは編集中ファイルの把握に有用」「コード差分ビューは通常の undo より undo 結果を事前確認できて優れる」という正のフィードバックを得た。ただし対象者 n=2 であり統計的な有効性は未実証。
  - 出典: Yoon, Myers, Koo / VL/HCC 2013, pp.119-126 / https://www.cs.cmu.edu/~NatProg/papers/P1_PP20_Yoon%20Azurite%20VLHCC13.pdf
  - 含意: lazygit 的なライブタイムライン表示は、スクロールで時系列を追う UX が開発者に直感的に受け入れられることを示唆する。ただし n=2 の予備研究であるため、設計根拠としては補足的に用いるにとどめ、独自のユーザーテストで検証することが望ましい。
- 【限定的/否定的知見】Kuhn, Erni, Nierstrasz (SOFTVIS 2010) は空間的ソフトウェアマップを IDE に組み込む探索的研究を実施 (n=複数、90分タスク、think-aloud)。結果は mixed: 検索結果・コールグラフの確認には有用だったが、ベースレイアウトが開発者のメンタルモデルと合わず「混乱をきたした」。開発者は north/south/east/west の意味的解釈を利用した軸に沿ったナビゲーションを行わなかった。スタンドアロンの SV ツールが IDE 統合されていないため日常的利用が進まないことも指摘。
  - 出典: Kuhn, Erni, Nierstrasz / SOFTVIS 2010 / arXiv:1007.4303 / https://arxiv.org/abs/1007.4303
  - 含意: unilyze の可視化レイアウトはユーザーが既に持つコード構造のメンタルモデル (ディレクトリ構造・名前空間階層など) に準拠すべき。独自の空間レイアウトを押しつけると混乱を招く。ソースへのジャンプ (code proximity) を最優先し、可視化はあくまでコード本体へのナビゲーション補助として設計する。
- 【実証済み手法・2025最新】Chen, Lanza, Hayashi (VISSOFT 2025) の ChangePrism は、行単位の赤緑ハイライトが「すべての変更を同等に扱い、リファクタリング等の意味的重要度を区別できない」「複数ファイルにまたがる変更の俯瞰が困難」という問題を指摘。RefactoringMiner でリファクタリングを抽出し、micro-change detector と組み合わせてレイヤー別カラー表示 (緑=追加/赤=削除/黄=変更/青=リファクタリング/紫=micro-change) を提案。ただし定量的ユーザー評価は未実施でケーススタディのみ。
  - 出典: Chen, Lanza, Hayashi / VISSOFT 2025 / arXiv:2508.12649 / https://arxiv.org/abs/2508.12649
  - 含意: unilyze の diff ビューに「変更タイプ別の意味的ラベリング」を導入すると差別化できる。C# ではリファクタリング種別 (Extract Method, Rename 等) を Roslyn で検出し、行変更と区別して表示することで、レビュワーが変更の重要度を素早く把握できるようになる。ただし定量評価が伴っていないため、設計仮説として位置付けた上でユーザー検証が必要。

### 制御フロー/呼び出しグラフ可視化研究

主要知見:

- VEIL (2025): dominator analysis を利用したドメイン特化レイアウトアルゴリズム。実世界CFGで汎用グラフ描画アルゴリズムと比較してレイアウト品質と可読性を定量的に改善。汎用アルゴリズムは「実行順序を破壊し後の命令を先に配置する」問題を持つと実証。ユーザースタディは今後の課題として残る（定量評価のみ実施）。有効性: 実証済み。
  - 出典: Schaad, Ben-Nun, Hoefler / arXiv:2511.05066 / 2025 — https://arxiv.org/abs/2511.05066
  - 含意: unilyzeのCFG表示にGraphvizデフォルトレイアウト（dot）をそのまま使うと実行フローと乖離した配置になる可能性が高い。dominator tree や loop structure を考慮したレイアウトパスを別途実装するか、CFGConf/CFGExplorer的な domain-aware レイアウト設定を採用すべき。
- CFGExplorer (2018): ループ構造を考慮したドメイン特化グラフ修正を加えた節点-リンク図レイアウトを開発。コンパイラ研究者との1年間の観察とインタビューに基づき、CFGとトレースの連動ナビゲーションを設計。後続研究CCNavではサブグラフ分割で大規模CFGを管理。有効性: 実践ユーザーとの長期デザインスタディで肯定的評価。
  - 出典: Devkota, Isaacs / Computer Graphics Forum (EuroVis 2018), DOI:10.1111/cgf.13433 — https://onlinelibrary.wiley.com/doi/abs/10.1111/cgf.13433
  - 含意: 大規模CFGを1枚のグラフで表示しようとするとスケールしない。関数・ループ単位のサブグラフ分割＋ドリルダウンUI（lazygit的なペイン切り替え）が有効。unilyzeの「関数を選んでそのCFGだけ表示」という設計方針を裏付ける。
- CFGConf (2021/2022): CFG専門家（コンパイラ・セキュリティ分析者）が既存の汎用グラフ描画では「ドメイン固有構造とタスクを十分に反映できない」と報告。JSONインターフェースで domain-aware レイアウトを宣言的に指定できるライブラリを開発。ユーザー研究とケーススタディで専門家からの高評価を確認。有効性: 実証済み。
  - 出典: Devkota, LeGendre, Kunen, Aschwanden, Isaacs / arXiv:2108.03047 / 2022 (IEEE VIS) — https://arxiv.org/abs/2108.03047
  - 含意: unilyzeのCFG可視化設計では「何を見せたいか（ループ構造か、例外パスか、支配関係か）」をタスク単位で分けて考える必要がある。汎用レイアウトエンジン1本で全タスクをカバーしようとしても失敗する。設定可能なレイアウト仕様（JSON等）を将来の拡張ポイントとして残す設計が合理的。
- コールグラフの比較可視化 (2020, IEEE VIS): Sankey図とボックスプロットを組み合わせたensemble-Sankey表現を提案。複数実行パラメータのコールグラフを並べてペアワイズ比較すると「認知負荷が高く明らかなスケーラビリティの課題がある」とドメイン専門家が報告。node-link レイアウトは深いコールスタックとユーザー操作性で限界がある。有効性: node-linkの限界を実証、Sankey的アプローチに代替可能性あり。
  - 出典: Kesavan, Bhatia, Bhatele, Gamblin, Bremer, Ma / arXiv:2007.01395 / IEEE VIS 2020 — https://arxiv.org/abs/2007.01395
  - 含意: unilyzeのコールグラフ表示では深いスタックを持つグラフに対してnode-linkを使い続けることのコストが高い。diff表示（変更前後の呼び出し構造比較）にはサンキー的なフロー表現が有効な代替手段となりうる。
- ノードリンク vs 隣接行列の比較ユーザースタディ (2018, TVCG): 大規模クラウドソーシング研究（約800名）。グラフサイズが大きく密度が高いほど隣接行列がnode-linkを上回る。小規模・疎グラフかつパス探索タスクではnode-linkが有利。ノードリンクは「記憶しやすさ」と「連結性タスク」で優位。隣接行列は「クラスタ検出」と「多属性タスク」で優位。20ノード以上になると隣接行列がほとんどのタスクでnode-linkを上回るという先行研究(Ghoniem et al.)を大規模データで再確認。
  - 出典: Okoe, Jianu, Kobourov / IEEE TVCG 25(10) 2018, DOI:10.1109/TVCG.2018.2865940 — https://pubmed.ncbi.nlm.nih.gov/30130228/
  - 含意: unilyzeが扱う実際のC#コードのコールグラフは数十〜数百ノードが現実的。この規模ではnode-linkはクラスタ・密度の把握が困難になる。「全体俯瞰（密度・モジュール境界の把握）」と「個別パス追跡」を切り替えられるハイブリッドUIが合理的。初期ビューは隣接行列またはSankeyで、詳細はnode-linkに切り替える設計を検討すべき。
- Path Based Framework vs Sugiyamaフレームワーク (2022, arXiv): ユーザースタディでPBFはSugiyama比較において「明確性・可読性・使いやすさ」で好評。エッジバンドリングと高さ圧縮により描画サイズを削減。特に「ユーザー定義パスが重要な用途」で有効。Sugiyamaは標準的な上-下フロー（下向きエッジ最大化）で一般的可読性は高いが、特定パスのハイライトが弱い。有効性: パス強調用途に限り有効性実証、汎用Sugiyamaより優位。
  - 出典: Lionakis, Kritikakis, Tollis / arXiv:2209.04522 / 2022 — https://arxiv.org/abs/2209.04522
  - 含意: unilyzeで「特定のメソッドからの到達経路」や「影響範囲パス」をハイライト表示する機能を実装する場合、標準Sugiyamaレイアウトは効果が薄い。パス単位でエッジを束ねて強調する手法が有効。lazygit的な「選択した要素に関連するパスだけ色付けして追える」インタラクションと組み合わせると効果的。
- ExplorViz ライブソフトウェア都市可視化 (2021, VISSOFT): ライブトレース解析に基づきソフトウェアシティをリアルタイム更新。更新ループのデフォルトは10秒とし、「視覚化要素の変化が速すぎるとユーザーの理解を損なう」という知見から意図的に低頻度更新を選択。データを構造情報と動的情報に分割してパイプライン負荷を軽減。高負荷時の並列ユーザー対応にも有効。有効性: 実証済み（ライブ更新の遅延設計が理解促進に寄与）。
  - 出典: Krause, Hansen, Hasselbring / VISSOFT 2021, arXiv:2109.14217 — https://arxiv.org/abs/2109.14217
  - 含意: unilyzeのライブ更新設計では「できるだけ高頻度更新」は誤った方針。変化が頻繁すぎるとユーザーが追えなくなる。静的解析結果（構造情報）は即時反映、動的メトリクス（複雑度変化、diff結果）は意図的に遅延・バッチ更新する設計が研究で支持されている。「コードを保存したら解析結果がスナップショットとして更新される」という単位が適切。

---

## 観点別詳細

### ライブ更新アーキテクチャ

現状: unilyzeは完全なワンショットCLIで、ライブ更新に必要なネットワーク基盤が一切存在しない。`rg 'HttpListener|FileSystemWatcher|WebSocket|TcpListener|AspNetCore'` は src/Unilyze 全体で0ヒット。配信経路は「HTMLファイル生成→ブラウザ起動」のみ(Program.cs:165-180、ProgramHelpers.TryOpenInBrowser=Process.Start("open"/"xdg-open"))。

データ供給はビルド時インライン埋め込み。viewerは `const DATA = __DATA_PLACEHOLDER__;`(main.js:1)、`const DIFF = __DIFF_DATA_PLACEHOLDER__;`(main.js:57)として解析JSONをJSリテラルに静的展開する。HtmlFormatter.Render(HtmlFormatter.cs:13-25)が `__DATA_PLACEHOLDER__`/`__DIFF_DATA_PLACEHOLDER__`/`__TITLE__`/`__VENDOR_SCRIPTS__` を文字列Replaceで差し込む。Cytoscape/dagre/cytoscape-dagreはEmbeddedResourceとして `<script>`インライン同梱(HtmlTemplate.cs:13-36、Unilyze.csproj EmbeddedResource行)。ELKのみCDN(index.html:73)。viewer内にfetch/EventSource/XHRは皆無で、データを差し替えるランタイム経路がない。

再解析の差分基盤は部分的に存在: `--incremental` は per-fileコンテンツハッシュキャッシュ(SyntaxCacheStore: .unilyze/cache/syntax/v1/manifest.json、SyntaxIncrementalCollector.cs:36-49でhash一致ならcache hitしre-parseをスキップ)。ただし `UseSyntaxIncrementalCache => Incremental && RequestedLevel==Syntax`(AnalysisBuildOptions.cs:39-40)で構文レベル限定、semantic経路は無効(AnalysisPipeline.cs:42-46で警告して無効化)。partial/interface変更の波及無効化ロジックあり(SyntaxIncrementalCollector.cs:192-241)。

ソースジャンプ素材は既にモデルにある: TypeMetrics.FilePath/StartLine(CodeHealthCalculator.cs:49-50)、MethodMetrics.StartLine(同12)がJSONにシリアライズ済み。ただしEndLineはメトリクスに永続化されていない(計算はTypeInfo.cs:218等でEndLinePositionから行数だけ算出し破棄)。

長時間プロセスの先例はMCP stdioサーバのみ: McpStdioServer.Run()が `while((line=reader.ReadLine()) is not null)` でstdinをブロッキング読みするJSON-RPCループ(McpStdioServer.cs:8-16)。McpAnalysisCacheが解析結果をメモリ保持(BuildKeyでキー化、McpAnalysisCache.cs:5-27)。これは「常駐プロセス内で解析を繰り返しメモリ再利用する」パターンの実証だが、HTTP/WS配信は持たない。

差分オーバーレイ配信は確立済み: DiffRunner.WriteHtmlOutput が HtmlFormatter.GenerateWithDiff(afterJson, diffJson, projectPath) で同一viewerにdelta注入(DiffRunner.cs:522-535)。viewerはdl[](main.js:60-77)でTypeDiffをバケット別索引しdiff-summaryを描画。つまり「diffデータを流し込めばviewerが差分表示する」入口は既にあり、これをライブ化で再利用できる。

#### unilyze serve: 常駐HTTPサーバ + SSEでライブ配信(HttpListenerベース、依存ゼロ)

- verdict: valid
- effort: L / impact: high
- what: 新サブコマンド `unilyze serve -p <path>` を追加。Program.cs:18-45のルーティングに `serve` を1行追加し ServeRunner.Run へ。実装はBCL内蔵の System.Net.HttpListener のみ(NuGet/ASP.NET Core不要、AOT/ゼロセットアップ思想を維持)。エンドポイント: GET / で現行viewer HTMLをそのまま返す(HtmlFormatter.Generate再利用、初回__DATA_PLACEHOLDER__は最新スナップショット)、GET /api/snapshot.json で解析JSON、GET /events で text/event-stream(SSE)。再解析完了時にSSEで `event: snapshot
data: {...}` をpushし、viewer側は新JSONでグラフを再構築する。配信方式はSSEを第一候補とする(単方向push=本用途に十分、HttpListenerでHTTP/1.1チャンク応答として自前実装可能、WebSocketのフレーミング実装やupgradeハンドシェイクが不要、ブラウザEventSourceが自動再接続を標準提供)。
- why: ユーザー要件の核「lazygit的に変更が即時に画面へ反映される」を満たす最小基盤。WebSocketは双方向だが本UIはサーバ→ブラウザの単方向更新が主で、HttpListenerにWS実装は無く自前フレーミングが重い。long-pollはタイムアウト管理と再リクエスト制御が煩雑。SSEはHttpListenerのレスポンスストリームにdataを書き続けるだけで成立し、依存ゼロ・実装最小。既存のHTML生成資産(HtmlFormatter/HtmlTemplate/同梱vendor)をそのまま配信できる。
- evidence: Program.cs:18-45(コマンドルーティング), HtmlFormatter.cs:7-25(HTML生成再利用点), index.html:73(viewerは既にCDN script取得=同一オリジン配信に違和感なし), src/Unilyze全体でHttpListener/WebSocket=0ヒット(新規導入が必要)
- 検証ノート: ルーティング前提は正確。Program.cs:18-45 が `if (args.Length>=1 && args[0]==...)` の素朴な分岐列で、ここに `serve` を1行足す設計は無理がない(serve/watch は未存在)。HtmlFormatter.Generate(HtmlFormatter.cs:7-8)再利用も成立、index.html:72 __VENDOR_SCRIPTS__ が同一HTMLに同梱されるので GET / での再利用は妥当。`rg HttpListener|WebSocket|TcpListener|AspNetCore` は src/ で0ヒットを当方でも再現、HttpListener新規導入の前提は事実。SSE採用判断(単方向push/HttpListenerにWS実装なし/EventSource自動再接続)も技術的に妥当。ただし2点補足が要る: (1)『依存ゼロ・オフライン』は厳密には不正確。viewerは index.html:73 で `https://unpkg.com/elkjs@0.9.3/lib/elk.bundled.js` を、さらに main.js:1200-1201 で elk-worker.min.js を実行時にCDNから取得する。serveのlocalhostオリジン配下でも外部fetchは発生し、完全オフラインではない(Cytoscape/dagreのみ同梱)。(2)HtmlFormatter.cs:17-18 で既に `</script` を明示エスケープしており、SSEで流すJSONも同じエスケープを通す必要がある(同一viewerが <script> 内でJSON束縛するため)。これらは実装上の留意点でserve方針自体は有効。

#### viewerにランタイムデータ注入経路を新設(インライン埋め込みと両立)

- verdict: valid
- effort: M / impact: high
- what: main.js:1 `const DATA = __DATA_PLACEHOLDER__;` を `let DATA = (typeof __INLINE_DATA__!=='undefined') ? __INLINE_DATA__ : null;` 相当に分離し、(a)従来のファイル出力モードはビルド時インライン、(b)serveモードは起動時に `await fetch('/api/snapshot.json')` で取得、の二系統を許容する。グラフ構築(asm/tl/tm索引: main.js:44-54)を `applySnapshot(data)` 関数に切り出し、初回ロードとSSE受信の双方から呼べるようにする。SSEは `new EventSource('/events')` で接続し `onmessage` で applySnapshot を再実行、自動再接続はブラウザ任せ。serveでないファイル単体HTMLでは EventSource を生成せず現状動作を完全維持。
- why: 現状viewerはDATAを静的リテラルとして1度だけ束縛し再代入経路がない(main.js:1,44-54)。ライブ反映には「同じJSON形状を後から差し替えグラフを作り直す」関数化が必須。インライン版を残すことで `-o file.html` の単体配布・オフライン・diff overlay(GenerateWithDiff)の既存挙動を壊さない。
- evidence: main.js:1(静的束縛), main.js:44-54(索引構築が初期化時1回), main.js:57-77(DIFFも同様に静的=同じ再構築課題), HtmlFormatter.cs:20-24(Replace方式の埋め込み)
- 検証ノート: 前提は実コードと一致。main.js:1 は `const DATA = __DATA_PLACEHOLDER__;`(再代入不可のconst束縛)で確認、`let`化+applySnapshot切り出しは必須という指摘は正しい。索引構築は main.js:44(asm)、:50(tl)、:54(tm)で初期化時1回実行され関数化対象として妥当。DIFFも main.js:57 `const DIFF = __DIFF_DATA_PLACEHOLDER__;` で同じ静的束縛(:64-77でバケット索引)。埋め込みは HtmlFormatter.cs:20-24 の文字列Replace方式で確認。ビルドは csproj:52-55 で combine.py が index.html+styles.css+main.js を viewer.html に結合しEmbeddedResource化(csproj:31)するので、main.js:1への変更はビルドを通って反映される。インライン版を残せば `-o file.html`/GenerateWithDiff の既存挙動を壊さない点も妥当。補足: 提案1は初期データを `__DATA_PLACEHOLDER__`の置換済みスナップショットで賄うと書き、本提案は `fetch('/api/snapshot.json')`で取ると書く。両者は択一的で矛盾はしないが、serve時にどちらを初期ロード経路にするか実装で一本化すべき(二重ロードを避ける)。

#### FileSystemWatcher + debounce + 構文incremental再解析でライブ差分を生成

- verdict: needs-revision
- effort: M / impact: high
- what: ServeRunner内でプロジェクトルートを `FileSystemWatcher(filter='*.cs', IncludeSubdirectories=true)` 監視。Changed/Created/Deleted/Renamedを単一の `Channel`/タイマーに集約し200-400msのdebounce(連続保存やエディタの一時ファイル書き込みを1回の再解析に束ねる)。debounce発火で既存 `AnalysisPipeline.Build(..., incremental:true)` を再実行(Program.cs:140-150と同じ呼び出し)。SyntaxCacheStoreのper-fileハッシュキャッシュ(SyntaxIncrementalCollector.cs:36-49)が変更ファイルのみ再パースするため2回目以降が高速。前回スナップショットを保持し DiffCalculator.Compare(before, after)(DiffRunner.cs:370参照)で差分を算出、SSEで snapshot+diff を同時push。これによりlazygit的な『変更箇所だけ光る』表現の素材(TypeDiff バケット)を毎回供給できる。
- why: 即時反映の心臓部。FileSystemWatcherは src 内に前例ゼロ(新規)。debounceなしだと1保存で複数イベント→多重解析になる。incrementalキャッシュは既に変更ファイル限定再パースを実装済み(SyntaxIncrementalCollector.cs:38-48)なので、ライブ用途のレイテンシ要件にそのまま効く。差分はDiffCalculatorが既にあるため再利用でviewerの既存diffオーバーレイ(main.js:60-77)を点灯できる。
- evidence: AnalysisPipeline.cs:31-46(incrementalビルド入口), SyntaxIncrementalCollector.cs:36-49(cache hitでre-parse省略), AnalysisBuildOptions.cs:39-40(syntax限定の制約), DiffRunner.cs:370(DiffCalculator.Compare), src全体でFileSystemWatcher=0ヒット
- 検証ノート: 大半の前提は正確だが、incrementalの効果範囲に重大な制約がある。SyntaxIncrementalCollector.cs:36-49 で content hash 一致時に re-parse をスキップしcache hitする実装は確認、変更ファイル限定再パースの主張は正しい。AnalysisPipeline.cs:42-46 と AnalysisBuildOptions.cs:39-40 で `UseSyntaxIncrementalCache => Incremental && RequestedLevel==AnalysisLevel.Syntax` を確認。DiffCalculator.Compare は DiffRunner.cs:370 で確認、viewer diffオーバーレイ(main.js:64-77)再利用も妥当。問題点: 提案文は『既存 AnalysisPipeline.Build(..., incremental:true) を Program.cs:140-150 と同じ呼び出しで再実行』と書くが、Program.cs の analyze 経路は requestedLevel を pin していない(--level未指定なら requestedLevel=null)。incremental:true でも RequestedLevel!=Syntax のため AnalysisPipeline.cs:42-46 で警告して incremental が即無効化され、毎回フル解析になる。つまり『2回目以降が高速』はSyntaxレベルにpinした場合のみ成立し、デフォルトのフルメトリクス表示とは両立しない。
- 修正案: ServeRunner では再解析を AnalysisLevel.Syntax に明示pinした軽量パスと、フル解析パスを分けて呼ぶ。debounce発火時はまず requestedLevel=Syntax + incremental=true で高速差分を出し(これが本当にcacheで速くなる唯一の経路)、フルメトリクスは別途 incremental=false のフル解析で供給する(提案5の2フェーズ構成と統合すべき)。『incremental:true を渡せば速くなる』という単純化は誤りで、レベルpinが前提条件であることを設計に明記する。

#### ソースジャンプAPI: GET /api/source とエディタ起動の両睨み

- verdict: needs-revision
- effort: M / impact: high
- what: serveモードに2系統のソース到達手段を追加。(1)ブラウザ内閲覧: `GET /api/source?file=<rel>&from=<line>&to=<line>` がプロジェクトルート配下の.csを返す(必ず Path.GetFullPath して projectRoot 配下を検証=path traversal遮断)。viewerのノード/メソッド行クリックでモーダルにソース断片を表示し該当行をハイライト。(2)エディタ起動: `POST /api/open` で `code --goto file:line`(または `$EDITOR`)を起動。TypeMetrics.FilePath/StartLine(CodeHealthCalculator.cs:49-50)とMethodMetrics.StartLine(同12)が既にJSONに乗っているのでviewerは座標を持っている。EndLineが無いため範囲ハイライトは StartLine+LineCount(TypeMetrics.LineCount:22)で近似する。
- why: 要件2「ブラウザからソースへ飛べる(閲覧/エディタ起動の両睨み)」を直接満たす。座標データは既存(FilePath/StartLine)で追加解析不要。serveが常駐ローカルプロセスなのでファイル読み出し・エディタ起動が自然に実現でき、ファイル単体HTMLでは不可能だった機能をserveモード限定で解禁できる。
- evidence: CodeHealthCalculator.cs:49-50(TypeMetrics.FilePath/StartLine), CodeHealthCalculator.cs:12(MethodMetrics.StartLine), CodeHealthCalculator.cs:22(LineCount=範囲近似), ProgramHelpers TryOpenInBrowser:234-244(Process.Startでの外部起動パターンが既にある=エディタ起動に流用), 脅威モデル上 docs/threat-model.md ありローカルファイル配信は要検証
- 検証ノート: 座標データの所在とフォールバック近似は概ね正確だが、『viewerは既に座標を持っている』が事実誤認。TypeMetrics.FilePath/StartLine は CodeHealthCalculator.cs:49-50 で確認、MethodMetrics.StartLine は同:12 で確認、JSONは camelCase で `filePath`/`startLine` として出る(AnalysisResult.cs:48)。EndLineが解析JSONに無いのも事実(persistされるのは StartLine+LineCount のみ。TypeInfo.cs:218 で EndLinePosition から行数だけ算出し破棄。EndLine実体は History/HotspotAnalyzer.cs:30 のhotspot用record限定)。StartLine+LineCount-1 近似は既に SarifFormattingHelpers.cs:176-181 で実装済みなので踏襲可能。Process.Start 起動パターンは ProgramHelpers.cs:234-244 で確認、流用は妥当。誤りの核心: 現行 main.js は `filePath`/`startLine` をどこからも参照していない(rg で main.js 内0ヒット)。JSONには載っているがviewerが読んでおらず、『座標を持っている=すぐ飛べる』はノードクリック→座標解決のJS新規実装を要する。またnullable+WhenWritingNull(AnalysisResult.cs:49)のため、StartLine/FilePathが未設定の型ではキーごと欠落し、クリックジャンプ不能なノードが出る。
- 修正案: (1)viewerに『JSONの filePath/startLine をノード/メソッド行に紐付けるクリックハンドラ』を新規実装する前提を明記(既存コードに到達導線は無い)。(2)filePath/startLine が null のノードはジャンプ不可としてUIで無効化する(WhenWritingNullで欠落するため undefined ガードが必須)。(3)path traversal対策は提案通り Path.GetFullPath で projectRoot 配下を検証で妥当だが、docs/threat-model.md を参照しローカルバインド(127.0.0.1限定)・任意ファイル読み出し制限・POST /api/openの任意コマンド実行リスク(EDITOR/codeのみ許可リスト化)を脅威モデルに追記すること。

#### 段階的反映UX: 構文レベル即時pre-flash → semantic完了で確定更新

- verdict: valid
- effort: L / impact: medium
- what: 再解析を2フェーズで配信する。保存直後は incremental(syntax)結果を即SSE push して『変更されたノードを即ハイライト』(レイテンシ最小、incrementalキャッシュが効く経路)。続けてバックグラウンドで full/semantic解析を完走し、couplingやCBO/DIT等のsemantic指標を含む確定スナップショットを2回目のSSEで上書きする。viewer側は snapshot に `phase:'syntax'|'final'` を持たせ、syntax段は枠色フラッシュのみ・final段でメトリクス数値を更新する。lazygitの『瞬時に状態が動く』感覚を、重いsemantic解析でブロックせずに再現する。
- why: semantic解析は重く(incrementalはsyntax限定: AnalysisBuildOptions.cs:39-40)、毎保存でfullを待つと即時性が損なわれる。構文段の高速pathで『光らせる』、確定段で『正しい数値にする』の2段にすると、即時性と正確性を両立できる。既存の段階レベル(syntax/core/full/complete: Program.cs:90-98)とincrementalキャッシュをそのまま活かせる。
- evidence: AnalysisBuildOptions.cs:39-40(incrementalはsyntax限定という制約への対処), AnalysisPipeline.cs:42-46(full時はincremental無効化される実装事実), Program.cs:89-98(AnalysisLevelは既に選択可能), SyntaxIncrementalSemanticPhase(semanticは別フェーズで走る既存構造)
- 検証ノート: 前提と既存構造が一致。AnalysisBuildOptions.cs:39-40 で incremental が Syntax レベル限定なのは確認、AnalysisPipeline.cs:42-46 で full時に incremental が無効化される実装事実も確認(警告ログ付き)。SyntaxIncrementalSemanticPhase は実在(src/Unilyze/Incremental/SyntaxIncrementalSemanticPhase.cs)し、AnalysisPipeline.cs:88-92 で UseSyntaxIncrementalCache 時に分岐呼び出しされる。Program.cs:89-98 で AnalysisLevel(syntax/core/full/complete)が選択可能なのも確認。2フェーズ(syntax即時flash→full確定)は提案3の制約(incrementalはSyntaxpin時のみ高速)を正しく回避する設計で、むしろ提案3の修正の受け皿になる。phase:'syntax'|'final' をsnapshotに持たせviewer側で枠色flashとメトリクス更新を分ける案も、main.js:44-54の索引再構築を applySnapshot 化(提案2)できれば実装可能。effort:L/impact:medium は妥当。

#### フロー図(呼び出し/制御フロー)はメソッドメトリクス基盤の上に段階導入

- verdict: valid
- effort: L / impact: medium
- what: 要件4のフロー図は現状データだけでは不足するため、まず到達可能な範囲から着手する。第1段: 既存の型依存グラフ(Cytoscape)に『メソッド単位ビュー』を追加し、MethodMetrics(CognitiveComplexity/StartLine等: CodeHealthCalculator.cs:5-16)をノード化、型内メソッド一覧を呼び出し元へのジャンプ起点にする。第2段: 呼び出しグラフ(call graph)はRoslynのSemanticModelで InvocationExpression を解決する新コレクタが必要(現状の依存抽出は型間DependencyEdge止まりでメソッド呼び出しエッジを持たない)。serveの常駐性を活かし、フォーカス中の型だけオンデマンドで呼び出しグラフを解析・配信する(全体call graphは重いため遅延)。Cytoscape+dagre(同梱済み)で描画でき新vendorは不要。
- why: 要件4を正面から満たすにはメソッド呼び出しエッジの新規抽出が要るが、これは解析パイプラインの拡張で重い。既存のメソッドメトリクスとCytoscape/dagre同梱資産を土台に段階導入すれば、新規描画ライブラリ依存を増やさずゼロセットアップ思想を保てる。serve常駐ならオンデマンド解析でレイテンシを許容できる。
- evidence: CodeHealthCalculator.cs:5-16(MethodMetricsは存在しノード化可能), HtmlTemplate.cs:16-24(cytoscape/dagre同梱=フロー図描画基盤あり), main.js全体は型ノードグラフ中心(メソッド/呼び出しエッジの描画は未実装), Unilyze.csproj EmbeddedResource(vendorはcytoscape/dagre/cytoscape-dagreのみ=call graph抽出は新規)
- 検証ノート: 現状認識が正確。MethodMetrics は CodeHealthCalculator.cs:5-16 に実在(CognitiveComplexity/CyclomaticComplexity/StartLine等)、ノード化の素材としては足りる。Cytoscape/dagre同梱は HtmlTemplate.cs:16-24 と csproj:34-38 で確認、フロー図描画基盤として新vendor不要は妥当(ただし提案1同様、現行レイアウトのデフォルトはELKでindex.html:73のCDN依存。dagreは同梱だがフロー図でdagreを使う前提を明記すべき)。call graphの欠如は事実: 一般的なメソッド呼び出しエッジ抽出は存在せず、InvocationExpressionSyntax の解析は DI 系(DIContainerAnalyzer.cs:41, Zenject/VContainerResolver)に限定。型間依存は TypeDependency(fromType/toType, DependencyKind, schema.txt:61-62)止まりで『メソッド呼び出しエッジを持たない』は正しい。よってRoslyn SemanticModelで InvocationExpression を解決する新コレクタが必要という結論は妥当で、serve常駐でフォーカス型だけオンデマンド解析する段階導入も現実的。effort:L は第1段(メソッドビュー)には妥当だが、第2段(call graph抽出)は新規セマンティック解析でM〜L相当になる点は留意。

Open questions:

- 配信方式の最終決定: SSE(HttpListenerで単方向push、依存ゼロ、自動再接続が標準)を第一候補としたが、将来viewer→サーバの双方向操作(エディタ起動、フォーカス連動の遅延call graph要求)が増えるならWebSocketが妥当。ただしHttpListenerにWS実装は無く自前フレーミング/ハンドシェイクが必要。POST APIで双方向を代替しSSEを維持するか、WSへ寄せるか。
- ローカルHTTPサーバのセキュリティ境界: 127.0.0.1バインド固定で十分か、ポート(固定/自動空きポート探索)とCORS(同一オリジンなら不要)、/api/source のpath traversal検証(projectRoot配下のみ許可)、/api/open による任意エディタ起動のコマンドインジェクション対策。docs/threat-model.md は現状ファイル出力前提で、常駐サーバ・ソース配信・プロセス起動を脅威モデルに追加する必要がある。
- incrementalキャッシュの並行性: serve常駐中にFileSystemWatcher由来の再解析が前回解析と重なった場合、SyntaxCacheStore.Save(temp+File.Move: SyntaxCacheStore.cs:54-76)とTryLoadの競合をどう直列化するか。再解析中の追加変更を最新で上書き(latest-wins)するキャンセル/再投入戦略。
- semanticライブ更新の実現可否: incrementalはsyntax限定(AnalysisBuildOptions.cs:39-40)。CBO/DIT/coupling等を含むsemantic指標を毎保存で更新するとレイテンシが許容外になる可能性。段階反映(syntax即時→semantic確定)案の体感レイテンシを実測で確認し、semantic incremental化(現状無効)の投資判断が要る。
- ソースジャンプの行範囲: TypeMetrics/MethodMetricsにEndLineが永続化されていない(TypeInfo.cs:218等で計算後に破棄、LineCountのみ保持)。範囲ハイライトをStartLine+LineCountの近似で許容するか、EndLineをモデル/JSONに追加する(スキーマ変更=schema/diff/baselineのフィンガープリント影響を要確認)か。
- フロー図のスコープ: 要件4の『フロー』が(a)型依存グラフの延長か、(b)メソッド呼び出しグラフ(call graph)か、(c)制御フローグラフ(CFG)かで実装規模が桁違い。現状の依存抽出は型間DependencyEdge止まりで、(b)(c)はRoslyn SemanticModelベースの新規コレクタが必須。どこまでを初期スコープに含めるか。
- viewer配信モードの二重メンテ: インライン埋め込み(ファイル出力/diff overlay)とfetch+SSE(serve)の2系統をmain.jsで共存させると分岐が増える。combine.py(combine.pyのReplace結合)とビルド時EmbeddedResource化(Unilyze.csproj CombineViewerTemplate)を崩さずにランタイムfetch経路を足す設計の検証が要る。

### serve/watchコマンドとライフサイクル

現状: No serve/watch or local HTTP server exists; each proposal below cites the relevant file:line (dispatch chain, file:// static output, inlined viewer DATA, unpkg ELK, no graceful-shutdown plumbing, SyntaxOnly incremental cache).

#### serve: zero-dep HttpListener local server

- verdict: valid
- effort: M / impact: high
- what: ServeRunner.Run into Program.cs:18-45 + ValidateTopLevelCommand (Program.cs:13). BCL HttpListener keeps csproj Microsoft.NET.Sdk on net8/net10. HTML at root, JSON at api/analysis.json; port 0 auto-assigns to avoid collisions; open 127.0.0.1 (unless --no-open); bind localhost only.
- why: file:// output (Program.cs:167,178) cannot run fetch/EventSource. Kestrel needs the web SDK plus AspNetCore reference, breaking zero-setup and single-nupkg; HttpListener is least invasive.
- evidence: Program.cs:18-45, Program.cs:13, Program.cs:165-180, ProgramHelpers.cs:234-250
- 検証ノート: Evidence checks out. Program.cs:18-45 is the literal if-chain of `args[0] == "..."` dispatches; serve/watch are absent (rg for "serve"/"watch"/HttpListener/FileSystemWatcher in src/ returns nothing). Program.cs:165-180 confirms the HTML/JSON branch writes files and ProgramHelpers.cs:234-250 (TryOpenInBrowser) opens them via `file://` (line 238), which indeed cannot run fetch/EventSource cross-origin. Unilyze.csproj:1 is Microsoft.NET.Sdk (not Web SDK) with only Microsoft.CodeAnalysis.CSharp + MinVer references (csproj:41-44), so Kestrel would force an AspNetCore reference; HttpListener (BCL) keeps the single-nupkg PackAsTool shape (csproj:7-8). One gap to flag, not a blocker: Program.cs:13 routes through CliArgValidation.ValidateTopLevelCommand, which is an ALLOWLIST (CliArgValidation.cs:5-11, TopLevelCommands) plus the explicit if-chain. `serve` must be added BOTH to TopLevelCommands and a new `if (args[0]=="serve")` dispatch, else it is rejected as an unknown subcommand. The proposal says "into Program.cs:18-45 + ValidateTopLevelCommand" which implicitly covers this, so verdict stays valid.

#### Graceful shutdown / shared lifecycle base

- verdict: needs-revision
- effort: S / impact: high
- what: CancellationTokenSource + Console.CancelKeyPress (Ctrl-C) + AppDomain.ProcessExit; on Ctrl-C stop HttpListener, dispose watcher, cancel analysis, exit 0. Share via a ServerLifetime helper with McpStdioServer.Run.
- why: No CancellationToken/CancelKeyPress/ProcessExit in src; MCP (McpStdioServer.cs:16) does no Ctrl-C cleanup. Resident mode holds a port/handles, so absence means port occupation and restart failures.
- evidence: McpStdioServer.cs:8-40, McpStdioServer.cs:16
- 検証ノート: The structural need is real but one factual premise is wrong. The claim "No CancellationToken/CancelKeyPress/ProcessExit in src" is inaccurate: rg finds a CancellationTokenSource already in StatuslineRunner.cs (5-minute timeout). CancelKeyPress and ProcessExit are genuinely absent. McpStdioServer.cs:8-40 confirmed: Run() loops on reader.ReadLine() with `using var reader` only, no Ctrl-C handler, and McpRunner just calls McpStdioServer.Run() in a try/catch with no shutdown plumbing. So the conclusion (resident serve mode needs explicit Ctrl-C teardown for the socket/watcher) holds, but the supporting statement over-claims.
- 修正案: Reword the premise to "no CancelKeyPress/ProcessExit anywhere; the only CancellationTokenSource (StatuslineRunner.cs) is a request-scoped timeout, not lifecycle plumbing." The ServerLifetime helper is sound; sharing it with McpStdioServer.Run is optional (stdin-close already terminates MCP cleanly per McpStdioServer.cs:16), so frame MCP integration as a nice-to-have, not a driver.

#### watch: incremental-cache file watch + reanalysis

- verdict: needs-revision
- effort: M / impact: high
- what: FileSystemWatcher on cs files, debounce 200-500ms, reanalyze via AnalysisPipeline.Build incremental true (Program.cs:140-150) + SyntaxCacheStore, regenerate JSON, swap latest snapshot, push via SSE; exclude the cache dir.
- why: Core of immediate change reflection. The content-hash cache speeds the SyntaxOnly path (SyntaxCacheStore.cs:9-17); reusing it as the watch engine avoids full reanalysis.
- evidence: Program.cs:140-150, SyntaxCacheStore.cs:9-17, SyntaxCacheStore.cs:54-76
- 検証ノート: The reuse-the-incremental-cache premise is contradicted by the code. AnalysisBuildOptions.cs:39-40 defines `UseSyntaxIncrementalCache => Incremental && RequestedLevel == AnalysisLevel.Syntax`, and AnalysisPipeline.cs:42-47 SILENTLY downgrades Incremental to false and warns "--incremental currently accelerates syntax-level analysis only; running full analysis" whenever RequestedLevel != AnalysisLevel.Syntax. So `AnalysisPipeline.Build(... incremental: true ...)` at Program.cs:140-150 does NOTHING unless the caller also pins --level syntax. But syntax level routes through SyntaxIncrementalSemanticPhase (AnalysisPipeline.cs:88-92) and produces a degraded result; the live dependency-graph viewer renders DATA.types / typeMetrics / deps / cycles (main.js:44-54) and the diff overlay, which come from the full semantic phase (AnalysisPipelineSemanticPhase.Run, AnalysisPipeline.cs:95). Pinning syntax to get cache reuse would strip exactly the data the viewer needs. SyntaxCacheStore.cs:9-17 citations are correct, but the cache is a SyntaxOnly accelerator, not a general watch engine.
- 修正案: Drop the claim that incremental:true avoids full reanalysis for the live viewer. Either (a) accept full reanalysis on each debounced change for the default (semantic) viewer and rely on FileSystemWatcher + debounce + a single in-flight CancellationToken to keep latency acceptable, or (b) treat the SyntaxOnly incremental cache as an explicit opt-in fast-path only for a syntax-level watch mode, documenting the reduced fidelity (no coupling/cycles/full CodeHealth). Still exclude the .unilyze/cache dir from the watcher (SyntaxCacheStore.cs:13-14 GetCacheDirectory) to avoid self-trigger loops, and gate writes via the existing atomic temp-file Save (SyntaxCacheStore.cs:54-76).

#### Live channel: SSE + fetch-bootstrap viewer variant

- verdict: valid
- effort: M / impact: high
- what: serve-mode flag in HtmlFormatter (HtmlFormatter.cs:7-24); instead of inlining DATA (main.js:1), fetch api/analysis.json initially and subscribe api/events via EventSource to redraw on reanalysis; keep inline embedding in file mode.
- why: Viewer is fixed to const DATA=__DATA_PLACEHOLDER__ (main.js:1) with no receive channel. SSE is simpler than WebSocket and fits server-to-client one-way.
- evidence: HtmlFormatter.cs:7-24, main.js:1, main.js:57
- 検証ノート: Confirmed. main.js:1 is literally `const DATA = __DATA_PLACEHOLDER__;` and HtmlFormatter.cs:13-24 (Render) injects it via `.Replace("__DATA_PLACEHOLDER__", safeAnalysisJson)`. main.js:57 `const DIFF = __DIFF_DATA_PLACEHOLDER__;` confirms a second inlined payload. The viewer has no receive channel today, so a serve-mode flag in HtmlFormatter that emits a fetch(api/analysis.json)+EventSource(api/events) bootstrap instead of inlining is the right swap, and SSE (server->client one-way) fits redraw-on-reanalysis. Minor scope note: serve mode must also resolve __DIFF_DATA_PLACEHOLDER__ (main.js:57) — emit `null` or a diff endpoint — not just the DATA placeholder, or the template will ship an unsubstituted token and break JS parse.

#### Bundle the ELK CDN dependency offline

- verdict: valid
- effort: S / impact: medium
- what: Embed elkjs 0.9.3 (elk.bundled.js, elk-worker.min.js) as EmbeddedResource like cytoscape/dagre (Unilyze.csproj:33-38), replacing the index.html:73 unpkg tag and main.js worker importScripts. Add SHA256/MIT notices.
- why: The default layout (index.html:27 elk selected) hits unpkg every startup, breaking offline use, though cytoscape/dagre are already bundled.
- evidence: index.html:73, index.html:27, Unilyze.csproj:33-38
- 検証ノート: Confirmed and the proposal actually understates the surface. index.html:73 is `<script src="https://unpkg.com/elkjs@0.9.3/lib/elk.bundled.js">` and index.html:27 has the ELK option marked `selected` (default layout), so a default-config launch hits unpkg. There is a SECOND network dependency the evidence omits: main.js:1200 `const source='importScripts("https://unpkg.com/elkjs@0.9.3/lib/elk-worker.min.js");'` (used by new ELK({workerUrl:...}) at main.js:1255). Both must be embedded for true offline. The bundling pattern is exactly Unilyze.csproj:33-38 (EmbeddedResource + SHA/MIT comment) consumed by HtmlTemplate.BuildVendorScripts (HtmlTemplate.cs:13-26 AppendInlineScript), and the worker needs a Blob/object-URL shim since importScripts can't load an inlined string directly. Verdict valid; just widen scope to the worker.

#### serve HTTP / source-serving security boundary

- verdict: valid
- effort: S / impact: medium
- what: Bind 127.0.0.1 only; prefix-validate any source endpoint against the project root to block traversal; return JSON as application/json (cf EscapeInlineScriptPayload, HtmlFormatter.cs:27-28); add a local-server item to docs/threat-model.md.
- why: The threat model assumes static-HTML escaping (HtmlFormatter.cs:27). serve adds a socket plus file-serving surface; serving local files for source-jump is a classic traversal hole.
- evidence: HtmlFormatter.cs:27-28, ProgramHelpers.cs:234-250
- 検証ノート: Confirmed. HtmlFormatter.cs:27-28 (EscapeInlineScriptPayload) only rewrites `</script` -> `<\/script`, and docs/threat-model.md (verified, 8 lines) explicitly scopes the threat to "analyzing an untrusted repository and then opening the generated HTML report" with mitigation = System.Text.Json encoder + the </script rewrite — i.e., a static-file model with no socket and no file-serving surface. Adding serve introduces both, and a source-jump endpoint that reads local files is a classic path-traversal hole, so 127.0.0.1-only bind + project-root prefix validation + application/json content-type + a new threat-model entry are all warranted. ProgramHelpers.cs:234-250 (TryOpenInBrowser) is the current open path; serve would replace its `file://` URL with http://127.0.0.1:<port>.

Open questions:

- serve vs watch separate or a serve --watch flag; reconcile with --incremental (Program.cs:73) and --no-open (Program.cs:83)
- SSE vs WebSocket; watch semantic support given cache is SyntaxOnly (SyntaxCacheStore.cs:10), forcing full reanalysis at full/complete on ~200 cs files
- Source-jump (goal 2) in-browser endpoint vs editor launch; port policy (OS-assigned vs fixed); FileSystemWatcher inotify/FSEvents limits and Unity Library/obj exclusion

### インクリメンタル再解析

現状: 既存の `--incremental` は構文レベル専用で、ライブ可視化が必要とする完全解析パスでは一切働かない。

1. ゲート条件が厳しい: `AnalysisBuildOptions.UseSyntaxIncrementalCache => Incremental && RequestedLevel == AnalysisLevel.Syntax`(src/Unilyze/Pipeline/AnalysisBuildOptions.cs:39-40)。`AnalysisPipeline.Build` は `Incremental && RequestedLevel != AnalysisLevel.Syntax` のとき警告して `Incremental=false` に落とす(AnalysisPipeline.cs:42-47)。デフォルトの `analyze`(--level未指定)は `RequestedLevel==null` → `EffectiveCap==Complete`(AnalysisBuildOptions.cs:35)なので、HTMLビューアが使う完全解析では常に非インクリメンタル。

2. キャッシュは構文+enrichmentを既にディスク保持: `SyntaxCacheFileEntry` は per-file の `ContentHash` に加え `RawTypes` と `EnrichedTypes`(=TypeMetrics 群)を持つ(src/Unilyze/Incremental/SyntaxCacheModels.cs:15-24)。`manifest.json` が `<project>/.unilyze/cache/syntax/v1/` に保存される(SyntaxCacheStore.cs:9-17)。fingerprint不一致やschema不一致で全破棄(SyntaxCacheStore.cs:39-44)。enrichment再利用ロジックも既に存在: `CachedEnrichmentByTypeId` を non-reparse 型に適用(SyntaxIncrementalSemanticPhase.cs:49-52)。

3. 無効化の粒度はファイル単位+限定的な閉包拡張のみ。`Collect` で content-hash 比較→不一致ファイルのみ `filesToParse`(SyntaxIncrementalCollector.cs:38-49)。閉包拡張は2種類だけ: partial型(同名typeIdの全partを巻き込む, line 192-210)とアセンブリ単位のinterface集合ハッシュ変化(変化時そのアセンブリ全ファイル再パース, line 212-241)。型/メソッド単位の再解析や「依存先が変わったら依存元を再enrich」する逆依存閉包は無い。`DetermineTypesToReEnrich` は reparsed ファイル所属型だけを対象(SyntaxIncrementalSemanticPhase.cs:78-107)。

4. semanticレベルのインクリメンタルは実質未実装。SyntaxOnly では `CompilationFactory.Create` が `maxLevel==Syntax` で即 `CompilationResult(null, Syntax)` を返す(CompilationFactory.cs:32-33)。よって incremental パスの `SyntaxIncrementalSemanticPhase.Run` は常に compilation==null で動き、`BaseTypeResolver`(BaseTypeResolver.cs:16 で null時early-out)や `SemanticEnricher`(SemanticEnricher.cs:67)はセマンティック情報なしで動く。完全解析(Complete)経路 `AnalysisPipelineSemanticPhase.Run` には差分・キャッシュの仕組みが無く全型を毎回 `SemanticEnricher.Enrich`(AnalysisPipelineSemanticPhase.cs:36)。

5. プロセス間ウォームステートが無い。`SyntaxIncrementalState.Current` は `[ThreadStatic]`(SyntaxIncrementalState.cs:5)で `Build` の finally で null リセット(AnalysisPipeline.cs:55)。毎回プロセス起動→manifest.json読み直し。watch/serve/FileSystemWatcher/HTTP は存在しない(grep結果ゼロ)。MCP のみ `McpAnalysisCache` で AnalysisResult を1件in-memory保持(McpAnalysisCache.cs:7-19, McpToolHandlers.cs:17)だが入力パス変化で丸ごと再解析。

6. 全解析でも最大コストはRoslyn `CSharpCompilation.Create`+SemanticModel生成(CompilationFactory.cs:61-65, 各 `GetSemanticModel`)。content-hash計算は全ファイルSHA256ストリーム(SyntaxCacheFingerprint.cs:44-49)で、変更検出のために毎回全ファイルを読む。

#### 完全解析パスへの差分enrichキャッシュ拡張(semantic incremental の土台)

- verdict: needs-revision
- effort: L / impact: high
- what: `UseSyntaxIncrementalCache` のゲートを `RequestedLevel==Syntax` 限定から外し、Complete/Full でも manifest を読み込む。compilation が非nullの完全解析でも、content-hash 一致ファイルの型は `CachedEnrichmentByTypeId.Metrics` を再利用し、変更ファイル所属型+逆依存で巻き込まれた型のみ `SemanticEnricher.Enrich` する。`SyntaxIncrementalSemanticPhase.Run` は既に compilation を受け取り `metricsByTypeId` で再利用/再enrichを分岐する構造(SyntaxIncrementalSemanticPhase.cs:35-72)なので、Complete経路を同関数に合流させる。manifest fingerprint に compilation 参照DLLセットのハッシュを追加し、参照変化時は全破棄。
- why: ライブ可視化が使うのはデフォルト(Complete)解析。現状そこにインクリメンタルが全く効かず、変更1ファイルでも全型を毎回 enrich する。キャッシュは既に per-file の EnrichedTypes(TypeMetrics)を持っており(SyntaxCacheModels.cs:19-20)、再利用機構も実装済み。最小の改修で「変更ファイルのみ再enrich」を完全解析に拡張でき、ライブ更新レイテンシを支配的に下げる。
- evidence: src/Unilyze/Pipeline/AnalysisBuildOptions.cs:39-40, src/Unilyze/Pipeline/AnalysisPipeline.cs:42-47, src/Unilyze/Incremental/SyntaxIncrementalSemanticPhase.cs:35-72, src/Unilyze/Incremental/SyntaxCacheModels.cs:19-20
- 検証ノート: ゲート/合流の事実関係はほぼ正しいが、再利用の中核前提が崩れている。(1) ゲート `UseSyntaxIncrementalCache => Incremental && RequestedLevel == AnalysisLevel.Syntax`(AnalysisBuildOptions.cs:39-40)と、非Syntaxでincrementalを強制offにする `AnalysisPipeline.Build`(AnalysisPipeline.cs:42-47)は記述どおり。(2) `SyntaxIncrementalSemanticPhase.Run` が compilation を受け取り `metricsByTypeId` で再利用/再enrichを分岐する構造(SyntaxIncrementalSemanticPhase.cs:35-72)も正しい。決定的な落とし穴: 現状この経路は『Syntaxレベル専用』のため、`AnalysisPipeline.cs:91` で渡される `compile.CompilationResult` は常に `null` compilation である。`CompilationFactory.Create` は `maxLevel == AnalysisLevel.Syntax` で即 `new CompilationResult(null, AnalysisLevel.Syntax)` を返す(CompilationFactory.cs:32-33)。つまりキャッシュに保存済みの `EnrichedTypes(=TypeMetrics)` は『compilationなしのsyntax-onlyエンリッチ結果』であり、CBO/DIT/LCOM/RFC 等のセマンティックメトリクスを欠く。これをComplete解析で再利用すると、変更なしファイルの型だけ semantic 値が抜けた結果になり、Complete解析の出力品質を破壊する。提案の『content-hash 一致ファイルの型は CachedEnrichmentByTypeId.Metrics を再利用』は、キャッシュ自体を Complete 経路で生成し直さない限り成立しない。さらに manifest fingerprint は参照DLLセットのハッシュを含まない(SyntaxCacheFingerprint.cs:15-42 にDLLパス/ハッシュなし)ので、提案の『参照DLLセットのハッシュを追加』は新規実装が必須(現状を前提にできない)。effort=L は過小評価。
- 修正案: (a) まずキャッシュをレベル別に分離する。manifest に AnalysisLevel を持たせ、Syntax用キャッシュと Complete用キャッシュを別ファイル/別キー(fingerprint に EffectiveCap を混ぜる)で管理。Complete経路で再利用するのは『Complete時に生成した EnrichedTypes』のみとする。(b) その上で Complete 経路にもインクリメンタルcollectを通し、`SyntaxIncrementalSemanticPhase.Run` を非null compilation で動かす(この場合 `BaseTypeResolver`/`SemanticEnricher` がセマンティック情報込みで動くので結果はComplete相当になる、BaseTypeResolver.cs:16, SemanticEnricher.cs:67)。(c) fingerprint に参照DLLパス+各DLLのmtime/size(またはハッシュ)を追加し、参照変化時は全破棄。effortは L→XL に上げる。

#### 逆依存(被依存)閉包ベースの無効化セットを導入

- verdict: needs-revision
- effort: L / impact: high
- what: manifest に型間依存(`TypeDependency` の from/to typeId)を保存し、warm時に逆依存グラフを構築。変更ファイル所属型 T に対し、T を参照する型のうち semantic な再enrichが必要なもの(継承元/インターフェイス実装/シグネチャ参照)だけを `DetermineTypesToReEnrich` に追加する。現状の partial(SyntaxIncrementalCollector.cs:192-210)・interfaceハッシュ(line 212-241)というアドホックな2経路を、汎用の依存閉包1経路に統合する。閉包は型単位に閉じ、ファイル全体ではなく該当型のみ再enrich。
- why: 現状の無効化は『partial同名』『アセンブリ単位interface集合変化』だけで、基底クラスのメンバ変更が派生クラスのメトリクスに波及するケース等を取りこぼすか、逆にアセンブリ丸ごと再パースで過剰無効化する(line 234-240)。型単位の逆依存閉包なら、正確さを保ちつつ最小集合だけ再計算でき、ライブ更新の体感速度と結果の正しさを両立する。
- evidence: src/Unilyze/Incremental/SyntaxIncrementalCollector.cs:192-241, src/Unilyze/Incremental/SyntaxIncrementalSemanticPhase.cs:78-107, src/Unilyze/Pipeline/AnalysisPipelineSemanticPhase.cs:25-36
- 検証ノート: 現状認識は正確。`DetermineTypesToReEnrich` は reparsed ファイル所属型 + partial同名型の閉包だけ(SyntaxIncrementalSemanticPhase.cs:78-104)、`ExpandPartialInvalidations`(SyntaxIncrementalCollector.cs:192-210)と `ExpandInterfaceInvalidations`(line 212-241、アセンブリ単位で interface 集合ハッシュ変化時にアセンブリ全ファイル再パース)の2経路のみ。逆依存閉包は存在しない。`TypeDependency` が `FromTypeId`/`ToTypeId` を持つ(TypeInfo.cs:13-18)ので manifest 保存自体は可能、という前提も正しい。ただし重大な見落とし: 提案は『型間依存を manifest に保存して逆依存グラフを構築』とするが、依存(`deps`)は `DependencyBuilder.Build`(TypeInfo.cs:369-392)で『全型集合をマージした後』に構築される。インクリメンタルではキャッシュからロードした非reparse型の RawTypes(SyntaxCacheModels.cs:19)も含めて毎回 deps を再構築している(SyntaxIncrementalSemanticPhase.cs:25)ので、依存グラフはそもそも全型分が手元にある=manifestに別途保存する必要はなく、その場で逆依存を引ける。提案の『manifestに依存を保存』は冗長で、保存版と再構築版の二重管理を生む。さらに本質的限界: ここで構築される依存は構文ベースのシグネチャ参照(基底/インターフェイス/フィールド/メソッド型等)に限られ、メソッド本体内の呼び出しや基底クラスのメンバ実体変更による派生メトリクス波及は `deps` に現れない(DependencyBuilder は型シグネチャのみ走査、本体は見ない)。よって『基底クラスのメンバ変更が派生クラスのメトリクスに波及するケースを取りこぼす』という問題提起に対し、提案の逆依存閉包は構文依存しか辿れず、まさにその波及(本体起因)を捕捉できない。
- 修正案: (a) manifestへの依存保存はやめ、collect後にメモリ上の `deps`(FromTypeId/ToTypeId)から逆依存グラフを構築して `DetermineTypesToReEnrich` に渡す。これで partial/interface の2アドホック経路の一部は統合できる。(b) ただし『正確さを保つ』主張は撤回するか限定する。構文依存閉包で安全に捕捉できるのは継承/インターフェイス/シグネチャ参照の変化のみ。メソッド本体変更が semantic メトリクス(CBO/RFC等)に与える波及を正確に追うには semantic model 経由の参照解析が要り、それは別タスク。MVPとしては『閉包は近似であり、取りこぼしうる』ことを明示し、interface集合ハッシュによるアセンブリ単位フォールバック(現状の過剰無効化)は安全網として残す。

#### 常駐watch/serveプロセスで in-memory ウォームステートを保持

- verdict: valid
- effort: L / impact: high
- what: 新サブコマンド `watch`(または `serve`)を追加し、1プロセス内で AnalysisResult・SyntaxTree・CSharpCompilation・manifest を常駐保持。FileSystemWatcher で .cs 変更を受け、変更ファイルだけを再パースして `Compilation.ReplaceSyntaxTree` で差し替え、上記の差分enrichのみ走らせて結果を push する。`SyntaxIncrementalState` の [ThreadStatic]+finally null化(SyntaxIncrementalState.cs:5, AnalysisPipeline.cs:55)はワンショット前提なので、常駐モードでは明示的なライフサイクルを持つ状態保持クラスに置き換える。MCP の `McpAnalysisCache`(McpAnalysisCache.cs:7-19)が示す単一結果キャッシュの発想を、ファイル単位差分で更新できるよう一般化。
- why: ライブ更新(lazygit的な即時反映)の最大の敵は『毎回プロセス起動→manifest.json読み直し→全ファイルSHA256→Roslyn compilation 再構築』。Roslyn の compilation/SemanticModel をメモリ保持し ReplaceSyntaxTree する常駐方式なら、変更1ファイルあたりの再解析を桁で短縮できる。ディスクmanifestは既にあるので、停止後の再起動でもコールドスタートを温める二段構えにできる。
- evidence: src/Unilyze/Incremental/SyntaxIncrementalState.cs:5, src/Unilyze/Pipeline/AnalysisPipeline.cs:55, src/Unilyze/Pipeline/CompilationFactory.cs:61-65, src/Unilyze/Mcp/McpAnalysisCache.cs:7-19, src/Unilyze/Pipeline/AnalysisPipelineDiscovery.cs:103-109
- 検証ノート: evidence をすべて確認し成立。`SyntaxIncrementalState.Current` は `[ThreadStatic]`(SyntaxIncrementalState.cs:5)で、`Build` の finally で `null` リセット(AnalysisPipeline.cs:55)。watch/serve/FileSystemWatcher/HttpListener はコードベースに存在しない(`rg -in 'FileSystemWatcher|"watch"|"serve"|HttpListener' src/` がゼロヒット)。`McpAnalysisCache` は AnalysisResult を1件 in-memory 保持し入力パスでキー(McpAnalysisCache.cs:7-19、`BuildKey` が `args.Input ?? FullPath` をキー化)、パス変化で丸ごと再ロードという記述も正しい。`CompilationFactory` が `CSharpCompilation.Create` でcompilationを生成する(CompilationFactory.cs:61-65)ので、これをメモリ保持して `ReplaceSyntaxTree` する設計は Roslyn API上も妥当。常駐モードでThreadStaticのワンショット前提を明示的ライフサイクルへ置き換える方向性は正しい。注意点(verdictは変えない): これは観点『インクリメンタル再解析』というよりライブ更新基盤そのもので、本提案単体では再解析を速くしない。効果を出すには提案1/2(Complete経路の差分enrich)とセットで初めて『変更1ファイル→該当型のみ再enrich』が成立する。effort=L は楽観的(常駐プロセス管理・スレッド安全・compilation差し替えの整合性確保で実質 L〜XL)。

#### 変更検出を mtime+size プレフィルタ化して全ファイルSHA256を回避

- verdict: valid
- effort: M / impact: medium
- what: `EnumerateScannedFiles` が全 .cs を `HashFileContent`(全文SHA256, SyntaxCacheFingerprint.cs:44-49)している(SyntaxIncrementalCollector.cs:158)。manifest に (mtime, length) を併記し、warm時はまず mtime+size 一致でスキップ、不一致のみ SHA256 で確定する。常駐watchでは FileSystemWatcher の変更通知ファイルだけハッシュすれば全走査自体が不要。
- why: 現状は変更ゼロでも毎回プロジェクト全 .cs(約200本+対象によっては数千)を読み切ってSHA256。ライブ更新の高頻度ループでこの I/O が固定コストになる。mtime+size プレフィルタで I/O を変更ファイル分だけに削減でき、cache-hit が支配的なライブ運用で効く。content-hash 自体は衝突安全のため最終確定に残す。
- evidence: src/Unilyze/Incremental/SyntaxIncrementalCollector.cs:136-163, src/Unilyze/Incremental/SyntaxCacheFingerprint.cs:44-49
- 検証ノート: evidence 成立。`EnumerateScannedFiles` は全 .cs に対し `SyntaxCacheFingerprint.HashFileContent`(全文SHA256ストリーム)を呼ぶ(SyntaxIncrementalCollector.cs:158, SyntaxCacheFingerprint.cs:44-49)。cache-hit でも `FileScanEntry.ContentHash` を埋めるため全走査で必ずSHA256を計算しており、変更ゼロでも全ファイルI/Oが固定コストになるという指摘は正しい。mtime+size プレフィルタ→不一致のみSHA256確定で hit支配ループのI/Oを削れる。追加で指摘(提案を強化する見落とし): `BuildManifest` も保存時に再度全ファイルを `HashFileContent` する(SyntaxIncrementalCollector.cs:115)。つまり1回のincremental実行で全ファイルが最低2回読まれる。プレフィルタ導入時はこの保存側も、collect で計算済みのハッシュを `FileScanEntry` から引き回して再ハッシュを避けるべき。content-hash を衝突安全の最終確定に残す方針も妥当。

#### 再解析結果を差分ペイロード(変更typeId集合)として出力しビューアへ最小更新

- verdict: needs-revision
- effort: M / impact: medium
- what: 解析結果に『今回再enrichした型/依存の集合』を付帯出力する。`SyntaxIncrementalCollectResult.ReparsedFiles`(SyntaxCacheModels.cs:31)と `DetermineTypesToReEnrich`(SyntaxIncrementalSemanticPhase.cs:78)は既にこの集合を内部で持つので、AnalysisResult にオプショナルな ChangedTypeIds/ChangedDeps として露出。watch/serve はフル結果ではなくこの差分を push し、Cytoscape 側は該当ノード/エッジだけ更新。既存 DiffResult/deltaScore(diff系)と同じ typeId 体系に揃え、ライブ差分とコミット間 diff を統一表現にする。
- why: ライブ更新で毎回フルJSON(数MB)を再生成・再転送・再レイアウトするとビューアがちらつき遅延する。再解析で『何が変わったか』は既に計算済みなので、それを差分として渡せばビューアの増分更新(ノード単位ハイライト/diff表示)が可能になり、即時反映・フロー図の部分再描画につながる。既存 diff オーバーレイ資産と整合。
- evidence: src/Unilyze/Incremental/SyntaxCacheModels.cs:26-32, src/Unilyze/Incremental/SyntaxIncrementalSemanticPhase.cs:78-107, src/Unilyze/Pipeline/AnalysisPipeline.cs:127-147
- 検証ノート: 内部に集合が既にある点は正しい。`SyntaxIncrementalCollectResult.ReparsedFiles`(SyntaxCacheModels.cs:31、型は `IReadOnlySet<string>`)、`DetermineTypesToReEnrich`(SyntaxIncrementalSemanticPhase.cs:78-104)が再enrich対象typeId集合を内部生成するのは事実。ただし露出経路に複数の前提崩れ: (1) `DetermineTypesToReEnrich` は `static` かつ戻り値を `Run` 内ローカル `typesToReEnrich` に閉じ込めており(SyntaxIncrementalSemanticPhase.cs:35)、`Run` の戻り値タプルにも `SyntaxIncrementalCollectResult` にも含まれない。AnalysisResultへ露出するには Run のシグネチャ変更と集合の戻し配線が必要で、effort=M は妥当だが『既に持つので露出するだけ』という軽さは過小。(2) `AnalysisResult` に ChangedTypeIds/ChangedDeps 相当のフィールドは無い(AnalysisResult.cs:12-30)。追加は source-gen JSON コンテキスト(AnalysisResult.cs:46-)の更新と、後方互換(nullableオプショナル)配慮が要る。(3) 最大の論理矛盾: この差分集合が意味を持つのはインクリメンタル経路が走った時だけ。だが現状インクリメンタルは Syntax 専用で、ライブ可視化が使う Complete 経路では `AnalysisPipelineSemanticPhase.Run`(差分機構なし、全型 `SemanticEnricher.Enrich`、AnalysisPipelineSemanticPhase.cs:36)が走る。よって本提案は提案1(Complete経路の差分enrich)が前提として成立していなければ、Complete解析では常に『全型変更』になり差分ペイロードが無意味。依存関係が evidence/why に明記されていない。(4) 『既存 DiffResult/deltaScore と同じ typeId 体系に揃える』は方向性として妥当だが、DiffResult はコミット間2スナップショット比較であり、ライブ差分(前回解析→今回解析)とは生成主体が別。統一表現にするには中間モデルの設計が要り、Mでは収まらない可能性。
- 修正案: (a) 依存を明示: 本提案は提案1(Complete経路インクリメンタル)に従属する後続タスクとして位置づける。提案1が無い状態では Complete で常に全型差分になることを受け入れる(初回フル→以降差分のフォールバック)。(b) 露出は Run の戻り値に `ChangedTypeIds`(と必要なら changed deps)を足し、`AnalysisResult` にオプショナルnullableフィールド+JSON source-gen 更新で追加。(c) ビューア最小更新(Cytoscapeのノード/エッジ単位ハイライト)は別タスクに切り出す。差分ペイロードのバックエンド露出(effort M)と、フロント側増分描画(別 effort)を分ける。

#### fingerprint不一致時の全破棄を段階的劣化に変更

- verdict: needs-revision
- effort: M / impact: medium
- what: `SyntaxCacheStore.TryLoad` は schema/fingerprint 不一致で manifest 全体を捨てる(SyntaxCacheStore.cs:39-44)。fingerprint をグローバル(ツールバージョン・config・参照)とper-file(content)に分離し、グローバル不一致でも RawTypes/EnrichedTypes の per-file キャッシュは可能な限り再検証して部分再利用する。config変更やバージョン更新の直後でも『変更ファイルのみ再解析』を維持。
- why: ライブ運用中に設定や依存DLLが少し変わるたびに全キャッシュ破棄→次回フルコールドスタートになるのは、即時反映の体験を大きく損なう。グローバル要因とファイル要因を分離すれば、グローバル変更でも semantic だけ作り直して構文キャッシュは温存でき、回復が速い。
- evidence: src/Unilyze/Incremental/SyntaxCacheStore.cs:29-52, src/Unilyze/Incremental/SyntaxCacheFingerprint.cs:15-42
- 検証ノート: 現状記述は正確。`SyntaxCacheStore.TryLoad` は `SchemaVersion` 不一致 or `Fingerprint`(=グローバル fingerprint)不一致で manifest 全体を `null` 返し(SyntaxCacheStore.cs:39-44)、結果として `existingByPath` が空になり全ファイル再パース(SyntaxIncrementalCollector.cs:23-27, 38-48)。`ComputeGlobalFingerprint` は ツールバージョン/メトリクスバージョン/preprocessorSymbols/profile/閾値/除外設定/ターゲット構成をまとめて1ハッシュにする(SyntaxCacheFingerprint.cs:15-42)ので、config を1つ変えるだけで全破棄になるという指摘も正しい。問題点: (1) 提案の核心『グローバル不一致でも構文キャッシュ(RawTypes)は温存し semantic だけ作り直す』は、RawTypes が config 非依存(構文パースのみ、TypeAnalyzer.ParseSingleFile はプリプロセッサシンボル以外 config を見ない)である限り妥当。しかし fingerprint 構成要素のうち preprocessorSymbols は RawTypes の中身を変える(条件コンパイル)ため、これを『グローバル一括』に含めたまま per-file content と分離すると、シンボル変更時に古い RawTypes を誤って温存するリスクがある。提案の『グローバル(version/config/参照)とper-file(content)に2分割』では粒度が粗く、preprocessorSymbols のような『構文に効くグローバル要因』を構文キャッシュ無効化側へ正しく振り分ける設計が欠けている。(2) ToolVersion/MetricsVersion 変更時は RawTypes のスキーマ(TypeNodeInfo の形)自体が変わりうるため、構文キャッシュ温存はデシリアライズ互換が保証されている範囲に限る必要がある(SchemaVersion 一致が前提)。提案はこの安全条件に触れていない。
- 修正案: fingerprint を2層でなく3層に分ける。(a) スキーマ層(SchemaVersion / ToolVersion / MetricsVersion): 不一致なら全破棄(デシリアライズ互換が崩れるため温存不可)。(b) 構文層(preprocessorSymbols / 言語バージョン / ターゲット構成): 不一致なら RawTypes を無効化し再パース、ただし他層が一致する限りファイル単位で content-hash 比較は継続。(c) semantic層(profile / 閾値 / disabledRules / 参照DLL): 不一致なら RawTypes は温存し EnrichedTypes だけ破棄して再enrich。この3層分割なら『config変更で semantic だけ作り直し、構文は温存』が安全に成立する。manifest の per-file エントリに層別フラグを持たせるか、層ごとに別ハッシュを格納する。

Open questions:

- ライブ更新は『常駐watchプロセス内のin-memory差分』方式と『毎回プロセス起動+ディスクmanifest差分』方式のどちらを主軸にするか。前者はRoslyn compilationのReplaceSyntaxTreeで最速だがプロセス寿命・メモリ・状態管理が増える。後者はゼロセットアップ思想に近いが全ファイルmtime走査とmanifest I/Oが残る。
- semanticレベルの正確な逆依存閉包をどこまで実装するか。Roslyn SemanticModelはコンパイル全体に依存し、1ファイル変更が遠隔の型推論に波及しうる。型単位の依存グラフ近似(継承/実装/明示参照のみ)で『正しさ』を担保できるか、それとも変更時はcompilationを丸ごと作り直し再enrichだけ差分にするのが安全か。
- manifestに型間依存グラフ・mtime・enrichment全体を保存するとサイズが肥大化する(現状でもRawTypes+EnrichedTypesをWriteIndentedでJSON保存, SyntaxCacheModels.cs:35)。スキーマ/圧縮/分割をどうするか。ライブ更新頻度での書き込みコストとSave(SyntaxCacheStore.cs:54-76 のtmp+Move)の頻度制御は。
- watch/serveでブラウザへ差分をpushする経路(SSE/WebSocket/ポーリング)はどれにするか。既存ビューアはオフライン静的HTML(vendor同梱, CDN非依存)が思想なので、ローカルHTTP常駐の導入がそのゼロセットアップ・オフライン前提とどう折り合うか。
- --incrementalの完全解析対応で、フル解析とインクリメンタル解析のメトリクス完全一致(既存テスト IncrementalAnalysisTests.cs の Equal(full, incremental) 不変条件)を維持できるか。CouplingInfo等のグローバル集計(CouplingMetricsCalculator)は全依存が揃って初めて正しいので、差分enrichでも結合度は毎回全体再計算が必要かの線引き。
- 変更検出をファイル単位より細かく(型/メソッド単位の構文diff)する価値があるか。1ファイルに巨大型が複数ある場合のみ効くが、TypeIdentity/SyntaxTree差分の実装コストに見合うか。現状partial/interfaceのアドホック閉包(SyntaxIncrementalCollector.cs:192-241)を汎用化する際の優先度。

### ブラウザからソースへのジャンプ

現状: 解析JSONには既にソース位置情報が乗っているが、ビューアは一切使っていない。データモデル: TypeNodeInfo は FilePath(必須) と StartLine(nullable) を持つ(src/Unilyze/Pipeline/TypeInfo.cs:54,57)。MemberInfo も StartLine を持つ(同:73)。TypeMetrics は FilePath/StartLine を type からコピー(src/Unilyze/Metrics/CodeHealthCalculator.cs:49-50,164-165)。CodeSmell は Line(nullable)(src/Unilyze/Detectors/CodeSmellDetector.cs:47)だが、構造系検出(GodClass/LongMethod等)は Line を一切セットせず null のまま(CodeSmellDetector.cs に `Line:` 代入なし)。AsyncFlow/HotPath/Closure系のセマンティック検出のみ行を埋める(例 src/Unilyze/Detectors/AsyncFlowAsyncVoidCollector.cs:75)。

実測接地: `unilyze -p src/Unilyze -f json` 実行(/tmp/out.json)で filePath 727件 / startLine 3132件 / line 397件が出力に存在。filePath は絶対パス(例 `/Users/bigdra/.../CliArgValidation.cs`)、projectPath も絶対(`/Users/bigdra/.../src/Unilyze`)。method.startLine も実値(例 159)。ソースジャンプに必要な (file, line) は全て揃っている。

ビューア側: main.js(2436行)は filePath/startLine/line を一度も参照しない(grep該当ゼロ)。型詳細パネル renderTypeDetail(src/Unilyze/Templates/viewer/main.js:400-485)は name/kind/assembly/metrics/codeSmells/members/依存を描画するが、ソースリンク・行番号表示は皆無。codeSmells描画(同:438-443)も kind/message のみで line を捨てている。

配信経路: HTML は file:// で開かれる(src/Unilyze/Program.cs:167-178, TryOpenInBrowser)。serve/watch/HttpListener コマンドは存在しない(Program.cs サブコマンド一覧・--help いずれにも無し)。file:// オリジンでは fetch によるソースファイル読込が同一オリジン制約で不可。

JSON は inline script へ生埋め込み(src/Unilyze/Output/HtmlFormatter.cs:13-28)、`</script` のみ無害化、System.Text.Json既定エンコーダ依存(docs/threat-model.md)。相対パス化の前例は SARIF にあり Path.GetRelativePath(projectPath, FilePath) を使用(src/Unilyze/Output/SarifFormattingHelpers.cs:64-66)。ベンダJSはオフライン同梱だが ELK のみ CDN(index.html:73)。

#### 型詳細パネルにエディタ起動リンク(vscode:///file://)を追加 — データはJSON既存、C#変更ゼロ

- verdict: needs-revision
- effort: S / impact: high
- what: renderTypeDetail(main.js:400-485)に「ソースを開く」リンクを追加。type.filePath+(type.startLine||1)からvscode://file/{path}:{line}とfile://{path}:{line}を生成。codeSmells描画(同:438-443)はsmell.line??type.startLineを使い各smell行をクリック可能に、members(同:460-465)はmember.startLineを使う。editorScheme(vscode/idea/file)はツールバーのセレクタかlocalStorageで切替。
- why: ユーザー要件『ソースへ飛べる』の最短実装。必要な(filePath,startLine,line)は実測でJSONに存在(/tmp/out.json: filePath727/startLine3132/line397)するのにビューアが捨てている。C#側変更ゼロで完結。
- evidence: src/Unilyze/Templates/viewer/main.js:400-485,438-443,460-465; src/Unilyze/Pipeline/TypeInfo.cs:54,57,73
- 検証ノート: データ前提は成立。renderTypeDetailのtypeはtl[typeId](main.js:402)で、tlはDATA.types=TypeNodeInfoから構築(main.js:49-50)。TypeNodeInfoはFilePath(必須)/StartLine(nullable)を持つ(TypeInfo.cs:54,57)。MemberInfo.StartLineも存在(TypeInfo.cs:73)しtype.membersに乗る。codeSmells描画(main.js:438-443)が使うmetricsはtm[typeId]=TypeMetrics(main.js:412,53-54)でFilePath/StartLineをtypeからコピー済(CodeHealthCalculator.cs:164-165)。JSONはcamelCase(AnalysisResult.cs:48)なのでfilePath/startLine/lineで実在し、実測でも727/3132/397件出力(/tmp/out.json)。よってC#変更ゼロでリンク生成可能という主張は正しい。ただし重大な見落とし2点: (1) セキュリティ。escapeHtml(main.js:192-198)は&<>"のみエスケープしシングルクォート未処理。filePathはthreat-model.md:3-5で『リポジトリ制御の信頼しない値』と明記され、これをhref/URLへ素埋めするとjavascript:等のスキーム注入やhref属性ブレイクの新攻撃面を作る。リンク生成時はencodeURIComponentでパス成分をエンコードし、スキームはvscode/idea/file固定allowlistに限定する必要がある。提案文にこの対策が無い。(2) smell.lineは構造系smellではnullになる(提案5参照)。WhenWritingNull(AnalysisResult.cs:49)でnull時はlineキー自体が出力されないため、smell.line??type.startLineのフォールバックは正しいが『各smell行をクリック可能』の精度は構造系smellでは型先頭止まり。また『removed-in-after型』の合成スタブ(main.js:405-409)はfilePath/startLineを持たずジャンプ不可、ガードが要る。
- 修正案: リンク生成を共通関数buildSourceLink(filePath,line,scheme)に切り出し、(a)パス成分をencodeURIComponentでエンコード、(b)schemeをvscode/idea/fileのallowlistに限定、(c)filePath未定義(removedスタブ)時はリンクを出さない、をガードとして入れる。smell行はsmell.line??(member経由のstartLine)??type.startLineの順でフォールバック。これによりthreat-modelの信頼境界を維持したままC#変更ゼロを保てる。

#### ブラウザ内ソース閲覧: serveコマンド+ローカルHTTPサーバ+ハイライト済みソース配信(ユーザー優先意向)

- verdict: valid
- effort: L / impact: high
- what: `unilyze serve -p <path>`(新サブコマンド、Program.csのルーティングに追加)でHttpListenerを起動。/(viewer HTML)、/api/source?file=<rel>&line=<n>(プロジェクト配下のcsをサーバ側でシンタックスハイライトしHTML/JSONで返す)を配信。viewerは詳細パネル内インラインまたは右ペインでソースを表示し該当行へスクロール。ハイライトはRoslyn Classifier(既にMicrosoft.CodeAnalysis依存)か軽量トークナイザで実装、CDN非依存を維持。
- why: ユーザーは『エディタを開くのが面倒=ブラウザ内閲覧を優先』。file://では同一オリジン制約でfetch不可なため、軽量ローカルサーバが必須。ライブ更新ゴール(serve/watch)とも基盤を共有できる。
- evidence: src/Unilyze/Program.cs:167-178(file://でopen, serve無し); src/Unilyze/Templates/viewer/index.html:73(ELK以外はオフライン同梱); 既存依存 Microsoft.CodeAnalysis(src/Unilyze/Pipeline/TypeInfo.cs:7-9)
- 検証ノート: 前提すべて確認。Program.cs:165-181のHtml経路はfile://でTryOpenInBrowser(Program.cs:177-178)、serve/watch/HttpListenerサブコマンドは不在(Program.cs全体およびルーティングに無し、grep該当ゼロ)。file://オリジンでのfetch同一オリジン制約の指摘は妥当で、ブラウザ内ソース表示には軽量ローカルサーバが事実上必須という結論は正しい。Roslyn(Microsoft.CodeAnalysis)依存は既存(TypeInfo.cs:7-9)でClassifierも利用可能。ベンダJSはオフライン同梱だがELKのみCDN(index.html:73 unpkg.com)なので『CDN非依存を維持』はserve化でむしろ自前ホスト可能になり整合。ライブ更新ゴールと基盤共有という設計判断も妥当。effort:Lは新サブコマンド+HTTPサーバ+ハイライト+viewer改修を含むため適切。提案単体としては前提に誤りなし。

#### パストラバーサル防御: source配信はprojectPath境界内のみ・拡張子allowlist・正規化後再検証

- verdict: valid
- effort: M / impact: high
- what: /api/source の file パラメータを Path.GetFullPath で正規化し、projectPath配下(StartsWith(projectRoot)かつ`..`除去後)かつ拡張子.csのみ許可。範囲外はキャンセル(404)。SARIFと同じ Path.GetRelativePath(projectPath, FilePath) で相対キーを採用し、絶対パスをクライアントに露出しない。
- why: serveでローカルFSを露出するとパストラバーサル(../../etc/passwd等)のリスク。既存threat-modelは『生成HTMLは信頼しない』前提だが、動的サーバ追加で新たな攻撃面が生まれる。ユーザー私的グローバル指示の『サニタイズを怠らない』にも合致。
- evidence: docs/threat-model.md:3-7(既知の信頼境界); src/Unilyze/Output/SarifFormattingHelpers.cs:64-66(相対パス化の前例)
- 検証ノート: 正当な防御策。threat-model.md:3-7は『生成HTMLは信頼しない成果物』が前提で、動的サーバ追加が新攻撃面を生むという認識は正しい。相対パス化の前例はSARIFにあり、SarifFormattingHelpers.cs:64-66でGetRelativePath(projectPath,FilePath)を使用済み(GetRelativePathの実体はGetRelativePathヘルパ)。Path.GetFullPathで正規化→projectRoot配下StartsWith検証→.cs拡張子allowlistという多層は標準的で妥当。1点補強: StartsWith比較はOSの大文字小文字/末尾セパレータ差で誤判定し得るため、両者を末尾セパレータ付きで正規化しOrdinal比較すること、シンボリックリンク経由の境界越えにも留意が必要(GetFullPathはシンボリックリンクを解決しないため、必要ならResolveLinkTarget併用)。提案の方向性は正しく前提誤りなし。

#### HTMLにfilePathの絶対パスを埋めない: 相対パス化+editorRootをviewer設定で注入

- verdict: valid
- effort: M / impact: medium
- what: HtmlFormatter.Generate(Program.cs:169)に渡す前段で、JSON内の各filePathを Path.GetRelativePath(result.ProjectPath, filePath) に置換し、projectPath自体は別フィールドworkspaceRootとして1回だけ持たせる。viewerはvscode://file/{workspaceRoot}/{relPath}:{line}を組み立てる。editorRootはユーザーがブラウザ側で上書き可能にし、別マシンで開いた共有HTMLでも機能させる。
- why: 現状filePathは絶対パス(/tmp/out.json実測)。静的HTMLを共有するとローカルディレクトリ構造とユーザー名が漏れる(プライバシー/移植性)。相対化すればエディタリンクのworkspaceRoot差し替えだけで別環境でも動く。
- evidence: src/Unilyze/Program.cs:162,169(絶対JSONをそのまま埋込); /tmp/out.json実測でfilePath/projectPath共に絶対; src/Unilyze/Output/SarifFormattingHelpers.cs:64-66(相対化の確立した手法)
- 検証ノート: 前提確認。filePathは絶対パスで出力(実測/tmp/out.json: /Users/bigdra/.../CliArgValidation.cs)、projectPathも絶対(/Users/bigdra/.../src/Unilyze)。Program.cs:169でHtmlFormatter.Generate(json,result.ProjectPath)に絶対JSONをそのまま渡す。相対化の前例はSarifFormattingHelpers.cs:64-66で確立。静的HTML共有時にユーザー名/ディレクトリ構造が漏れるという指摘は妥当。1点注意: filePathはTypeNodeInfo(types)とTypeMetrics(typeMetrics)の両方に重複して存在(CodeHealthCalculator.cs:164でtypeからコピー)するため、相対化はHtmlFormatter手前の1箇所ではなくAnalysisResult内の両配列に施す必要があり、『JSON内の各filePathを置換』の実装はtypes/typeMetricsの両系統を漏れなく対象にすること。またJSON文字列を正規表現置換ではなくシリアライズ前のモデル段階で相対化する方が安全(生埋め込みのescape前提=HtmlFormatter.cs:27-28を崩さない)。方向性は正しくvalid。

#### 構造系CodeSmellにLineを補完し、警告クリックを正確な行へ接地

- verdict: valid
- effort: M / impact: medium
- what: CodeSmellDetector(src/Unilyze/Detectors/CodeSmellDetector.cs)の各 new CodeSmell に、対応するmemberのStartLine(LongMethod/HighComplexity等はmethod.StartLine、GodClass等はtype.StartLine)をLineに渡す。viewerは smell.line を優先しジャンプ先を決定。
- why: 現状397件のlineはセマンティック系のみで、構造系smell(GodClass/LongMethod等)はLine=null→型先頭にしか飛べず精度が低い。member.StartLineは既にMemberInfo/MethodMetricsに存在(TypeInfo.cs:73, CodeHealthCalculator.cs:118)するので接続するだけ。
- evidence: src/Unilyze/Detectors/CodeSmellDetector.cs:47,94,127,138(Line渡し無し); src/Unilyze/Metrics/CodeHealthCalculator.cs:111-118(method.StartLine保持)
- 検証ノート: 事実確認完了。CodeSmellはLine(nullable,既定null)を持つ(CodeSmellDetector.cs:47)。構造系検出のnew CodeSmellはすべてLineを渡さずnull: GodClass(:94)、LongMethod(:127-129)、ExcessiveParameters(:138-141)、HighComplexity(:156-159)、DeepNesting(:172-174)、LowMaintainability(:183-186)、LowCohesion(:194-197)、HighCoupling(:209-212)、DeepInheritance(:221-224)。メソッド系検出器はMethodMetrics methodを受け取り(CodeSmellDetector.cs:99-106,109-110)、MethodMetricsはStartLineを保持(CodeHealthCalculator.cs:111,118でm.StartLineを格納)。よってLongMethod/HighComplexity/DeepNesting/ExcessiveParameters/LowMaintainabilityはmethod.StartLineをLineに渡せる。GodClass/LowCohesion/HighCoupling/DeepInheritanceは型レベルなのでmetrics(TypeMetrics)経由でtype.StartLine相当を使う(TypeMetrics.StartLineはCodeHealthCalculator.cs:165でtype.StartLineをコピー)。提案の『method.StartLine/type.StartLineで補完』は実装可能で前提に誤り無し。WhenWritingNull(AnalysisResult.cs:49)によりLine未設定時はviewerでsmell.lineがundefinedになるためフォールバックは引き続き必要だが、提案はそれを織り込み済み。

#### diffビューにも同じソースジャンプを通す(difit/ライブ連携の布石)

- verdict: needs-revision
- effort: M / impact: medium
- what: diffの GenerateWithDiff(src/Unilyze/Output/HtmlFormatter.cs:10)経路でも、TypeDiff側にfilePath/startLineを通し(現状TypeDiffにソース位置が無いか要確認)、変更型・変更メソッドの行へ飛べるようにする。提案1のリンク生成を共通関数化し、analyze/diff両viewerで共有。
- why: diff表示(difit相当)ゴールと本観点が交差する箇所。変更箇所からソース/diffハンク行へ直接飛べると価値が高い。提案1のリンク生成器を使い回せば追加コストは小さい。
- evidence: src/Unilyze/Output/HtmlFormatter.cs:10-11(GenerateWithDiff経路); src/Unilyze/Diff/DiffResult.cs:16(TypeDiff定義—ソース位置フィールドの有無は実装時に確認要)
- 検証ノート: TypeDiffにソース位置フィールドが無いのは事実(DiffResult.cs:16-25: TypeKey/TypeName/Namespace/Assembly/Status/各Deltas/MethodDiffs/SmellChangesのみ、FilePath/StartLine無し)。しかし『TypeDiff側にfilePath/startLineを通す』という前提は不要かつ的外れな可能性が高い。diff HTMLはGenerateWithDiff(ctx.AfterJson,ctx.DiffJson,...)を呼ぶ(DiffRunner.cs:528)。AfterJsonはAfter側AnalysisResultの完全JSONで、types/typeMetricsにfilePath/startLineを含む。viewerのrenderTypeDetailはまずtl[typeId](=After types由来,main.js:402)を引くため、changed/improved/degraded/unchanged/added型は提案1の仕組みだけでAfterソースへジャンプ可能で、TypeDiffへの位置追加もHtmlFormatter経路の共通化も不要。本当にTypeDiff拡張が要るのはremoved型のみ——removed型はAfterに存在せずtlに無く、dlのスタブ(main.js:405-409)から合成されfilePath/startLine欠落。よって『diff全般でTypeDiffにソース位置を通す』は過剰で、正しくはremoved型に限りBefore側の位置を載せる話。
- 修正案: (1)提案1のbuildSourceLink共通関数をanalyze/diff両viewerで共有するのは妥当(これは正しい)。(2)ただしchanged/added型はAfter AnalysisResult(AfterJson)のtypes由来でtl経由ジャンプ済みなので追加コストゼロ。TypeDiff拡張は不要。(3)removed型のソースジャンプが要件なら、TypeDiffにBefore側FilePath/StartLineを1組だけ追加し、Before AnalysisResultから引いてremovedスタブ(main.js:405-409)に載せる。MethodDiff行ジャンプまで求めるならMethodDiffにStartLineを足す。スコープをこの2点(removed型/メソッド行)に限定し、diff全般の汎用拡張という表現を改める。

Open questions:

- ブラウザ内閲覧 vs エディタ起動の優先度: ユーザーは前者優先だが、serve常駐(L)を先に作るか、まずエディタリンク(S)で価値を出してからserveを足す段階導入か。lazygit的ライブ更新ゴールとserve基盤を共有する前提なら後者→前者の順が合理的。
- エディタスキームのデフォルト: vscode://file/ を既定にするか、ユーザー環境(JetBrains Rider/idea://、cursor://、zed://)を検出/選択させるか。Unityユーザーは Rider/VS が多く vscode 固定は外れやすい。
- 絶対パス露出の扱い: 共有用途を想定し相対パス化するか、ローカル専用ツールと割り切り絶対パスのまま簡潔さを優先するか(ゼロセットアップ思想との兼ね合い)。
- serve追加時の依存とAOT: 現状PublishAot/Trimmingか要確認。HttpListenerやRoslyn Classifierがネイティブ発行・トリミングと両立するか、配布バイナリサイズへの影響は。
- ハイライト方式: Roslyn Classifier(意味色付け・正確だがsemantic compilation必要)か、軽量な構文トークナイザ(高速・CDN非依存)か。--incremental/SyntaxOnly経路との整合をどう取るか。
- ライブ更新との結線: serveがファイル監視+再解析(--incremental活用)してWebSocket/SSEで差分push する設計に進む場合、ソース配信APIもその監視ループに相乗りさせるか別系統にするか。

### difit的コードdiff表示

現状: unilyzeの「diff」は完全にメトリクス差分であり、ソースコードのテキストdiffは一切扱っていない。

データモデルの事実:
- 差分の入口は `DiffCalculator.Compare(before, after)`（src/Unilyze/Diff/DiffCalculator.cs:17）。比較対象は `TypeMetrics`/`MethodMetrics` のみで、ソーステキストは入力に含まれない。
- 結果型 `DiffResult`/`TypeDiff`/`MethodDiff`（src/Unilyze/Diff/DiffResult.cs:8-47）には `FilePath` も `StartLine` も無い。`MethodDiff` は `MethodName`+`ParameterCount`+`IntDeltas` のみ（DiffResult.cs:8-12）。diff結果からは「どのファイルの何行目か」へ直接たどれない。
- 上流の `TypeMetrics` には `FilePath`/`StartLine`（src/Unilyze/Metrics/CodeHealthCalculator.cs:49-50）、`MethodMetrics` には `StartLine`+`LineCount`（同5-12）、`CodeSmell` には `Line`（src/Unilyze/Detectors/CodeSmellDetector.cs:47）が存在。ソース位置は解析側に在るがdiff側へ伝播していない。

ソースジャンプ/ソース表示は皆無:
- ビューア `main.js` は `filePath`/`startLine` を一切参照しない(grep0件)。レンダリングは `renderDiffSections`(src/Unilyze/Templates/viewer/main.js:144)等で `innerHTML`(16箇所)によるHTML文字列組み立て。diffはメトリクスdeltaの矢印表示(`deltaSpan` main.js:119、`renderDiffSections` 144-190、テーブル行delta注記 592-606、グラフ着色 953-959)に留まる。
- HTML出力は解析JSON+diffJSONをテンプレへ生埋め込みするのみ(src/Unilyze/Output/HtmlFormatter.cs:13-25)。ソース本文はHTMLに同梱されず、閲覧時点でファイルへアクセスする経路も無い(serve/watch/HTTP無し: grep0件)。

行レベルdiffの「位置→span」変換の既存資産:
- SARIF出力が唯一ソースの行範囲を算出。`SarifFormattingHelpers.BuildRegion`(src/Unilyze/Output/SarifFormattingHelpers.cs:161-188)が `smell.Line` / `method.StartLine`+`LineCount` / `type.StartLine`+`LineCount` から startLine/endLine を導出。`GetRelativePath`(230-235)で `ProjectPath` 相対へ正規化。`TypeMetrics.FilePath` は解析時の生パスで、相対化は出力時に各フォーマッタが実施(FindingFingerprint/SarifFormattingHelpers が同じ正規化を使用)。

git連携の既存資産:
- `--base-ref` は一時worktreeで基準を再解析(src/Unilyze/Runners/DiffRunner.cs:281-303、`GitWorktreeSession` src/Unilyze/History/GitWorktreeSession.cs:29-61)。ただしworktreeは `RunComparison` 完了後の `finally` で必ず破棄(DiffRunner.cs:299-302)。HTMLを開く時点でbefore側ソースは消えている。
- git実行ヘルパ `GitProcess.Run`(src/Unilyze/History/GitProcess.cs:7)はArgumentList経由でシェル非介在(GitProcess.cs:18-19)。`git show`/`git diff` を安全に呼べる基盤は既存。

セキュリティ前提:
- HtmlFormatterのXSS対策は `</script` 置換のみ(HtmlFormatter.cs:27-28)、本文は System.Text.Json 既定エンコーダの暗黙の `<` エスケープ依存。main.js側は `escapeHtml`(192-198)を一部で使うが、ソース本文をHTMLへ流す経路は未整備。docs/threat-model.md:5 が `</script>` 注入を既知点として明記。生ソースをHTMLへ入れるなら新たなXSS面が増える。

#### DiffResultにソース位置(FilePath/StartLine/EndLine)を伝播させる

- verdict: needs-revision
- effort: M / impact: high
- what: TypeDiff に FilePath/StartLine/EndLine、MethodDiff に StartLine/EndLine を追加し、DiffCalculator.ComputeTypeDiff(DiffCalculator.cs:175)/ComputeMethodDiffs(同236)で after 側 TypeMetrics.FilePath+StartLine+LineCount / MethodMetrics.StartLine+LineCount から埋める。行範囲導出は SarifFormattingHelpers.BuildRegion(SarifFormattingHelpers.cs:161-188) を共通ヘルパへ抽出して再利用。FilePathは出力時に GetRelativePath(同230) でProjectPath相対へ正規化してから載せる。
- why: ソースジャンプ・行レベルdiff・diff⇔メトリクスdelta連動の全ての前提。現状diff結果は劣化した型/メソッド名しか持たず、ユーザー要件『ブラウザからソースへ飛ぶ』『diffを画面に出す』へ橋渡しできない。位置算出ロジックは既にSARIFに存在し新規実装不要。
- evidence: src/Unilyze/Diff/DiffResult.cs:8-25, src/Unilyze/Diff/DiffCalculator.cs:175-265, src/Unilyze/Output/SarifFormattingHelpers.cs:161-188, src/Unilyze/Metrics/CodeHealthCalculator.cs:5-50
- 検証ノート: データモデルの主張は全て実コードと一致。MethodDiff(DiffResult.cs:8-12)はMethodName/ParameterCount/Status/IntDeltasのみ、TypeDiff(DiffResult.cs:16-25)にFilePath/StartLineなし。上流のTypeMetrics.FilePath(CodeHealthCalculator.cs:49)/StartLine(:50)、MethodMetrics.StartLine(:12)/LineCount(:11)は実在。BuildRegion(SarifFormattingHelpers.cs:161-188)の行範囲導出ロジックも提案通り存在し再利用可能、GetRelativePath(:230-235、提案は:230と表記、許容範囲)も正確。DiffResultは[JsonSerializable](AnalysisResult.cs:58)登録済みでsource-gen JSONに自動シリアライズされるためフィールド追加は配線済み。ただし2点の見落としあり。(1)MethodMetricsにEndLineプロパティは存在しない(CodeHealthCalculator.cs:5-16)。提案が言う『MethodDiffにStartLine/EndLineを追加』のEndLineはMethodMetricsから直接取れず、BuildRegionと同じくStartLine+LineCount-1で算出する必要がある。提案文の『MethodMetrics.StartLine+LineCount から埋める』は正しいがEndLineの算出が暗黙。(2)致命的: ComputeMethodDiffs(DiffCalculator.cs:236-265)はbefore/after両方に存在するメソッドのみMethodDiffを生成する(:246-262でbeforeをループしafterByKeyにマッチした時だけdiffs.Add)。新規追加・削除メソッドはMethodDiffに一切含まれない。そのためソースジャンプ対象が『変更されたが両snapshotに存在する』メソッドに限定され、追加メソッド(diffで最も注目される)へのジャンプが欠落する。
- 修正案: MethodDiffにStartLine(必須)とEndLine(StartLine+LineCount-1で算出、LineCount<=0ならnull)を追加し、SarifFormattingHelpers.BuildRegionの行範囲算出を共通ヘルパ(例 SourceRegionHelper.ResolveLineRange)へ抽出して両者で共用する。加えてComputeMethodDiffs(DiffCalculator.cs:236-265)を拡張し、afterのみに存在する追加メソッド・beforeのみの削除メソッドもMethodDiff化する(またはTypeDiffに別途AddedMethods/RemovedMethodsを設ける)。after側位置がないremoved methodはジャンプ不可と明示する。FilePathはTypeDiffに1本持たせ、出力時GetRelativePathでProjectPath相対化する方針はそのままで妥当。

#### 変更メソッドのリスク分類(deltaScore)を行レベルdiffのハイライトへ接続

- verdict: needs-revision
- effort: M / impact: high
- what: DiffCalculator.CountChangedMethods(DiffCalculator.cs:95-133)が既に持つ『変更メソッド×high/low risk』判定を MethodDiff 上のフラグ(例 IsHighRiskChange)として露出。ビューアの行レベルdiff表示で変更ハンク(MethodDiff.StartLine..EndLine)を IsHighRisk(DiffCalculator.cs:150-153: CognitiveComplexity>=15 / Nesting>=4 / Line>=80)に応じ色分け。既存 deltaSpan/diffRowClass(main.js:116-127) のup/down配色とトーンを揃える。
- why: difitとの差別化点。単なるテキストdiffではなく『この変更が品質的に危険か』を行に重ねるのがunilyzeの強み。deltaScoreの分子分母を生む判定は既存(DiffCalculator.cs:64-81)で表示へ繋ぐだけ。ユーザー要件『変更メソッドのリスクハイライト』に直結。
- evidence: src/Unilyze/Diff/DiffCalculator.cs:95-153, src/Unilyze/Diff/DiffResult.cs:8-12, src/Unilyze/Templates/viewer/main.js:116-127
- 検証ノート: IsHighRisk(MethodMetrics)の閾値(DiffCalculator.cs:150-153: CognitiveComplexity>=15 / MaxNestingDepth>=4 / LineCount>=80)は実コードと完全一致。CountChangedMethods(:95-133)が変更メソッド×high/low risk判定を行い、CountRisk(:155-161)でlow/highをカウントしdelta score分子分母を生むのも事実(:78-80)。main.jsのdeltaSpan up/down配色(:119-127)も実在。ただし重大な事実誤認: CountChangedMethodsはCalculateDeltaScore経路(:64-81)専用で、ComputeTypeDiff→ComputeMethodDiffs経路(:175-265)とは完全に別物。MethodDiffを生成する経路(ComputeMethodDiffs)はIsHighRiskを一切呼ばない。提案は『既存のMethodDiff上のフラグとして露出』『判定は既存で表示へ繋ぐだけ』と言うが、CountChangedMethodsとComputeMethodDiffsはメソッドのマッチング方式すら異なる(前者はDictionary<key,List>で重複キー対応・after全件ループ、後者はbeforeループでafter1件マッチ)。risk判定を『繋ぐだけ』では済まず、ComputeMethodDiffs側でIsHighRisk(a)を新規に呼び出してMethodDiffへ載せる実装追加が必要。『既存(:64-81)で表示へ繋ぐだけ』は過小評価。
- 修正案: ComputeMethodDiffs(DiffCalculator.cs:260)のdiffs.Add時にIsHighRisk(a)(:150-153)を評価してMethodDiff.IsHighRiskChangeへ載せる。CountChangedMethods経路のロジックを共有したいならIsHighRisk判定だけを共通利用し、変更検出ロジック自体は両経路で別管理のままにする(マッチング方式の差異を統一するのは別タスクでリスク高)。main.js側はMethodDiff.isHighRiskChangeで色分けし、deltaSpanのup/down配色トーンに揃える方針は妥当。

#### ソーステキストdiffは git show/diff をオンデマンド実行する serve 経路で供給する

- verdict: valid
- effort: L / impact: high
- what: 将来の serve/watch 経路からのHTTPエンドポイント /diff?file=<rel>&base=<ref> で GitProcess.Run(repoRoot,"diff",base,"--",relPath) または git show base:relPath を実行し unified diff か before/after本文を返す。ビューアは MethodDiff.StartLine..EndLine でハンクを絞り side-by-side描画。--base-ref ワークフロー(DiffRunner.cs:281-303)はworktreeを即破棄するため静的HTML単体ではbefore本文を持てない→ソーステキストdiffはserve前提と明示。relPath/base は厳格バリデーション(リポジトリ外参照・.. ・引数注入の遮断)。
- why: ユーザー要件『difitのようにdiffを画面表示』。だが静的HTML同梱方式ではbefore側ソースを保持できない(worktree破棄: DiffRunner.cs:299)。GitProcessでArgumentList経由の安全なgit実行基盤は既存(GitProcess.cs:18-19)で再実装不要。ゼロセットアップ思想を壊さずローカルgitでオフライン完結。
- evidence: src/Unilyze/Runners/DiffRunner.cs:281-303, src/Unilyze/History/GitWorktreeSession.cs:63-101, src/Unilyze/History/GitProcess.cs:7-19
- 検証ノート: 中核主張は全て実コードで裏取り済み。GitWorktreeSessionはRunBaseRefComparisonのfinallyでsession?.Dispose()(DiffRunner.cs:299-302)、Dispose(GitWorktreeSession.cs:63-101)でworktree remove --force+ディレクトリ削除を実行→HTMLを開く時点でbefore側ソースは消えるという主張は正確。GitProcess.RunはArgumentList経由(GitProcess.cs:18-19)でシェル非介在、UseShellExecute=false(:15)で安全なgit実行基盤として既存。git show/git diffはコードベースのどこでも未使用(rg確認、GitWorktreeSessionはworktree/rev-parseのみ)で新規導入になる。serve/watch/HTTPエンドポイントも一切なし(Program.csにserve/watchコマンドなし、HttpListener未使用)。『静的HTML単体ではbefore本文を持てない→serve前提』の論理は正しい。relPath/baseの厳格バリデーション要求も妥当(GitProcessはArgumentListで引数注入は防げるがリポジトリ外参照・..トラバーサルは別途検証が要る)。effort L(大)も新規serve基盤+git連携+バリデーションを考えれば妥当。

#### メトリクスdeltaパネルから該当ソースdiffハンクへスクロール連動させる

- verdict: needs-revision
- effort: M / impact: medium
- what: 既存 renderDiffSections(main.js:144-190) の 'Methods Changed' 各行(166-174)に対応する MethodDiff.StartLine へのデータ属性/リンクを付与。クリックで同画面のソースdiffペインを当該ハンクへスクロール+ハイライト。'Changes vs Baseline'/'Smells Δ' も Line(CodeSmellDetector.cs:47)があればジャンプ可能に。現状これらは表示のみで遷移先を持たない。
- why: ユーザー要件『メトリクスdeltaとコードdiffの連動表示』。今は左にメトリクス差分・別物としてのソースという分離が起きる。deltaパネルとdiffハンクを同一画面で双方向に結ぶことがエディタを開かずに済ませる体験の核。表示骨格は既存で遷移配線の追加で済む。
- evidence: src/Unilyze/Templates/viewer/main.js:144-190, src/Unilyze/Diff/DiffResult.cs:8-12, src/Unilyze/Detectors/CodeSmellDetector.cs:47
- 検証ノート: renderDiffSections(main.js:144-190)の構造は正確: 'Methods Changed'各行(:166-174)はmd.methodName/parameterCountのみ表示で遷移先データ属性なし、'Changes vs Baseline'(:151-161)/'Smells Δ'(:177-188)も表示のみ。CodeSmell.Line(:47)は実在。だが前提に2つの綻び。(1)この提案はMethodDiffがStartLineを持つこと(提案1の実装完了)に依存するが、提案1で指摘した通りMethodDiffは現状『両snapshotに存在する変更メソッド』しか含まず追加メソッドが欠落する→deltaパネルのMethods Changed行が追加メソッドを表示しないため、最も飛びたい新規メソッドへ連動できない。(2)Smells Δのジャンプ可否: smell.Lineは多くの構造系smell(GodClass/LongMethod/HighComplexity)でnull(CodeSmellDetector.csでこれらはLine引数なしで構築、SarifFormattingHelpers.cs:163はsmell.Line>0のときのみ使いそれ以外はmethod/type.StartLineへフォールバック:176-181)。アロケーション/例外系smell(BoxingDetector.cs:59等がStartLinePosition.Line+1を設定)のみLineを持つ。提案文の『Line(CodeSmellDetector.cs:47)があれば』の条件付き表現は正しいが、CodeSmellDetector.cs:47はLineフィールドの宣言位置であって『Lineが常にある』根拠ではない点に注意。
- 修正案: 提案1の修正(追加/削除メソッドもMethodDiff化、MethodDiff.StartLine付与)を前提条件として明記する。Smells Δのジャンプはsmell.Lineがnullの構造系smellではBuildRegion(SarifFormattingHelpers.cs:172-181)と同じくmethod/type.StartLineへフォールバックして遷移先を導出する。これら位置情報は提案1でDiffResultへ伝播済みであることが前提。スクロール連動の配線追加自体(data属性+クリックハンドラ)は表示骨格既存で軽量という評価は妥当。

#### ソース本文表示の前にXSSサニタイズ層を確立する

- verdict: needs-revision
- effort: S / impact: medium
- what: ソース/diff本文をビューアへ載せる経路では、サーバ側で WebUtility.HtmlEncode 相当で全本文をエスケープしてから返すか、ビューアの escapeHtml(main.js:192-198) を必ず通す方針を固定。innerHTML 直挿入(16箇所)にソース本文を混ぜない。serve経路レスポンスは text/plain か構造化JSON(本文はエンコード済み文字列)に限定。threat-model.md にソース表示の新規攻撃面(悪意あるソース内 </script>/HTML)を追記。
- why: 現状のHTML安全性は </script 置換(HtmlFormatter.cs:27-28)とJSON既定エンコーダ依存のみで、生ソース本文を流す前提では不十分。解析対象リポジトリ自体が攻撃者制御下のソースを含みうる(他人のリポジトリ解析)。docs/threat-model.md:5 が既に </script> 注入を既知点として挙げており、ソース表示はこの面を直接拡大する。
- evidence: src/Unilyze/Output/HtmlFormatter.cs:27-28, src/Unilyze/Templates/viewer/main.js:192-198, docs/threat-model.md:5
- 検証ノート: セキュリティ懸念の方向性は正当だが現状認識に不正確さがある。提案は『現状のHTML安全性は</script置換(HtmlFormatter.cs:27-28)とJSON既定エンコーダ依存のみ』と言うが、HtmlFormatterは既にWebUtility.HtmlEncode(:1のusing System.Net、:24でtitleに適用)も使っている。EscapeInlineScriptPayload(:27-28)はJSON payloadの</script置換のみだが、これはthreat-model.md:6が明記する設計(System.Text.Jsonの<エスケープ+</script書き換えの二段)と一致する。threat-model.md:4-6は確かに</script>注入を既知点として挙げる(提案のthreat-model.md:5引用は正確)。escapeHtml(main.js:192-198)も実在し&/</>/"をエスケープ。問題はescapeHtmlがmain.js全16箇所のinnerHTMLで一貫適用されていない点(renderDiffSectionsは引数escFnでescapeHtmlを通すが:446は呼出時escFn未指定でデフォルト適用、一方グラフ着色等は別経路)。生ソース本文を新たに流すなら追加防御が要るという結論は妥当。effort S(小)はサーバ側エンコード方針固定+threat-model追記なら妥当だが、main.js全innerHTML経路の監査込みなら過小。
- 修正案: 現状記述を訂正: HtmlFormatterはtitleにWebUtility.HtmlEncode適用済み・JSON payloadはSystem.Text.Json既定<エスケープ+</script書き換えの二段防御(threat-model.md:6記載の設計)である、と正しく前提を述べる。その上で『生ソース本文という新しい大きな攻撃面』に対しては、serve経路レスポンスをtext/plainかエンコード済みJSON文字列に限定し、ビューア側でinnerHTML直挿入する前に必ずescapeHtml(main.js:192-198)を通す(diff/ソース表示専用の描画関数を新設し、その関数内でのみ本文を扱う)。effortはmain.js既存16 innerHTML経路への影響確認込みでM寄りに見積もる。

#### 行レベルdiffはvendor同梱の軽量unified→side-by-side変換で描画する

- verdict: valid
- effort: L / impact: medium
- what: serve経路が返す unified diff(git diff 出力)または before/after 2本文を、ビューア内のJSで side-by-side(2カラム)へ変換描画する小モジュールを追加。vendor/ 同梱方針(Cytoscape等をローカル埋め込み: HtmlFormatter.cs:21)に合わせCDN非依存・オフライン動作の軽量実装に。MethodDiff.StartLine..EndLine でハンクを絞り大ファイルでも変更近傍のみ描画。
- why: ユーザー要件『side-by-side行レベルdiff』。既存ビューアはCytoscape/dagreをvendor同梱でオフライン動作させる設計(CDN非依存)で、diff描画も同方針に揃えるのが資産保全。サーバ側は生のgit出力を返すだけに留め整形をクライアントに寄せると serve のロジックが薄く保てる。
- evidence: src/Unilyze/Output/HtmlFormatter.cs:20-24, src/Unilyze/Templates/viewer/main.js:144-190
- 検証ノート: vendor同梱・CDN非依存の設計方針は実コードで確認: HtmlFormatter.Render(:20-24)はHtmlTemplate.VendorScripts(__VENDOR_SCRIPTS__)を埋め込み、Cytoscape/dagre等をローカル同梱する設計(タスク前提とも一致)。renderDiffSections(main.js:144-190)が既存のdiff表示骨格である点も正確。提案の『サーバ側は生のgit出力を返し整形をクライアントへ寄せる』はserve経路(提案3)前提で一貫しており、提案3がvalidなら本提案も成立。MethodDiff.StartLine..EndLineでハンクを絞る点は提案1のMethodDiff位置付与(修正版)に依存する。依存関係(提案3のserve基盤・提案1の位置情報)が明示されており、それ自体の主張に実コードとの矛盾はない。effort L(大)もvendor互換の軽量diff描画モジュール新規実装として妥当。

Open questions:

- ソーステキストdiffは serve/watch 前提でしか成立しない(--base-refのworktreeは即破棄: DiffRunner.cs:299)。静的HTML単体でも限定的にdiffを出すなら生成時にbefore/after本文を抜粋してHTMLへ同梱する案があるが、これはゼロセットアップ思想とファイルサイズ/XSS面のトレードオフ。どちらを正とするか。
- diffの基準(base)は git ref か、それとも2つの解析JSONスナップショットか。前者ならソース本文をgitから引けるが後者(ファイルdiff経路: DiffRunner.cs:236)は元リポジトリの状態が不定でソース取得不能。serve経路ではgit ref基準に絞るべきか。
- FilePath正規化の基準パス選定。TypeMetrics.FilePath は解析時の生パスで、ProjectPath相対化は出力時に各フォーマッタが実施(SarifFormattingHelpers.GetRelativePath:230, FindingFingerprint)。diffにFilePathを載せる際、リポジトリルート相対(git diff用)とProjectPath相対(SARIF用)のどちらを正規形にするか統一が必要。
- メソッドのマッチングキーは MethodKey=MethodName:ParameterCount(DiffCalculator.cs:267)でオーバーロードや行移動に弱い。行レベルdiffと突き合わせる際、リネーム/シグネチャ変更したメソッドの before↔after 対応が崩れる。git diffのハンク境界とメトリクス上のメソッド対応をどう整合させるか。
- ソースジャンプの宛先(ブラウザ内ビュー / エディタ起動 / GitHub等のURL)をどう選ばせるか。エディタ起動は serve のローカルプロセス前提で、静的HTML単体では vscode:// / file:// スキーム頼みになりブラウザ制約を受ける。観点横断の設計判断。
- 巨大diff/バイナリ/生成コードの扱い。解析は generated code 除外オプションを持つが git diff は除外しない。diff表示で生成ファイルや巨大ハンクをどう間引くか(MethodDiff.StartLine/EndLine外の変更は表示するか)。

### コードフロー図の描画

現状: 現状のフロー図は「型粒度の依存グラフ」のみで、メソッド粒度の呼び出しグラフ・制御フローは一切存在しない。

エッジモデル: `TypeDependency(FromType, ToType, Kind, FromTypeId, ToTypeId)` が唯一のグラフ辺（src/Unilyze/Pipeline/TypeInfo.cs:13-18）。`DependencyKind` は Inheritance/InterfaceImpl/FieldType/PropertyType/ConstructorParam/MethodParam/ReturnType/EventType/GenericConstraint/DIRegistration/SerializedReference の11種で、すべて「型→型」（TypeInfo.cs:20-33）。`DependencyBuilder.Build` は型のシグネチャ（基底/IF/メンバ型/パラメータ型/制約）からのみ辺を作り、メソッド本体の呼び出しは見ない（TypeInfo.cs:369-446, CollectMemberDeps 394-415）。

ビューア: ノードは namespace 複合ノード(`cp:`)・型ノード(`t:`)の2階層のみ。辺は `DATA.dependencies` を型IDで結ぶだけ（src/Unilyze/Templates/viewer/main.js:827-879 typeNodeElement/dependencyElements）。メンバ情報はサイドパネルに列挙されるだけでグラフ化されない（main.js:460-463）。レイアウトは ELK(layered/DOWN/ORTHOGONAL) を CDN ワーカーで実行し、失敗時 dagre へフォールバック（main.js:1182-1271、elkWorkerUrl が unpkg.com 依存=オフライン非対応）。cytoscape/dagre は同梱（src/Unilyze/Templates/vendor/）。

呼び出し抽出の素地は既にある: RfcCalculator が `InvocationExpressionSyntax` を走査し `model.GetSymbolInfo` で `IMethodSymbol` を取得済み。ただし呼び出し先 symbol をカウントするだけで辺として保持しない（src/Unilyze/Metrics/RfcCalculator.cs:51-61）。SemanticModel 基盤も完備: CompilationResult + ModelCache を prewarm し型ごとに共有（src/Unilyze/Pipeline/SemanticEnricher.cs:46-79, 113-120）。辺生成と semantic enrich の合流点は AnalysisPipelineSemanticPhase.Run（src/Unilyze/Pipeline/AnalysisPipelineSemanticPhase.cs:27-42）で、ここに CallEdge コレクタを差し込める。

ソースジャンプ素地: メソッドは `MemberInfo.StartLine` を、型は `TypeNodeInfo.FilePath`/`StartLine` を保持（TypeInfo.cs:54,57,73、MemberExtractor.cs:105-111）。これらは positional record プロパティで JsonIgnore も無く DATA に出力済みだが、ビューア main.js は filePath/startLine を一切参照していない（rg で 0 件）=フロー図ノードからのジャンプに即利用可能なのに未使用。

差分の素地: `MethodDiff(MethodName, ParameterCount, Status, Deltas)` がメソッド粒度で算出済み（src/Unilyze/Diff/DiffResult.cs:8、DiffCalculator.cs:236-260）。ビューアは methodDiffs をパネルに出すのみ（main.js:163-171）。

インクリメンタル制約: syntax キャッシュは `StripCouplingFields` で coupling 系を除外し SyntaxOnly のみ対象（src/Unilyze/Incremental/SyntaxIncrementalCollector.cs:90）。呼び出しグラフは SemanticModel 必須なので現キャッシュ対象外=ライブ即時反映時は呼び出しグラフだけ再計算コストが高い。

#### メソッド粒度の呼び出しエッジを抽出して MethodCall として AnalysisResult に追加

- verdict: valid
- effort: M / impact: high
- what: RfcCalculator.CollectInvokedSymbols と同型のロジックで、各メソッド本体の InvocationExpressionSyntax を走査し model.GetSymbolInfo で IMethodSymbol を解決、呼び出し元memberId→呼び出し先memberId の辺を `MethodCallEdge(FromMemberId, ToMemberId, FromTypeId, ToTypeId, Kind)` として収集する新コレクタを作る。AnalysisPipelineSemanticPhase.Run の deps 生成直後(L27付近)に呼び出し、AnalysisResult に `MethodCalls` プロパティを追加。プロジェクト外(BCL/Unity)呼び出しは ToTypeId=null で集約し、内部呼び出しのみエッジ化。memberId は TypeId + メソッド名 + パラメータ数で生成（既存 MethodDiff の MethodName+ParameterCount と整合）。
- why: フロー図(呼び出し/制御/依存)の核は呼び出しグラフ。現状は型粒度のみで『どのメソッドが何を呼ぶか』が完全に欠落。これが無いとゴール4(フロー図)もゴール2(該当メソッドへジャンプ)も成立しない。
- evidence: src/Unilyze/Metrics/RfcCalculator.cs:51-61 (走査ロジック流用元), src/Unilyze/Pipeline/AnalysisPipelineSemanticPhase.cs:27-42 (差し込み点), src/Unilyze/Pipeline/TypeInfo.cs:13-33 (型粒度のみのエッジモデル), src/Unilyze/Pipeline/AnalysisResult.cs:12-30
- 検証ノート: 走査ロジック流用元は実在: RfcCalculator.cs:51-61 (CollectInvokedSymbols が DescendantNodes().OfType<InvocationExpressionSyntax>() を回し model.GetSymbolInfo→IMethodSymbol を取得、L58 で OriginalDefinition を使う)。SemanticModel 基盤も完備: SemanticEnricher.cs:53-54 (ModelCache prewarm)、L116-119 (型ごとに GetSemanticModel をキャッシュ共有)。差し込み点 AnalysisPipelineSemanticPhase.cs:27-42 は deps 生成(L27)→DI/Serialized 追加(L28-29)→semantic enrich(L36)の流れで、Run のタプル戻り値(L13-17)に MethodCalls を足し AnalysisResult.cs:12-30 にプロパティ追加すれば露出可能。MethodCall/methodCalls は現状コードベースに存在せず(rg 0件、LinqInHotPathDetector の 'MethodCall' 文字列ヒットは無関係)、新規追加で正しい。1点だけ誇張あり: memberId を MethodDiff の MethodName+ParameterCount と『整合』とするが、MethodDiff のキーは DiffCalculator.cs:267 で `MethodName:ParameterCount` のみで TypeId を含まない。提案の memberId(TypeId+名+パラメータ数)は MethodDiff より細かく、同型ではない。実害はないが evidence の表現が不正確。

#### ビューアにメソッドノード階層を追加し型ノードを展開で呼び出しグラフ表示

- verdict: valid
- effort: L / impact: high
- what: main.js の要素ビルダーに第3階層ノード `m:<memberId>`(parent=`t:<typeId>`)を追加。型ノード展開時に当該型のメソッドノードと MethodCalls 辺を遅延生成し cytoscape へ add。デフォルトは現状の型グラフのまま、ノードのコンテキストメニュー/ダブルクリックで『呼び出しを展開』。既存の compound-node・ELK layered レイアウトをそのまま再利用（既に hierarchyHandling:INCLUDE_CHILDREN 設定済み）。辺色/矢印は DC/DS スタイル表に CallKind を追加。
- why: 既存 Cytoscape+ELK 資産(複合ノード+layered)はメソッド粒度フロー図にそのまま流用できる。新規ライブラリ不要。型→メソッドのドリルダウンは大規模時の集約(後述)とも自然に両立する。
- evidence: src/Unilyze/Templates/viewer/main.js:827-879 (型ノード/辺ビルダー), main.js:836 (parent:'cp:'で既に複合ノード運用), main.js:1222-1234 (ELK INCLUDE_CHILDREN既設定)
- 検証ノート: 複合ノード運用は実在: main.js:836 で型ノードは parent:'cp:'+namespace を持つ(typeNodeElement, L827-842)。ELK は INCLUDE_CHILDREN 設定済み: main.js:1231 (buildElkGraph の layoutOptions)、子ノードは childrenByParent で再帰構築(L1207-1219)。辺ビルダーは DATA.dependencies を型ID(t:fromId/t:toId)で結ぶのみ(L863-879)。method ノード('m:'/nodeType:'method'/memberId)は現状 0件(rg 確認)で第3階層は新規。DC/DS スタイル表(L21-35)に CallKind を足す案も妥当。注意点1: 現状の階層は cp:(namespace複合)→t:(型) の2段で、提案の m:(parent=t:) は3段目。ELK INCLUDE_CHILDREN は多段ネストに対応するが、namespace 複合ノード(cp:)と型ノード(t:)の二重複合(main.js:910-927 で compound と type が別 nodeType)に method を足すと3層複合になる。動作はするが3層複合のレイアウト/サイズ計測(L1216-1217 outerWidth/outerHeight)は未検証で実装時に確認要。注意点2: 『遅延生成し add』は rebuild()/meta-edge ロジック(L1140-1169)と整合させる必要があるが破壊的ではない。

#### フロー図ノードからソースへジャンプ（既出力済み filePath/startLine を活用）

- verdict: valid
- effort: S / impact: high
- what: 型ノード・メソッドノードのクリック時に DATA 内の既存 filePath/startLine を使い、(a)`vscode://file/<abs>:<line>` 形式のエディタ起動リンク、(b)ブラウザ内ソース閲覧パネル（後述 serve 連携時はHTTP GETで該当ファイルを取得しハイライト表示）の両方を提供。まずは vscode:// リンクだけなら main.js のパネル描画(L460付近)に1行追加で済む。型は TypeNodeInfo.FilePath/StartLine、メソッドは MemberInfo.StartLine（同一ファイル前提で型のFilePathを継承）。
- why: ゴール2(エディタ起動とブラウザ内閲覧の両睨み)に直結。データは既にJSONに出力済みでビューアが参照していないだけなので、抽出側の変更ゼロで実現できる最小コスト施策。フロー図の各ノードが即ソースに接続される。
- evidence: src/Unilyze/Pipeline/TypeInfo.cs:54,57,73 (FilePath/StartLine 保持), src/Unilyze/Pipeline/MemberExtractor.cs:105-111 (method StartLine), main.js で filePath/startLine 参照 0 件（rg 確認済み、未使用）
- 検証ノート: 実データで裏取り完了(analyze 実行→JSON 検査)。型は filePath/startLine を DATA に出力済み: TypeInfo.cs:54,57 が positional record プロパティ、サンプル JSON で types[0].filePath=絶対パス, startLine=3 を確認。メソッドは MemberInfo.StartLine を保持(TypeInfo.cs:73, MemberExtractor.cs:107,111 で methodStartLine を CreateMethodMember が設定)、サンプルで Method 1217件中 1216件が非null。ビューア未使用も確認: main.js で filePath/startLine 参照 0件(rg)、Members パネル(L460-465)は name/type のみ描画し startLine 不使用。HtmlFormatter.Generate(HtmlFormatter.cs:7) は Program.cs:162 でシリアライズした全 AnalysisResult JSON を埋め込む(L169)ので DATA に確実に含まれる。注意点: メソッドは自前 filePath を持たず型の FilePath を継承する前提だが、partial 型(TypeAnalyzer.MergePartialTypes, TypeInfo.cs:336-360)では複数ファイルにまたがるため、別ファイル定義のメソッドへのジャンプは型の FilePath ではズレる可能性がある。フィールド/プロパティ/EnumMember は startLine=null(サンプルで Field 186件/Property 810件が null)なのでメソッド以外のメンバージャンプは現状不可。

#### 制御フロー（CFG）はメソッド単体のオンデマンド・フローチャートに限定

- verdict: valid
- effort: M / impact: medium
- what: 全メソッドの CFG を一括描画せず、選択した1メソッドについて Roslyn の構文木(if/for/while/switch/try/return)からフローチャート(基本ブロック→分岐)を生成する軽量ビルダーを追加。出力は cytoscape の preset/dagre レイアウトで矩形ノード+菱形分岐。Microsoft.CodeAnalysis.FlowAnalysis(ControlFlowGraph)は内部APIで不安定なため使わず、SyntaxWalker でステートメント単位の簡易フローに留める。
- why: 制御フローは情報量が多く全体図に混ぜると破綻する。ゴール4の『制御フロー』はメソッド単位の詳細ビューとして分離するのが現実的。SyntaxOnly で済むので incremental キャッシュとも相性が良い（semantic 不要）。
- evidence: src/Unilyze/Pipeline/MemberExtractor.cs:103-104 (method.Body/ExpressionBody を既取得), src/Unilyze/Metrics/CognitiveComplexity.cs 等が既に制御構文を走査している前提（Detectors 群が SyntaxNode ベース）, main.js:1191-1196 (dagre レイアウト流用可)
- 検証ノート: 前提の『Detectors/Metrics が制御構文を SyntaxNode ベースで走査済み』は実在: CognitiveComplexity.cs/CyclomaticComplexity.cs/NestingDepth.cs が IfStatementSyntax/ForStatementSyntax/WhileStatementSyntax/SwitchStatementSyntax/TryStatementSyntax を走査(rg 確認)。method.Body/ExpressionBody は MemberExtractor.cs:103 で bodyNode として既取得。Microsoft.CodeAnalysis.FlowAnalysis(ControlFlowGraph) を避ける判断も妥当で、ControlFlowGraph/FlowAnalysis はコードベースで 0件使用(rg)=新規依存を避ける方針と整合。dagre レイアウト流用(main.js:1191-1196 layoutDagre)も実在。incremental との相性(SyntaxOnly で済む)も正しい: Program.cs:265 で --incremental は --level syntax 必須、SyntaxIncrementalCollector の対象は構文のみ(L36-49)で semantic 不要な CFG はキャッシュ対象に乗る。妥当な提案。

#### 大規模時の集約: 呼び出しエッジの namespace/assembly ロールアップと閾値ガード

- verdict: valid
- effort: M / impact: medium
- what: MethodCalls をそのまま全描画せず、(a)折りたたみ時は型ペアへ集約しエッジ太さ=呼び出し本数、(b)namespace/assembly レベルへの meta-edge ロールアップ（既存の DATA.dependencies meta-edge 集約ロジック L1146-1176 を MethodCalls にも適用）、(c)抽出側で 1メソッドあたりエッジ数や総エッジ数に上限を設けトリミング（超過時はサマリのみ）。MCP の query/analyze 出力にも MethodCalls サマリを露出。
- why: cs約200本規模でもメソッド呼び出しは型依存の数倍に膨らみ、無制限描画はライブ即時反映のレイアウト時間を悪化させる。既存の meta-edge 集約とエッジ可視性トグル(_edgeVis)資産を再利用すれば破壊なく段階表示できる。
- evidence: src/Unilyze/Templates/viewer/main.js:1146-1176 (meta-edge 集約とancestor ルーティング既実装), main.js:849-850/_edgeVis (kind 別可視トグル), src/Unilyze/Mcp 配下(query/analyze ツール群への露出先)
- 検証ノート: meta-edge 集約と ancestor ルーティングは実装済み: main.js:1146-1169 で DATA.dependencies を走査し、可視でない端点を findVisibleAncestor で祖先へルーティング、ペアごとに本数をカウント(L1162 mm.set)してラベル付き meta-edge を生成(L1166-1169, label=cnt)。エッジ可視トグルも実在: main.js:849-850 で _edgeVis=new Map() を DC のキー(DependencyKind)で初期化、rebuild 時に kind 別 display 切替(L1143)。これを MethodCalls に適用する案は既存資産の自然な拡張で破壊的でない。提案の『折りたたみ時は型ペアへ集約しエッジ太さ=本数』も _pairCount(L854-861)の延長で実現可。MCP の analyze 露出先(McpToolHandlers.cs:25 HandleAnalyze)も実在。閾値ガードは新規だが妥当。

#### diff オーバーレイを呼び出しグラフに拡張（変更メソッドを起点に影響波及を可視化）

- verdict: needs-revision
- effort: M / impact: medium
- what: 既存の MethodDiff(Status: Added/Removed/Modified)を呼び出しグラフのメソッドノードに着色オーバーレイ。さらに MethodCalls の逆辺をたどり『変更メソッドを呼んでいる側』をN階層ハイライト（影響波及/blast radius）。diff コマンドの deltaScore 分類と連動し、リスク高メソッドを赤系で強調。ビューア側は既存 diffBucket 着色機構(main.js:838)を流用。
- why: ゴール3(diff表示)をフロー図と統合する核。型粒度の delta では『変更がどのメソッドから呼ばれ何に波及するか』が見えない。呼び出しグラフ(提案1)があれば逆辺探索で影響範囲を即可視化でき、difit的な差分体験を超える価値を出せる。
- evidence: src/Unilyze/Diff/DiffResult.cs:8 (MethodDiff), src/Unilyze/Diff/DiffCalculator.cs:236-260 (メソッド diff 算出済み), main.js:163-171 (methodDiffs はパネル表示のみ=グラフ未連動), main.js:838 (diffBucket 着色機構)
- 検証ノート: 前提に事実誤認あり。提案は『MethodDiff(Status: Added/Removed/Modified)』とするが、MethodDiff の Status は ChangeStatus 型で値は Improved/Degraded/Unchanged のみ(DiffResult.cs:4,8-12)。Added/Removed という値は存在しない。さらに ComputeMethodDiffs(DiffCalculator.cs:236-264)は before を回し after に存在するメソッドだけをマッチして diff 化する(L246-261)ため、追加メソッド(after のみ)・削除メソッド(before のみ)はそもそも MethodDiff として出力されない=メソッド粒度の Added/Removed 着色のデータ源が無い。型粒度では TypeDiff に Added/Removed リストがある(DiffResult.cs:46-47)が、メソッド粒度には無い。また diffBucket 着色機構(main.js:838, セレクタ L953-959)は型ノード(data.diffBucket)に対するもので、バケットは added/degraded/improved/unchanged で TypeDiff 由来(L60-70, dl マップ)。メソッドノードへの流用は『機構を流用』では済まずデータ層の拡張が必須。methodDiffs がパネル表示のみ(main.js:163-176)でグラフ未連動という観察自体は正しい。
- 修正案: (1) Added/Removed の前提を撤回し、まず DiffCalculator.ComputeMethodDiffs を拡張して after-only/before-only のメソッドを Added/Removed として MethodDiff に含めるか、ChangeStatus に Added/Removed を追加する(DiffResult.cs:4 と DiffCalculator.cs:246-264 の両方を変更)。(2) MethodDiff にメソッドの所属 TypeKey を付与(現状キーは MethodName:ParameterCount のみで型情報が無く、グラフのメソッドノード m:<typeId+name+paramCount> と突合できない)。(3) 影響波及(blast radius)の逆辺探索は提案1の MethodCalls が前提なので依存順を明示し、提案1完了後に着手。(4) 着色は型ノード用の diffBucket セレクタ(main.js:953-959)をそのまま流用せず、メソッドノード用の新セレクタ/データキーを追加する。

Open questions:

- memberId(ノード安定識別子)の設計: TypeId+メソッド名+パラメータ数で十分か。オーバーロード/ジェネリック/明示的IF実装/部分メソッドの衝突をどう避けるか。MethodDiff は MethodName+ParameterCount のみで既に衝突リスクがある（DiffCalculator.cs:236-260）
- ライブ即時反映時の呼び出しグラフ再計算コスト: 呼び出しグラフは SemanticModel 必須で incremental syntax キャッシュ対象外（SyntaxIncrementalCollector.cs:90 が coupling/semantic を除外）。変更ファイルだけ semantic 再解析する部分コンパイル戦略を新設するか、呼び出しグラフはフルビルド限定にして折り合うか
- 抽出のデフォルト ON/OFF と粒度: MethodCalls を常時抽出すると analyze の時間/JSON サイズが増える。`--call-graph` フラグでオプトインにするか、AnalysisLevel に連動させるか。MCP 経由のデフォルトはどうするか
- 制御フロー(CFG)の実装基盤: Roslyn の ControlFlowGraph(Microsoft.CodeAnalysis.FlowAnalysis)を使うか、安定性優先で SyntaxWalker ベースの簡易フローに留めるか。前者は基本ブロック精度が高いが内部API依存
- ブラウザ内ソース閲覧の配信方式: 現状は単一静的HTMLでゼロセットアップ思想。ソース全文をHTMLに埋め込むとサイズ爆発・機密混入リスク。serve/watch サーバ前提でHTTP配信するか、vscode:// リンク（エディタ起動）のみに留めるか。両睨みの線引き
- 大規模集約の閾値とUX: メソッドノード/エッジの上限値、ロールアップの既定レベル(型/namespace/assembly)、初期表示で展開する範囲(全折りたたみ vs 選択型のみ展開)の決定基準
- 外部呼び出し(BCL/Unity/サードパーティ)の扱い: ToTypeId=null で捨てるか、API表面(ApiSurface, AnalysisResult.cs:26)と紐付けて『外部API呼び出し』ノードとして残すか。Unity hot-path 検出(SemanticEnricher の HotPathMethodNames)とフロー図を連動させる価値があるか

### 既存ビューアのリアルタイム化改修

現状: ビューアは単一ファイル main.js(2436行)で、解析JSONが `const DATA = __DATA_PLACEHOLDER__;`(main.js:1)としてパース時にインライン展開される。DATAから派生する約18個のモジュールレベルglobalがビルド時に一度だけ構築される: 型ルックアップ `tl`(main.js:49-50)、メトリクス `tm`(main.js:53-54)、`nsInfo`(main.js:694-707)、`nsTree`(main.js:710-758)、`nsHealthMap`(main.js:763-790)、Cytoscape要素配列 `els`(main.js:795-810,820-842)、`typesByNamespace`(main.js:812-825)、`dependencyElements`(main.js:863-879)、`asm`/`ac`(main.js:44-46)。これらを新データから再導出する関数は存在しない。

増分更新の素地はある: `rebuild()`(main.js:1076-1174)は `cy.startBatch()`〜`endBatch()` 内でnamespaceの materialization(main.js:1121-1137)により型ノードを部分的に add/remove し、可視エッジのみ再構築する。ただし固定された `DATA`/`dependencyElements`/`els` に対してのみで、新しいデータセットとの reconcile(差分検出)機構は無い。`diffChangedOnlyHandler`(main.js:1189)は `rebuild();layout();` を呼ぶだけ。

状態管理: 展開状態 `expanded`(main.js:986-987)、materialization済み `materializedNamespaces`(main.js:988)、`searchFilters`(main.js:1690)、`_edgeVis`/`_edgeStyleMode`(main.js:849-851)、`diffState`(main.js:61)、メモ化キャッシュ `hotMethods`/`hotTypes`(main.js:2167-2169 — DATA由来で一度だけ構築、ライブ更新で陳腐化)。選択/詳細パネルは DOM の `.hidden` クラスで管理(main.js:483-484)、Cytoscape選択は `node:selected`(main.js:948)。レイアウトは ELK(worker, CDN依存 main.js:1200)→main-thread ELK→dagre フォールバック(main.js:1249-1273)で `animate:true,fit:true`(main.js:1268-1270)固定。

ソースジャンプの素地: `TypeNodeInfo` は `FilePath`(TypeInfo.cs:54)と `StartLine`(TypeInfo.cs:57)、`MemberInfo` も `StartLine`(TypeInfo.cs:73)を持ち、`AnalysisResult.Types`(AnalysisResult.cs:16)経由でcamelCase(AnalysisResult.cs:48)シリアライズされ `DATA.types[*].filePath`/`startLine` として既にビューアに届いている。だが `renderTypeDetail`(main.js:400-485)はこれを一切読まずソースリンクを出さない。SARIFは同フィールドを利用済み(SarifFormattingHelpers.cs:64,176-183)。

diff表示の素地: `__DIFF_DATA_PLACEHOLDER__`(main.js:57)、bucket索引 `dl`(main.js:60,64-77)、ノード underlay スタイル `diffBucket=added/degraded/improved`(main.js:953-961)、diffサマリーバー(main.js:79-105)が end-to-end で動作。`diff <before> <after>` 経路(DiffRunner.cs:528)が `GenerateWithDiff`(HtmlFormatter.cs:10)で同ビューアに注入する。

配信: HTTPサーバは src 全体に皆無(HttpListener/Kestrel/WebApplication/TcpListener いずれもヒット無し)。ビューアは `file://` 静的HTMLで `TryOpenInBrowser`(Program.cs:178, ProgramHelpers.cs:234)で開くのみ。Program.cs に serve/watch コマンドは無い。ビルドは `combine.py`(viewer/combine.py)が index.html+styles.css+main.js を文字列置換連結し viewer.html を生成→埋め込みリソース化(Unilyze.csproj:31,52-55)。JSのユニットテストは皆無(test.js/spec.js 0件)。`--incremental` は SyntaxOnly経路のper-fileハッシュキャッシュ(TypeInfo.cs に Incremental import)で再解析を高速化済み。

#### DATA再ハイドレーション層を抽出し『派生インデックス再構築 + Cytoscape reconcile』を関数化する

- verdict: valid
- effort: L / impact: high
- what: 現在パース時に一度だけ走る派生グローバル構築(tl/tm/nsInfo/nsTree/nsHealthMap/els/typesByNamespace/dependencyElements/asm/ac、main.js:44-879)を `buildDerivedState(data)` 純関数に括り出し、結果を `let store` に束ねる。`applyDataset(newData)` を新設し、(1)storeを再構築、(2)新旧 typeId 集合を差分して Cytoscape の型ノードを add/remove/update(既存 rebuild の materialization ロジック main.js:1121-1137 を新storeに対して再適用)、(3)hotMethods/hotTypes メモ化(main.js:2167)を無効化。初回も `applyDataset(DATA)` で通す。これにより『全 cy.destroy() 再生成』ではなく増分パッチで反映できる。
- why: ライブ更新の必須前提。現状 DATA派生がモジュールスコープ即時実行で固まっており、新データを流す入口が無い。全再描画(iframe/cy再生成)は expanded/pan/zoom/選択/フィルタを全消失させ lazygit的な即時差分体験にならない。既存 rebuild が既に増分add/removeできる事実を活かせば、最小改修で『変わった型だけ光らせて差し替える』が実現できる。
- evidence: main.js:1 (DATAインライン), main.js:44-879 (派生globalの一括即時構築), main.js:1076-1137 (rebuildの既存materialization増分ロジック), main.js:2167-2169 (DATA由来メモ化キャッシュ)
- 検証ノート: evidence全て成立。const DATA = __DATA_PLACEHOLDER__ は main.js:1。派生globalはモジュール最上位で一度だけ構築: asm/ac(main.js:44-46), tl(49-50), tm(53-54), nsInfo(694-707), nsTree(710-758), nsHealthMap(763-790), els(795-810), typesByNamespace(812-825), dependencyElements(863-879)。rebuildの増分materializationは main.js:1121-1137 で desiredNamespaces 差分→remove/add を実装済み(主張通り)。メモ化 hotMethods/hotTypes は main.js:2167-2169 で DATA.typeMetrics 由来・一度だけ構築(ライブ更新で陳腐化する指摘も正しい)。重大な見落としは無いが工数Lは妥当: cy 本体は fontReady.then(...) クロージャ内(main.js:885)で生成され、expanded(986)/materializedNamespaces(988)もその後で定義される。applyDataset は『モジュール最上位の派生global』と『cy生成後クロージャ内のグラフ状態』の2層境界をまたぐため、これらを let store に束ねるには const→let 化と参照箇所(tl/tm/els等を読む全関数)の書き換えが広範に及ぶ。typeNodeElement(main.js:827)がmaterialization時に tm/ac/dl/_mw を都度参照しているので、storeさえ差し替えれば新データのノードは生成できる構造で、reconcile自体は現実的。

#### 状態スナップショット&リストアで pan/zoom/選択/展開/フィルタ/スクロールを保持する

- verdict: valid
- effort: M / impact: high
- what: `applyDataset` の前後で `captureViewState()`/`restoreViewState()` を実装。保存対象: `cy.pan()`/`cy.zoom()`、選択中ノードid、`expanded`(main.js:986)、`searchFilters`(main.js:1690)、`_edgeVis`/`_edgeStyleMode`(main.js:849-851)、`diffState`(main.js:61)、開いている詳細パネルの typeId、hpList/cycList のスクロール位置。レイアウトは増分更新時のみ `fit:false` に切替(main.js:1268 の固定 fit:true を引数化)し、消えていないノードの座標を温存して『画面が飛ばない』ようにする。
- why: ユーザー要件の核心『基本UIは今のまま、それがリアルタイムにソース状態を反映』を満たすため。現状 rebuild→layout は毎回 `fit:true`(main.js:1194,1269)でビューポートをリセットするので、保存中のソース編集ごとに画面位置・選択・展開が失われ実用にならない。状態は既に個別globalに散在しているので集約するだけで保持可能。
- evidence: main.js:986-988 (expanded/materialized), main.js:849-851 (edge state), main.js:1690 (searchFilters), main.js:61 (diffState), main.js:1194 (dagre fit:true), main.js:1268-1270 (elk preset fit:true固定), main.js:483-484 (パネルはDOM .hidden)
- 検証ノート: evidence全て成立。expanded(main.js:986-988), materializedNamespaces(988), _edgeVis/_edgeStyleMode(849-851), searchFilters(1690), diffState(61) は実在し集約可能。レイアウトの fit:true は layoutDagre(main.js:1194) と layoutElk の preset 適用(main.js:1268-1270)で確かにハードコードされ、毎回ビューポートをリセットする(主張通り)。詳細パネルは DOM .hidden クラス管理(panelEl.classList main.js:484, dp.classList main.js:1685-1686)で、開いているtypeIdの復元は renderTypeDetail(typeId) 再呼び出しで可能。見落とし2点(needs-revisionには倒さないが実装時必須): (1)materializedNamespaces(988)もスナップショット/復元対象に含めないと、reconcile後にどのnsが実体化済みかが不整合になる。提案のcapture対象リストに未列挙。(2)layoutElkは非同期(main.js:1249, await ELK().layout)で完了が layoutstop イベント(既存利用例 main.js:2157)に乗るため、restoreViewState の pan/zoom 復元は layout適用後=layoutstop後に行う必要がある。fit:false の引数化(1268)は妥当だが、消えていないノードの座標温存はpreset/dagre双方で別経路のため両方の対応が要る。

#### 既存JSONの filePath/startLine を使い詳細パネルにソースジャンプを追加する

- verdict: valid
- effort: S / impact: high
- what: `renderTypeDetail`(main.js:400)とメンバー行(main.js:462-464)に、`type.filePath`+`type.startLine`(メンバーは `member.startLine`)からソースリンクを生成。file://では `vscode://file/<path>:<line>` や `idea://`、`<a href="file://...">` を出し、後述のserve配信時は `/source?path=...&line=...` エンドポイントに飛ばす(両睨み)。パスは projectPath 相対化して表示。データは既にビューアJSONに届いているのでバックエンド改修不要。
- why: ユーザー要件『ブラウザからソースへ飛べる(エディタ起動 or ブラウザ内閲覧の両睨み)』に直結。`TypeNodeInfo.FilePath`/`StartLine`(TypeInfo.cs:54,57)は既にcamelCaseで DATA.types に載っているが renderTypeDetail はメトリクスと依存のみ表示しソース位置を捨てている。SARIFは同フィールドで location を出せている(SarifFormattingHelpers.cs:176-183)ので確実に存在する。
- evidence: TypeInfo.cs:54 (FilePath), TypeInfo.cs:57 (StartLine), TypeInfo.cs:73 (MemberInfo.StartLine), AnalysisResult.cs:16,48 (Types serialize camelCase), main.js:400-485 (renderTypeDetailがfilePath未使用), SarifFormattingHelpers.cs:176-183 (同フィールド利用実績)
- 検証ノート: evidence全て成立。TypeNodeInfo.FilePath(TypeInfo.cs:54), StartLine(TypeInfo.cs:57), MemberInfo.StartLine(TypeInfo.cs:73) 実在。AnalysisResult.Types(AnalysisResult.cs:16)は JsonKnownNamingPolicy.CamelCase(AnalysisResult.cs:48)でシリアライズされ DATA.types[*].filePath/startLine として届く。renderTypeDetail(main.js:400-485)は tl[typeId] から name/kind/qualifiedName/members/依存のみ描画し filePath/startLine を一切読まない(主張通り、ソース位置を捨てている)。SARIFは同フィールドで location/region を生成済み(SarifFormattingHelpers.cs の startLine = method?.StartLine ?? typeMetrics.StartLine)。注意点(実装上の微差、verdictは valid 維持): メンバー行(main.js:462-464)が反復するのは type.members(=MemberInfo)で、MemberInfo は自前の filePath を持たず(TypeInfo.cs:62-77 に FilePath無し)親型の type.filePath を流用する前提になる。partial型はファイルをまたぐ可能性があるが、メンバーのStartLineは親型ファイル基準で概ね妥当。file://直開きのスキーム(vscode://file/<path>:<line>等)はOS/エディタ依存で確実性は環境次第だが、データ不足ではない。バックエンド改修不要の主張は正しい。

#### `unilyze serve --watch` を新設しSSE+静的配信でファイル変更を即時プッシュする

- verdict: needs-revision
- effort: L / impact: high
- what: Program.cs に serve コマンドを追加(既存ルーティング Program.cs:286 の隣)。HttpListener ベースの最小サーバで (1)埋め込み viewer.html を配信、(2)FileSystemWatcher で対象 .cs を監視、(3)デバウンス後に `--incremental` 経路で再解析し新JSONを生成、(4)SSE(EventSource)で差分JSON or 全JSONをクライアントへ push、(5)`/source` で filePath+line のソース断片をブラウザ内表示。viewer側は `__LIVE_ENDPOINT__` プレースホルダがあれば EventSource を張り、無ければ従来通り静的(オフライン無改変)。テンプレ生成は HtmlFormatter(HtmlFormatter.cs:7)を温存し serve 用の薄いラッパで配信。
- why: lazygit的ライブ更新の配信路。現状 file:// 静的HTMLにはサーバpushの手段が無く(HttpListener等ヒット0)、watch機構も無い。`--incremental`(per-fileハッシュキャッシュ)が既にあるので再解析コストは小さく、ゼロセットアップ思想(単一バイナリ、CDN非依存)も HttpListener 自作なら維持できる。既存の静的HTML出力経路は一切壊さず付加機能にできる。
- evidence: Program.cs:286 (既存コマンド一覧、serve/watch無し), HtmlFormatter.cs:7-25 (テンプレ生成は流用可能), Program.cs:140-150 (incremental解析パイプライン), TryOpenInBrowser Program.cs:178/ProgramHelpers.cs:234 (現状はfile://を開くのみ)
- 検証ノート: 配信路の現状認識は正しい: Program.cs のコマンド分岐(Program.cs:18-49 の args[0]=='diff'..'skills')に serve/watch は無く、HtmlFormatter.Generate(HtmlFormatter.cs:7)は流用可能。だが核心の前提『--incremental があるので再解析コストは小さい』が誤り。AnalysisPipeline.Build は options.Incremental && RequestedLevel != AnalysisLevel.Syntax のとき incremental を強制無効化し full 解析へフォールバックする(AnalysisPipeline.cs:42-46, 警告『--incremental currently accelerates syntax-level analysis only; running full analysis』)。ライブビューアは codeHealth/codeSmells/typeMetrics を要し(renderTypeDetail main.js:423-444, diff underlay 等)、それらは syntax レベルでは生成されず core+ が必須。つまり serve --watch の再解析は必ず非syntaxレベル→incrementalキャッシュは効かず毎回フルパース+semanticになる。提案の低コスト前提は崩れる。
- 修正案: (1)『--incremental で安く再解析』を撤回し、watch再解析はフルパイプライン(core/full)前提でデバウンス時間を長めに取る(保存連打を1回に畳む)、または変更ファイル集合だけ再パースして既存JSONへ部分マージするカスタム差分再解析を別途設計する(現状この経路は無いため新規実装が要る)。(2)__LIVE_ENDPOINT__ プレースホルダ案はテンプレ側に新規プレースホルダ追加が必要で、HtmlFormatter.Render(HtmlFormatter.cs:13-25)の Replace チェーンと combine.py の置換規約(combine.py:14)双方に手を入れる必要がある点を工数に織り込む。(3)SSEで全JSONを毎回pushするか差分のみかは提案1/5の reconcile が前提なので依存順序を明示する。

#### 既存diffオーバーレイをライブdiffに転用しサーバが diffJson を push する

- verdict: needs-revision
- effort: M / impact: high
- what: serve --watch のベースライン(直近スナップショット or git HEAD)に対し、再解析ごとに DiffRunner 相当(DiffRunner.cs:528 の `GenerateWithDiff` 経路)で TypeDiff を算出し、`__DIFF_DATA_PLACEHOLDER__`(main.js:57)に相当するペイロードを SSE で送る。viewer側は `DIFF` を再代入し `dl` 索引(main.js:64-77)と `diffBucket` underlay(main.js:953-961)を再適用、diffサマリーバー(main.js:79-105)を更新。difit的な行レベルdiff表示は `/source?path&line` のソース断片に before/after を併記して実現。
- why: ユーザー要件『difitのようにdiff表示』。diffの可視化資産(bucket索引・underlayスタイル・サマリーバー・changed-only絞り込み main.js:1016-1021)が既に完成しており、ライブ更新時に DIFF を差し替えるだけで『編集した型がリアルタイムで improved/degraded に染まる』を低コストで実現できる。新規にdiff描画系を作る必要がない。
- evidence: main.js:57 (DIFFペイロード), main.js:60-77 (dl索引), main.js:953-961 (diffBucket underlay), main.js:79-105 (diffサマリーバー), main.js:1016-1021 (changedOnly materialization), DiffRunner.cs:528 (GenerateWithDiff経路), MarkdownDiffFormatter.cs (差分計算資産)
- 検証ノート: diff可視化資産は全て実在・end-to-end動作を確認: DIFF(main.js:57), dl索引の improved/degraded/unchanged/added/removed ループ(main.js:68-74), node[diffBucket=...] underlay スタイル(main.js:953-961), diffサマリーバー initDiffSummary(main.js:79-105), changedOnly materialization typePassesMaterialization(main.js:1016-1021), GenerateWithDiff(DiffRunner.cs:528 → HtmlFormatter.cs:10)。だが『DIFFを再代入するだけ』が誤り。DIFFは const __DIFF_DATA_PLACEHOLDER__(main.js:57)でビルド時C#文字列置換(HtmlFormatter.cs:23 の .Replace)による静的埋め込み。constなので実行時再代入不可。さらに diffBucket はノード生成時に typeNodeElement(main.js:838 data.diffBucket=diffEntry._bucket)で焼き込まれるため、ライブでdiffを変えるには dl 再構築 + 既存ノードの data('diffBucket') 更新 + サマリーバー再描画(initDiffSummary は dataset.init==='1' でガードされ二度目を弾く main.js:81)まで手当てが要る。『再代入だけ』の低コスト主張は成立しない。
- 修正案: (1)DIFFを const から let store.diff へ移し(提案1の store 化に合流)、applyDiff(newDiff) を新設して dl 再構築→cy.nodes('[nodeType=type]') の data('diffBucket') を一括更新→underlayは data駆動なので自動反映、を行う。(2)initDiffSummary のワンショットガード(main.js:81 el.dataset.init)を解除し再描画可能にする。(3)difit的な行レベルdiffは『/source に before/after併記』だが、serve側でbefore側ソース(git HEAD等)を取得する経路は現状存在しない(DiffRunnerはJSON対JSON比較で生ソース行差分は扱わない)ため、行diff表示は新規にgit/ファイル読み取り層が必要で工数Mでは収まらない可能性が高い。(4)再解析コストは提案4と同じく--incremental不適用(AnalysisPipeline.cs:42-46)の制約を受ける。

#### combine.py を ES-module連結に進化させ main.js を分割・Vitestでテスト可能にする

- verdict: valid
- effort: M / impact: medium
- what: 純粋ロジック(typeKey/qualifiedName/stripGenericArgs main.js:3-15、buildDerivedState、collectSearchMatches main.js:1724、typePassesQuickFilters main.js:1706、diff索引)を Cytoscape/DOM非依存モジュールへ切り出し、combine.py(現状は単純文字列連結)を簡易バンドル(import解決して1ファイル化、IIFE化)に拡張。これら純関数に Vitest を導入し reconcile差分ロジック(新旧型集合のadd/remove判定)を回帰テスト。ビルド出力(埋め込み viewer.html)は従来と同一形式を維持。
- why: ライブ化で reconcile/状態保持/差分パッチという壊れやすいロジックが増える。2436行・テスト0件・全グローバル即時実行の現状では回帰検出ができず、増分更新のバグ(ノード重複/孤児エッジ/状態リーク)が温床化する。提案1の純関数抽出と相補的で、`Functional Core, Imperative Shell` に沿ってテスト可能境界を作る。出力フォーマット不変なら埋め込み(Unilyze.csproj:52-55)もオフライン動作も無改変。
- evidence: main.js:2436行(単一巨大ファイル), viewer/combine.py(単純str.replace連結), Unilyze.csproj:52-55 (combine.py exec→埋め込み), main.js:3-15/1706-1739 (DOM非依存で抽出可能な純ロジック), テストファイル0件(fd test.js/spec.js空)
- 検証ノート: evidence全て成立。main.js は2436行の単一ファイル。combine.py は単純な str.replace 2回(combine.py:14: index.html の /*__VIEWER_STYLES__*/ と //__VIEWER_MAIN_JS__ を置換)で、import解決もバンドルもしていない。csproj は combine.py を Exec し $(IntermediateOutputPath)viewer.html を EmbeddedResource 化(Unilyze.csproj:52-55, 31)。DOM/Cytoscape非依存の純ロジックは抽出可能: stripGenericArgs/qualifiedName/typeKey/metricKey/depFromId/depToId(main.js:3-15), typePassesQuickFilters(1706-1718), collectSearchMatches(1724-1740)。JSのテストは0件(test.js/spec.jsヒット無し)。Functional Core/Imperative Shell 方針も妥当。1点補足(verdictは valid): 提案文の『出力フォーマット不変ならオフライン動作も無改変』のうちオフライン部分は元から完全でない—ELKレイアウトはCDN(index.html:73 の elk.bundled.js, main.js:1200 の unpkg importScripts)依存でdagreフォールバックする構造であり、combine.py改修とは無関係にオフライン時はELK不可。combine.pyのバンドル化自体はこの事実に影響しないので提案の正当性は揺らがない。impact:medium も妥当。

Open questions:

- 差分パッチ vs 全再描画の境界をどこに引くか: rebuild の materialization(main.js:1121-1137)はnamespace粒度で型をadd/removeできるが、エッジは毎回全削除→再構築(main.js:1080,1140)。型1個変更で全エッジ再構築は許容か、それともエッジもtypeId差分でパッチすべきか。グラフ規模(約200型)なら全再描画で十分軽い可能性もあり、計測が要る。
- ライブ更新の単位は『全JSON送り直してクライアントでreconcile』か『サーバ側でTypeDiffを計算しパッチだけ送る』か。前者はクライアントが重いがサーバ単純、後者は提案5のdiff資産を活かせるがプロトコル設計が要る。--incremental の再解析粒度(per-file)とどう噛み合わせるか。
- ELKレイアウトの非決定性: 増分更新のたび全体レイアウト(main.js:1180-1187)を回すとノード位置が毎回変わり画面がチラつく。変更が無いノードの座標を固定し新規/移動ノードのみ再配置する『安定レイアウト』が必要だが、ELK/dagre は部分レイアウトAPIを持たない。pinned positions + 局所再配置の実装可否。
- ソースジャンプの宛先: file:// 静的HTMLでは `vscode://file` 等のカスタムスキームに頼るしかなく、ブラウザ内ソース閲覧(difit的diff含む)はサーバ配信(提案4)が前提。エディタ起動とブラウザ内閲覧のどちらを既定にするか、両対応のUI(リンク2種)をどう出すか。filePathは絶対パスのままか projectPath相対化か(セキュリティ: 任意パス露出の懸念、docs/threat-model.md)。
- 『コードのフロー(呼び出し/制御/依存)図』のデータ源: 現状 DATA.dependencies は型レベル依存(DependencyKind, TypeInfo.cs:20-33)のみで、メソッド呼び出しグラフや制御フローは解析・シリアライズされていない。フロー図には新たな解析(call graph 抽出)とJSONスキーマ拡張が必要で、これは本観点(既存ビューア改修)の範囲を超える別タスクとして切るべきか。
- serve --watch のゼロセットアップ思想との両立: HttpListenerは単一バイナリで足りるがポート選択・ブラウザ自動オープン・複数クライアント・SSE再接続の堅牢性をどこまで作り込むか。オフライン静的HTML出力(現行の既定)は無改変で残す前提でよいか。
- combine.py のES-module化に伴うビルド依存: 現状 python3 単体で連結(Unilyze.csproj:55)。簡易バンドラ自作で済ませるか、node/esbuild等のビルド依存を新規に持ち込むか。後者は単一バイナリ配布のビルド要件を増やす。

### インタラクションUX設計

現状: 現状ビューアは「file:// で開く単一HTML」で、ライブ更新の土台が一切ない。`unilyze analyze` は HTML を書き出して `Process.Start("open", url)` で開くだけ（src/Unilyze/Cli/ProgramHelpers.cs:238-244, src/Unilyze/Program.cs:177）。serve/watch コマンドは無く（src/Unilyze/Program.cs:18-45 のルーティングに存在しない）、サーバープロセスもWebSocket/SSE/FileSystemWatcher も無い（grep で `src/Unilyze` 配下にヒット0、唯一の `GetData` は無関係）。

キーボードモデルは最小限。`installViewerKeyboard`（src/Unilyze/Templates/viewer/main.js:1887-1901）が拾うのは `/`(検索フォーカス)と `Escape`(パネル/モーダルを閉じる→検索クリア、handleEscapeKey main.js:1861-1885)のみ。lazygit的な j/k によるリスト移動、Tab/数字によるパネル間フォーカス遷移、ノード選択のキーボード操作は無い。ホットスポット/サイクルの左パネル（index.html:34-55）やノード詳細パネル（index.html:58-61）への移動はすべてマウスクリック（cy.on('tap',...) main.js:1556, hpList/cycList の click main.js:2426-2430）。

ライブ更新時の視点保持の障壁が構造的に存在する。名前空間の展開状態 `expanded` はインメモリ `Set`（main.js:986）で、localStorage 等への永続化が無い（grep でストレージAPIヒット0）。ノード位置は毎回レイアウト計算で生成され保存されない。さらに全レイアウトが `fit:true` 固定（layoutDagre main.js:1191-1196, layoutElk main.js:1267-1270）、検索ハイライトも `cy.animate({fit:...})`（main.js:1768）。つまり再解析→再描画のたびにズーム/パン/展開状態が全部リセットされ、lazygitのような「変更箇所だけ差し替え、視点は据え置き」が今の rebuild()/layout() 経路では成立しない。

ソースジャンプの素材は既にデータに入っているが未活用。`TypeNodeInfo` は `FilePath` と `StartLine`（src/Unilyze/Pipeline/TypeInfo.cs:54,57）、`MemberInfo` は `StartLine`（TypeInfo.cs:73）を持ち、camelCase で JSON 化されてビューアに渡る（src/Unilyze/Pipeline/AnalysisResult.cs:46-57 の JsonSourceGenerationOptions, HtmlFormatter.Generate src/Unilyze/Output/HtmlFormatter.cs:7）。よってクライアントには `DATA.types[].filePath` / `startLine` が既にある。しかし型詳細パネル（main.js:1556-1648）にもメンバー一覧（main.js:1616-1631）にも「エディタで開く/ソース表示」導線が一切無い。

diff表示は同じビューアを再利用する基盤が既にある。`DIFF` を読み込み `_bucket` バッジ＋"Changed only"トグルでオーバーレイ（main.js:60-105, diffBucketBadge main.js:107-115, renderDiffSections main.js:144-190）。ただしメトリクスΔとメソッド単位の増減表示までで、行レベルのdiff（difit的なside-by-side）は無い。

情報過多抑制の既存手段: 検索の `SEARCH_EXPAND_CAP=50`（main.js:1689,1795-1812 で上限超過時は自動展開せずヒント表示）、フィルタチップ（Health<7/Smells/Cycles, index.html:15-17, wireFilterChips main.js:1903）、エッジ種別フィルタ（main.js:1936-1959）。バッジ再構築は requestAnimationFrame でスロットリング（markBadgesDirty/rebuildBadges main.js:1332-1336）。再解析の素材として `--incremental` のper-fileコンテンツハッシュキャッシュが存在（src/Unilyze/Incremental/SyntaxIncrementalCollector.cs:36-49）し、変更ファイルのみ再パースできる。

#### 差分パッチ型の再描画パスを新設し、ライブ更新で視点を据え置く

- verdict: needs-revision
- effort: L / impact: high
- what: 再解析データを受けてグラフ全体を rebuild()/layout() で作り直すのではなく、(a)現在の zoom/pan を保存→復元する applyViewportPreserved() を追加し、layoutDagre/layoutElk の `fit:true` をライブ更新時のみ `fit:false`+位置プリセット復元に切り替える、(b)既存ノードは cy.getElementById で差分パッチ(data更新のみ)、追加/削除ノードだけ add/remove する増分更新関数 applyDelta(prevData,nextData) を main.js に追加する。`expanded` Set もそのまま保持して再利用する。
- why: ユーザー要件『lazygitのように即時反映、基本UIは今のまま』を満たす核。現状は全レイアウトが fit:true 固定(main.js:1194,1269)＋expandedがインメモリ(main.js:986)のため、再描画でズーム/パン/展開が毎回リセットされ『ちらつかず視点据え置きの差し替え』が原理的に不可能。
- evidence: src/Unilyze/Templates/viewer/main.js:1191-1196,1267-1270,1768,986,1076-1180
- 検証ノート: fit:true固定の主張は正しい: layoutDagre は fit:true,animate:true,animationDuration:250 (main.js:1192-1194)、layoutElk の preset レイアウトも fit:true,animate:true (main.js:1267-1270)、検索ハイライトの fit も cy.animate({fit:...}) (main.js:1768)。expanded がインメモリ Set で localStorage 永続化なしも正しい (main.js:986-987; grep でストレージAPIヒット0)。ただし『既存ノードは getElementById で差分パッチ、追加/削除だけ add/remove する増分更新関数を新設』という前提は実コードと食い違う。rebuild() (main.js:1076-1174) は既に増分パッチ型: materializedNamespaces を保持し、cy.getElementById('t:'+tk).empty() で未追加ノードのみ add (main.js:1132-1135)、不要ノードのみ remove (main.js:1123-1131)、エッジは毎回全 remove→再構築 (main.js:1080,1139-1169)。つまり『rebuild()/layout() で全部作り直す』という現状認識は誤りで、ノード増分機構は既存。真のギャップは (1) layout() が常に fit:true+animate で視点リセット、(2) 再解析をまたいだ expanded/位置/zoom-pan の持続化が無い、の2点に限定される。applyDelta を一から作る必要は無い。
- 修正案: applyDelta 新設ではなく既存 rebuild() の増分機構を再利用する方向に縮小せよ。具体的には: (a) layoutDagre/layoutElk に live フラグを足し、ライブ更新時のみ fit:false+animate:false で呼ぶ(rebuild 内のノード増分はそのまま流用)、(b) 再描画前後で cy.zoom()/cy.pan() を保存・復元する applyViewportPreserved() を追加、(c) expanded Set を再解析間で持続させる(同一プロセスのライブ更新ならメモリ保持で足りる。file:// 再読込シナリオなら localStorage 化が必要)。ただしエッジは毎回 remove→再構築される設計(main.js:1080)なので、エッジのちらつき抑制には別途エッジ増分パッチの追加実装が要る点を見積もりに含めること。effort L は妥当だが、根拠を『増分機構の新設』から『fit制御+viewport保存+エッジ増分』に差し替える。

#### 型/メソッド詳細パネルにソースジャンプ導線を追加する

- verdict: needs-revision
- effort: M / impact: high
- what: 型詳細(main.js:1556-1648)とメンバー行(main.js:1616-1631)に『Open in editor』ボタンと『View source』リンクを追加。データは既存の filePath/startLine を使う。エディタ起動は serve コマンド導入時はサーバー側で `code -g file:line`/`cursor`/`$EDITOR` を叩く軽量エンドポイント、純file://運用時は vscode://file/{path}:{line} 形式のディープリンクで両睨み。エディタ種別は設定(既存 ConfigRunner 経由)で切替。
- why: 要件2『ブラウザからソースへ飛ぶ(エディタ起動とブラウザ内閲覧の両睨み)』に直結。素材(filePath/startLine)は既にDATAに入っているのに詳細パネルが一切使っていない。最小の接ぎ木で実現できる。
- evidence: src/Unilyze/Pipeline/TypeInfo.cs:54,57,73 / src/Unilyze/Pipeline/AnalysisResult.cs:46-57 / src/Unilyze/Templates/viewer/main.js:1556-1648,1616-1631
- 検証ノート: 型レベルの素材は揃う: TypeNodeInfo に FilePath (TypeInfo.cs:54) と StartLine (TypeInfo.cs:57) があり、AnalysisResult.Types として camelCase で JSON 化 (AnalysisResult.cs:46-48 の JsonSourceGenerationOptions, HtmlFormatter.cs:7,21 で生埋め込み)。型詳細ハンドラは tl[typeId] で完全な t を引く (main.js:1557) ため t.filePath/t.startLine にアクセス可能。型詳細パネル(main.js:1556-1648)にソース導線が無いのも正しい。だがメンバー行レベル(main.js:1616-1631)への StartLine 付与には穴がある。(1) MemberInfo.StartLine は method のみ設定 (MemberExtractor.cs:105-111)、フィールド/プロパティ/イベント/enum メンバーには StartLine が無い(該当箇所で span 計算なし; enum も TypeInfo.cs:262-264 で未設定)。(2) MemberInfo に FilePath フィールドが無い(TypeInfo.cs:62-77)。partial 型はメンバーが複数ファイルから merge される(TypeInfo.cs:355)ため、メンバーの所属ファイルは親 t.filePath と一致しない場合がある。よって『メンバー行に View source』はメソッド限定かつ partial で誤リンクし得る。型レベルの Open in editor は前提が成立し valid。
- 修正案: スコープを2段に分ける。(1) 型レベルの『Open in editor』『View source』は t.filePath+t.startLine で即実装(valid、ここは最小接ぎ木で確実)。(2) メンバー行のソースジャンプは、対象を StartLine が存在するメソッド系メンバーに限定し、フィールド/プロパティ/enum には出さない(または型先頭にフォールバック)。partial 型の誤リンク回避のため、MemberInfo に FilePath を追加する小改修(MemberExtractor 側で member.GetLocation().SourceTree.FilePath を埋める)を前提に入れるか、partial 型ではメンバー行ジャンプを無効化する。vscode://file/{path}:{line} とサーバー側 code -g の両睨み方針自体は妥当。

#### lazygit流のキーボードナビゲーション層を載せる

- verdict: valid
- effort: M / impact: medium
- what: installViewerKeyboard(main.js:1887-1901)を拡張: j/k で左パネル(hpList/cycList)とノード詳細内リストを上下移動、Enter で選択(=tap相当を発火)、Tab/Shift+Tab で『グラフ↔左パネル↔詳細パネル』のフォーカス循環、数字キー1-3でパネル切替(Hotspots/Cycles/Assemblies)、g でソースジャンプ、? でキーバインド一覧オーバーレイ。フォーカス中要素に視覚的アウトラインを付与。既存の isEditableTarget ガード(main.js:1841-1845)で入力中は無効化。
- why: 要件『基本UIは今のまま』を保ちつつ、lazygitのインタラクションモデル(マウス不要・キーで全操作)へ寄せる。現状は / と Escape の2キーのみで、リスト移動もパネル遷移も全部マウス依存(cy.on tap, hpList click main.js:2426)。
- evidence: src/Unilyze/Templates/viewer/main.js:1887-1901,1841-1859,2426-2430 / index.html:34-61
- 検証ノート: 現状キーは / と Escape のみ: installViewerKeyboard (main.js:1887-1901) は Escape→handleEscapeKey と / →focusSearchInput だけを拾い、j/k/Tab/数字キーは未処理。isEditableTarget ガード (main.js:1841-1845) で入力中無効化は実在し再利用可能。リスト/パネル遷移がマウス依存も正しい: 型タップ→詳細パネルは cy.on('tap','node[nodeType="type"]') (main.js:1556)、hpList/cycList は addEventListener('click',...)→navigateToType (main.js:2426-2434)。左パネル(hp/cycp)とノード詳細(dp)の DOM 構造も index.html:34-61 に実在。Enter=tap 相当の発火先 navigateToType (main.js:2151) が既にあり、選択→展開→詳細パネル表示まで一気通貫で呼べるため Enter ハンドラの接続先は確保済み。前提に矛盾なし。effort M・impact medium も妥当。

#### 変更ハイライトのトランジェント・アニメーションをライブ更新に追加する

- verdict: needs-revision
- effort: S / impact: medium
- what: applyDelta時に、追加/変更/削除されたノード・エッジへ一時的な強調クラス(例 .flash-added/.flash-changed)を付与し1.5秒後に自動フェードアウトするCSSアニメーションを追加。色は既存のdiffバケット配色(改善#7ee787/劣化#f97583/追加#58a6ff, styles.css:293-301)を流用。既存の dim/hl クラス機構(main.js:1764-1767)と同じ add/removeClass パターンで実装。
- why: lazygitの『どこが変わったか一瞬で分かる』体験の要。差分パッチ型描画(提案1)だけだと変化が静かすぎて見落とす。情報過多を避けつつ注意を引くため、恒久ハイライトでなく自動消滅型にする。
- evidence: src/Unilyze/Templates/viewer/main.js:1764-1767,1332-1336 / src/Unilyze/Templates/viewer/styles.css:293-301
- 検証ノート: 実装手段の前提は概ね成立: dim/hl の add/removeClass パターンは実在(applyGraphSearchHighlight main.js:1764-1765, navigateToType の node.addClass('hl')+setTimeout removeClass main.js:2159-2160 が既に『一時クラス→自動消滅』の完成例)。diffバケット配色も styles.css に実在: 改善 #7ee787 / 劣化 #f97583 / 追加 #58a6ff (styles.css:293-300)。ただし evidence の styles.css:293-301 は diff-badge と diff-row の定義であり、提案文の『改善#7ee787/劣化#f97583/追加#58a6ff』の対応は dA(added)が #7ee787、dI(improved)が #7ee787、dD(degraded)が #f97583、dA でなく added の box-shadow が #58a6ff(styles.css:300)で、提案文の『追加#58a6ff』は diff-row.diff-added 由来。色値自体は正しいが badge と row で added の色が緑/青に割れている点は実装時の注意点。より本質的な見落とし: この提案は提案1(差分パッチ型ライブ更新)に全面依存するが、提案1を needs-revision に倒した(エッジ毎回再構築・viewport未保存)ため、applyDelta が存在しない現状ではフラッシュを差し込む対象フック(どのノードが added/changed/removed か)を提案1側で先に確定する必要がある。単独では着手不可。
- 修正案: 提案1の修正後フック(増分ノード add/remove 箇所 main.js:1123-1135、エッジ増分)に依存することを明記し、提案1の後続として順序づける。実装自体は navigateToType の hl+setTimeout パターン(main.js:2159-2160)を雛形にすれば S 妥当。added 色は badge(#7ee787)と row(#58a6ff)で割れているので、フラッシュ用クラスは独自に .flash-added=#58a6ff 等を新規定義して曖昧さを排す。

#### 詳細パネルに difit 風の行レベル diff ビューを追加する

- verdict: needs-revision
- effort: L / impact: medium
- what: diffモード(DIFF読込済み, main.js:60-105)の型詳細で、現状のメトリクスΔ/メソッド増減(renderDiffSections main.js:144-190)に加え、変更メソッドの実ソース行を before/after でside-by-side表示するセクションを追加。before/after のソーススナップショットはserve/diffコマンド側で各JSONに対応するファイル内容(filePath+startLine+lineCountで範囲特定)を読み出して同梱、ビューア側は差分計算(LCSベースの軽量diff)して色付けレンダリング。情報過多回避のため折り畳み(デフォルト閉じ)。
- why: 要件3『difitのようにdiffを画面に表示』。現状はメトリクスとメソッド名の増減までで、コード行そのもののdiffは無い。filePath/startLine/lineCountで対象範囲を一意に切り出せる素材は揃っている。
- evidence: src/Unilyze/Templates/viewer/main.js:144-190,60-105 / src/Unilyze/Pipeline/TypeInfo.cs:54,56-57,72-73
- 検証ノート: diff読込・オーバーレイ基盤は実在: DIFF 読込と dl への _bucket 注釈 (main.js:57-77)、renderDiffSections が型詳細に差し込まれる (main.js:144-190, 呼び出し main.js:1604)。現状はメトリクスΔ(main.js:150-162)・メソッド単位の増減(methodDiffs main.js:163-176)・smell増減(main.js:177-188)までで行レベル diff は無い、も正しい。だが『filePath/startLine/lineCount で対象範囲を一意に切り出せる素材は揃っている』は型レベルでしか成立しない。範囲特定に使う lineCount/startLine は型は TypeInfo.cs:56-57 で揃うが、メソッド単位では MemberInfo.LineCount/StartLine がメソッドのみ設定(MemberExtractor.cs:106-107,111)で、しかも MemberInfo に FilePath が無い(TypeInfo.cs:62-77)。さらに致命的なのは『before/after のソーススナップショットを serve/diff コマンド側で読み出して同梱』という前提で、serve コマンドは存在せず(Program.cs:18-45 のルーティングに無し)、diff コマンドの JSON は filePath を持つが before スナップショット時点のソース内容は保存されない(diff は2つの解析JSONの比較であってソース本文を含まない)。before のファイル本文を後から復元する手段(git ref 等)が別途必要で、現素材だけでは side-by-side は組めない。
- 修正案: 『素材は揃っている』を撤回し、ソース本文の取得経路を設計に追加せよ。実装ルート: serve/diff 実行時に before=git ref(または baseline スナップショット)、after=作業ツリーから該当ファイル範囲を読み出してJSONに同梱する仕組みを新設する(diff コマンドが2つのパス/refを取る前提に拡張)。メソッド範囲特定は MemberInfo.StartLine+LineCount(メソッドのみ)に依存するため、対象を変更メソッドに限定し、FilePath 欠落は親型 filePath で代替(partial は誤特定リスクありガード必須)。LCSベース軽量diff+折り畳みのレンダリング方針自体は妥当。effort L は据え置きだが、ソース取得インフラ込みなら過小評価気味。

#### フロー図モード(呼び出し/制御フロー)を専用パネルとして追加する

- verdict: needs-revision
- effort: L / impact: medium
- what: 型詳細パネルからメソッドを選ぶと、そのメソッドの呼び出し/依存フローを別レイヤーのCytoscapeインスタンス(同梱のdagre/elkを再利用 index.html:71-73)で描画するサブビューを追加。まずは既存の型間 dependencies(DC/DS のエッジ種別 main.js:855-)とメンバーの参照関係をスコープした部分グラフから着手し、制御フローはRoslyn側で ControlFlowGraph を将来抽出して段階導入。レイアウトは LR(rankDir)固定でフロー向きを明示。
- why: 要件4『フロー(呼び出し/制御/依存)を図として描画』。Cytoscape+dagre+elkが既に同梱(オフライン動作可, index.html:71-73)なので追加vendorゼロで部分グラフ描画が可能。型レベル依存は既にDATA.dependenciesにある。
- evidence: src/Unilyze/Templates/viewer/index.html:71-73 / src/Unilyze/Templates/viewer/main.js:853-865,1180-1196 / src/Unilyze/Pipeline/TypeInfo.cs:13-33
- 検証ノート: 型レベル依存が DATA.dependencies にあるのは正しい: dependencyElements を DATA.dependencies から構築 (main.js:855-878)、DC/DS のエッジ種別も実在。Cytoscape+dagre が同梱なのも正しい(vendor/ に cytoscape.min.js, dagre.min.js, cytoscape-dagre.js、index.html:71 __VENDOR_SCRIPTS__ でインライン展開、combine.py 経由)。だが『同梱の dagre/elk を再利用(オフライン動作可, index.html:71-73)』は誤り。elk は同梱されておらず CDN 依存: index.html:73 が <script src="https://unpkg.com/elkjs@0.9.3/lib/elk.bundled.js"> でロード、worker も unpkg から(main.js:1200 elkWorkerUrl)。vendor/ に elk は無い(cytoscape/dagre/cytoscape-dagre の3つのみ)。よってオフラインで使えるのは dagre のみ。さらに『制御フローは ControlFlowGraph を将来抽出』は要件4の核(呼び出し/制御フロー)が現状データに無いことを認めており、着手範囲は実質『型間依存の部分グラフ描画』に縮む。メソッド呼び出しグラフ・制御フローは Roslyn の追加抽出が前提で、メンバー参照関係(member references)は現データに含まれない(DATA.dependencies は型間のみ、TypeInfo.cs:13-18 TypeDependency は型単位)。
- 修正案: 『elk 同梱でオフライン可』を撤回し、フロー図のレイアウトは同梱 dagre(LR/rankDir)に限定する(elk はオフライン保証外)。第1段の実現範囲を『DATA.dependencies の型間部分グラフを別 Cytoscape インスタンスで LR 描画』に正しく限定して明記する。メンバー参照関係・呼び出しグラフ・制御フローは現データに存在しないため、Roslyn 側で呼び出しエッジ(InvocationExpression 解決)/ControlFlowGraph を新規抽出し DATA に追加する別フェーズとして分離。impact medium・effort L は妥当だが、要件4の『呼び出し/制御フロー』までやるなら Roslyn 抽出のコストが L を超える可能性を見積もりに反映する。

Open questions:

- ライブ更新の配信方式: file:// 単一HTMLのゼロセットアップ思想を維持しつつどう即時反映するか。(a)軽量HTTPサーバー(`unilyze serve`)+SSE/WebSocketでpush、(b)file://のまま meta refresh/ポーリングでJSON再読込、(c)file://+ローカルJSONをポーリング。serve導入はProcess.Startの開き方(ProgramHelpers.cs:238)とProgram.csのルーティング(L18-45)に新コマンドを足す前提だが、AOT/単一実行ファイルでのHTTPサーバー同梱コストとセキュリティ(localhost束縛/CORS/任意ファイル読取防止)をどう抑えるか。
- 再解析トリガと粒度: FileSystemWatcherでファイル保存を検知→`--incremental`(SyntaxIncrementalCollector.cs:36-49)で変更ファイルのみ再パースする想定だが、現状incrementalはSyntaxOnly経路のみでsemanticは無効。ライブ更新でCodeHealth等のsemanticメトリクスをどこまでリアルタイム更新するか(syntaxだけ即時/semanticは遅延バッチ等)。
- 視点保持の正確な単位: ズーム/パン/expanded展開状態に加え、選択中ノード・開いているパネル・スクロール位置・検索クエリのどこまでを再描画後に復元するか。lazygit的には『カーソル位置(選択)』が最重要だが、現状ビューアに永続的な選択状態の概念が無い(tapで都度パネル描画 main.js:1556)。選択状態モデルを新規に持つ必要がある。
- ソースジャンプの両睨み実装: エディタ起動はサーバー経由(`code -g`)とディープリンク(vscode://, cursor://)のどちらを既定にするか。file://運用ではプロセス起動できずディープリンク頼みになり、エディタ種別の検出/設定(ConfigRunner連携)が必要。ブラウザ内ソース閲覧を選ぶ場合、ソース全文をHTMLに同梱するとサイズ肥大とセキュリティ(生ソース埋め込み時のXSS, threat-model.md参照)が問題になる。
- 変更ハイライトと情報過多のバランス: 大規模グラフ(型200本規模)でライブ更新のたびに多数ノードがflashすると逆に煩雑。変更ノード数が閾値超過時はサマリーバナーのみにフォールバックする等の抑制ポリシーが要る(既存のSEARCH_EXPAND_CAP main.js:1689と同思想)。
- フロー図の制御フロー抽出スコープ: 呼び出しグラフはsemanticモデル(SymbolInfo)が必要で、現状の型レベルdependencies(DATA.dependencies)だけでは『どのメソッドがどのメソッドを呼ぶか』が出せない。Roslyn ControlFlowGraph/SymbolFinderを使うとフルcompilation必須でライブ更新のコストが跳ね上がる。どのレベル(型依存のみ/メソッド呼び出し/制御フロー)を最初に出すか。

### ローカルサーバー/ソース配信のセキュリティ

現状: 現状はサーバーレス。ネットワーク待受プリミティブは一切存在しない（`Program.cs:11-45` は15コマンドをルーティングするが `serve`/`watch` は無く、`HttpListener`/`TcpListener`/`WebSocket`/`FileSystemWatcher`/`Kestrel` の使用は0件、確認済み）。ビューアは `file://` で開く静的HTML1枚で、`ProgramHelpers.TryOpenInBrowser`(`src/Unilyze/Cli/ProgramHelpers.cs:234-250`)が `open`/`xdg-open`/`UseShellExecute` でブラウザに渡すのみ。MCPは stdio 専用で待受しない（`src/Unilyze/Mcp/McpStdioServer.cs:10-12` が `Console.OpenStandardInput()` を読むだけ）。

データ流入の構造: `Program.cs:169` で `HtmlFormatter.Generate(json, result.ProjectPath)` を呼び、`HtmlFormatter.cs:13-28` が解析JSONを `__DATA_PLACEHOLDER__` に生埋め込みし、`main.js:1` で `const DATA = __DATA_PLACEHOLDER__;` として読む。XSS緩和は2点のみ: (a) System.Text.Json 既定エンコーダ（`<>&+` をエスケープ、`AnalysisResult.cs:46` の `JsonSourceGenerationOptions` にカスタムエンコーダ指定なし）、(b) `HtmlFormatter.cs:27-28` の `</script` → `<\/script` 書換え。クライアント側は `main.js:192-198` の `escapeHtml`（`&<>"` のみ、`'` 非対応）を約15箇所の `innerHTML` シンク（`main.js:84,441,463,491,572,600,1648,1944` 等）に呼び出し側責任で都度適用する設計。テンプレートに CSP meta タグは無い（`src/Unilyze/Templates/viewer/index.html:4-5` は charset/viewport のみ）。`docs/threat-model.md` は「未信頼リポジトリ解析→生成HTMLを開く」脅威を `</script` 書換えで緩和すると明記。

重要: 解析JSON(=DATA)には絶対パスが既に含まれる。実際に生成して確認すると `"filePath": "/Users/.../src/Unilyze/Cli/CliArgValidation.cs"`（全型に付与、`CodeHealthCalculator.cs:49` の `string? FilePath`）と `"projectPath": "/Users/.../src/Unilyze"`（`AnalysisResult.cs:13`）が出力される。両者とも `[JsonIgnore]` 無し+`DefaultIgnoreCondition=WhenWritingNull` のため非null時は必ずシリアライズされる。現状この `filePath` はビューアJSでDOM描画には未使用（`main.js` に `filePath` 参照0件、データのみ）。つまりserve化/ソースジャンプ実装が初めてこの絶対パスを「サーバーが読むファイルパス」として消費する=パストラバーサルの第一シンクになる。`--incremental` キャッシュは `<project>/.unilyze/cache/syntax/v1/` のper-fileハッシュ（`SyntaxIncrementalCollector.cs` 系）で、ライブ再解析の差分検知に流用できるが現状ファイル監視は無い。

#### serve はループバック専用バインド + ランダムポート + 起動毎トークン必須を既定にする

- verdict: valid
- effort: M / impact: high
- what: 新設する `unilyze serve`/`watch` の HTTP リスナを `127.0.0.1`(必要なら `::1`)固定でバインドし、`0.0.0.0`/`--host` 公開は明示フラグ+警告無しには許さない。ポートは0指定でOS割当のランダムにし、起動毎に高エントロピーのセッショントークンを生成、ブラウザURLに `?token=...` を載せて `TryOpenInBrowser` に渡す(`ProgramHelpers.cs:234`を流用)。全エンドポイント(静的配信/ソース取得/WS)でトークンを検証し、未一致は404。これによりlocalhostマルウェア/他ユーザープロセスからの無認可アクセスとDNSリバインディングの初手を塞ぐ。
- why: serve化の最大の攻撃面増分はネットワーク待受そのもの。現状 `file://` で攻撃面ゼロ(`ProgramHelpers.cs:238`)なので、待受を足すなら最小権限が必須。ライブ反映/ソースジャンプ/diff/フロー図いずれもHTTP配信を前提にするため、土台のバインド方針を最初に固定しないと後段の全機能がリスクを継承する。
- evidence: src/Unilyze/Cli/ProgramHelpers.cs:234-250, src/Unilyze/Program.cs:11-45
- 検証ノート: 前提は実コードと整合。ネットワーク待受プリミティブは0件を確認(rg で HttpListener/TcpListener/WebSocket/FileSystemWatcher/Kestrel/WebApplication いずれも src/ にヒットなし)。現状 file:// 配信は ProgramHelpers.cs:234-250 の TryOpenInBrowser のみで、攻撃面ゼロという認識は正しい(238行 'file://'+GetFullPath)。?token 付与URLを TryOpenInBrowser に渡す流用は技術的に妥当。Program.cs:11-45 に serve/watch コマンドは無く(15コマンドのルーティングのみ)、新設が前提という認識も正確。バインド方針を土台に固定すべきという主張は妥当。1点だけ実装注意: TryOpenInBrowser のブラウザ起動は macOS=Process.Start('open',url)、Linux=xdg-open、Windows のみ UseShellExecute=true(ProgramHelpers.cs:239-244)で、引用の 'ProgramHelpers.cs:234' でURLを渡す流用自体は問題ないが、token入りURLをコマンド引数に載せる際は引数配列渡し(open/xdg-open は既に引数渡し)を維持すること。提案内容自体に欠陥なし。

#### ソース配信は『解析時に確定したファイル集合のallowlist』からのみ供給し、生パスをサーバーに渡さない

- verdict: valid
- effort: M / impact: high
- what: ソースジャンプ/ソース閲覧/diff表示のバックエンドを、リクエストの生パスでファイルを開く実装にしない。解析結果に既に含まれる絶対 `filePath`(`CodeHealthCalculator.cs:49`、実測で全型に絶対パス出力を確認)を起動時に正規化(`Path.GetFullPath`)して不変なallowlist Set/Dictに格納し、クライアントには `fileId`(ハッシュ or インデックス)のみ公開。`/source/{fileId}` は Set に存在する実体だけを返す。さらに返却前に `projectRoot` 配下かを `Path.GetFullPath(resolved).StartsWith(canonicalRoot)` で再検証し、シンボリックリンク経由の脱出も拒否。これでパストラバーサル(`../`, 絶対パス注入, URLエンコード, ヌルバイト)を構造的に排除する。
- why: ゴール2(ソースジャンプ/ブラウザ内ソース閲覧)とゴール3(diff表示)は必ずファイル内容をHTTPで返す。DATAに絶対パスが入る(`projectPath`/`filePath`を実測確認)現状をそのまま『パスを受け取って開く』設計にすると古典的パストラバーサルで `/etc/passwd` やリポジトリ外の秘密ファイルが読める。allowlist方式なら未信頼リポジトリ解析時でも配信対象が解析済みソースに限定される。
- evidence: src/Unilyze/Metrics/CodeHealthCalculator.cs:49, src/Unilyze/Pipeline/AnalysisResult.cs:13, src/Unilyze/Pipeline/TypeInfo.cs:54
- 検証ノート: 中核前提を実測で裏取り済み。dotnet run --framework net10.0 -- -p src/Unilyze/History -f json で生成したJSONに projectPath='/Users/bigdra/.../src/Unilyze/History'(絶対)、filePath='/Users/bigdra/.../src/Unilyze/Cli/CliArgValidation.cs'(絶対、全型に付与)を確認。引用の根拠ファイルも正確: TypeInfo.cs:54 の TypeNodeInfo.FilePath は非null string(ParseSingleFile が path:filePath で原パスを格納、TypeInfo.cs:140)、CodeHealthCalculator.cs:49 の TypeMetrics.FilePath は string?、AnalysisResult.cs:13 ProjectPath は非null string。DefaultIgnoreCondition=WhenWritingNull(AnalysisResult.cs:49)+ [JsonIgnore]無しのため非null時は必ず出力されるという主張も正しい。重要な追加発見として、-p で History を指定しても Cli/ 等プロジェクト全体の絶対パスがDATAに載った(プロジェクトルート解決で範囲が広がる)ため、allowlist を『解析時に確定したファイル集合』に固定する設計はむしろ必須。Path.GetFullPath正規化 + StartsWith(canonicalRoot)再検証 + fileId公開という構造的排除は適切。シンボリックリンク脱出言及も妥当(StartsWith だけでは不十分なので canonical 化前提なのも正しい)。

#### ライブ配信に移行する際、HtmlFormatterの生埋め込みをやめ DATA を別エンドポイント+CSPで配る

- verdict: needs-revision
- effort: M / impact: high
- what: serve化に合わせ、解析JSONを `__DATA_PLACEHOLDER__` への文字列置換(`HtmlFormatter.cs:13-28`)で埋めるのをやめ、`/data.json`(と `/diff.json`)としてContent-Type固定で配信、ビューアは `fetch` で取得する。HTMLレスポンスに `Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:` 等を付与し、同梱vendor(`HtmlTemplate.cs:14-26`のCytoscape/dagre)も `'self'` 化(現状インライン`<script>`なのでnonce付与か外部ファイル化が必要)。ELKのCDNフォールバックはCSPと両立するよう `connect-src`/`script-src` を限定。生埋め込みXSSの主たる緩和(`</script`書換え+STJ既定エンコーダ)への暗黙依存を、CSPという多層防御で補強する。
- why: docs/threat-model.md が認める通り現状XSS耐性は『STJ既定エンコーダ』『</script書換え』の2点の暗黙依存に過ぎず、`escapeHtml`(`main.js:192`)は呼び出し側責任・`'`非対応で漏れが起きやすい。serve化でブラウザが `http://localhost` 原点を持つと file:// 時より権限(fetch/WS/cookie)が増えるため、未信頼リポジトリ名/型名/smellメッセージ由来のXSSが成立した場合の被害が拡大する。CSPはこの新原点での被害を構造的に抑える。
- evidence: src/Unilyze/Output/HtmlFormatter.cs:13-28, src/Unilyze/Templates/viewer/index.html:4-5, src/Unilyze/Templates/viewer/main.js:192-198, docs/threat-model.md:5-6
- 検証ノート: 提案の方向性(別エンドポイント配信+CSP多層防御)は妥当で、根拠も概ね正確: HtmlFormatter.cs:13-28 の __DATA_PLACEHOLDER__ 文字列置換埋め込み、escapeHtml の ' 非対応(main.js:192-198 で &<>" のみ、rg で ' 置換なしを確認)、threat-model.md:5-6 が STJ既定エンコーダ + </script書換えの2点依存を明記、index.html:4-5 に CSP meta 無し、を全て確認。ただしCSPディレクティブ案に実装と矛盾する見落としがある。(1)提案の connect-src 'self' だけでは ELK が壊れる: index.html:73 は <script src='https://unpkg.com/elkjs@0.9.3/...'> という外部CDNの実スクリプトロード(『CDNフォールバック』ではなく既定でロードされる)。CSPで動かすには script-src に https://unpkg.com を明示追加(またはELKもvendor同梱でインライン化)が必要で、提案の『ELKのCDNフォールバックは connect-src/script-src を限定』では unpkg を許可リスト化する具体が抜けている。(2)vendor(Cytoscape/dagre/cytoscape-dagre)は HtmlTemplate.cs:13-36 で <script>...payload...</script> のインライン埋め込み。script-src 'self' 化には nonce 付与か外部ファイル化が必須で、提案はこれを認識しているが『'self'化』と書きつつ現状インラインなので nonce 無しでは即破綻する点を実装手順として明示すべき。
- 修正案: CSPディレクティブを実コードに合わせて具体化する。(a) ELK(index.html:73)は2択: ①vendor/ に elk.bundled.js を同梱しインライン化して script-src 'self'/nonce に寄せる(他vendorと統一)、または ②CDN継続なら script-src に 'self' https://unpkg.com を明示。connect-src だけ開けてもscriptはロードされない。(b) インラインvendor(HtmlTemplate.cs:13-36)は nonce 付与(<script nonce=...>)か外部静的ファイル(/vendor/cytoscape.js 等)化のいずれかを serve 化と同時に実施。'unsafe-inline' をscript-srcに入れない方針なら nonce 必須。(c) DATA を /data.json でContent-Type固定配信し fetch する案、style-src 'unsafe-inline' 許容(index.html の <style> インライン用)はそのままで可。提案の被害拡大ロジック(http://localhost原点でfetch/WS/cookie権限増)は正しいので保持。

#### WebSocket/SSEのライブ更新チャネルに Origin 検証 + トークン検証 + ローカルピア確認を必須化

- verdict: valid
- effort: M / impact: high
- what: ゴール1(lazygit的即時反映)を `FileSystemWatcher`+WS/SSEで実装する際、WSハンドシェイクで `Origin` ヘッダを期待値(`http://127.0.0.1:{port}`/`http://localhost:{port}`)にホワイトリスト一致させ、不一致は接続拒否。提案1のセッショントークンもサブプロトコル or 初回メッセージで検証。SSE採用なら同様にOrigin+トークン+`X-Requested-With`を要求しCORSを既定で閉じる(`Access-Control-Allow-Origin`をワイルドカードにしない)。更新通知のペイロードはイベント種別+fileIdのみに留め、ソース本文は提案2の認可済みエンドポイント経由でのみ取得させる。
- why: WSはSame-Origin Policyの強制対象外でブラウザが任意オリジンから接続を試せるため、悪意あるWebページがユーザーのローカル `unilyze serve` に接続し解析データ/ソースを盗む『WebSocketハイジャック』が成立しうる。CSRF/CORSの既定を閉じておかないと、ライブチャネルがそのまま情報漏洩経路になる。
- evidence: src/Unilyze/Program.cs:11-45, src/Unilyze/Mcp/McpStdioServer.cs:10-12
- 検証ノート: 提案は標準的かつ正しい。WebSocketがSame-Origin Policy強制外でCSWSH(Cross-Site WebSocket Hijacking)が成立しうるのは事実で、Origin ヘッダのホワイトリスト一致 + トークン + CORS既定クローズは妥当な対策。前提の実コード状況も整合: WS/SSE実装は現状ゼロ(rg で WebSocket ヒットなし)、MCPは McpStdioServer.cs が stdio 専用で待受しない(Console.OpenStandardInput を読むのみ、Program.cs:44-45 の mcp ルーティングも stdio)。ライブ更新を FileSystemWatcher + WS/SSE で新設する前提も Program.cs:11-45 に該当コマンド無しと整合。更新通知ペイロードを『イベント種別+fileIdのみ』に絞り本文は提案2の認可済みエンドポイント経由でのみ取得させる多層化も提案2と整合し妥当。--incremental の per-file ハッシュキャッシュ(syntax レベル限定、AnalysisPipeline.cs:45 が 'accelerates syntax-level analysis only' と警告)を差分検知に流用する案とも矛盾しない。

#### エディタ起動(vscode:// 等URIスキーム / プロセス起動)はクライアントから直叩きさせず、サーバー側allowlistコマンドに限定

- verdict: valid
- effort: M / impact: high
- what: ゴール2の『エディタ起動』を実装する際、ブラウザから `vscode://file/...` 等の任意URIをそのまま `window.location`/`window.open` で発火させない(任意スキーム/任意コマンド起動リスク)。代わりに `/open?fileId=...&line=...` をサーバーに送り、サーバーが (a) fileIdを提案2のallowlistで解決、(b) 起動エディタを設定(`--editor code|cursor|...`)からの固定allowlist+引数を `ProcessStartInfo` で `UseShellExecute=false`・引数配列渡し(`GitProcess.cs:15`/`StatuslineRunner.cs:275`の既存パターンに倣う)で起動する。`code -g {path}:{line}` 形式のpath/lineは数値・allowlist検証後にのみ渡し、シェル展開を経由させない。
- why: ブラウザ内から任意URIスキームを叩ける設計は、未信頼リポジトリ由来の細工された型名/パスがエディタや外部ハンドラの任意起動・引数注入に化ける。既存コードは `UseShellExecute=false`+引数配列(`GitProcess.cs:15-24`)という安全パターンを持つので、エディタ起動も同方式に寄せれば新たなコマンドインジェクション面を増やさない。`TryOpenInBrowser`(`ProgramHelpers.cs:242`)の `UseShellExecute=true` はブラウザ起動の既存用途に留め、ソース由来データは渡さない。
- evidence: src/Unilyze/History/GitProcess.cs:15-24, src/Unilyze/Runners/StatuslineRunner.cs:275-283, src/Unilyze/Cli/ProgramHelpers.cs:238-244
- 検証ノート: 根拠パターンを全て実コードで確認。GitProcess.cs:7-19 は UseShellExecute=false + psi.ArgumentList.Add(arg) の引数配列渡しという安全パターンを持つ(引用の GitProcess.cs:15-24 と一致)。StatuslineRunner.cs:270-284 の StartDetachedProcess も UseShellExecute=false + ArgumentList.Add(引用 275-283 と一致)。よって『既存の安全パターンに寄せる』主張は成立。エディタ起動を /open?fileId&line でサーバー受け→fileIdを提案2のallowlistで解決→ProcessStartInfo引数配列で起動、line/pathを数値・allowlist検証後のみ渡しシェル展開を経由させない、という設計は妥当。1点の精度補足: 提案は『TryOpenInBrowser(ProgramHelpers.cs:242)の UseShellExecute=true はブラウザ起動の既存用途に留め』と書くが、242行(UseShellExecute=true)は Windows 分岐のみで、macOS は Process.Start('open',url)・Linux は xdg-open(239-244)。いずれもソース由来データを渡さない方針は正しく、提案の結論に影響なし。任意URIスキームをブラウザ内で発火させない判断は適切。

#### projectPath/filePath の絶対パス露出を最小化し、相対パス+fileIdへ正規化する

- verdict: valid
- effort: S / impact: medium
- what: DATAに絶対 `projectPath`/`filePath` がそのまま載る現状(実測確認)を、serve化を機にビューア向けには相対パス(`Path.GetRelativePath` は既に `SarifFormattingHelpers.cs:230` / `FindingFingerprint.cs:49` で実績あり)+不透明fileIdへ変換する選択肢を用意する。サーバー内部のallowlistだけが絶対パスを保持し、クライアントへは漏らさない。`--input` 経由(`Program.cs:128-133`)で他者作成JSONを読む経路では、含まれる絶対パスがホスト名/ユーザー名/ディレクトリ構造を暴露する点も明示。完全な抑止が過剰なら、最低限ドキュメント(threat-model.md)に『生成JSON/HTMLは絶対パスを含む共有注意artifact』と追記する。
- why: 絶対パスはユーザー名/マシン構成を暴露し、共有された解析HTML/JSONから環境情報が漏れる。serve化でソース配信のキー(fileId)を導入するなら、ついでにクライアント露出を相対化でき、提案2のパストラバーサル対策とも整合する。既存の相対化ヘルパを再利用するため新規実装は最小。
- evidence: src/Unilyze/Pipeline/AnalysisResult.cs:13, src/Unilyze/Output/SarifFormattingHelpers.cs:230-233, src/Unilyze/Findings/FindingFingerprint.cs:49-55, src/Unilyze/Program.cs:128-133
- 検証ノート: 前提と再利用ヘルパの存在を確認。絶対パス露出は実測で裏取り済み(proposal2 と同JSON: projectPath/filePath ともに /Users/bigdra/... の絶対パス=ユーザー名・マシン構成を暴露)。相対化ヘルパの実績も正確: SarifFormattingHelpers.cs:230-235 GetRelativePath(Path.GetRelativePath + \→/ 置換)、FindingFingerprint.cs:49-56 GetRelativePath(null/空ガード付き)を確認。引用の AnalysisResult.cs:13 ProjectPath も実在。--input 経由(Program.cs:128-133 で File.ReadAllText(input)→Deserialize)で他者作成JSONを読む経路も実在し、含まれる絶対パスが環境情報を暴露する指摘は妥当。effort=S/impact=medium も現実的(既存ヘルパ再利用)。最低限 threat-model.md(現状 docs/threat-model.md は XSS のみ言及、絶対パス露出には未言及)へ『生成JSON/HTMLは絶対パスを含む共有注意artifact』と追記する代替案も妥当な落とし所。proposal2 の fileId 導入と整合しており矛盾なし。

Open questions:

- ライブ反映のトリガをどうするか: serve内蔵の `FileSystemWatcher`(現状未使用、新規)で生ソース変更を監視し再解析するのか、既存の `--incremental` キャッシュ(`<project>/.unilyze/cache/syntax/v1/`)を流用して差分のみ再解析するのか。後者は semantic 無効(syntaxのみ)制約があり、CodeHealth等semantic依存メトリクスがライブで更新されない懸念。どこまでをライブ更新対象にするか要決定。
- serveのデフォルト寿命とアクセス制御の強度: 単発(ブラウザ閉じたら終了)か常駐か。常駐ならトークン+localhostバインドで十分か、それともOSユーザー単位の権限(ファイルパーミッション/Unixソケット)まで踏み込むか。ゼロセットアップ思想とのバランス。
- CSP導入時の同梱vendor(`HtmlTemplate.cs:14-26`のCytoscape/dagre/cytoscape-dagre)の扱い: 現状インライン`<script>`埋め込みなので `script-src 'self'` と非互換。nonce付与に切り替えるか外部静的ファイルとして配信するか。ELKのCDNフォールバックを残すなら `script-src`/`connect-src` にどのオリジンを許すか(オフライン動作思想との両立)。
- ソース閲覧 vs エディタ起動の優先度と既定値: ブラウザ内ソース表示(syntax highlight必要→新規依存 or 既存資産で完結?)を主にするか、エディタ起動を主にするか。エディタ起動を既定にすると `--editor` allowlistの初期セット(code/cursor/idea/vim?)とプラットフォーム差(macOS/Windows/Linux)の扱いが未定。
- `unilyze serve` を新コマンドとして `Program.cs` のルーティングに足すか、既存 `analyze` に `--serve`/`--watch` フラグを足すか。MCP(`McpRunner`)との関係: serveのバックエンドとMCPツール(analyze/diff/query)を共有して解析ロジック重複を避けられるか。
- フロー図(呼び出し/制御フロー)描画に必要なメソッドレベルのcall-graphデータが現状の解析結果に含まれるか未確認。含まれない場合、ライブ描画のためにどのデータをDATA(=クライアント露出)に追加するかで、追加情報(メソッド本体由来の文字列等)の新たなXSS/情報露出面を再評価する必要がある。

### 性能とスケーラビリティ

現状: ライブ更新の前提となる常駐・監視・差分配信機構は現状ゼロ。watch/serve コマンドは存在せず（Program.cs:18-45 のルーティングに無い）、FileSystemWatcher/HttpListener/WebSocket はリポジトリ全体に1件もない（rg -li で0件）。各 analyze はワンショットでプロセス起動→解析→HTML/JSON書き出し→ブラウザ起動（Program.cs:165-180）で終わり、再解析はプロセス再起動が前提。

再解析レイテンシ（実測、unilyze 自プロジェクト=326 csファイル/365型/800依存）:
- 既定 Complete レベル（HTMLビューアが使う経路）: 4.13s real / 9.25s user（並列）
- syntax レベル full: 0.90s
- syntax incremental cold: 1.45s（キャッシュ書込オーバヘッド）
- syntax incremental warm（無変更）: 0.59s
小規模でこの値なので、数千〜万ファイル級では Complete が数十秒〜分オーダーになる。プロセス起動＋JITコストも毎回発生。

--incremental の致命的制約: 構文レベル専用。Complete/full/core では明示的に無効化される（AnalysisPipeline.cs:42-47, options with { Incremental=false } して警告）。インクリメンタルが効くのは構文解析の再パースのみで（SyntaxIncrementalCollector.cs:36-49 で content-hash 一致ファイルをスキップ）、semantic phase の Compile（Roslyn CSharpCompilation 生成）と SemanticModel 取得は常に全体再実行される（SyntaxIncrementalSemanticPhase.cs は re-enrich 対象型だけ SemanticEnricher.Enrich を呼ぶが、その手前で BaseTypeResolver/DependencyBuilder/CouplingMetrics/CompilationResult は全体計算; AnalysisPipeline.cs:77-79 の compile phase 自体はキャッシュされない）。

ビューアが要求するデータと incremental の食い違い: 実測で syntax レベルは codeHealth=10/cbo=0 の縮退値しか出さず、Inheritance/InterfaceImpl 以外の usage 依存も欠落（depsKinds に Inheritance が無い）。一方 viewer は healthColor（main.js:135,395）・hotspot 表（main.js:293）・CBO/結合（typeMetrics）に依存（main.js:54,273）。つまり「速い incremental(syntax)」と「リッチな viewer データ(Complete)」が現状は両立しない。

差分配信ペイロード: 解析結果は const DATA = __DATA_PLACEHOLDER__（main.js:1）として HTML へ生文字列 Replace で丸ごと埋め込む（HtmlFormatter.cs:20-24）。JSON は WriteIndented=true（AnalysisResult.cs:47）で整形済み。実測ペイロード 2.88MB（indented）/ 1.84MB（minified, 36%削減余地）。差分専用の delta 配信プロトコルは無く、diff 機能も before/after 2スナップショットの GenerateWithDiff（HtmlFormatter.cs:10）で再び全 DATA を埋め込む方式。ライブ更新では毎回フル DATA を作り直して全 HTML を再生成する構造。

ブラウザ描画コスト: cytoscape にフル elements を渡し（main.js:898-900）、ネームスペース折りたたみ＋遅延 materialize（rebuild() main.js:1076-1175）で可視ノードを絞る設計は既にある。ただし rebuild()→layout()（main.js:1180-1189）は ELK/dagre レイアウトを毎回フル再計算（diffChangedOnlyHandler=function(){rebuild();layout();} main.js:1189）。ELK は CDN+dagre フォールバック、performance.mark でレイアウト計測済み（main.js:1251-1272）。debounce/throttle・差分パッチ適用は無く、データ更新＝全 elements 再構築＋全レイアウト再計算となる。

rc=134(SIGABRT) 既知点: CHANGELOG に「maxParallelism で Parallel.ForEach のパース/セマンティック pre-warm 並列度を上限化（既定 Environment.ProcessorCount）し、Complete レベル解析中の rc=134(SIGABRT) OOM 仮説を緩和（#62）」と明記。実体は SemanticEnricher の Parallel.ForEach（main.js相当 SemanticEnricher.cs:75 PrewarmModelCache, :98 Parallel.For で全型 enrich）。ResolveMaxParallelism は既定で全コア（UnilyzeConfig.cs:43-44）。ライブ更新で短間隔に Complete 解析を多重起動すると、この OOM/SIGABRT リスクが再燃する（プロセス常駐化＝メモリ蓄積、コアレッシング無しなら多重起動）。

#### watch/serve を常駐プロセス化し、ファイル監視＋debounce/コアレッシングで再解析を直列化

- verdict: valid
- effort: L / impact: high
- what: `unilyze serve -p <path>` 系コマンドを Program.cs のルーティング（Program.cs:18-45）に追加。FileSystemWatcher で .cs/.asmdef/シーン等を監視し、変更イベントを 150-300ms の debounce でまとめ、解析中に来たイベントは『次回1本』にコアレッシングする（多重起動禁止のセマフォ1）。HttpListener で軽量 HTTP+SSE を立て、既存 HTML をそのまま配信。AnalysisPipeline.Build は常駐内で再利用し、プロセス起動/JITコストを初回のみに償却する。
- why: ライブ更新ゴールの基盤。現状はワンショットでプロセス再起動が前提（Program.cs:165-180）。debounce/コアレッシング無しで Complete 解析（実測4.13s）を保存のたびに多重起動すると rc=134(SIGABRT/OOM, #62) が再燃する。直列化＋単一プロセスでこのリスクを構造的に断つ。
- evidence: src/Unilyze/Program.cs:18-45, src/Unilyze/Program.cs:165-180, src/Unilyze/Pipeline/AnalysisPipeline.cs:40-57
- 検証ノート: 現状認識は実コードと一致。Program.cs:18-45 のルーティングに serve/watch は無く、各 analyze はワンショット（Program.cs:165-180 で HTML/JSON 書き出し→TryOpenInBrowser→return 0）で終わる。FileSystemWatcher/HttpListener/WebSocket/SSE は src/ 全体に1件も無いことを rg で確認（CSharpCompilation.Create は CompilationFactory.cs:61 のみ）。AnalysisPipeline.Build（AnalysisPipeline.cs:18-38）は static で都度フル実行され、Compilation 構築コスト（後述）も毎回発生するため『常駐内で Build 再利用』の余地は実在する。rc=134(SIGABRT) OOM 仮説と maxParallelism 緩和は CHANGELOG.md:43 に #62 として明記済みで evidence と一致。debounce/コアレッシング＋セマフォ1の直列化方針は妥当で、ライブ更新基盤として正しい起点。effort=L も妥当（全くゼロからの常駐＋HTTP+SSE 配線）。

#### semantic 段を含むインクリメンタル化（Compilation の差分再利用）でライブ再解析レイテンシを目標化

- verdict: needs-revision
- effort: L / impact: high
- what: 現状 syntax 専用の incremental（AnalysisPipeline.cs:42-47 で Complete時に無効化）を、常駐プロセス内で Roslyn の `Compilation.ReplaceSyntaxTree(old,new)` を使い変更ツリーだけ差し替える方式へ拡張。SemanticModel キャッシュ（SemanticEnricher.cs:53 ConcurrentDictionary）を常駐保持し、変更/依存型のみ再 enrich（SyntaxIncrementalSemanticPhase.cs:35 の DetermineTypesToReEnrich を semantic 経路へ流用）。レイテンシ目標を『p50 < 1s / p95 < 3s（1ファイル変更時, 中規模）』と明示して計測ゲート化。
- why: viewer は codeHealth/CBO 等 Complete レベル semantic データに依存（main.js:135,273,395 / 実測 syntax は codeHealth=10/cbo=0 の縮退値）。一方 incremental が効くのは syntax のみ。『速い再解析』と『リッチな viewer データ』が現状両立しない。常駐 Compilation 差分再利用がこの分断を埋める唯一の現実解。
- evidence: src/Unilyze/Pipeline/AnalysisPipeline.cs:42-47, src/Unilyze/Pipeline/AnalysisPipelineSemanticPhase.cs:25-39, src/Unilyze/Incremental/SyntaxIncrementalSemanticPhase.cs:35-63, src/Unilyze/Pipeline/SemanticEnricher.cs:53-78
- 検証ノート: 課題認識は正確だが、提案文の前提に複数の不正確さがある。(1) incremental が syntax 専用なのは AnalysisPipeline.cs:42-47 で確認（RequestedLevel != Syntax なら警告して Incremental=false）。syntax 級が縮退値になるのは CompilationFactory.cs:32-33 が maxLevel==Syntax で Compilation=null を返し、AnalysisPipelineDiscovery.cs:196-197 が『Semantic metrics (boxing, CBO, DIT, etc.) are understated』と警告するため、cbo=0 等の縮退は実コードで裏取りできる。(2) 誤り: 『SemanticEnricher.cs:53 ConcurrentDictionary を常駐保持』。SemanticEnricher.cs:53 の ModelCache は EnrichmentContext.Create 内で Enrich() 呼び出しごとに new される“ローカル”キャッシュであり、現状は静的に常駐していない。Compilation も CompilationFactory.cs:61 で毎回 CSharpCompilation.Create され、ReplaceSyntaxTree/AddSyntaxTrees は src/ に1件も無い（rg 確認）。つまり『差分再利用』は既存機構の拡張ではなく完全な新規アーキ。(3) DetermineTypesToReEnrich（SyntaxIncrementalSemanticPhase.cs:78-107）は reparsedFiles（content-hash 由来）に依存しており、これは syntax-incremental 経路（SyntaxIncrementalCollector）専用入力。semantic 経路へ『流用』するには『変更ツリー集合』を別経路（FileSystemWatcher イベント等）から供給する設計が必要で、単なる流用では成立しない。さらに ReplaceSyntaxTree で差し替えても BaseTypeResolver/DependencyBuilder/CouplingMetricsCalculator（AnalysisPipelineSemanticPhase.cs:25-33）は全体再計算される設計で、ここを差分化しないとレイテンシ目標 p50<1s は達成困難。
- 修正案: (a) ModelCache と Compilation を『常駐保持』ではなく『常駐プロセス内で世代管理する新規キャッシュ層を導入』と書き換える（現状ローカルスコープである事実を明記）。(b) ReplaceSyntaxTree で差し替えた後も BaseTypeResolver/DependencyBuilder/CouplingMetrics は全体再計算される点を制約として明示し、レイテンシ目標は『SemanticModel 再取得＋影響型 re-enrich のみ』のスコープに限定する（型関係・依存グラフの差分化は別タスク）。(c) 変更ツリー集合の供給源を FileSystemWatcher イベントに固定し、DetermineTypesToReEnrich の『流用』ではなく『同等ロジックの再実装』と位置づける。(d) p50<1s/p95<3s は計測ゲートとして残しつつ、初期は『semantic full 再実行でも常駐 JIT 償却で 4.13s→数百ms 短縮』を最低ラインに段階化する。

#### 差分配信プロトコル: フル DATA 再埋め込みをやめ、JSON-Patch/型単位デルタを SSE で配信

- verdict: needs-revision
- effort: M / impact: high
- what: ライブ経路では HTML を毎回再生成せず（HtmlFormatter.cs:20-24 の全 Replace を回避）、初回のみフル DATA を配り、以降は『変更型の typeMetrics/依存エッジ差分』だけを SSE で push する。既存 DiffResult/deltaScore（diff の bucket 分類）の構造を流用し、サーバ側で前回スナップショットとの型単位 diff を計算して JSON-Patch 風ペイロードを送る。ライブ配信時は WriteIndented=false（AnalysisResult.cs:47 は integrated と分離）で minify。
- why: 実測でフル DATA は 2.88MB（indented）。保存のたびに全 HTML/JSON を再生成・再ロードするのは大規模で非現実的。1ファイル変更なら影響型は数個〜数十なので、デルタ配信でペイロードを桁で削減できる。minify だけで36%減（1.84MB）も即効。
- evidence: src/Unilyze/Output/HtmlFormatter.cs:10-24, src/Unilyze/Pipeline/AnalysisResult.cs:46-47, src/Unilyze/Templates/viewer/main.js:1, src/Unilyze/Templates/viewer/main.js:54
- 検証ノート: 方向性は妥当だが minify の根拠に技術的誤りがある。DATA 埋め込み（main.js:1 const DATA=__DATA_PLACEHOLDER__、HtmlFormatter.cs:20-24 の全 Replace）は確認。viewer が DATA.typeMetrics に依存することも main.js:54/273/293 で裏取り済み。DiffResult（DiffResult.cs:34-47）は TypeDiff（TypeKey＋MetricDelta 群）を Improved/Degraded/Added/Removed に分類保持しており、型単位 diff の骨格は流用可能 — ただし TypeDiff が持つのは“メトリクス差分値”であって“型の完全ペイロード（typeMetrics オブジェクト＋依存エッジ）”ではない。viewer が変更型を再描画するには full type object が要るため『DiffResult をそのまま JSON-Patch ペイロードに』は成立せず、型完全体の再シリアライズが別途必要。誤り: 『WriteIndented=false で minify、AnalysisResult.cs:47 は integrated と分離』。AnalysisResult.cs:46-56 の WriteIndented=true は source-generated AnalysisJsonContext（AnalysisResult/DiffResult/Hotspot 等を共有する単一コンテキスト）にコンパイル時固定された属性であり、ランタイムで false に切替できない。Program.cs:162 は AnalysisJsonContext.Default.AnalysisResult を使うため、minify するには別コンテキスト or ランタイム JsonSerializerOptions の新設が必要（rg で WriteIndented=true は同コンテキスト1箇所、別の integrated コンテキストは存在しない）。
- 修正案: (a) 『WriteIndented=false にするだけ』ではなく『ライブ配信用に WriteIndented=false の別 JsonSerializerContext（または明示 JsonSerializerOptions 経路）を新設する』と修正。source-gen 属性はコンパイル時固定である事実を明記。(b) デルタペイロードは MetricDelta ではなく『変更型の完全 typeMetrics オブジェクト＋追加/削除された TypeDependency エッジ』を JSON-Patch 風に送る設計に直す（DiffResult は変更型集合の特定にのみ流用）。(c) 初回フル＋以降型単位デルタの方針自体は valid なので維持。

#### ビューアに差分パッチ適用＋インクリメンタルレイアウトを実装し、全再構築を回避

- verdict: needs-revision
- effort: M / impact: high
- what: SSE 受信時に rebuild()+layout()（main.js:1189 diffChangedOnlyHandler）の全再計算を呼ばず、cy.startBatch 内で変更ノード/エッジだけ add/remove/data 更新する差分適用ハンドラを追加。レイアウトは変更近傍のみ再配置（incremental/constrained layout）か、ELK 再計算を 200-500ms debounce してコアレッシング。既存の materializedNamespaces 遅延 materialize（main.js:1124-1146）と performance.mark 計測（main.js:1251-1272）を活かす。
- why: 現状データ更新＝全 elements 再構築＋ELK/dagre フルレイアウト（main.js:1180-1189）。大規模グラフでレイアウトは最も重い処理で、ライブ更新の体感を支配する。差分パッチ＋debounce でフレーム落ちと再レイアウトコストを抑える。
- evidence: src/Unilyze/Templates/viewer/main.js:1180-1189, src/Unilyze/Templates/viewer/main.js:1076-1175, src/Unilyze/Templates/viewer/main.js:1249-1272
- 検証ノート: core 主張（全レイアウト再計算が体感を支配）は valid だが『全 elements 再構築』の現状認識が過剰。rebuild()（main.js:1076-1174）は既に cy.startBatch() 内（:1079）で動き、型ノードは namespace 単位で増分 add/remove（:1128-1137 で desiredTypes を差分追加、不要 ns を :1121-1126 で remove）しており、全ノード teardown ではない。実際に毎回フル破棄されるのは『エッジ』のみ（:1080 cy.edges().remove() → :1140 typeEdgeElements で再 add）と layout()（:1180-1189）の ELK/dagre フルレイアウト。diffChangedOnlyHandler=function(){rebuild();layout();}（:1189）は確認できるが、これは DIFF オーバーレイ（snapshot 比較）切替時のハンドラ（main.js:63 で宣言、:98-99 で diff トグルから呼ばれる）であり、ライブ更新用ではない点に注意。performance.mark 計測（:1172-1173, :1251-1272）と materialize 遅延（:1118-1146）も evidence 通り実在。つまり『差分パッチ＋レイアウト debounce』の価値は本物だが、ノード再構築は既に増分化済みなので、改善対象は (i) エッジの全再構築 (ii) layout() の全再計算 に絞るべき。
- 修正案: (a) 現状認識を『データ更新＝全 elements 再構築』から『エッジ全再構築＋レイアウト全再計算が重い。型ノード materialize は既に増分（main.js:1128-1137）』に訂正。(b) 差分パッチの対象を『変更型ノードの data 更新＋追加/削除エッジのみ』に限定し、cy.edges().remove() の全消去（:1080）を回避するエッジ差分ロジックを新設。(c) layout() の debounce/incremental は最も効果が高いので主眼に据える（既存 _layoutRequest 世代管理 main.js:1250/1266 と整合させる）。effort=M は妥当。

#### 大規模グラフ向けの上限ガードとサーバ側プリ集約（描画コスト上限化）

- verdict: valid
- effort: M / impact: medium
- what: 型数/エッジ数が閾値超過時、サーバ側であらかじめ namespace/assembly 集約のメタグラフだけを初期ペイロードに含め、ドリルダウン時に該当 namespace の型を遅延フェッチ（既存 typesByNamespace materialize と整合; main.js:820-826, 1124-1146）。閾値・上限はライブ経路の HTTP API でページング。cytoscape 初期 elements（main.js:898-900）に全件渡す現状を、可視範囲のみへ縮小。
- why: 現状 cytoscape へ全 elements を渡す（main.js:900）。万型級では初期構築だけでブラウザが固まる。サーバ側プリ集約＋遅延フェッチで初期描画コストをノード数に対してほぼ一定化でき、ゼロセットアップ思想（CDN非依存の同梱 vendor; cytoscape/dagre）も維持できる。
- evidence: src/Unilyze/Templates/viewer/main.js:898-900, src/Unilyze/Templates/viewer/main.js:820-826, src/Unilyze/Templates/viewer/main.js:1124-1146
- 検証ノート: evidence は実コードと一致。cytoscape へフル elements を渡す（main.js:898-900 elements:els）、typesByNamespace 構築（main.js:820-825）、materialize 遅延（main.js:1118-1146）すべて確認。viewer は既に namespace 折りたたみ＋遅延 materialize で“可視ノードを絞る”設計を持つ（typePassesMaterialization, :1129）が、初期 cytoscape() には全 els を渡すため初期構築は全件依存のまま — 万型級で初期描画が固まるという指摘は構造的に正しい。サーバ側で namespace/assembly メタグラフのみ初期配信し型をドリルダウン時に遅延フェッチする案は、既存 materialize 設計と整合し実装余地がある。CDN 非依存の同梱 vendor（src/Unilyze/Templates/vendor/ に cytoscape.min.js/dagre.min.js/cytoscape-dagre.js を確認、ELK は CDN+dagre フォールバック main.js:1198-1201,1261-1262）というゼロセットアップ前提も維持可能。impact=medium／effort=M も妥当。ただし初期 els 構築（main.js:892-900）は型ノードを直接渡すのではなく namespace ノード主体である点は実装時に要精査（型ノードは materialize で後付け）。

#### 常駐時のメモリ上限・並列度ガードで rc=134(SIGABRT) 再燃を防止

- verdict: needs-revision
- effort: S / impact: medium
- what: serve 常駐モードでは maxParallelism の既定を Environment.ProcessorCount（UnilyzeConfig.cs:43-44）から下げる（例 max(2, cores/2)）か設定必須化し、再解析ごとに前回 Compilation/SemanticModel キャッシュ（SemanticEnricher.cs:53）を明示破棄して世代蓄積を防ぐ。OOM 検知時は並列度を自動半減してリトライするバックオフを入れる。
- why: CHANGELOG 記載どおり rc=134(SIGABRT) は Complete 解析中の OOM 仮説で、maxParallelism 上限化のみで緩和した既知の不安定点。常駐＋頻回再解析はメモリ蓄積と多重並列でこのリスクを増幅する。常駐特有のガードが必要。
- evidence: src/Unilyze/Config/UnilyzeConfig.cs:43-44, src/Unilyze/Pipeline/SemanticEnricher.cs:53-78, src/Unilyze/Pipeline/SemanticEnricher.cs:98-101
- 検証ノート: 課題は実在し方針も妥当だが、キャッシュ破棄の前提に事実誤認がある。maxParallelism 既定が Environment.ProcessorCount は UnilyzeConfig.cs:43-44（ResolveMaxParallelism: configValue>0 ? value : Environment.ProcessorCount）で確認。Parallel.ForEach（SemanticEnricher.cs:75 PrewarmModelCache）と Parallel.For（:98 Enrich の全型 enrich）も確認、rc=134 #62 は CHANGELOG.md:43 に明記。並列度を max(2,cores/2) へ下げる／OOM バックオフは妥当。ただし『再解析ごとに前回 Compilation/SemanticModel キャッシュ（SemanticEnricher.cs:53）を明示破棄』は前提が崩れている: :53 の ModelCache は EnrichmentContext.Create 内で Enrich() ごとに new されるローカル変数で、Enrich 戻り後は自然に GC 対象になる（静的常駐していない）。Compilation も毎回 CSharpCompilation.Create（CompilationFactory.cs:61）で新規生成され破棄される。つまり“現状は”世代蓄積しない。蓄積リスクは『提案2/提案1で常駐キャッシュ化した後に初めて発生する』ため、本提案は単独では前提が成立せず、提案2の常駐化に依存する条件付きガードとして位置づける必要がある。
- 修正案: (a) 『前回キャッシュを明示破棄』は『提案1/2 で Compilation・ModelCache を常駐化した場合に、世代蓄積を防ぐため再解析前に旧世代を明示破棄する』という条件付きに書き換える（現状ローカルスコープで GC される事実を明記）。(b) 本提案を提案1/2 の前提に依存する“ガード”として依存関係を明示。(c) maxParallelism 下げ＋OOM バックオフ＋並列度設定必須化は前提に関係なく valid なので維持。effort=S は妥当。

Open questions:

- 再解析レイテンシ目標を SLA として何に置くか。実測 Complete=4.13s(365型) を基準に、中規模(数千型)で p95<3s を狙うなら semantic インクリメンタル必須だが、Roslyn の Compilation.ReplaceSyntaxTree でも cross-file の SemanticModel 無効化範囲をどこまで限定できるか（変更型の被参照型まで再 enrich が必要か）が未検証。
- ライブ経路で viewer が要求する Complete レベルデータと、incremental が効く syntax レベルの分断（実測 codeHealth=10/cbo=0）をどう埋めるか。常駐 Compilation 差分再利用に倒すのか、それとも『初回 Complete → 以降は変更型のみ semantic 部分更新』のハイブリッドにするのか。
- 差分配信を JSON-Patch にするか型単位フル差し替えにするか。既存 DiffResult/deltaScore（before/after 2スナップショット前提）をライブのストリーミング差分にどこまで流用できるか、サーバが前回スナップショットをメモリ保持する前提でよいか。
- ビューアのレイアウト戦略。ELK は CDN フォールバック依存（main.js:1255-1262）でオフライン時 dagre に落ちる。ライブ更新でインクリメンタル/制約付きレイアウトを使う場合、同梱 dagre のみで安定動作させられるか、ELK をローカル同梱すべきか（ゼロセットアップ思想との兼ね合い）。
- 常駐サーバの配信方式。SSE で十分か WebSocket が要るか、localhost バインドとトークンでセキュリティ（docs/threat-model.md, HtmlFormatter の生埋め込み既知点 HtmlFormatter.cs:27-28）をどう担保するか。ブラウザからのソースジャンプ（エディタ起動）はローカルサーバ経由のため CSRF/任意ファイル読み出しの面が新たに増える。
- MCP との関係。既存 stdio MCP サーバ（McpStdioServer）が解析を提供するが、ライブ serve は別プロセスか。常駐解析エンジンを両者で共有する設計にするか、二重メンテを避けるためどちらかに寄せるか。

### エージェント連携との整合

現状: unilyze のエージェント連携は現状3経路あり、いずれも「都度起動・ステートレス」なバッチ前提で、ライブ状態を共有する仕組みは存在しない。

1) MCPサーバー(stdio): `unilyze mcp` → McpRunner.Run (src/Unilyze/Runners/McpRunner.cs:18) → McpStdioServer.Run (src/Unilyze/Mcp/McpStdioServer.cs:8) が stdin を行単位で読みJSON-RPCを処理。公開ツールは10個 (McpToolSchemas.cs:7-11: analyze/get_summary/worst_types/query_type/diff/hotspot/baseline_status/triage_add/schema/version)。HandleToolCall→McpToolHandlers.Call (McpToolHandlers.cs:19) でディスパッチ。出力は MCP の text content (McpJsonRpc.BuildToolCallResult, McpJsonRpc.cs:54) で、analyze系は Markdown サマリ (McpAnalyzeSummary.ToMarkdown, McpToolHandlers.cs:52)、query系は md/json 選択可 (FormatQueryResult, McpToolHandlers.cs:204)。max_chars でトリム (McpResponseTrimmer.cs:6, 既定16000字)。

2) MCPの解析キャッシュは「プロセス内メモリ1件のみ」。McpAnalysisCache (src/Unilyze/Mcp/McpAnalysisCache.cs:5-29) は `_cached`/`_cacheKey` を1組だけ持ち、キーは入力パスのみ (BuildKey, line 27: `args.Input ?? Path.GetFullPath(path)|api=...`)。ファイル変更検知は一切なく、同一path/inputなら再解析しない。stdioサーバーが生きている間だけ有効で、別プロセス(CLI)とは共有されない。

3) CLI経由(スキルが実際に使う経路): バンドル済みスキル refactor-loop (src/Unilyze/Skills/refactor-loop/SKILL.md) と quality-audit (src/Unilyze/Skills/quality-audit/SKILL.md) は MCP を一切参照せず、すべて `unilyze -p ... -f json -o snapshot.json` / `unilyze query --worst` / `unilyze diff before.json after.json` のサブプロセス呼び出しで駆動 (grep結果: 両SKILL.md内に "mcp" の出現ゼロ)。スナップショットは `.unilyze/` にJSONとして手動保存し、before/afterを毎回ファイル比較する。

4) HTTP/常駐サーバー/FileSystemWatcher/WebSocketは存在しない。serve/watch コマンドもない (Program.cs:18-45 のルーティングに無し)。"serve"/"watch" のgrepヒットは検出器コメントやヘルプ文のみで実体なし。

5) ビューアへのデータ供給は「ビルド時の文字列置換」のみ。HtmlFormatter.Render (src/Unilyze/Output/HtmlFormatter.cs:13-25) が `__DATA_PLACEHOLDER__`/`__DIFF_DATA_PLACEHOLDER__` を解析JSON/diffJSONで置換し、viewer側は `const DATA = __DATA_PLACEHOLDER__;` (main.js:1)、`const DIFF = __DIFF_DATA_PLACEHOLDER__;` (main.js:57) として静的に埋め込み読みする。fetch/EventSource/WebSocketによるランタイム取得フックは皆無 (main.js内の window 使用はマウスイベントのみ, main.js:974/979)。差分表示は既に GenerateWithDiff (HtmlFormatter.cs:10) でdeltaを埋め込み可能だが、これも生成時1回限り。

6) 増分解析の土台は既にある。SyntaxCacheStore (src/Unilyze/Incremental/SyntaxCacheStore.cs:9-17) が `.unilyze/cache/syntax/v1/` にper-fileコンテンツハッシュmanifestを永続化 (HashFileContent=SHA256, SyntaxCacheFingerprint.cs:44-49)。グローバルfingerprint (SyntaxCacheFingerprint.cs:15) は config/threshold/対象asmdefから算出。ただし SyntaxOnly経路のみでsemanticは無効、かつ Program.cs:74 で `--incremental` は `-i/--input` と排他、ライブ更新ループから呼ぶAPIは未整備。

7) triage はファイル永続のみ。triage_add (McpToolHandlers.cs:153-179) が `.unilyze/triage.json` にUpsert、analyze時に TriageApplication.TryApply で適用 (QueryRunner.cs:124)。エージェントが書いた verdict をライブ画面へ即反映する経路はない。

要するに「ライブ状態を保持する単一の常駐プロセス」と「その状態をHTTP/JSONで外部公開する口」が欠けており、MCP(stdio)・CLI(ファイル)・viewer(静的埋め込み)が三者三様に分断されている。

#### serve常駐プロセスの解析状態を /api/analysis.json でHTTP公開し、MCPの責務分担を明確化

- verdict: needs-revision
- effort: L / impact: high
- what: 新規 `unilyze serve -p <path>` を追加し、ローカルHTTPサーバー(System.Net.HttpListener)を立てる。viewer用HTML/vendor資産に加え、現在の解析結果を `GET /api/analysis.json`、diffを `GET /api/diff.json` で配信する読み取り専用エンドポイントを持つ。MCP(stdio)は従来どおりエージェント↔モデルのツール呼び出し専用に残し、serveはブラウザ↔ライブ状態の配信専用とする。両者は同じ AnalysisResult シリアライズ (AnalysisJsonContext) を共有し、serve側はそのプロセスが保持する最新 AnalysisResult を返すだけにする。
- why: ユーザー要件の即時反映の前提は『常駐プロセスが最新状態を保持し、画面がそれを参照する』こと。現状 McpAnalysisCache はstdioプロセス内1件メモリ(McpAnalysisCache.cs:5-29)で外部公開口がなく、viewerはビルド時埋め込み(HtmlFormatter.cs:13-25)。HTTP配信口を1つ足せば、viewerのデータ供給をランタイムfetch化する受け皿になり、MCP=エージェント用/serve=画面用という責務分担が成立する。
- evidence: src/Unilyze/Mcp/McpAnalysisCache.cs:5-29, src/Unilyze/Output/HtmlFormatter.cs:13-25, src/Unilyze/Program.cs:18-45
- 検証ノート: evidence は概ね正確。McpAnalysisCache.cs:5-29 はプロセス内1件メモリ(_cached/_cacheKey)でファイル変更検知なし(BuildKey 27-28 はパス/inputのみ)、外部公開口なしを確認。HtmlFormatter.cs:13-25 のビルド時文字列置換も確認。McpStdioServer.cs:8-40 は純粋なstdin/stdout JSON-RPCでHTTP/HttpListenerは皆無、McpToolHandlers は1プロセス1インスタンス(line 10)。MCP=エージェント用/serve=画面用の責務分担は実態と整合する。ただし重大な見落とし: 提案は `unilyze serve -p <path>` と書くが、Program.cs:11-16 は args[0] が '-' で始まらない場合 CliArgValidation.ValidateTopLevelCommand を通す。'serve' は CliArgValidation.cs:5-11 の TopLevelCommands に無いため、現状では即 'Unknown subcommand: serve' でエラー終了する(CliArgValidation.cs:233-239)。さらに Program.cs:18-45 の if 連鎖に serve 分岐を足す必要がある。evidence が指す Program.cs:18-45 はルーティングのみで、登録リスト(CliArgValidation.cs:5-11)への追加が抜けている。
- 修正案: serve を (1)CliArgValidation.cs:5-11 の TopLevelCommands に追加し、(2)専用の SBooleanOptions/SValueOptions 検証を用意、(3)Program.cs:18-45 の if 連鎖に `if (args[0]=="serve") return ServeRunner.Run(args[1..]);` を追加、の3点をセットで明記する。HttpListener はGUI/管理者権限不要のループバック(http://127.0.0.1:port/)前提とし、bindなどOS差異(macOSでの権限)も検討材料に含める。読み取り専用エンドポイントという方針自体は妥当。

#### viewerのデータ取得を埋め込みからランタイムfetchへ切替え可能にする（静的HTML互換を維持）

- verdict: needs-revision
- effort: M / impact: high
- what: main.js:1 の `const DATA = __DATA_PLACEHOLDER__;` を『プレースホルダが置換済みならそれを使い、未置換(serveモード)なら `fetch('/api/analysis.json')` で取得』する分岐に変える。具体的には HtmlFormatter にserve用の別レンダリング(プレースホルダを `null` のまま残す or sentinel値)を用意し、viewer側は `DATA ?? await fetch(...)` のような初期化に統一。diff(main.js:57)も同様。SSE(`/events`)かポーリングで再fetch→グラフ再描画フックを足す。
- why: 即時反映の実現には、viewerが状態を外部から取得する口が必須。現状はfetch/EventSourceフックが皆無(main.js全体でwindowはマウス用途のみ, main.js:974/979)。埋め込み経路を壊さず分岐させれば、従来の単発HTML生成(オフライン配布)とライブserveの両方を1つのviewerコードで賄え、Cytoscape同梱・ゼロセットアップ思想を維持できる。
- evidence: src/Unilyze/Templates/viewer/main.js:1, src/Unilyze/Templates/viewer/main.js:57, src/Unilyze/Output/HtmlFormatter.cs:20-24
- 検証ノート: main.js:1 `const DATA = __DATA_PLACEHOLDER__;`、main.js:57 `const DIFF = __DIFF_DATA_PLACEHOLDER__;`、HtmlFormatter.cs:20-24 の置換は全て確認。fetch/EventSource/WebSocketフックが皆無なのも実態通り。ただし提案の `DATA ?? await fetch(...)` という分岐表現には欠陥がある: __DATA_PLACEHOLDER__ は生のJS式として埋め込まれる(HtmlFormatter.cs:22)ため、serveモードで未置換のまま残すと `const DATA = __DATA_PLACEHOLDER__;` は JS構文エラーになり ?? 評価に到達しない。提案文中の『sentinel値 or null』はこれを示唆しているが、`DATA ?? await fetch` という擬似コードと矛盾しており曖昧。また DATA はモジュール冒頭の同期 const で、直後に DATA.assemblies(44行)/DATA.types(50行)を即時参照しているため、await を挟む初期化への書き換えはトップレベル制御フローの再構成が必要で『分岐を足すだけ』では済まない(影響範囲が main.js 全体の初期化順序に及ぶ)。
- 修正案: HtmlFormatter に serve用レンダリングを追加し、__DATA_PLACEHOLDER__ を必ず有効なJSリテラル(例: `null`)で置換する(構文エラー回避)。viewer側は初期化全体を async ブートストラップ関数で包み、`let DATA = INJECTED; if (DATA === null) DATA = await (await fetch('/api/analysis.json')).json();` の後に既存の派生(asm/tl/tm 構築, 44-54行)を実行する形に再構成する。SSE/ポーリング再描画フックは cy 再生成(main.js の cy 初期化)まで含めて設計する。静的HTML互換は維持可能だが、初期化順序の再構成コストを effort:M に織り込むこと。

#### increment解析をserveループから呼べる内部APIに昇格し、ファイル変更時の再解析を高速化

- verdict: needs-revision
- effort: L / impact: medium
- what: `--incremental`(現状 Program.cs:73, SyntaxOnly + `-i`排他)に依存せず、serveの再解析パスから SyntaxCacheStore/SyntaxCacheFingerprint を直接使う内部メソッドを切り出す。FileSystemWatcher で `.cs` 変更を検知→変更ファイルのみ HashFileContent(SyntaxCacheFingerprint.cs:44) で差分判定→manifest(SyntaxCacheStore.cs:16) を更新→AnalysisResult を部分再構築。デバウンス付きで最新状態をserveのメモリ状態へ反映する。
- why: lazygit的な即時反映には、保存のたびに全体再解析(C#約200本)を走らせると遅い。既に per-fileコンテンツハッシュmanifest(`.unilyze/cache/syntax/v1/`)という土台がある(SyntaxCacheStore.cs:9-17)のに、CLIフラグ越しにしか使えず排他制約もある(Program.cs:74)。serve常駐ループから直接叩けるAPIにすれば、既存資産を壊さず差分再解析でライブ更新の応答性を確保できる。
- evidence: src/Unilyze/Incremental/SyntaxCacheStore.cs:9-17, src/Unilyze/Incremental/SyntaxCacheFingerprint.cs:44-49, src/Unilyze/Program.cs:73-78
- 検証ノート: SyntaxCacheStore.cs:9-17 のmanifest永続化、SyntaxCacheFingerprint.cs:44-49 の HashFileContent(SHA256 per-file)、Program.cs:73-78 の `--incremental` と `-i` 排他は全て確認。しかし前提に誤りがある。(1)『--incremental は syntax level のみで semantic 無効』という現状認識は不正確。AnalysisPipeline.cs:42-47 が真の制約で、incremental は RequestedLevel==AnalysisLevel.Syntax のときだけ有効、それ以外は警告を出して incremental を false に降格し通常解析する。Program.cs:73 の排他は -i との排他にすぎず、レベル制約はここではない。(2)semantic が無効なのではなく、SyntaxIncrementalSemanticPhase.cs:23-45 が示すとおり incremental 経路でも BaseTypeResolver/DependencyBuilder/CouplingMetricsCalculator は走り、変更型のみ選択的に再エンリッチ(DetermineTypesToReEnrich, 35行)する。(3)重大な見落とし: 通常の `unilyze analyze`(Program.cs:140-150)は requestedLevel=null で自動解決され syntax より高いレベルになり得る。incremental は syntax level に固定されるため、serveが incremental で生成する AnalysisResult は今日ユーザーが見ている静的HTML(自動解決レベル)より metrics が少ない/異なる可能性がある。ライブ画面と既存HTMLで内容が食い違うリスクを提案が見落としている。
- 修正案: 『increment=syntax固定』である事実を明記し、serveのライブ更新が既存の自動解決レベル相当の解析結果と一致するかをまず検証する。選択肢として(a)serveは syntax-level incremental の軽量結果を割り切って配信(高速だが metrics 縮小)、(b)変更検知は incremental fingerprint を流用しつつ再解析自体はフルレベルで走らせデバウンスで応答性を確保、の2案を検討材料として提示する。『semantic 無効』という記述は『incremental は syntax level に固定され、変更型のみ選択的再エンリッチ』へ訂正する。

#### serveが保持するライブ状態をMCPツールからも参照できるよう get_summary/diff にlive取得経路を追加

- verdict: needs-revision
- effort: M / impact: medium
- what: serve常駐中に書き出す最新スナップショット(例: `.unilyze/live.json` をserveがアトミック更新)を、MCPツール analyze/get_summary/diff のデフォルト入力候補にする。McpToolArgs に `live` フラグ(or 既存inputが無い場合に `.unilyze/live.json` を自動探索)を足し、McpAnalysisCache.Load(McpAnalysisCache.cs:10) が live.json を優先ロードする経路を追加。これにより、エージェント(refactor-loop/quality-audit)が『serve画面が今映している状態』とズレない解析を参照できる。
- why: 現状はエージェントが `unilyze -p ... -f json -o snapshot.json` で独自にスナップショットを作り(refactor-loop/quality-audit SKILL.md、両者ともMCP不使用)、serve画面とは別の解析を見るため状態が二重化する。serveの単一ライブ状態をMCPの入力に橋渡しすれば、画面・エージェント・スキルが同じ真実を共有でき、人間とエージェントの協調作業が噛み合う。
- evidence: src/Unilyze/Mcp/McpAnalysisCache.cs:10-19, src/Unilyze/Mcp/McpToolArgs.cs:38-40, src/Unilyze/Skills/refactor-loop/SKILL.md:71
- 検証ノート: McpAnalysisCache.cs:10-19 の Load、McpToolArgs.cs:38-40 の PathOrDefault/Input、refactor-loop/SKILL.md:71 の `unilyze -p <path> $UNILYZE_FILTER -f json -o .../refactor-before.json`(CLI snapshot、MCP不使用)を確認。quality-audit/SKILL.md にも MCP 参照ゼロ(grep空)を確認、状態二重化の問題提起は正しい。技術的にも McpAnalysisCache.Load(10) に live.json 優先ロード分岐を足すこと自体は可能。ただし懸念: (1)serve と MCP(stdio)は別プロセスで、live.json はファイル経由の疎結合になる。serve が aborted/stale のとき MCP が古い live.json を真実として返す危険があり、提案が『単一の真実を共有』と言う割に鮮度保証(timestamp/lock/fingerprint照合)の設計が無い。(2)前述のとおり serve が incremental(syntax level)で書く live.json は、MCP の get_summary が期待する自動解決レベルの AnalysisResult と metrics 粒度が異なり得る(proposal 3 と同根の不整合)。(3)live自動探索を入れると、既存の『path/input が無ければ . を解析』(McpToolArgs.cs:38)という決定的挙動が暗黙に変わり、後方互換に影響する。
- 修正案: live.json は明示 `live:true` フラグ時のみ参照する opt-in に限定し(暗黙の自動探索は後方互換を壊すので避ける)、live.json にserve書き込み時刻と fingerprint を含め、MCP側で stale(例: N秒以上前 or fingerprint不一致)なら従来の解析にフォールバックする鮮度ガードを設計に含める。serve生成データのレベル(syntax固定か否か)を live.json に記録し、MCPが粒度差を判別できるようにする。

#### triage_addの書き込みをserveへ通知し、画面のdeltaScore/オーバーレイへ即時反映

- verdict: valid
- effort: M / impact: medium
- what: triage_add(McpToolHandlers.cs:153-179)が `.unilyze/triage.json` を更新したら、serveがそのファイル(またはserveの内部状態)を監視して再適用し、`/api/analysis.json` の suppressed/triage 反映済み版を即配信。viewer は再fetch でアラート件数や色付けを更新する。serveが居なければ従来どおりファイルのみ更新(後方互換)。
- why: エージェントが verdict を付けた瞬間に画面のノイズ(false-positive)が消えれば、人間レビュアとエージェントのループが締まる。現状 triage はファイル永続後、次回analyze時に TriageApplication.TryApply(QueryRunner.cs:124)でしか反映されず、ライブ画面への即時反映経路がない。serveの状態監視に1本載せるだけで実現でき、triage_addのI/F自体は変えずに済む。
- evidence: src/Unilyze/Mcp/McpToolHandlers.cs:153-179, src/Unilyze/Runners/QueryRunner.cs:120-127
- 検証ノート: McpToolHandlers.cs:153-179 の triage_add が .unilyze/triage.json を Upsert→Save する(168-172)こと、verdict 検証(159-161)、TriageFile.DefaultPath 解決(164)を確認。QueryRunner.cs:120-127 で Directory.Exists 時に TriageApplication.TryApply で triage を適用すること、つまり triage は永続後 次回 analyze/query 時にのみ反映され、ライブ画面への即時反映経路が無いことを確認。Program.cs:157-158 でも analyze 経路で TriageApplication.TryApply が走る。提案の『triage_add の I/F を変えずに serve 側の状態監視で再適用』という方針は、triage_add が既にファイル永続のみで副作用が閉じている(McpToolHandlers.cs:172 で Save して JSON返すだけ)ため、serve に FileSystemWatcher で triage.json を監視させる追加は既存挙動を壊さず実装でき、後方互換(serve不在ならファイルのみ更新)も成立する。evidence と方針が実コードと整合する。

#### serve配信JSONのスキーマ整合をschema/versionツールと一致させ、エージェント・viewer両方の契約を一元化

- verdict: needs-revision
- effort: S / impact: medium
- what: serveが返す analysis.json/diff.json を、既存 `unilyze schema`(McpToolHandlers.HandleSchema, McpToolHandlers.cs:181 = EmbeddedCliText.Schema)と同じフィールド定義に必ず一致させる。serve起動時に metricsVersion/toolVersion を `/api/version` で公開(version ツールと同形, McpToolHandlers.cs:183-188)し、viewer・エージェントが互換性を検証できるようにする。配信JSONは AnalysisJsonContext.Default を必ず通し、HtmlFormatter の `</script` エスケープ(HtmlFormatter.cs:28)に相当するコンテキスト別サニタイズをHTTPレスポンスでも担保。
- why: ライブ化でデータ供給経路が増えるほど『どのフィールドが正か』の契約ズレリスクが上がる。既に schema/version というエージェント向け契約(McpToolSchemas.cs:38-39)があるのに、serveが別シリアライズを返すと viewer・MCP・スキルが食い違う。配信を既存AnalysisJsonContext+schema定義に揃えれば、ゼロセットアップ思想を保ちつつ契約を一本化できる。サニタイズはHTML埋め込みと配信でコンテキストが違う点に注意(脅威モデル既知点)。
- evidence: src/Unilyze/Mcp/McpToolHandlers.cs:181-188, src/Unilyze/Mcp/McpToolSchemas.cs:38-39, src/Unilyze/Output/HtmlFormatter.cs:27-28
- 検証ノート: McpToolHandlers.cs:181 `HandleSchema => EmbeddedCliText.Schema`、183-188 の version(toolVersion/metricsVersion)、McpToolSchemas.cs:38-39 の schema/version 記述、HtmlFormatter.cs:27-28 の EscapeInlineScriptPayload(`</script`→`<\/script`)を全て確認。AnalysisJsonContext で配信を統一する方針自体は妥当(AnalysisResult.cs:57-71 が単一の source-gen context)。ただしサニタイズの記述に技術的誤りがある: HtmlFormatter.cs:27-28 の `</script` エスケープは『JSONをHTMLの<script>内にインライン埋め込みする』固有の対策であって、serve が `Content-Type: application/json` で配信する HTTP レスポンスには無関係(HTMLパース文脈が無いので script終端の概念がない)。さらに AnalysisResult.cs:46-56 の JsonSourceGenerationOptions は Encoder を未指定=既定 JavaScriptEncoder のため、`<` は既に `<` にエスケープされ、HtmlFormatter の置換は二重防御にすぎない。よって『HTMLエスケープに相当するコンテキスト別サニタイズをHTTPでも担保』という表現は対策の方向を取り違えている(提案末尾で文脈差に言及はあるが、本文の指示は誤誘導)。
- 修正案: HTTP配信の正しい対策に差し替える: (1)`Content-Type: application/json; charset=utf-8` を明示、(2)`X-Content-Type-Options: nosniff` でMIMEスニッフィング防止、(3)ループバック限定bindとCORS既定拒否(またはOrigin検証)で外部fetchを防ぐ。`</script` 置換はHTML埋め込み専用と明記し、HTTP-JSON経路には適用しない(不要かつ誤解の元)。schema/version との契約一元化(AnalysisJsonContext.Default 統一・/api/version で toolVersion/metricsVersion 公開)というコア提案は維持してよい。effort は S のままで妥当。

Open questions:

- serveの常駐プロセスとMCP(stdio)プロセスは別物として並走させるか、それとも `unilyze mcp` が内部でHTTP配信も兼ねる単一プロセスにするか。前者は責務分離が明確だが状態を共有する仕組み(共有ファイル/IPC)が要り、後者はstdioとHTTPの寿命管理が複雑になる。
- ライブ状態の正本(source of truth)をどこに置くか。serveのメモリ状態か、`.unilyze/live.json` のようなファイルか。ファイルにすると複数プロセス/エージェントから参照しやすいがアトミック更新と競合制御が必要、メモリだとMCPや別CLIから参照できない。
- viewerへの即時反映方式はSSE/WebSocket/ポーリングのどれか。vendor同梱・CDN非依存・オフライン動作という既存制約(elkjsはCDNでdagreフォールバック)を踏まえると、追加依存ゼロのSSEかポーリングが無難だが、SSEはHttpListenerでの実装コストがある。
- 増分再解析のスコープ。SyntaxOnlyキャッシュ(`.unilyze/cache/syntax/v1/`)はsemanticを含まず、CodeHealthやcoupling(AfferentCoupling等)はsemantic依存。ライブ更新でどこまでの精度を即時に出すか(syntaxだけ即時更新しsemanticは遅延バッチ、等)の段階設計が必要。
- serveのHTTPバインドはlocalhost固定か、ポート選択/認証はどうするか。脅威モデル(docs/threat-model.md)はHTML埋め込みのエスケープのみ想定で、ローカルHTTPサーバー(解析対象ソースの内容やパスを配信)という新しい攻撃面の評価がまだない。
- ブラウザからの『ソースコードへ飛ぶ』を、serveがソース内容を `/api/source?path=...` で配信する方式にするか、エディタ起動(vscode://等のdeep link)にするか。前者はパストラバーサル対策(プロジェクトルート外参照の遮断)が必須、後者はサーバー側実装が軽い。
- フロー図(呼び出し/制御/依存)の生成元データをどこに持たせるか。現状の解析JSONは型単位メトリクスと依存関係が中心で、メソッド単位のコールグラフ/制御フローを serve配信JSON/MCPツールのどちらかに追加するか、その粒度をライブ更新コストとどう両立させるか。

### 配布とパッケージング

現状: 配布は3経路が既に整備されている。(1) NuGet tool: `PackAsTool=true`/`ToolCommandName=unilyze`、`net8.0;net10.0`のframework-dependent (`src/Unilyze/Unilyze.csproj:7-8,4`)。(2) self-contained単一バイナリ: publish.ymlで4 RID (osx-arm64/x64, linux-x64, win-x64) を `dotnet publish -r <RID> --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true` でビルドし tar.gz/zip 化 (`.github/workflows/publish.yml:115-150`)。(3) Homebrew formula (`packaging/unilyze.rb`) と scoop manifest (`packaging/unilyze.json`) をリリース時にSHA256埋め込みで自動生成・配布、tapへ自動push (`publish.yml:212-287`)。README記載のインストールは3経路 (`README.md:31,38,41`)。

埋め込みリソース配信は確立済み。ビューアは `combine.py` でindex.html+styles.css+main.js (98KB) を1ファイルへ結合 (`src/Unilyze/Templates/viewer/combine.py`)、ビルド時に `CombineViewerTemplate` ターゲット (csproj:51-56) で生成し `EmbeddedResource` 化 (csproj:31)。vendor JS (cytoscape.min.js 365KB, dagre.min.js 277KB, cytoscape-dagre.js 12KB) も埋め込み (csproj:34-38)。HtmlFormatterはこれらを `GetManifestResourceStream` で読み出し1枚のHTMLへインライン埋め込み (`Output/HtmlTemplate.cs:38-44`, `Output/HtmlFormatter.cs:20-24`)。単一バイナリ安全なリソースアクセス (`typeof(X).Assembly`) を一貫採用。

クロスプラットフォームのブラウザ起動は実装済みだがファイルURL前提。`ProgramHelpers.TryOpenInBrowser` がmacOS `open`/Windows `UseShellExecute`/Linux `xdg-open` で `file://` を開く (`Cli/ProgramHelpers.cs:234-250`)。HTML/JSONをtempか `-o` 先に書き、`--no-open` なしならブラウザを開く (`Program.cs:165-180`)。

サーバー機能は皆無。Program.csのコマンドルーティングにserve/watchは無し (`Program.cs:18-50`)。HttpListener/Kestrel/WebApplication/TcpListener/WebSocket/FileSystemWatcher のいずれもsrc配下に存在しない (grep全滅)。MCPはstdioのみ (`Mcp/McpStdioServer.cs:8-16` でstdin ReadLineループ)。

単一バイナリの既知の重要制約: 意味解析はランタイム参照DLLの実体パスに依存する。`DotnetRuntimeReferenceResolver.CollectFrameworkAssemblyPaths` はまず `TRUSTED_PLATFORM_ASSEMBLIES` の各DLLが `File.Exists` する前提で集める (`Discovery/DotnetRuntimeReferenceResolver.cs:36-42`)。self-extract無しの単一バイナリではDLLが実体化されず、フォールバックの `typeof(object).Assembly.Location` も空になり意味解析が degrade する旨を警告する (同:48-57)。`publish.yml:126-127` が `IncludeAllContentForSelfExtract=true` を(重複指定で)付けているのはこのため。トリミング/AOT/InvariantGlobalizationは未設定 (csproj/publish.ymlにフラグ無し) で、Roslyn (Microsoft.CodeAnalysis.CSharp 4.12.0, csproj:42) との互換のため非トリム。結果として単一バイナリは大きく、起動時に自己展開コスト+JITがかかる。

CDN非依存は不完全。デフォルトレイアウトエンジンELKは実行時に `https://unpkg.com/elkjs@0.9.3/lib/elk-worker.min.js` をWeb Workerで取得 (`Templates/viewer/main.js:1200`, `_layoutEngine='elk'` がデフォルト main.js:1176)。失敗時のみ同梱dagreへフォールバック (main.js:1257-1267)。つまりオフライン時はデフォルトレイアウトがCDN取得に失敗してからdagreに落ちる。

#### serveサブコマンドを単一バイナリに同梱し HttpListener でローカル配信

- verdict: valid
- effort: M / impact: high
- what: Program.csのルーティングに `serve` を追加し (`Program.cs:18-45` の if 連鎖と同形)、System.Net.HttpListener (BCL内蔵、追加依存ゼロ) で 127.0.0.1:<port> にバインド。既存の HtmlFormatter.Generate が返すHTMLをそのまま `GET /` で返し、解析JSONを `GET /api/analysis.json` で返す。ブラウザ起動は TryOpenInBrowser を `http://127.0.0.1:<port>` へ向けるよう URL引数化 (現状 file:// 固定 `ProgramHelpers.cs:238`)。Kestrel/AspNetCoreは導入しない (トリム非対応・バイナリ肥大化を避ける)。
- why: ライブ更新・ソースジャンプ・diff の全ゴールがブラウザ↔プロセス間の双方向通信を要求するが、現状は file:// 静的HTMLで通信路が無い (`Program.cs:165-180`)。HttpListenerはBCL同梱で self-contained 単一バイナリ・NuGet tool・Homebrew/scoop の全配布経路でゼロ追加依存のまま動き、既存のオフライン/軽量思想を壊さない。
- evidence: src/Unilyze/Program.cs:18-45, src/Unilyze/Cli/ProgramHelpers.cs:234-250, src/Unilyze/Output/HtmlFormatter.cs:7-11
- 検証ノート: 証拠は概ね正確。Program.cs:18-45 は serve/watch を含まない if 連鎖(diff/hotspot/.../mcp まで、serve/watch 不在を確認)。ProgramHelpers.TryOpenInBrowser は ProgramHelpers.cs:238 で `var url = "file://" + Path.GetFullPath(path);` と file:// 固定(macOS open / Windows UseShellExecute / Linux xdg-open、234-250)。HtmlFormatter.Generate は HtmlFormatter.cs:7-8 で HTML文字列を返すので GET / でそのまま返せる。HttpListener/TcpListener/Kestrel/WebApplication は src 全域に不在(grep 全滅)で BCL の HttpListener はゼロ追加依存。net8.0/net10.0 両TFM(csproj:4)とも HttpListener 利用可。配布3経路(NuGet tool/単一バイナリ/brew・scoop)を壊さない方針も妥当。1点補足: 解析JSONを別途 /api で返す設計は良いが、現状 Program.cs:165-180 は HTML と .json を同一ベース名で両方書き出している(170,174行)ので、serve では in-memory 保持に切り替える方が一貫する(ファイル書き出し副作用を避ける)。

#### ELKのCDN依存を撤廃しオフライン完全自己完結にする

- verdict: needs-revision
- effort: S / impact: medium
- what: elk-worker.min.js (≈0.9.3, MIT) をvendorに追加して csproj:34-38 と同形で EmbeddedResource 化し、HtmlTemplate.BuildVendorScripts (`Output/HtmlTemplate.cs:13-26`) でインライン同梱。main.js:1200 の `importScripts('https://unpkg.com/...')` を Blob URL 経由の埋め込みworkerソースへ差し替え (cytoscape同様 `</script` エスケープ処理を流用)。
- why: vendorを埋め込んで『CDN非依存・オフライン動作』を謳う設計 (csproj:33-38) に反し、デフォルトのELKレイアウトが実行時にunpkgを取得する (main.js:1200,1176)。serve化でローカルサーバーから配信する将来も、オフライン環境やネット遮断CIでデフォルトレイアウトがCDNタイムアウト後にdagreへ劣化フォールバックする (main.js:1257) のは配布品質として不適。フロー図ゴール (ELKの階層レイアウトが主役) の信頼性に直結。
- evidence: src/Unilyze/Templates/viewer/main.js:1176,1200,1257, src/Unilyze/Unilyze.csproj:33-38, src/Unilyze/Output/HtmlTemplate.cs:13-26
- 検証ノート: ELKのCDN依存の本体を取り違えている。提案は main.js:1200 の worker(`importScripts("https://unpkg.com/elkjs@0.9.3/lib/elk-worker.min.js")`)だけを埋め込み対象にしているが、`ELK` グローバル定数そのものは index.html:73 の `<script src="https://unpkg.com/elkjs@0.9.3/lib/elk.bundled.js"></script>` で読み込まれている(提案はこのファイル/行に一切触れていない)。layout() は main.js:1182 で `if(_layoutEngine==='elk' && typeof ELK!=='undefined')` と判定するため、bundled.js がオフラインで読めなければ ELK は undefined になり、worker を埋め込んでもデフォルトで dagre へ即落ちする(1184行 else)。つまりこの提案単独ではゴールの『オフラインELK』を達成しない。またフォールバック行の引用 main.js:1257 は不正確で、実際は worker失敗→メインスレッドELK(1259)→それも失敗で dagre(1262)の三段。csproj:34-38 の EmbeddedResource 同形化・HtmlTemplate.cs:13-26 のインライン化手法自体は妥当。
- 修正案: elk.bundled.js(メインライブラリ、≈ELKグローバルを定義)を最優先で vendor 追加し index.html:73 の CDN script を埋め込み <script> へ置換する。そのうえで elk-worker.min.js も埋め込み、main.js:1200 の Blob workerソースを埋め込みworkerへ差し替える。bundled が ELK グローバルを供給するので worker埋め込みは性能用の二次対応。両ファイルを埋めて初めて typeof ELK!=='undefined' がオフラインで成立し、dagre 劣化を防げる。

#### serveのライブ更新はSSE (Server-Sent Events) を採用しWebSocketを避ける

- verdict: needs-revision
- effort: L / impact: high
- what: serveに `GET /events` を設け text/event-stream で再解析完了イベントをpush。ファイル変更検知は FileSystemWatcher (BCL内蔵、現状未使用) で .cs を監視し、debounce後に既存の AnalysisPipeline.Build を `incremental:true` で再実行 (`Program.cs:140-150` と同じ呼び口) し新JSONを配信。ブラウザ側はEventSourceで受けグラフを差し替え。WebSocketライブラリは導入しない。
- why: lazygit的な即時反映を最小依存で実現するため。SSEはHttpListenerの素のHTTPレスポンスで成立しBCLのみで完結 (WebSocketは ASP.NET Core か手書きハンドシェイクが要り単一バイナリ配布を重くする)。再解析は既存の `--incremental` キャッシュ (`Program.cs:73,150`) を再利用でき、毎回フル解析の起動コストを避けられる。MCPがstdio片方向 (`Mcp/McpStdioServer.cs:8-16`) なのと役割を分離できる。
- evidence: src/Unilyze/Program.cs:73,140-150, src/Unilyze/Mcp/McpStdioServer.cs:8-16, grep: FileSystemWatcher 不在
- 検証ノート: SSE+FileSystemWatcher の機構選定は妥当(SSE は HttpListener の素のレスポンスで成立、FileSystemWatcher は BCL で src 不在=grep全滅)。しかし中核の前提『再解析を incremental:true で再実行すれば既存キャッシュを再利用しフル解析コストを避けられる』が実コードと矛盾する。AnalysisBuildOptions.cs:39-40 で `UseSyntaxIncrementalCache => Incremental && RequestedLevel == AnalysisLevel.Syntax` と定義され、AnalysisPipeline.cs:42-47 は Incremental が真でも RequestedLevel が Syntax 以外なら『--incremental currently accelerates syntax-level analysis only』と警告して Incremental を false に落とし、フル解析を実行する。つまり incremental キャッシュは syntax レベル限定。syntax レベルは意味解析(依存解決・型解決)を行わないため、依存グラフ viewer が描く edges/型関係が出ない。Program.cs:73,140-150 を incremental:true で呼んでも、依存グラフ用の core/full/complete 解析ではキャッシュが効かず毎回フル解析になる。MCP が stdio片方向(McpStdioServer)で役割分離という点は正しい。
- 修正案: ライブ更新の再解析は『syntax incremental キャッシュ再利用』を前提にしない。依存グラフは意味解析必須(AnalysisLevel core 以上)なので、(a) FileSystemWatcher で変更ファイルを集約し debounce 後にフル(または希望レベル)解析を非同期実行、(b) 体感速度は別途のメモリ常駐(プロセスを落とさず Roslyn Compilation を保持)や差分再コンパイルで稼ぐ、という設計に改める。SSE/FileSystemWatcher の採用自体は維持。--incremental の現状制約(syntax限定・semantic無効、AnalysisPipeline.cs:42-47)をロードマップに明記し、semantic incremental は別タスク(キャッシュ層の新規実装)として切り出す。

#### 単一バイナリでの意味解析パリティをserve起動時に検証・明示する

- verdict: valid
- effort: S / impact: medium
- what: serve起動シーケンスで DotnetRuntimeReferenceResolver の解決結果 (resolvedLevel) をHTMLヘッダかAPIに載せ、self-extract無しで semantic が落ちている場合に画面上へ明示。あわせて publish.yml:126-127 の重複した `IncludeAllContentForSelfExtract=true` を1つに整理し、self-extract前提をビルドコメントで固定。
- why: 単一バイナリは self-extract 無しだとフレームワーク参照DLLが実体化されず意味解析が degrade する既知制約がある (`DotnetRuntimeReferenceResolver.cs:48-57`)。ライブ画面が常時表示される運用では、ユーザーが『フロー図/依存が出ない』原因を解析レベル低下だと気づけない。配布物 (brew/scoop/単一バイナリ) と NuGet tool (framework-dependent でフルDLLあり) の挙動差を画面で接地させる。
- evidence: src/Unilyze/Discovery/DotnetRuntimeReferenceResolver.cs:36-57, .github/workflows/publish.yml:119-128
- 検証ノート: 証拠は成立。DotnetRuntimeReferenceResolver.cs:48-57 は単一ファイル(self-extract無し)で typeof(object).Assembly.Location が空になり『semantic analysis may be reduced』を警告する旨を確認(36-42 で TRUSTED_PLATFORM_ASSEMBLIES の File.Exists 前提も確認)。publish.yml は 125 行と 127 行で `-p:IncludeAllContentForSelfExtract=true` を重複指定しており(115-128 の publish ブロック内)、整理対象として正しい。1点補足: 画面に載せる項目名を『resolvedLevel』としているが、実コードのリゾルバは ResolvedDlls(level,...) を返し、最終的に JSON へ出るフィールドは `analysisLevel`(publish.yml:182-187 のスモークが jq -r .analysisLevel で参照)である。HTML/APIに載せる際は既存の analysisLevel を流用し、self-extract無し由来の degrade はリゾルバの警告(54-56)を画面ログに転送する形にすると既存の出力契約と整合する。

#### NuGet toolにserve体験を統一し net8.0 ターゲットでのHttpListener可搬性を確認

- verdict: valid
- effort: M / impact: medium
- what: serveをframework-dependentなNuGet tool版でも同一コマンドで提供。`net8.0;net10.0` 両TFM (csproj:8) でHttpListener/FileSystemWatcher/SSEが同挙動か release-smoke.sh / publish.yml:164-187 のスモークに `serve --port 0 --once` 的な非対話検証 (バインド→1リクエスト→終了) を追加。Homebrew formula の test ブロック (`packaging/unilyze.rb:29-31`) もポートバインド確認に拡張。
- why: 配布経路ごと (NuGet tool / 単一バイナリ / brew / scoop) で serve 体験が割れると『軽量ゼロセットアップ』の一貫性が崩れる。現状スモークは --version/badge/json止まり (`publish.yml:170-187`) でサーバー起動を検証しない。CIで各RID・各TFMのバインド可否を早期に捕捉する。
- evidence: .github/workflows/publish.yml:164-187, packaging/unilyze.rb:29-31, src/Unilyze/Unilyze.csproj:8, scripts/release-smoke.sh
- 検証ノート: 証拠は成立。publish.yml の Linux スモークは 164-187 で --version 検証(171)、skills list(176)、badge --fail-under(177)、-f json 出力(178)、hosted との analysisLevel 一致確認(182-187)に留まり、サーバー起動は検証しない(指摘どおり)。unilyze.rb の test ブロックは 29-31 で `assert_match version.to_s, shell_output("#{bin}/unilyze --version")` のみ(ポートバインド未検証、拡張対象として正当)。両TFM(net8.0;net10.0)は csproj:4 で確認でき HttpListener/FileSystemWatcher は両方で利用可。scripts/release-smoke.sh は dotnet tool install→`--version` のみ(130-138)でサーバー検証なし。1点軽微: 提案は『csproj:8』を両TFM根拠に挙げるが、実際の TargetFrameworks は csproj:4 で、csproj:8 は ToolCommandName。スモーク追加は `serve --port 0`(任意ポート)→1リクエスト→終了の非対話形にすれば CI で各RID/各TFMのバインド可否を捕捉できるという方向性は妥当。

#### ソースジャンプはエディタ起動とブラウザ内閲覧の二段構えにし配布制約に合わせる

- verdict: valid
- effort: M / impact: high
- what: serveに `GET /source?file=<rel>&line=<n>` を追加しサニタイズ済みでソース断片を返す (ブラウザ内閲覧)。並行して `POST /open-in-editor` で TryOpenInBrowser と同じ Process.Start 分岐 (`ProgramHelpers.cs:239-244`) を流用し、$EDITOR や `code -g file:line`/`cursor` を起動。パスは解析ルート配下に厳格に限定 (path traversal 防止)。
- why: 『エディタ起動とブラウザ内閲覧の両睨み』要件を、追加依存なし (Process.Start は既存パターン) で満たす。file:// 静的HTMLでは不可能で serve が前提。docs/threat-model.md がある通り、HtmlFormatter が JSON を生埋め込みする現状 (`HtmlFormatter.cs:27-28` の最小エスケープ) を踏まえ、サーバー化で増える入力経路 (file/line) はルート配下限定とエスケープを必須にする。
- evidence: src/Unilyze/Cli/ProgramHelpers.cs:234-250, src/Unilyze/Output/HtmlFormatter.cs:27-28, docs/threat-model.md
- 検証ノート: 証拠は成立し、必要データも実在する。ProgramHelpers.cs:234-250 の Process.Start 分岐(macOS open/Windows UseShellExecute/Linux xdg-open)は editor 起動に流用可能。HtmlFormatter.cs:27-28 の EscapeInlineScriptPayload は `</script`→`<\/script` の最小エスケープで、docs/threat-model.md(存在確認、リポジトリ管理untrusted前提を明記)が示すとおり入力経路追加にはサニタイズ必須という認識は正しい。ソースジャンプの裏付けデータも存在: TypeMetrics は FilePath を持ち(FindingFingerprint.cs:22,55 で projectPath 相対化に使用)、CodeSmell は `int? Line`(CodeSmellDetector.cs:41-47)を持つ。/source?file=&line= はルート配下限定+正規化で path traversal を防げる。1点注意: CodeSmell.Line は nullable で GodClass 等は null を渡す(CodeSmellDetector.cs:94)ため、行番号が無い smell では型先頭(FilePath のみ)へジャンプするフォールバックが要る。サニタイズ必須・ルート配下限定の方針は妥当。

Open questions:

- serveをどの配布物にバンドルするか: 単一バイナリ専用にするか、NuGet tool (framework-dependent) でも同等提供するか。後者は net8.0 でのHttpListenerコールバック挙動差の検証が要る (csproj:8 の二重TFM)。
- ローカルサーバーのバインド先・認証: 127.0.0.1固定か0.0.0.0許可か、CSRF/同一オリジン保護をどこまで入れるか。docs/threat-model.md の生JSON埋め込み前提 (HtmlFormatter.cs:27-28) にHTTPサーフェスが加わるため脅威モデル再評価が必要。
- ライブ更新の再解析コスト: --incremental は --level syntax 経路のみ有効 (Program.cs:73 周辺の制約) で semantic 再解析はフル。フロー図/依存に必要な意味情報を毎変更で再計算する起動・解析コストをdebounceでどこまで抑えるか。
- 単一バイナリの肥大化と起動時間: 現状トリム/AOT非適用 (Roslyn互換のため) で self-extract コストがかかる。serve追加でHttpListener常駐するが、AOT化はRoslynが阻むため選べない。起動時間の許容ラインをどこに置くか。
- ELK worker埋め込みのライセンス/サイズ: elk-worker.min.js追加でvendor合計が増える (現状cytoscape365KB+dagre277KB)。インライン埋め込みHTMLのサイズ上限と、serve配信時は別ファイル参照に切り替えるかの方針。
- クロスプラットフォームのエディタ起動: $EDITOR/code/cursor の検出順とWindows/WSL/SSHリモート環境でのProcess.Start挙動 (ProgramHelpers.cs:239-244 はブラウザ用で、エディタ起動の信頼性は未検証)。
