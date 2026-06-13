# unilyze ライブ可視化 改訂ロードマップ

## 戦略テーマ

1. ライブ化は既存 `analyze -f html` を変更せず、独立した `unilyze serve` として追加する。現状は解析、HTML/JSON書き出し、`file://` 起動で終了するワンショット処理である（`src/Unilyze/Program.cs:140-180`、`src/Unilyze/Output/HtmlFormatter.cs:13-24`）。
2. 配信方式は **ETag付きロングポーリング + fetch** に固定する。認証ヘッダ、`AbortController`、タイムアウトを同じ経路で扱い、SSE/EventSourceとWebSocketは採用しない。初期実装は差分ペイロードではなく、世代番号付きの完全スナップショットを配信する。
3. Phase 1は毎回完全解析する。`--incremental` はSyntaxレベルに限定され、他レベルでは無効化される（`src/Unilyze/Pipeline/AnalysisPipeline.cs:42-47`、`src/Unilyze/Pipeline/AnalysisBuildOptions.cs:39-40`）。semantic incrementalは実測で解析時間が支配的と判明した場合だけPhase 2で扱う。
4. ビューア更新はページリロードやCytoscape再生成ではなく、完全スナップショットから派生状態を再構築して既存 `rebuild()` に渡す。`rebuild()` はすでにbatch内でエッジ削除、ノード追加・削除を行っており、`cy.destroy()` は存在しない（`src/Unilyze/Templates/viewer/main.js:1076-1173`）。
5. ソース到達方式は **ブラウザ内の読み取り専用ソース表示** に固定する。エディタ起動やURIスキームは実装しない。型には既存の `FilePath/StartLine` を使うが、partial型では最初の宣言位置しか残らないため、MVPでは「型の代表宣言」への移動に限定する（`src/Unilyze/Pipeline/TypeInfo.cs:54-59,345-359`）。
6. 優先順位は「ライブ更新の正確性と安全性 → ソース閲覧 → 行diff → 呼び出し/制御フロー」とする。diffは現在メトリクス比較のみ、フローは型間依存のみであり、後二者はデータモデル拡張が先に必要になる（`src/Unilyze/Diff/DiffCalculator.cs:17-61`、`src/Unilyze/Pipeline/TypeInfo.cs:13-33`）。

**MVP完了条件:** `unilyze serve -p <path>` を起動し、対象入力の変更後に完全解析結果がページリロードなしで反映され、解析中・成功・失敗・古い表示の状態が明示され、型からブラウザ内ソースへ移動できること。

## クイックウィン（effort S × impact medium 以上）

- [ ] ELK本体とworkerを埋め込みリソース化し、unpkg参照とblob workerを撤廃する（effort S, impact medium）。現状は本体が外部script、workerも `importScripts` で外部取得する（`src/Unilyze/Templates/viewer/index.html:73`、`src/Unilyze/Templates/viewer/main.js:1198-1202`）。既存のvendor同梱方式を踏襲する（`src/Unilyze/Unilyze.csproj:33-38`）。
- [ ] 解析フェーズ時間、生成JSONサイズ、ブラウザ適用時間を記録する計測点を追加する（effort S, impact high）。解析側には既にdiscover/parse/compile/semantic/aggregateの区間計測がある（`src/Unilyze/Pipeline/AnalysisPipeline.cs:63-104`）。
- [ ] ライブ画面に世代番号、解析中、最終成功時刻、エラーを表示するステータスバーを追加する（effort S, impact high）。解析失敗時は直前スナップショットを残すが、古い結果であることを明示する。

## Phase 1（MVP）

- [ ] `serve` を独立コマンドとして追加し、既存analyze/MCPとはライフサイクルを共有しない（effort M, impact high）。コマンドallowlistと分岐の両方へ登録する（`src/Unilyze/Cli/CliArgValidation.cs:5-11`、`src/Unilyze/Program.cs:18-45`）。MCPはstdin EOFまでの同期ループであり契約が異なる（`src/Unilyze/Mcp/McpStdioServer.cs:8-40`）。
- [ ] BCL `HttpListener` を127.0.0.1専用で起動する（effort M, impact high）。`--host` は設けず、`--port` 指定またはランダムな高位ポートを選んで競合時に再試行する。ポート0割当には依存しない。`--no-open` を維持し、URLは常に標準エラーへ表示する。
- [ ] セキュリティ境界を固定する（effort M, impact high）。起動時トークンをURLへ載せず、`GET /` のno-store HTML内へ一度だけ埋め込み、API呼び出しは `Authorization: Bearer` を必須化する。Hostを完全一致で検証し、Originが存在する場合も同一オリジンのみ許可、CORSとcookieは使用しない。
- [ ] serve専用HTMLではscript/vendor/styleを同一オリジンの個別リソースとして配信し、`default-src 'none'; script-src 'self'; connect-src 'self'; worker-src 'self'; style-src 'self' 'unsafe-inline'` を適用する（effort M, impact high）。現行テンプレートはinline style/script/vendorと外部ELKを使うため、そのまま `script-src 'self'` にはできない（`src/Unilyze/Templates/viewer/index.html:7-9,71-76`、`src/Unilyze/Output/HtmlTemplate.cs:28-35`）。
- [ ] 変更検知を「FileSystemWatcherは即時通知、定期fingerprint照合は取りこぼし防止」として実装する（effort L, impact high）。`.cs`だけでなく、`.sln/.csproj`、`.asmdef/.meta`、生成ソース、解決済み参照DLL、UnityのProjectVersion/ScriptAssembliesを解析入力として追跡する（`src/Unilyze/Pipeline/AnalysisPipelineDiscovery.cs:30-68,133-177`、`src/Unilyze/Discovery/AsmdefInfo.cs:18-36`、`src/Unilyze/Discovery/GeneratedSourcesResolver.cs:20-38`、`src/Unilyze/Discovery/UnityDllResolver.cs:53-63`）。
- [ ] 変更イベントを300ms程度でまとめ、解析は常に1本だけ実行する（effort M, impact high）。新しい変更が来た場合は現在の解析完了後に最新世代を再解析する。Phase 1では `incremental:false` の完全解析を使い、成功時だけimmutableな最新JSONへ原子的に差し替える。
- [ ] `GET /api/state?after=<generation>` をロングポーリングし、変更時またはタイムアウト時に状態を返す（effort M, impact high）。スナップショットは `GET /api/snapshot` のETag/`If-None-Match` で取得し、ブラウザ側は`AbortController`でキャンセルする。HTTP JSONには`application/json`、`nosniff`、`no-store`を設定する。
- [ ] viewerを `buildDerivedState(data)` と `applySnapshot(data)` に分離し、既存 `rebuild()` を再利用する（effort L, impact high）。`DATA`、`tl`、`tm`、diff索引は現在初期化時に一度だけ構築される（`src/Unilyze/Templates/viewer/main.js:1,44-77`）。pan/zoom、展開、選択、検索条件を保持し、更新時のlayoutは`fit:false`とする（`src/Unilyze/Templates/viewer/main.js:1191-1195,1268-1269,1690`）。
- [ ] ブラウザ内ソースAPIを追加する（effort M, impact high）。クライアントには絶対パスを返さず、解析済みファイルから作った不透明`fileId`と相対表示名だけを渡す。APIは生パスを受け取らず、allowlist内の実体を`text/plain`で返し、画面では`textContent`だけで描画する。現在のJSONは絶対`ProjectPath/FilePath`を保持する（`src/Unilyze/Pipeline/AnalysisResult.cs:12-18`、`src/Unilyze/Metrics/CodeHealthCalculator.cs:49-50`）。
- [ ] CLI、HTTP、監視、解析失敗、認証、ソース境界のE2Eを追加し、net8.0/net9.0/net10.0で検証する（effort M, impact high）。対象TFMは3つであり（`src/Unilyze/Unilyze.csproj:4`）、既存のCLI/MCPプロセスE2Eを土台にする。

## Phase 2

- [ ] Phase 1の計測結果を固定データセットで記録し、解析、転送、JSON parse、派生状態再構築、layoutのどこが支配的か判定する（effort S, impact high）。差分パッチとsemantic incrementalはこの結果なしに着手しない。
- [ ] ソース位置モデルを修正する（effort L, impact high）。型には全partial宣言、メンバーには`fileId/startLine/endLine`を保持する。現状の`MemberInfo`はFilePathを持たず（`src/Unilyze/Pipeline/TypeInfo.cs:62-77`）、partialマージ後は最初のFilePath/StartLineが残る（`src/Unilyze/Pipeline/TypeInfo.cs:345-359`）。
- [ ] SymbolKey相当の安定`memberId`を導入する（effort L, impact high）。assembly、含有型ID、metadata名、generic arity、明示interface、parameter型とref-kindを含む正規IDを`IMethodSymbol.OriginalDefinition`から生成する。現在のdiffキーは名前+引数数だけで衝突する（`src/Unilyze/Diff/DiffCalculator.cs:267`）。型IDのassembly/namespace/nested/generic arity形式を基礎にする（`src/Unilyze/Pipeline/TypeIdentity.cs:82-96`）。
- [ ] `MethodDiff`を追加・削除・変更メソッドを表せるモデルへ更新する（effort M, impact high）。現状はbeforeを走査してafterに一致したメソッドだけを追加するため、追加・削除が欠落する（`src/Unilyze/Diff/DiffCalculator.cs:236-265`）。before/after双方の`memberId`とソース範囲を保持する。
- [ ] difit的表示は「現在の作業ツリー対HEAD」に固定し、`git diff --no-ext-diff -- HEAD -- <path>`をサーバ側で実行してside-by-side表示する（effort L, impact high）。クライアントからgit refやパスは受け取らない。既存のGit実行はshellを使わずArgumentListを使用する（`src/Unilyze/History/GitProcess.cs:7-19`）。
- [ ] `deltaScore`は行diffの意味分類に使わず、別枠の品質リスク指標として表示する（effort S, impact medium）。これは変更対象のlow/high-risk件数比である（`src/Unilyze/Diff/DiffCalculator.cs:64-80,145-160`）。行diffには追加・削除・変更だけを表示し、メソッドメトリクス変化を隣接バッジとして重ねる。
- [ ] 完全スナップショットの転送・再構築が支配的だった場合だけ、typeId/memberId keyed patchを追加する（effort L, impact medium）。Cytoscape内部は既に要素単位で更新するため（`src/Unilyze/Templates/viewer/main.js:1076-1143`）、まずネットワーク・派生状態部分だけを対象にする。
- [ ] semantic解析が支配的だった場合だけ、常駐`LiveAnalysisSession`による差分解析を試作する（effort L, impact high）。`ReplaceSyntaxTree`だけではなく、全treeをprewarmする処理を除去し、変更閉包だけSemanticModelを取得する必要がある（`src/Unilyze/Pipeline/SemanticEnricher.cs:53-78`）。各結果をクリーンな完全解析と比較し、一致しない最適化は採用しない。

## Phase 3

- [ ] 選択型または選択メソッドだけを対象にMethodCallEdgeを抽出する（effort L, impact high）。呼び出し解決は既存RFC計算と同じ`SemanticModel.GetSymbolInfo`と`OriginalDefinition`を利用する（`src/Unilyze/Metrics/RfcCalculator.cs:51-59`）。全プロジェクト常時抽出は行わない。
- [ ] 呼び出しグラフを深さ2、100ノード程度の上限付きで表示し、型/namespaceへ集約できるようにする（effort L, impact high）。既存のmeta-edge集約を再利用する（`src/Unilyze/Templates/viewer/main.js:1146-1169`）。
- [ ] 選択した1メソッドだけのCFGをRoslynから生成し、分岐、ループ、return、例外辺を専用ペインで表示する（effort L, impact medium）。現行パイプラインはRoslyn 4.12.0を参照する（`src/Unilyze/Unilyze.csproj:42`）。全メソッド一括生成は行わない。
- [ ] MethodDiffを呼び出しグラフへ重ね、変更メソッドと直接呼び出し元だけを強調する（effort M, impact medium）。安定`memberId`完成後にのみ着手し、推測による誤結合は許可しない。
- [ ] フロー図の抽出時間、JSONサイズ、layout時間、誤解決率を計測し、既定OFFの実験機能として評価する（effort M, impact medium）。上限超過時はグラフを省略し、ソース一覧へフォールバックする。

## リスク

1. 完全解析が保存頻度に追いつかない可能性がある。Phase 1では正確性を優先して最新要求をコアレスし、Phase 2の実測結果から最適化対象を決める。
2. FileSystemWatcherだけでは通知欠落や参照変更を保証できない。解析入力manifestの定期照合、watcher error時の全再走査、解析中/失敗/古い状態の表示を必須とする。
3. serve化により未信頼リポジトリのデータがHTTP原点へ入る。絶対パス非公開、fileId allowlist、認証、Host/Origin検証、CSP、`textContent`描画を同時に導入する。既存には多数の`innerHTML` sinkがある（`src/Unilyze/Templates/viewer/main.js:483,1648,1944`）。
4. source位置とmember IDの変更はJSON、diff、baseline、viewerへ波及する。`metricsVersion`とは別にviewer schema versionを設け、旧JSONの読み込み互換を維持する（`src/Unilyze/Pipeline/AnalysisResult.cs:21-36`）。
5. HEAD基準diffはgit未導入リポジトリでは使えない。その場合はソース閲覧だけを提供し、diffペインに明示的な利用不可理由を表示する。
6. 配布検証は3TFMに加えて、viewer生成が`python3`へ依存する点を維持管理する必要がある（`src/Unilyze/Unilyze.csproj:51-56`）。serve用個別リソースと静的単一HTMLの二経路にドリフト検査を置く。
7. フロー図は解析・描画とも高コストで、正確さもシンボル解決品質に依存する。既定OFF、選択スコープ限定、上限付きとする。

## やらないこと（スコープ外）

- SSE、WebSocket、EventSourceによるpush配信。
- Phase 1でのJSON差分パッチ、semantic incremental、`Compilation.ReplaceSyntaxTree`最適化。
- `location.reload()`、iframe再読込、`cy.destroy()`によるライブ更新。
- `vscode://`、`idea://`、`code --goto`などのエディタ起動。
- ループバック外への公開、LAN共有、リモート共同閲覧、TLS対応。
- `serve`とMCPの同一プロセス化、ライブ状態のMCP共有。MCPはstdio寿命を維持する。
- `deltaScore`を意味的な行diffやリファクタリング分類として扱うこと。
- 任意git ref間のソースdiff、AST編集スクリプト、リネーム・移動・リファクタリング自動分類。
- 全プロジェクトのメソッド呼び出しグラフまたはCFGの常時抽出・一括描画。
- Phase 1での`python3`ビルド依存撤廃、Kestrel/ASP.NET Coreへの移行。