// Noizes "Watch this tab" — injected on demand by background.js via
// chrome.scripting.executeScript({files:["watch.js"]}).
//
// Toggle-aware: if a watcher is already running on this page, this injection
// STOPS it; otherwise it starts one. State lives on window.__noizesWatch in
// this script's isolated world, which persists for the page's lifetime.
//
// Watches a GENERIC set of busy/loading indicators; after a busy period ends
// (debounced: page idle for 1.5s) it fires the browser-watch-done event. A
// small on-page badge shows that the tab is being watched. Auto-stops after
// 30 minutes so a watch never runs forever.

"use strict";

(() => {
  if (window.__noizesWatch) {
    window.__noizesWatch.stop();
    return;
  }

  // Generic selector list: every busy indicator used by the site watchers
  // (claude.ai + Obvious) plus broad loading/spinner/progress/skeleton marks.
  const BUSY_SELECTORS = [
    // claude.ai-style stop/generating buttons
    'button[aria-label="Stop"]',
    'button[aria-label*="stop" i]',
    'button[data-testid*="stop" i]',
    'button:has(svg[data-testid*="stop" i])',
    // generic busy/generating indicators
    '[aria-busy="true"]',
    ".animate-spin",
    '[data-testid*="spinner" i]',
    '[class*="generating" i]',
    '[class*="thinking" i]',
    // generic loading/progress/skeleton marks
    '[class*="loading" i]',
    '[class*="spinner" i]',
    "progress",
    '[role="progressbar"]',
    '[class*="skeleton" i]',
  ];

  const IDLE_DEBOUNCE_MS = 1500; // page must stay idle this long before firing
  const AUTO_STOP_MS = 30 * 60 * 1000; // never-ending watch guard

  let observer = null;
  let idleTimer = null;
  let autoStopTimer = null;
  let badge = null;
  let busySeen = false; // busy observed since the last fired event (or start)
  let stopped = false;

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

  function sendMessage(payload) {
    try {
      const result = chrome.runtime.sendMessage(payload);
      if (result && typeof result.catch === "function") result.catch(() => {});
    } catch (_) {
      // Extension context invalidated — nothing to do.
    }
  }

  function notifyDone() {
    sendMessage({
      type: "noizes-event",
      id: "browser-watch-done",
      detail: { url: location.href, title: document.title },
    });
  }

  function onMutation() {
    if (stopped) return;
    if (isBusy()) {
      busySeen = true;
      if (idleTimer) {
        clearTimeout(idleTimer);
        idleTimer = null;
      }
    } else if (busySeen && !idleTimer) {
      // Only schedule after a real busy period — never for idle pages.
      idleTimer = setTimeout(() => {
        idleTimer = null;
        busySeen = false;
        if (!stopped) notifyDone();
      }, IDLE_DEBOUNCE_MS);
    }
  }

  function start() {
    observer = new MutationObserver(onMutation);
    observer.observe(document.documentElement, {
      childList: true,
      subtree: true,
      attributes: true,
      characterData: true,
    });
    onMutation(); // the page may already be busy at injection time

    badge = document.createElement("div");
    badge.textContent = "● Noizes watching";
    badge.style.cssText =
      "position:fixed;bottom:12px;right:12px;z-index:2147483647;pointer-events:none;" +
      "background:#111827;color:#ffffff;font:12px/1.4 system-ui,-apple-system,sans-serif;" +
      "padding:4px 10px;border-radius:999px;box-shadow:0 2px 10px rgba(0,0,0,0.35);";
    (document.body || document.documentElement).appendChild(badge);

    autoStopTimer = setTimeout(() => stop(), AUTO_STOP_MS);

    sendMessage({ type: "noizes-watch-state", watching: true });
  }

  function stop() {
    if (stopped) return;
    stopped = true;
    if (observer) {
      try {
        observer.disconnect();
      } catch (_) {
        /* ignore */
      }
      observer = null;
    }
    if (idleTimer) {
      clearTimeout(idleTimer);
      idleTimer = null;
    }
    if (autoStopTimer) {
      clearTimeout(autoStopTimer);
      autoStopTimer = null;
    }
    if (badge && badge.parentNode) {
      badge.parentNode.removeChild(badge);
    }
    badge = null;
    window.__noizesWatch = null; // a later injection can start a fresh watch
    sendMessage({ type: "noizes-watch-state", watching: false });
  }

  window.__noizesWatch = { stop: stop };
  start();
})();
