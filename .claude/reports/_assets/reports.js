/* ============================================================
 * .reports reports.js
 * 共通ユーティリティ:
 *   - Mermaid 初期化 (動的ロード)
 *   - Prism シンタックスハイライト
 *   - タブ / アンカースクロール
 *   - data-show-when による属性別セクション切替
 *   - copy-brief / copy-json / copy-prompt ボタン
 * ============================================================ */

(function () {
  "use strict";

  // ------------------------------------------------------------
  // Mermaid setup (loads on-demand via CDN ESM)
  // ------------------------------------------------------------
  async function initMermaid() {
    if (!document.querySelector(".mermaid")) return;
    try {
      const { default: mermaid } = await import(
        "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs"
      );
      mermaid.initialize({
        startOnLoad: true,
        theme: "dark",
        themeVariables: {
          background: "#1a2129",
          primaryColor: "#1f2730",
          primaryTextColor: "#e6edf3",
          primaryBorderColor: "#2d3742",
          lineColor: "#8b949e",
          secondaryColor: "#161b22",
          tertiaryColor: "#0f1419",
        },
        flowchart: { curve: "basis" },
        sequence: { actorMargin: 50 },
      });
      mermaid.run();
      window.mermaid = mermaid;
    } catch (e) {
      console.warn("[reports.js] Mermaid load failed:", e);
    }
  }

  // ------------------------------------------------------------
  // Prism setup
  // ------------------------------------------------------------
  function injectPrism() {
    if (!document.querySelector('pre code[class*="language-"]')) return;
    const css = document.createElement("link");
    css.rel = "stylesheet";
    css.href = "https://cdn.jsdelivr.net/npm/prismjs@1.29.0/themes/prism-tomorrow.css";
    document.head.appendChild(css);

    const core = document.createElement("script");
    core.src = "https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-core.min.js";
    core.onload = () => {
      const autoloader = document.createElement("script");
      autoloader.src = "https://cdn.jsdelivr.net/npm/prismjs@1.29.0/plugins/autoloader/prism-autoloader.min.js";
      document.head.appendChild(autoloader);
    };
    document.head.appendChild(core);
  }

  // ------------------------------------------------------------
  // Tabs
  // ------------------------------------------------------------
  function showSelectedTabPanel() {
    document.querySelectorAll(".tabs").forEach((tabs) => {
      const radios = tabs.querySelectorAll('input[type="radio"]');
      const sync = () => {
        radios.forEach((r) => {
          const panel = tabs.querySelector(`.tab-panel[data-tab="${r.value}"]`);
          if (panel) panel.style.display = r.checked ? "block" : "none";
        });
        tabs.querySelectorAll(".tab-labels label").forEach((l) => {
          const targetId = l.getAttribute("for");
          const radio = document.getElementById(targetId);
          l.classList.toggle("active", radio && radio.checked);
        });
      };
      radios.forEach((r) => r.addEventListener("change", sync));
      sync();
    });
  }

  // ------------------------------------------------------------
  // Anchor smooth scroll
  // ------------------------------------------------------------
  function smoothScrollAnchors() {
    document.querySelectorAll('a[href^="#"]').forEach((a) => {
      a.addEventListener("click", (e) => {
        const id = a.getAttribute("href").slice(1);
        if (!id) return;
        const el = document.getElementById(id);
        if (!el) return;
        e.preventDefault();
        el.scrollIntoView({ behavior: "smooth", block: "start" });
      });
    });
  }

  // ------------------------------------------------------------
  // data-show-when による属性別セクション切替
  // 例: <section data-show-when="scope:periodic">
  //   body[data-scope] が一致しない場合は display:none
  // CSS でも同等の制御を行うため、JS は念のための補助。
  // ------------------------------------------------------------
  function applyShowWhen() {
    const body = document.body;
    document.querySelectorAll("[data-show-when]").forEach((el) => {
      const cond = el.getAttribute("data-show-when") || "";
      const [attr, val] = cond.split(":");
      if (!attr || !val) return;
      const current = body.getAttribute(`data-${attr}`);
      // 一致しない場合は hidden に
      if (current && current !== val) {
        el.style.display = "none";
      } else if (current === val) {
        el.style.display = "";
      }
    });
  }

  // ------------------------------------------------------------
  // Brief の copy ボタン (data-action="copy-brief" data-format="...")
  // フォーマット: issue / pr / handoff / json
  // ------------------------------------------------------------
  const SECTION_TITLES = {
    "tldr": "TL;DR",
    "why-now": "Why now",
    "scope": "Scope",
    "decisions": "設計判断",
    "review-points": "レビュー観点",
    "known-limitations": "既知の制約 / フォローアップ",
    "related": "関連",
  };

  const BRIEF_SECTIONS_BY_FORMAT = {
    issue:   ["tldr", "why-now", "scope", "known-limitations"],
    pr:      ["tldr", "scope", "decisions", "review-points", "known-limitations"],
    handoff: ["tldr", "why-now", "scope", "decisions", "review-points", "known-limitations", "related"],
  };

  function extractMarkdownFromSection(sectionKey) {
    const bodyEl = document.querySelector(`[data-md-section-body="${sectionKey}"]`);
    if (!bodyEl) return "";
    return htmlToMarkdown(bodyEl);
  }

  function htmlToMarkdown(el) {
    // Light-weight HTML -> Markdown 変換 (実用最小限)
    const clone = el.cloneNode(true);
    // pre/code を保持
    clone.querySelectorAll("pre").forEach(p => {
      const code = p.textContent;
      const lang = p.querySelector("code")?.className?.match(/language-(\w+)/)?.[1] || "";
      const md = "\n```" + lang + "\n" + code + "\n```\n";
      p.replaceWith(document.createTextNode(md));
    });
    clone.querySelectorAll("code").forEach(c => {
      c.replaceWith(document.createTextNode("`" + c.textContent + "`"));
    });
    clone.querySelectorAll("strong, b").forEach(b => {
      b.replaceWith(document.createTextNode("**" + b.textContent + "**"));
    });
    clone.querySelectorAll("em, i").forEach(i => {
      i.replaceWith(document.createTextNode("*" + i.textContent + "*"));
    });
    clone.querySelectorAll("a").forEach(a => {
      const txt = a.textContent;
      const href = a.getAttribute("href") || "";
      a.replaceWith(document.createTextNode(`[${txt}](${href})`));
    });
    clone.querySelectorAll("li").forEach(li => {
      const txt = li.textContent.trim();
      li.replaceWith(document.createTextNode(`- ${txt}\n`));
    });
    clone.querySelectorAll("ul, ol").forEach(l => {
      // li が既に改行付きテキストになっているので、リストタグは除去
      const txt = l.textContent;
      l.replaceWith(document.createTextNode("\n" + txt));
    });
    clone.querySelectorAll("table").forEach(t => {
      const rows = [...t.querySelectorAll("tr")];
      if (rows.length === 0) return;
      const headers = [...rows[0].querySelectorAll("th, td")].map(c => c.textContent.trim());
      const body = rows.slice(1).map(r => [...r.querySelectorAll("td")].map(c => c.textContent.trim()));
      let md = "\n| " + headers.join(" | ") + " |\n";
      md += "| " + headers.map(() => "---").join(" | ") + " |\n";
      body.forEach(row => { md += "| " + row.join(" | ") + " |\n"; });
      t.replaceWith(document.createTextNode(md));
    });
    clone.querySelectorAll("p").forEach(p => {
      p.replaceWith(document.createTextNode("\n" + p.textContent + "\n"));
    });
    return clone.textContent.trim().replace(/\n{3,}/g, "\n\n");
  }

  function buildBriefMarkdown(format) {
    const sections = BRIEF_SECTIONS_BY_FORMAT[format] || [];
    const titleEl = document.querySelector("header.report-header h1");
    const title = titleEl ? titleEl.textContent.trim() : "Brief";
    let md = `# ${title}\n\n`;
    sections.forEach(key => {
      const body = extractMarkdownFromSection(key);
      if (!body) return;
      md += `## ${SECTION_TITLES[key] || key}\n\n${body}\n\n`;
    });
    return md.trim() + "\n";
  }

  function buildBriefJson() {
    const titleEl = document.querySelector("header.report-header h1");
    const target = document.body.getAttribute("data-target") || "";
    const data = {
      title: titleEl ? titleEl.textContent.trim() : "",
      type: "brief",
      target,
      sections: {},
    };
    Object.keys(SECTION_TITLES).forEach(key => {
      const body = extractMarkdownFromSection(key);
      if (body) data.sections[key] = body;
    });
    return JSON.stringify(data, null, 2);
  }

  async function handleCopyBrief(btn) {
    const format = btn.getAttribute("data-format");
    let text = "";
    try {
      text = format === "json" ? buildBriefJson() : buildBriefMarkdown(format);
    } catch (e) {
      console.warn("[reports.js] build failed:", e);
      return;
    }
    try {
      await navigator.clipboard.writeText(text);
      btn.classList.add("copied");
      const orig = btn.textContent;
      btn.textContent = "Copied ✓";
      setTimeout(() => {
        btn.classList.remove("copied");
        btn.textContent = orig;
      }, 1500);
    } catch (e) {
      console.warn("[reports.js] clipboard failed:", e);
      // Fallback: select & manual copy via textarea
      const ta = document.createElement("textarea");
      ta.value = text;
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand("copy"); } catch (_) {}
      document.body.removeChild(ta);
    }
  }

  function wireCopyButtons() {
    document.addEventListener("click", (e) => {
      const btn = e.target.closest('[data-action="copy-brief"]');
      if (btn) handleCopyBrief(btn);
    });
  }

  // ------------------------------------------------------------
  // Architecture: LOD slider + Layer toggle
  // ------------------------------------------------------------
  const LOD_LABELS = {
    "0": "L0: 全体俯瞰",
    "1": "L1: 主要モジュール",
    "2": "L2: 内部構造",
    "3": "L3: クラス / ファイル",
  };

  function wireLodSlider() {
    const slider = document.querySelector('input[data-lod-control]');
    if (!slider) return;
    const labelEl = document.getElementById("lod-current");
    const apply = (v) => {
      document.body.setAttribute("data-current-lod", String(v));
      if (labelEl) labelEl.textContent = LOD_LABELS[String(v)] || `L${v}`;
    };
    slider.addEventListener("input", (e) => apply(e.target.value));
    apply(slider.value);
  }

  function wireLayerToggle() {
    const container = document.getElementById("layer-toggle");
    if (!container) return;
    container.addEventListener("click", (e) => {
      const btn = e.target.closest('[data-layer-btn]');
      if (!btn) return;
      container.querySelectorAll('[data-layer-btn]').forEach(b => b.classList.remove("active"));
      btn.classList.add("active");
      document.body.setAttribute("data-current-layer", btn.getAttribute("data-layer-btn"));
    });
  }

  // ------------------------------------------------------------
  // Public API
  // ------------------------------------------------------------
  window.Reports = {
    init() {
      applyShowWhen();
      initMermaid();
      injectPrism();
      showSelectedTabPanel();
      smoothScrollAnchors();
      wireCopyButtons();
      wireLodSlider();
      wireLayerToggle();
    },
    // テスト用に公開
    _buildBriefMarkdown: buildBriefMarkdown,
    _buildBriefJson: buildBriefJson,
  };

  document.addEventListener("DOMContentLoaded", () => window.Reports.init());
})();
