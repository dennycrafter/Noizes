// Noizes Companion — MV3 service worker.
//
// Reports browser events to the Noizes tray app's local server:
//   POST http://127.0.0.1:7351/event/<event-id>
//   body: {"source":"chrome","url":"...","title":"..."}
//
// Events relayed/produced here:
//   browser-claude-done   — from claude-watcher.js (claude.ai response finished)
//   browser-obvious-done  — from obvious-watcher.js (Obvious tab finished)
//   browser-watch-done    — from watch.js ("Watch this tab" finished generating/loading)
//   browser-download-done — from chrome.downloads.onChanged (download completed)
//
// MV3 rules: no DOM access in the service worker; page work is done via
// chrome.scripting injection. Every chrome.* call is guarded — the tray app
// may be closed and tabs may disappear at any moment, so errors stay silent.

"use strict";

const SERVER_BASE = "http://127.0.0.1:7351";
const FETCH_TIMEOUT_MS = 3000;

// Only these event ids are accepted from content scripts.
const EVENT_IDS = new Set([
  "browser-claude-done",
  "browser-obvious-done",
  "browser-watch-done",
]);

const WATCHED_TABS_KEY = "watchedTabs";
const BADGE_TEXT = "N";
const BADGE_COLOR = "#16a34a"; // green

// Badge color is global per extension; the text is set/cleared per tab.
try {
  chrome.action.setBadgeBackgroundColor({ color: BADGE_COLOR });
} catch (_) {
  /* action API unavailable — nothing else depends on it */
}

chrome.runtime.onInstalled.addListener(() => {
  // Remove any previous menu first so reload/update cannot crash the worker
  // with a "duplicate id" error.
  try {
    chrome.contextMenus.removeAll(() => {
      chrome.contextMenus.create(
        {
          id: "noizes-watch",
          title: "Noizes: Watch this tab",
          contexts: ["all"],
        },
        () => void chrome.runtime.lastError // ignore create errors
      );
    });
  } catch (_) {
    /* ignore */
  }
});

// ---------------------------------------------------------------------------
// Watched-tab tracking (chrome.storage.session — survives service worker
// restarts within one browser session)
// ---------------------------------------------------------------------------

async function getWatchedTabs() {
  try {
    const data = await chrome.storage.session.get(WATCHED_TABS_KEY);
    const ids = data ? data[WATCHED_TABS_KEY] : null;
    return Array.isArray(ids) ? ids.filter((n) => typeof n === "number") : [];
  } catch (_) {
    return [];
  }
}

async function setWatchedTabs(ids) {
  try {
    await chrome.storage.session.set({ [WATCHED_TABS_KEY]: ids });
  } catch (_) {
    /* watch tracking degrades; event delivery keeps working */
  }
}

function setBadge(tabId, on) {
  try {
    const result = chrome.action.setBadgeText({ tabId, text: on ? BADGE_TEXT : "" });
    if (result && typeof result.catch === "function") result.catch(() => {});
  } catch (_) {
    /* tab may already be gone */
  }
}

async function addWatchedTab(tabId) {
  const ids = await getWatchedTabs();
  if (!ids.includes(tabId)) {
    ids.push(tabId);
    await setWatchedTabs(ids);
  }
  setBadge(tabId, true);
}

async function removeWatchedTab(tabId) {
  const ids = await getWatchedTabs();
  await setWatchedTabs(ids.filter((id) => id !== tabId));
  setBadge(tabId, false);
}

// ---------------------------------------------------------------------------
// Event delivery
// ---------------------------------------------------------------------------

// Fire-and-forget POST to the tray app. Swallows every error: the server is
// often simply not running (tray app closed) and that must stay silent.
async function postEvent(id, detail = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), FETCH_TIMEOUT_MS);
  try {
    await fetch(`${SERVER_BASE}/event/${encodeURIComponent(id)}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      // detail carries url/title (+ any extra fields) from the event source,
      // so the wire body is {source:"chrome", url, title, ...extras}.
      body: JSON.stringify({ source: "chrome", ...detail }),
      signal: controller.signal,
    });
    // Response body is intentionally ignored (fire-and-forget).
  } catch (_) {
    // Connection refused / timeout / abort — the tray app may be closed.
  } finally {
    clearTimeout(timer);
  }
}

// ---------------------------------------------------------------------------
// Messages from content scripts
// ---------------------------------------------------------------------------

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || typeof msg !== "object") return undefined;

  // Event relay: {type:"noizes-event", id, detail}
  if (msg.type === "noizes-event") {
    if (typeof msg.id === "string" && EVENT_IDS.has(msg.id)) {
      postEvent(msg.id, msg.detail && typeof msg.detail === "object" ? msg.detail : {});
    }
    sendResponse({ ok: true });
    return undefined;
  }

  // watch.js reports authoritative start/stop after every toggle and after
  // auto-stop: {type:"noizes-watch-state", watching: true|false}
  if (msg.type === "noizes-watch-state" && sender.tab && typeof sender.tab.id === "number") {
    if (msg.watching) {
      addWatchedTab(sender.tab.id);
    } else {
      removeWatchedTab(sender.tab.id);
    }
    sendResponse({ ok: true });
  }

  return undefined;
});

// ---------------------------------------------------------------------------
// "Watch this tab" toggle — toolbar action button and context menu
// ---------------------------------------------------------------------------

async function toggleWatch(tab) {
  if (!tab || typeof tab.id !== "number" || tab.id < 0) return;
  try {
    // watch.js is toggle-aware: if this tab is already watched it stops the
    // watcher, otherwise it starts one. It reports the resulting state via a
    // noizes-watch-state message, which updates storage + badge.
    await chrome.scripting.executeScript({
      files: ["watch.js"],
      target: { tabId: tab.id },
    });
  } catch (err) {
    // Injection fails on chrome:// pages, the Web Store, PDF viewer, etc.
    console.warn("Noizes: could not inject watch.js:", err && err.message ? err.message : err);
  }
}

chrome.action.onClicked.addListener((tab) => {
  toggleWatch(tab);
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "noizes-watch") toggleWatch(tab);
});

// A full page navigation destroys the injected watcher and its window flag —
// clear the badge (and the watched-tab entry) when a tab starts navigating.
chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (!changeInfo || changeInfo.status !== "loading") return;
  (async () => {
    const ids = await getWatchedTabs();
    if (ids.includes(tabId)) {
      await removeWatchedTab(tabId);
    } else {
      setBadge(tabId, false); // clear badge on navigation regardless
    }
  })();
});

// Closed tabs can no longer be watched.
chrome.tabs.onRemoved.addListener((tabId) => {
  (async () => {
    const ids = await getWatchedTabs();
    if (ids.includes(tabId)) await setWatchedTabs(ids.filter((id) => id !== tabId));
  })();
});

// ---------------------------------------------------------------------------
// Downloads
// ---------------------------------------------------------------------------

chrome.downloads.onChanged.addListener((delta) => {
  if (!delta || !delta.state || delta.state.current !== "complete") return;
  try {
    // Callback form + lastError check: the download record can vanish before
    // we search for it.
    chrome.downloads.search({ id: delta.id }, (results) => {
      if (chrome.runtime.lastError) return;
      const item = Array.isArray(results) ? results.find((d) => d.id === delta.id) : null;
      if (!item) return;
      // DownloadItem.filename is an absolute local path; report just the name.
      const filename =
        typeof item.filename === "string" ? item.filename.split(/[\\/]/).pop() : "";
      postEvent("browser-download-done", {
        url: item.finalUrl || item.url || "",
        title: filename,
      });
    });
  } catch (_) {
    /* ignore */
  }
});
