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
- bare catch / catch (Exception) による致命的例外の握り潰し

## 精度が低い領域

- Syntactic fallback 時の CBO (var, using alias, generic 型引数を見逃す)
- 名前ベースのインターフェース判定 (I + 大文字 ヒューリスティック)
- 名前ベースの再帰検出 (オーバーロードで偽陽性)
- .sln パーサの簡易実装 (引用符内パスの誤判定)

## 設計レベルの問題 (メトリクスに現れにくい)

- メトリクス名のハードコード (higher-is-better 判定等)
- 重複した型定義 (TypeKey 等の同一 record が複数ファイルに存在)
- 拡張時の変更箇所が散在 (新メトリクス追加時に複数ファイル変更必要)
