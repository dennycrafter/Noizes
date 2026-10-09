// Noizes claude.ai watcher (content script).
//
// NOTE: The selectors below are BEST-EFFORT and may need updates as claude.ai's
// DOM changes. claude.ai exposes no public "response finished" hook, so we
// approximate "generating" by the presence of the Stop button and fire
// browser-claude-done when a real busy period ends.
//
// Behavior: watch for a busy period (a stop/generating button is present) that
// lasts at least 1 second; when it ends (debounced: page idle for 1.5s), tell
// the service worker to deliver the browser-claude-done event. The watcher
// stays alive for the page lifetime so every finished response fires.

"use strict";

(() => {
  // Guard against double injection (extension reload / re-injection).
  if (window.__noizesClaudeWatcher) return;
  window.__noizesClaudeWatcher = true;

  // Candidate selectors for claude.ai's stop/generating control. Any match
  // means a response is streaming. Wrapped per-selector so an unsupported
  // selector (e.g. :has() on old Chrome) is skipped, not fatal.
  const BUSY_SELECTORS = [
    'button[aria-label="Stop"]',
    'button[aria-label*="stop" i]',
    'button[data-testid*="stop" i]',
    'button:has(svg[data-testid*="stop" i])',
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
        id: "browser-claude-done",
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
        // Fire only for real response generations, not brief UI flickers.
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

  onMutation(); // the page may already be generating at injection time
})();
