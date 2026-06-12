/* ============================================================
 * .claude/reports/_index.js
 * レポートのマニフェスト。
 * new-report.sh が自動でエントリ追加するため、通常は手で編集しなくて良い。
 *
 * 6 種類のレポート (decision / plan / brief / review / retrospective / architecture) と
 * 属性 (weight / target / scope / base / detail) でライフサイクル全体を網羅する。
 * ============================================================ */

window.REPORTS_INDEX = [
  {
    id: "2026-06-05-semgrep-adoption",
    title: "semgrepからunilyzeへ取り入れる機能の選定",
    type: "decision",
    weight: "heavy",
    date: "2026-06-05",
    path: "decisions/2026-06-05-semgrep-adoption.html",
    tags: ["semgrep", "roadmap", "ci-gate", "sarif", "exit-code"],
    status: "done",
    scent: {
      one_line: "semgrep調査から12候補を統合し、第1弾 E(schemaVersion)→A(diff --fail-on)→B(StableFindingId) を採用決定。エンジン系翻案は不採用。",
      key_terms: ["schemaVersion", "fail-on", "partialFingerprints", "unilyze-ignore", "confidence", "StableFindingId"],
      reading_minutes: 15,
      prereqs: []
    },
    related: [],
  },
  // ----------------------------------------------------------
  // ここに各レポートのエントリが入る (new-report.sh が prepend する)
  // ----------------------------------------------------------
];

/**
 * 型定義 (JSDoc)
 *
 * @typedef {Object} ReportEntry
 * @property {string}   id              - "YYYY-MM-DD-<slug>"
 * @property {string}   title           - 表示タイトル
 * @property {"decision"|"plan"|"brief"|"review"|"retrospective"|"architecture"} type
 * @property {string}   date            - 作成日 "YYYY-MM-DD"
 * @property {string}   [updated]       - 改訂日 (任意)
 * @property {string}   path            - .claude/reports/ からの相対パス
 * @property {string[]} tags
 * @property {"draft"|"in-progress"|"done"|"archived"|"template"} status
 * @property {string}   [author]
 *
 * // --- 種別固有属性 ---
 * @property {"light"|"heavy"}              [weight]   // decision
 * @property {"issue"|"pr"|"handoff"}       [target]   // brief
 * @property {"pr"|"module"|"periodic"}     [scope]    // review
 * @property {"plan"|"incident"}            [base]     // retrospective
 * @property {"overview"|"module"|"class"}  [detail]   // architecture
 * @property {string}                       [commit_hash] // architecture のみ: 作成/更新時の HEAD コミットハッシュ (html-reports-arch が更新時の差分基点に使う)
 * @property {string}                       [review_at] // review scope=periodic の次回監査日 (YYYY-MM-DD)
 *
 * // --- 検索ヒント (Pirolli の Information Foraging Theory) ---
 * @property {Object}   [scent]
 * @property {string}   scent.one_line         - 1 行サマリ
 * @property {string[]} scent.key_terms        - 検索強調用語
 * @property {number}   scent.reading_minutes  - 読了見積もり (分)
 * @property {string[]} scent.prereqs          - 前提 (related id 推奨)
 *
 * // --- 関係性 ---
 * @property {string[]} [related]          - 双方向リンク
 * @property {string[]} [derived_from]     - 派生元 (plan -> brief 等)
 * @property {string[]} [supersedes]       - 旧版を置き換える
 * @property {string[]} [retrospective_of] - retrospective が振り返る対象
 *
 * // --- 後方互換 (廃止予定) ---
 * @property {string}   [summary]          - scent.one_line が無い場合のみ表示
 */

/* 記入例:
window.REPORTS_INDEX = [
  {
    id: "2026-06-05-semgrep-adoption",
    title: "semgrepからunilyzeへ取り入れる機能の選定",
    type: "decision",
    weight: "heavy",
    date: "2026-06-05",
    path: "decisions/2026-06-05-semgrep-adoption.html",
    tags: [],
    status: "draft",
    author: "",
    scent: { one_line: "", key_terms: [], reading_minutes: 0, prereqs: [] },
    related: [],
  },
  {
    id: "2026-05-21-di-container",
    title: "DI コンテナ採用判断",
    type: "decision",
    weight: "heavy",
    date: "2026-05-21",
    updated: "2026-05-22",
    path: "decisions/2026-05-21-di-container.html",
    tags: ["di", "architecture", "vcontainer"],
    status: "done",
    author: "",
    scent: {
      one_line: "VContainer 採用を決定。Zenject から移行する根拠と影響範囲。",
      key_terms: ["VContainer", "Zenject", "DI", "LifetimeScope"],
      reading_minutes: 8,
      prereqs: []
    },
    related: ["2026-05-23-di-migration-plan"],
    supersedes: []
  },
  // ...
];
*/
