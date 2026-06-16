// unilyze serve client: ETag long-polling over fetch (no SSE/WebSocket — EventSource
// cannot send Authorization). Reads the per-start token from the page, applies each new
// full snapshot through the viewer's window.unilyzeApplySnapshot (no reload), and surfaces
// the analyzing / ready / failed (stale) state in the status bar.
(function () {
  'use strict';

  // serve runs ELK on the main thread (CSP-friendly, no cross-origin blob worker).
  window.__UNILYZE_ELK_MAIN_THREAD__ = true;

  // Measurement point (#199): browser-side derived-state + layout apply time per generation.
  var lastApplyMs = null;
  window.__unilyzeOnApply = function (ms) {
    lastApplyMs = ms;
    console.info('[unilyze] browser apply ' + ms.toFixed(1) + 'ms');
  };

  var tokenMeta = document.querySelector('meta[name="unilyze-token"]');
  var token = tokenMeta ? tokenMeta.getAttribute('content') : '';
  var authHeaders = { 'Authorization': 'Bearer ' + token };

  var etag = null;
  var generation = -1;
  var aborter = null;
  var stopped = false;

  function sleep(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }

  async function whenViewerReady() {
    while (typeof window.unilyzeApplySnapshot !== 'function') {
      await sleep(30);
    }
  }

  async function fetchSnapshot() {
    var headers = Object.assign({}, authHeaders);
    if (etag) headers['If-None-Match'] = etag;
    var res = await fetch('/api/snapshot', { headers: headers });
    if (res.status === 304) throw new Error('snapshot not updated');
    if (res.status === 503) throw new Error('snapshot not ready');
    if (!res.ok) throw new Error('snapshot HTTP ' + res.status);
    var nextEtag = res.headers.get('ETag');
    if (!nextEtag) throw new Error('snapshot response missing ETag');
    var data = await res.json();
    await Promise.resolve(window.unilyzeApplySnapshot(data));
    etag = nextEtag;
    // The server lists the blocks the user just edited (opaque fileIds). Pan/highlight
    // them so the live update draws the eye to what changed. Empty on the first snapshot.
    var changed = res.headers.get('X-Unilyze-Changed-FileIds');
    if (changed && typeof window.unilyzeFocusChanged === 'function') {
      var ids = changed.split(',').filter(function (id) { return id; });
      if (ids.length) window.unilyzeFocusChanged(ids);
    }
    return true;
  }

  async function applyLatestSnapshot() {
    setSnapshotPending();
    while (!stopped) {
      try {
        await fetchSnapshot();
        return;
      } catch (e) {
        setSnapshotFailure(e);
        await sleep(1000);
      }
    }
  }

  async function pollLoop() {
    while (!stopped) {
      aborter = new AbortController();
      var state;
      try {
        var res = await fetch('/api/state?after=' + generation, {
          headers: authHeaders,
          signal: aborter.signal
        });
        if (!res.ok) { setConnection(false); await sleep(1000); continue; }
        state = await res.json();
      } catch (e) {
        if (e && e.name === 'AbortError') return;
        setConnection(false);
        await sleep(1000);
        continue;
      }
      generation = state.generation;
      if (state.snapshotGeneration != null && state.snapshotEtag !== etag) {
        await applyLatestSnapshot();
      }
      if (stopped) return;
      updateStatus(state);
    }
  }

  // --- Status bar ---
  var dotEl = document.getElementById('ssDot');
  var textEl = document.getElementById('ssText');
  var genEl = document.getElementById('ssGen');
  var timeEl = document.getElementById('ssTime');
  var staleEl = document.getElementById('staleBanner');

  function setStale(show, message) {
    if (!staleEl) return;
    if (show) {
      staleEl.textContent = message;
      staleEl.classList.remove('hidden');
    } else {
      staleEl.classList.add('hidden');
    }
  }

  function setConnection(connected) {
    if (!dotEl) return;
    if (!connected) {
      dotEl.className = 'ss-dot ss-reconnect';
      if (textEl) textEl.textContent = 'reconnecting…';
    }
  }

  function setSnapshotPending() {
    if (dotEl) dotEl.className = 'ss-dot ss-analyzing';
    if (textEl) textEl.textContent = 'updating…';
    setStale(true, 'Updating snapshot — showing the last good result');
  }

  function setSnapshotFailure(error) {
    var message = error && error.message ? error.message : String(error);
    if (dotEl) dotEl.className = 'ss-dot ss-failed';
    if (textEl) textEl.textContent = 'snapshot update failed — showing stale result';
    if (timeEl) timeEl.textContent = message ? (' · ' + message) : '';
    setStale(true, 'Snapshot update failed — showing the last good result'
      + (message ? (' · ' + message) : ''));
    console.warn('[unilyze] snapshot update failed; retrying', error);
  }

  function formatTime(iso) {
    if (!iso) return '';
    try { return new Date(iso).toLocaleTimeString(); } catch (e) { return ''; }
  }

  function updateStatus(state) {
    if (!dotEl) return;
    if (genEl) genEl.textContent = 'gen ' + state.generation;
    if (state.phase === 'analyzing') {
      dotEl.className = 'ss-dot ss-analyzing';
      if (textEl) textEl.textContent = 'analyzing…';
    } else if (state.phase === 'failed') {
      dotEl.className = 'ss-dot ss-failed';
      if (textEl) textEl.textContent = 'analysis failed — showing stale result';
      if (timeEl) timeEl.textContent = state.lastError ? (' · ' + state.lastError) : '';
      setStale(true, '⚠ Analysis failed — showing the last good result'
        + (state.lastError ? (' · ' + state.lastError) : ''));
      return;
    } else {
      dotEl.className = 'ss-dot ss-ready';
      if (textEl) textEl.textContent = 'live';
      setStale(false);
      if (state.metrics) {
        console.info('[unilyze] gen ' + state.generation
          + ': analysis ' + Math.round(state.metrics.analysisMillis) + 'ms'
          + ', json ' + state.metrics.jsonSizeBytes + ' bytes'
          + (lastApplyMs != null ? (', apply ' + lastApplyMs.toFixed(1) + 'ms') : ''));
      }
    }
    if (timeEl) {
      var t = formatTime(state.lastSuccessUtc);
      var applyNote = lastApplyMs != null ? (' · apply ' + lastApplyMs.toFixed(0) + 'ms') : '';
      timeEl.textContent = (t ? (' · updated ' + t) : '') + applyNote;
    }
  }

  // Read the source body for a fileId (opaque) and return {path, text}. Used by the
  // in-browser read-only source view. The server enforces the allowlist.
  window.unilyzeFetchSource = async function (fileId) {
    var res = await fetch('/api/source?fileId=' + encodeURIComponent(fileId), { headers: authHeaders });
    if (!res.ok) throw new Error('source HTTP ' + res.status);
    var path = res.headers.get('X-Unilyze-Source-Path') || '';
    var text = await res.text();
    return { path: path, text: text };
  };

  window.addEventListener('beforeunload', function () {
    stopped = true;
    if (aborter) aborter.abort();
  });

  (async function () {
    await whenViewerReady();
    // The poll loop fetches the snapshot once /api/state reports one exists, so we never
    // request /api/snapshot before the first analysis completes (avoids a 503).
    pollLoop();
  })();
})();
