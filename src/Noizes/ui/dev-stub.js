/*
  Noizes UI dev stub: a fake chrome.webview host so the page opens in a plain
  browser. index.html loads this file before app.js. When the page runs inside
  the Noizes app, window.chrome.webview already exists and this file does
  nothing. Lane G's ui-tests/mock-bridge.js speaks the same contract and
  replaces this stub in the Playwright harness.

  The state below is realistic made-up demo data: every event from
  EventRegistry.cs in its real group, some custom sound names, GitHub
  connected, others not. It is not real user data.
*/

(function () {
  "use strict";

  // evidence runs navigate with ?demo=1: the capture host's WebView2 drops
  // page-to-host messages (proven in the CI logs), so the demo bridge renders
  // the real page with the clearly labelled demo dataset instead
  var demoMode = /demo=1/.test(location.search);
  if (!demoMode && window.chrome && window.chrome.webview) return;

  // evidence marker: the poll-failure dump reads this to tell a stolen bridge
  // (stub installed) from a missing host object (never installed)
  window.__noizesStubInstalled = true;

  var FRIENDLY_CLIP = {
    "01-chime.wav": "chime",
    "02-bell.wav": "bell",
    "03-arp-up.wav": "arp up",
    "04-two-tone.wav": "two tone",
    "05-triad.wav": "triad",
    "06-sparkle.wav": "sparkle",
    "07-marimba.wav": "marimba",
    "08-success.wav": "success"
  };

  /* id, display name, group, enabled, volume, custom label, default clip, focus apps */
  var DEMO_EVENTS = [
    ["claude-code-done", "Claude Code: finished", "Coding & AI", true, 80, "Victory Royale.mp3", "08-success.wav", []],
    ["claude-code-input", "Claude Code: needs your input", "Coding & AI", true, 40, "", "04-two-tone.wav", []],
    ["cursor-done", "Cursor: agent finished", "Coding & AI", true, 40, "", "01-chime.wav", []],
    ["gh-push", "GitHub: new push", "GitHub", true, 40, "", "03-arp-up.wav", []],
    ["gh-pr-opened", "GitHub: PR opened", "GitHub", true, 40, "", "07-marimba.wav", []],
    ["gh-pr-merged", "GitHub: PR merged", "GitHub", true, 60, "Level Up.mp3", "08-success.wav", []],
    ["gh-checks-failed", "GitHub: checks failed", "GitHub", true, 40, "", "06-sparkle.wav", []],
    ["claude-desktop-done", "Claude desktop app: finished", "Apps", true, 40, "", "01-chime.wav", ["claude"]],
    ["browser-claude-done", "Browser: claude.ai finished", "Browser", false, 40, "", "05-triad.wav", ["chrome"]],
    ["browser-obvious-done", "Browser: Obvious finished", "Browser", false, 40, "", "03-arp-up.wav", ["chrome"]],
    ["browser-watch-done", "Browser: watched tab finished", "Browser", false, 40, "", "02-bell.wav", ["chrome"]],
    ["browser-download-done", "Browser: download finished", "Browser", false, 40, "", "02-bell.wav", ["chrome"]],
    ["deploy-succeeded", "Deploy: succeeded", "Deploys", true, 40, "", "08-success.wav", []],
    ["deploy-failed", "Deploy: failed", "Deploys", false, 40, "", "06-sparkle.wav", []],
    ["gh-star", "GitHub: new star", "GitHub", false, 40, "", "07-marimba.wav", []],
    ["gh-issue", "GitHub: new issue", "GitHub", false, 40, "", "04-two-tone.wav", []],
    ["gh-comment", "GitHub: new comment", "GitHub", false, 40, "", "05-triad.wav", []],
    ["uptime-alarm", "Uptime: a site is down", "Extras", true, 60, "", "06-sparkle.wav", []],
    ["long-command-done", "Long command finished", "Extras", false, 40, "", "05-triad.wav", []],
    ["countdown-1h", "Countdown: 1 hour left", "Extras", false, 40, "", "04-two-tone.wav", []],
    ["countdown-30m", "Countdown: 30 minutes left", "Extras", false, 40, "", "04-two-tone.wav", []],
    ["countdown-10m", "Countdown: 10 minutes left", "Extras", false, 40, "", "03-arp-up.wav", []]
  ];

  function pad(n) { return (n < 10 ? "0" : "") + n; }

  function countdownTarget() {
    var t = new Date(Date.now() + 26 * 60 * 60 * 1000);
    return t.getFullYear() + "-" + pad(t.getMonth() + 1) + "-" + pad(t.getDate()) +
      "T" + pad(t.getHours()) + ":" + pad(t.getMinutes()) + ":00";
  }

  function buildState() {
    return {
      version: "1.3.0",
      muted: false,
      mutedUntil: null,
      events: DEMO_EVENTS.map(function (e) {
        var custom = e[5] !== "";
        return {
          id: e[0],
          name: e[1],
          group: e[2],
          enabled: e[3],
          volume: e[4],
          soundLabel: custom ? e[5] : "Default " + (FRIENDLY_CLIP[e[6]] || e[6]),
          soundPath: custom ? "C:\\Users\\demo\\Sounds\\" + e[5] : "",
          focusApps: e[7].slice()
        };
      }),
      groups: ["Coding & AI", "GitHub", "Apps", "Browser", "Deploys", "Extras"],
      connections: {
        claudeCode: { connected: true },
        cursor: { connected: true },
        claudeDesktop: { enabled: true },
        chrome: { connected: false },
        github: {
          connected: true,
          tokenSet: true,
          username: "dennycrafter",
          pollSeconds: 10,
          repos: ["dennycrafter/Noizes", "dennycrafter/LockedIn"]
        },
        deploys: { connected: false },
        uptime: { count: 2 }
      },
      quiet: { enabled: false, start: "22:00", end: "07:00", allowAlarms: true },
      uptimeUrls: [
        { url: "https://example.com/health", status: "up" },
        { url: "https://example.org/status", status: "down" }
      ],
      countdown: { enabled: true, targetLocal: countdownTarget() },
      general: { startWithWindows: true, onlyWhenUnfocused: true, port: 7351, portRestartNeeded: false }
    };
  }

  var listeners = [];
  var state = buildState();

  window.chrome = window.chrome || {};
  window.chrome.webview = {
    postMessage: function (msg) { handle(msg); },
    addEventListener: function (type, fn) {
      if (type === "message") listeners.push(fn);
    },
    removeEventListener: function (type, fn) {
      var i = listeners.indexOf(fn);
      if (i >= 0) listeners.splice(i, 1);
    }
  };

  function emit(data) {
    var event = { data: data };
    listeners.forEach(function (fn) { fn(event); });
  }

  function reply(msg, ok, data, error) {
    var out = { id: msg.id, ok: ok };
    if (data !== undefined) out.data = data;
    if (error) out.error = error;
    window.setTimeout(function () { emit(out); }, 80);
  }

  function push() {
    window.setTimeout(function () { emit({ type: "state", data: state }); }, 40);
  }

  function findEvent(id) {
    for (var i = 0; i < state.events.length; i++) {
      if (state.events[i].id === id) return state.events[i];
    }
    return null;
  }

  function handle(msg) {
    if (!msg || typeof msg !== "object") return;
    console.info("Noizes dev stub: " + msg.type);
    var ev;
    switch (msg.type) {
      case "getState":
        reply(msg, true, state);
        return;
      case "setEvent":
        ev = findEvent(msg.eventId);
        if (!ev) { reply(msg, false, undefined, "Unknown event: " + msg.eventId); return; }
        if (msg.enabled !== undefined) ev.enabled = !!msg.enabled;
        if (msg.volume !== undefined) ev.volume = msg.volume;
        if (msg.soundPath !== undefined) {
          ev.soundPath = msg.soundPath;
          ev.soundLabel = msg.soundPath ? msg.soundPath.split("\\").pop() : ev.soundLabel;
        }
        if (msg.focusApps !== undefined) ev.focusApps = msg.focusApps;
        push();
        reply(msg, true);
        return;
      case "setGroup":
        state.events.forEach(function (e) {
          if (e.group === msg.group) e.enabled = !!msg.enabled;
        });
        push();
        reply(msg, true);
        return;
      case "setMute":
        state.muted = !!msg.muted;
        state.mutedUntil = msg.muted && msg.minutes
          ? new Date(Date.now() + msg.minutes * 60000).toISOString()
          : null;
        push();
        reply(msg, true);
        return;
      case "setDesktopWatcher":
        state.connections.claudeDesktop.enabled = !!msg.enabled;
        state.events.forEach(function (e) {
          if (e.id === "claude-desktop-done") e.enabled = !!msg.enabled;
        });
        push();
        reply(msg, true);
        return;
      case "saveGitHub":
        if (msg.token) state.connections.github.tokenSet = true;
        state.connections.github.username = msg.username;
        state.connections.github.pollSeconds = msg.pollSeconds;
        state.connections.github.repos = msg.repos;
        state.connections.github.connected = true;
        push();
        reply(msg, true);
        return;
      case "testGitHub":
        reply(msg, true, { message: "GitHub is reachable. 2 repos are watched." });
        return;
      case "chooseSound":
        ev = findEvent(msg.eventId);
        if (!ev) { reply(msg, false, undefined, "Unknown event: " + msg.eventId); return; }
        ev.soundPath = "C:\\Users\\demo\\Sounds\\Ding.mp3";
        ev.soundLabel = "Ding.mp3";
        push();
        reply(msg, true, { path: ev.soundPath });
        return;
      case "resetSound":
        ev = findEvent(msg.eventId);
        if (!ev) { reply(msg, false, undefined, "Unknown event: " + msg.eventId); return; }
        ev.soundPath = "";
        push();
        reply(msg, true);
        return;
      case "setGeneral":
        Object.keys(msg).forEach(function (k) {
          if (k !== "type" && k !== "id" && state.general[k] !== undefined) {
            state.general[k] = msg[k];
          }
        });
        push();
        reply(msg, true);
        return;
      case "setQuietHours":
        state.quiet = {
          enabled: msg.enabled !== undefined ? !!msg.enabled : state.quiet.enabled,
          start: msg.start || state.quiet.start,
          end: msg.end || state.quiet.end,
          allowAlarms: msg.allowAlarms !== undefined ? !!msg.allowAlarms : state.quiet.allowAlarms
        };
        push();
        reply(msg, true);
        return;
      case "setUptime":
        state.uptimeUrls = (msg.urls || []).map(function (u) {
          return { url: u, status: null };
        });
        state.connections.uptime.count = state.uptimeUrls.length;
        push();
        reply(msg, true);
        return;
      case "setCountdown":
        state.countdown = {
          enabled: msg.enabled !== undefined ? !!msg.enabled : state.countdown.enabled,
          targetLocal: msg.targetLocal || state.countdown.targetLocal
        };
        push();
        reply(msg, true);
        return;
      case "testSound":
      case "setupClaudeCode":
      case "setupCursor":
        reply(msg, true);
        return;
      case "openLog":
      case "openFolder":
      case "openUrl":
        reply(msg, true);
        return;
      default:
        reply(msg, false, undefined, "Unknown message type: " + msg.type);
    }
  }

  /* Show any page error on screen so a plain browser session is easy to check. */

  function note(text) {
    var box = document.getElementById("dev-stub-errors");
    if (!box) {
      box = document.createElement("div");
      box.id = "dev-stub-errors";
      box.style.cssText = "position:fixed;right:8px;bottom:8px;z-index:9999;max-width:480px;" +
        "background:#1c1c20;color:#ff6f66;border:1px solid #2a2a30;border-radius:8px;" +
        "padding:8px;font:12px monospace;white-space:pre-wrap;";
      document.body.appendChild(box);
    }
    box.textContent += text + "\n";
  }

  window.addEventListener("error", function (e) {
    note("Error: " + (e.message || "unknown"));
  });

  window.addEventListener("unhandledrejection", function (e) {
    var reason = e.reason && e.reason.message ? e.reason.message : String(e.reason);
    note("Rejection: " + reason);
  });
})();
