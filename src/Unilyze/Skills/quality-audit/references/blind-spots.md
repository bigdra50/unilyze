# Unilyze Blind Spots

unilyze が計測できない / 精度が低い領域。AI レビューで補完する。

## 計測対象外

- Top-level statements (Program.cs 等): 型に属さないため TypeMetrics に含まれない
- Global functions / script-style files
- Generated code (.g.cs) がフィルタされていない場合

## ランタイムリスク (静的解析で検出困難)

- Process.Start + StandardOutput.ReadToEnd のデッドロック
- 再帰呼び出しの StackOverflowException リスク
- IDisposable 未 Dispose (JsonDocument, Process 等)

> catch (Exception) の握り潰しは ExceptionFlowAnalyzer (CatchAllException) で検出可能になった。

## 精度が低い領域

- Syntactic fallback 時の CBO (var, using alias, generic 型引数を見逃す)
- 名前ベースのインターフェース判定 (I + 大文字 ヒューリスティック)
- 名前ベースの再帰検出 (オーバーロードで偽陽性)
- .sln パーサの簡易実装 (引用符内パスの誤判定)

## スコアが実態より低く出るパターン

- partial class: 複数ファイルに分割していても1型として合算される。メソッド数・行数が膨れ GodClass 判定になりやすい
- static 拡張メソッドクラス: Rx の Observable や LINQ 風 API は設計上1クラスにオペレータを集約する。メソッド数が閾値を大幅に超えるが、これは C# の慣例的パターン
- variadic generics 不在への対応: Zip<T1..TN> のようなオーバーロード群はパラメータ数で ExcessiveParameters が出るが、言語制約上避けられない

これらに該当する場合、スコアの低さは設計品質の問題ではなく計測特性によるもの。改善対象から除外してよい。

## 設計レベルの問題 (メトリクスに現れにくい)

- メトリクス名のハードコード (higher-is-better 判定等)
- 重複した型定義 (TypeKey 等の同一 record が複数ファイルに存在)
- 拡張時の変更箇所が散在 (新メトリクス追加時に複数ファイル変更必要)
