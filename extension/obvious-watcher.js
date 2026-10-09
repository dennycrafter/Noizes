// Noizes Obvious watcher (content script).
//
// NOTE: Detection here is BEST-EFFORT. The Obvious app exposes no public
// "finished" hook, so we look for generic busy/generating indicators. The
// reliable path is the "Watch this tab" feature (toolbar button or context
// menu), which injects watch.js on demand.
//
// Same pattern as claude-watcher.js: a busy period lasting at least 1 second
// that ends (debounced: page idle for 1.5s) fires browser-obvious-done.

"use strict";

(() => {
  // Guard against double injection.
  if (window.__noizesObviousWatcher) return;
  window.__noizesObviousWatcher = true;

  // Generic busy/generating indicators.
  const BUSY_SELECTORS = [
    '[aria-busy="true"]',
    ".animate-spin",
    '[data-testid*="spinner" i]',
    '[class*="generating" i]',
    '[class*="thinking" i]',
  ];

  const MIN_BUSY_MS = 1000; // ignore busy blips shorter than this
  const IDLE_DEBOUNCE_MS = 1500; // page must stay idle this long before firing

  let busySince = null; // timestamp the current busy period started (null = idle)
  let busyEnd = null; // timestamp the current busy period ended
  let idleTimer = null;

  function isBusy() {
    for (let i = 0; i < BUSY_SELECTORS.length; i++) {
      try {
        if (document.querySelector(BUSY_SELECTORS[i])) return true;
      } catch (_) {
        // Selector unsupported on this Chrome version — skip it.
      }
    }
    return false;
  }

  function notifyDone() {
    const detail = { url: location.href, title: document.title };
    try {
      const result = chrome.runtime.sendMessage({
        type: "noizes-event",
        id: "browser-obvious-done",
        detail: detail,
      });
      if (result && typeof result.catch === "function") result.catch(() => {});
    } catch (_) {
      // Extension context invalidated (extension reloaded) — nothing to do.
    }
  }

  function onMutation() {
    if (isBusy()) {
      busyEnd = null;
      if (idleTimer) {
        clearTimeout(idleTimer);
        idleTimer = null;
      }
      if (!busySince) busySince = Date.now();
    } else if (busySince && !idleTimer) {
      busyEnd = Date.now();
      idleTimer = setTimeout(() => {
        idleTimer = null;
        const startedAt = busySince;
        const endedAt = busyEnd;
        busySince = null;
        busyEnd = null;
        // Fire only for real busy periods, not brief UI flickers.
        if (startedAt && endedAt && endedAt - startedAt >= MIN_BUSY_MS) notifyDone();
      }, IDLE_DEBOUNCE_MS);
    }
  }

  const observer = new MutationObserver(onMutation);
  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    characterData: true,
  });

  onMutation(); // the page may already be busy at injection time
})();
