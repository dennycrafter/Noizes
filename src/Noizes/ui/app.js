/*
  Noizes UI shell: bootstrap, WebView2 bridge, shared state, and shared components.

  Bridge contract (UiBridge.cs and the test mock must match this):
  - Page to app: postMessage({ id: <number>, type: "<command>", ...payload }).
  - Reply, app to page: { id, ok, data?, error? }. Matched to the request by id.
  - Push, app to page: { type: "state", data: { ...state } }. A push always carries
    the whole state and replaces what the page renders.
  - The GitHub token never travels to the page. State carries tokenSet: true or false.

  State shape (camelCase JSON, produced by the app):
  {
    version: "1.3.0",
    muted: false,
    mutedUntil: null,                       // ISO string or null
    events: [{ id, name, group, enabled,    // volume 0 to 100
               volume, soundLabel, soundPath,
               focusApps: [] }],            // soundPath "" means the default sound
    groups: ["Coding & AI", "GitHub", "Apps", "Browser", "Deploys", "Extras"],
    connections: { claudeCode: { connected }, cursor: { connected },
                   claudeDesktop: { enabled }, chrome: { connected },
                   github: { connected, tokenSet, username, pollSeconds, repos: [] },
                   deploys: { connected }, uptime: { count } },
    quiet: { enabled, start, end, allowAlarms },
    uptimeUrls: [{ url, status }],          // status: "up", "down" or null
    countdown: { enabled, targetLocal },
    general: { startWithWindows, onlyWhenUnfocused, port, portRestartNeeded }
  }

  Page modules live in pages/, register with NZ.registerPage, and render into #page.
  See pages/README.md for the full contract.
*/

(function () {
  "use strict";

  var NZ = (window.NZ = window.NZ || {});

  var NAV = [
    { id: "sounds", label: "Sounds", icon: "sounds" },
    { id: "connections", label: "Connections", icon: "connections" },
    { id: "schedule", label: "Schedule", icon: "schedule" },
    { id: "general", label: "General", icon: "general" }
  ];

  var pages = {};
  var activePageId = null;
  var activePageDestroy = null;
  var pageNeedsRender = true;
  var state = null;
  var reqId = 0;
  var pendingReplies = new Map();
  var stateListeners = [];
  var escapeHandlers = [];
  var panelState = null;
  var els = {};
  var muteRecheckTimer = 0;

  /* ----- Bridge ----- */

  function webview() {
    var wv = window.chrome && window.chrome.webview;
    return wv && typeof wv.postMessage === "function" ? wv : null;
  }

  function request(payload) {
    var wv = webview();
    if (!wv) {
      return Promise.resolve({ ok: false, error: "The Noizes app host is not available." });
    }
    reqId += 1;
    var id = reqId;
    var msg = Object.assign({ id: id }, payload);
    window.__noizesLastRequest = { id: id, type: payload && payload.type }; // evidence trail
    return new Promise(function (resolve) {
      pendingReplies.set(id, resolve);
      wv.postMessage(msg);
      window.setTimeout(function () {
        if (pendingReplies.has(id)) {
          pendingReplies.delete(id);
          window.__noizesLastResult = { id: id, timedOut: true }; // evidence trail
          resolve({ id: id, ok: false, error: "The app did not answer this request." });
        }
      }, 5000);
    });
  }

  function onMessage(event) {
    var msg = event && event.data;
    if (!msg || typeof msg !== "object") return;
    if (msg.type === "state") {
      if (msg.data !== undefined && msg.data !== null) {
        applyState(msg.data);
      } else if (msg.state !== undefined && msg.state !== null) {
        applyState(msg.state);
      }
      return;
    }
    if (msg.id !== undefined && msg.id !== null && typeof msg.ok === "boolean") {
      var resolve = pendingReplies.get(msg.id);
      if (resolve) {
        pendingReplies.delete(msg.id);
        window.__noizesLastResult = { id: msg.id, ok: msg.ok }; // evidence trail
        resolve(msg);
      }
    }
  }

  function attachListener() {
    var wv = webview();
    if (wv && typeof wv.addEventListener === "function") {
      wv.addEventListener("message", onMessage);
    }
  }

  /* Every message from section 2 of the brief, as a promise returning the reply. */

  var api = {
    // flat escape hatch for page modules that build their own messages
    request: function (fields) { return request(fields); },
    getState: function () { return request({ type: "getState" }); },
    setEvent: function (fields) { return request(Object.assign({ type: "setEvent" }, fields)); },
    setGroup: function (group, enabled) { return request({ type: "setGroup", group: group, enabled: enabled }); },
    setMute: function (muted, minutes) { return request({ type: "setMute", muted: muted, minutes: minutes }); },
    testSound: function (eventId) { return request({ type: "testSound", eventId: eventId }); },
    chooseSound: function (eventId) { return request({ type: "chooseSound", eventId: eventId }); },
    resetSound: function (eventId) { return request({ type: "resetSound", eventId: eventId }); },
    setupClaudeCode: function () { return request({ type: "setupClaudeCode" }); },
    setupCursor: function () { return request({ type: "setupCursor" }); },
    saveGitHub: function (fields) { return request(Object.assign({ type: "saveGitHub" }, fields)); },
    testGitHub: function () { return request({ type: "testGitHub" }); },
    setDesktopWatcher: function (enabled) { return request({ type: "setDesktopWatcher", enabled: enabled }); },
    setGeneral: function (fields) { return request(Object.assign({ type: "setGeneral" }, fields)); },
    setQuietHours: function (fields) { return request(Object.assign({ type: "setQuietHours" }, fields)); },
    setUptime: function (urls) { return request({ type: "setUptime", urls: urls }); },
    setCountdown: function (fields) { return request(Object.assign({ type: "setCountdown" }, fields)); },
    openLog: function () { return request({ type: "openLog" }); },
    openFolder: function (which) { return request({ type: "openFolder", which: which }); },
    openUrl: function (url) { return request({ type: "openUrl", url: url }); }
  };

  NZ.api = api;
  NZ.request = request;

  /* ----- State ----- */

  function applyState(next) {
    state = next;
    NZ.state = state;
    renderHeader();
    renderVersion();
    stateListeners.forEach(function (fn) { fn(state); });
    var page = pages[activePageId];
    if (!page) return;
    if (pageNeedsRender) { renderActivePage(); return; }
    if (typeof page.update === "function") {
      page.update(state, pageCtx());
    } else {
      renderActivePage();
    }
  }

  NZ.onState = function (fn) { stateListeners.push(fn); };

  /* ----- Header: master switch, mute button, muted until ----- */

  function formatTime(ms) {
    return new Date(ms).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
  }

  function renderHeader() {
    if (!state || !els.masterSwitch) return;
    var muted = !!state.muted;
    var untilMs = muted && state.mutedUntil ? Date.parse(state.mutedUntil) : NaN;
    var mutedForTime = !isNaN(untilMs) && untilMs > Date.now();
    els.masterSwitch.setAttribute("aria-checked", String(!muted));
    els.masterLabel.textContent = muted ? "Sounds off" : "Sounds on";
    if (mutedForTime) {
      els.mutedUntil.hidden = false;
      els.mutedUntil.textContent = "Muted until " + formatTime(untilMs);
      els.muteBtn.textContent = "Unmute";
    } else {
      els.mutedUntil.hidden = true;
      els.mutedUntil.textContent = "";
      els.muteBtn.textContent = "Mute 1 hour";
    }
    if (muteRecheckTimer) {
      window.clearTimeout(muteRecheckTimer);
      muteRecheckTimer = 0;
    }
    if (mutedForTime) {
      muteRecheckTimer = window.setTimeout(function () {
        muteRecheckTimer = 0;
        renderHeader();
      }, untilMs - Date.now() + 250);
    }
  }

  function renderVersion() {
    var v = state && state.version ? String(state.version) : "1.3.0";
    if (v.charAt(0) !== "v") v = "v" + v;
    els.version.textContent = v;
  }

  /* ----- Toasts: bottom center, auto hide after 3s ----- */

  NZ.toast = function (message, kind) {
    var el = document.createElement("div");
    el.className = "nz-toast" + (kind ? " nz-toast-" + kind : "");
    if (kind) {
      var dot = document.createElement("span");
      dot.className = "nz-toast-dot";
      el.appendChild(dot);
    }
    el.appendChild(document.createTextNode(String(message)));
    els.toasts.appendChild(el);
    while (els.toasts.children.length > 3) {
      els.toasts.removeChild(els.toasts.firstChild);
    }
    window.setTimeout(function () {
      el.classList.add("nz-toast-out");
      window.setTimeout(function () { el.remove(); }, 250);
    }, 3000);
  };

  /* ----- Toggle switch: role switch, aria-checked, Space operable ----- */

  NZ.toggle = function (opts) {
    var b = document.createElement("button");
    b.type = "button";
    b.className = "nz-switch";
    b.setAttribute("role", "switch");
    b.setAttribute("aria-checked", String(!!(opts && opts.checked)));
    if (opts && opts.label) b.setAttribute("aria-label", opts.label);
    if (opts && opts.labelledBy) b.setAttribute("aria-labelledby", opts.labelledBy);
    var thumb = document.createElement("span");
    thumb.className = "nz-switch-thumb";
    b.appendChild(thumb);
    b.addEventListener("click", function () {
      var next = b.getAttribute("aria-checked") !== "true";
      b.setAttribute("aria-checked", String(next));
      if (opts && typeof opts.onchange === "function") opts.onchange(next, b);
    });
    return b;
  };

  /* ----- Floating panel: closes on Escape, X, or click outside ----- */

  NZ.closePanel = function () {
    if (!panelState) return;
    var ps = panelState;
    panelState = null;
    document.removeEventListener("keydown", ps.onKeydown, true);
    ps.overlay.remove();
    if (ps.restoreFocus && typeof ps.restoreFocus.focus === "function") {
      ps.restoreFocus.focus();
    }
  };

  NZ.panel = function (opts) {
    NZ.closePanel();
    var overlay = document.createElement("div");
    overlay.className = "nz-overlay";
    var panel = document.createElement("div");
    panel.className = "nz-panel";
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-modal", "true");
    if (opts && opts.title) panel.setAttribute("aria-label", opts.title);
    panel.tabIndex = -1;

    var head = document.createElement("div");
    head.className = "nz-panel-head";
    var title = document.createElement("h2");
    title.className = "nz-panel-title";
    title.textContent = (opts && opts.title) || "";
    var closeBtn = document.createElement("button");
    closeBtn.type = "button";
    closeBtn.className = "nz-iconbtn";
    closeBtn.setAttribute("aria-label", "Close");
    closeBtn.appendChild(NZ.icon("close"));
    head.appendChild(title);
    head.appendChild(closeBtn);

    var body = document.createElement("div");
    body.className = "nz-panel-body";
    panel.appendChild(head);
    panel.appendChild(body);
    overlay.appendChild(panel);

    function onKeydown(e) {
      if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        NZ.closePanel();
      }
    }

    overlay.addEventListener("mousedown", function (e) {
      if (e.target === overlay) NZ.closePanel();
    });
    closeBtn.addEventListener("click", function () { NZ.closePanel(); });
    document.addEventListener("keydown", onKeydown, true);

    document.body.appendChild(overlay);
    panelState = { overlay: overlay, onKeydown: onKeydown, restoreFocus: document.activeElement };
    panel.focus();
    if (opts && typeof opts.build === "function") {
      opts.build(body, { close: NZ.closePanel });
    }
    return { close: NZ.closePanel, body: body };
  };

  /* ----- Inline SVG icons (no web fonts, no CDN) ----- */

  var ICONS = {
    sounds: [
      ["path", { d: "M11 5 6 9H3v6h3l5 4z" }],
      ["path", { d: "M15.5 8.5a5 5 0 0 1 0 7" }],
      ["path", { d: "M18 6a8.5 8.5 0 0 1 0 12" }]
    ],
    connections: [
      ["path", { d: "M9 7V3" }],
      ["path", { d: "M15 7V3" }],
      ["path", { d: "M7 7h10v4a5 5 0 0 1-10 0z" }],
      ["path", { d: "M12 16v5" }]
    ],
    schedule: [
      ["circle", { cx: 12, cy: 12, r: 8.5 }],
      ["path", { d: "M12 7.5V12l3 2" }]
    ],
    general: [
      ["path", { d: "M4 7h16" }],
      ["path", { d: "M4 12h16" }],
      ["path", { d: "M4 17h16" }],
      ["circle", { cx: 9, cy: 7, r: 1.6, fill: "currentColor", stroke: "none" }],
      ["circle", { cx: 15, cy: 12, r: 1.6, fill: "currentColor", stroke: "none" }],
      ["circle", { cx: 7, cy: 17, r: 1.6, fill: "currentColor", stroke: "none" }]
    ],
    close: [
      ["path", { d: "M6 6l12 12" }],
      ["path", { d: "M18 6L6 18" }]
    ],
    play: [
      ["path", { d: "M8 5.5v13l10.5-6.5z", fill: "currentColor", stroke: "none" }]
    ],
    trash: [
      ["path", { d: "M4 7h16" }],
      ["path", { d: "M9 7V4h6v3" }],
      ["path", { d: "M6.5 7l1 13h9l1-13" }]
    ],
    plus: [
      ["path", { d: "M12 5v14" }],
      ["path", { d: "M5 12h14" }]
    ],
    copy: [
      ["rect", { x: 9, y: 9, width: 11, height: 11, rx: 2 }],
      ["path", { d: "M5 15V5a1 1 0 0 1 1-1h10" }]
    ],
    external: [
      ["path", { d: "M14 4h6v6" }],
      ["path", { d: "M20 4L11 13" }],
      ["path", { d: "M19 14v5a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h5" }]
    ],
    log: [
      ["rect", { x: 5, y: 3, width: 14, height: 18, rx: 2 }],
      ["path", { d: "M9 8h6" }],
      ["path", { d: "M9 12h6" }],
      ["path", { d: "M9 16h4" }]
    ]
  };

  var SVG_NS = "http://www.w3.org/2000/svg";

  NZ.icon = function (name, size) {
    var svg = document.createElementNS(SVG_NS, "svg");
    svg.setAttribute("viewBox", "0 0 24 24");
    svg.setAttribute("width", size || 20);
    svg.setAttribute("height", size || 20);
    svg.setAttribute("aria-hidden", "true");
    svg.setAttribute("focusable", "false");
    var parts = ICONS[name] || [];
    parts.forEach(function (part) {
      var el = document.createElementNS(SVG_NS, part[0]);
      var attrs = part[1] || {};
      Object.keys(attrs).forEach(function (key) {
        el.setAttribute(key, attrs[key]);
      });
      if (!attrs.fill) el.setAttribute("fill", "none");
      if (attrs.stroke !== "none") {
        el.setAttribute("stroke", attrs.stroke || "currentColor");
        el.setAttribute("stroke-width", attrs["stroke-width"] || "1.8");
        el.setAttribute("stroke-linecap", "round");
        el.setAttribute("stroke-linejoin", "round");
      }
      svg.appendChild(el);
    });
    return svg;
  };

  /* ----- Escape routing: the panel first, then the active page ----- */

  NZ.onEscape = function (fn) {
    escapeHandlers.push(fn);
    return function () {
      var i = escapeHandlers.indexOf(fn);
      if (i >= 0) escapeHandlers.splice(i, 1);
    };
  };

  document.addEventListener("keydown", function (e) {
    if (e.key !== "Escape") return;
    if (panelState) return;
    for (var i = escapeHandlers.length - 1; i >= 0; i--) escapeHandlers[i]();
  });

  /* ----- Page registry and the #page container swap ----- */

  NZ.registerPage = function (page) {
    if (!page || !page.id) return;
    pages[page.id] = page;
    if (activePageId === page.id && els.page) {
      pageNeedsRender = true;
      renderActivePage();
    }
  };

  function pageCtx() {
    return {
      state: state,
      api: api,
      toast: NZ.toast,
      panel: NZ.panel,
      closePanel: NZ.closePanel,
      toggle: NZ.toggle,
      icon: NZ.icon,
      onEscape: NZ.onEscape
    };
  }

  function renderActivePage() {
    var page = pages[activePageId];
    els.page.textContent = "";
    if (!page) {
      var p = document.createElement("p");
      p.className = "nz-page-missing";
      p.textContent = "This page module did not load.";
      els.page.appendChild(p);
      return;
    }
    if (!state) {
      /* Pages get a real state on their first render: the shell shows a
         loading line until the first push or reply arrives. */
      var loading = document.createElement("p");
      loading.className = "nz-page-missing";
      loading.textContent = "Loading settings.";
      els.page.appendChild(loading);
      pageNeedsRender = true;
      return;
    }
    try {
      /* A page may return a destroy function (unsubscribes listeners and
         timers). Run it before any re-render so a page that has been swapped
         out never keeps live listeners, then take the next one if given. */
      if (typeof activePageDestroy === "function") activePageDestroy();
      activePageDestroy = null;
      var maybeDestroy = page.render(els.page, pageCtx());
      if (typeof maybeDestroy === "function") activePageDestroy = maybeDestroy;
      pageNeedsRender = false;
    } catch (err) {
      console.error(err);
      els.page.textContent = "";
      var errEl = document.createElement("p");
      errEl.className = "nz-page-error";
      errEl.textContent = "This page failed to render. " + err.message;
      els.page.appendChild(errEl);
    }
  }

  /* ----- Navigation ----- */

  function pageFromHash() {
    var id = (location.hash || "").replace(/^#/, "");
    return NAV.some(function (n) { return n.id === id; }) ? id : "sounds";
  }

  function setActive(id) {
    activePageId = id;
    Array.prototype.forEach.call(els.nav.children, function (b) {
      if (b.dataset.page === id) b.setAttribute("aria-current", "page");
      else b.removeAttribute("aria-current");
    });
    var def = NAV.filter(function (n) { return n.id === id; })[0];
    els.pageTitle.textContent = def ? def.label : "";
    renderActivePage();
    els.page.focus({ preventScroll: true });
  }

  function activate(id) {
    if (location.hash !== "#" + id) location.hash = id;
    setActive(id);
  }

  window.addEventListener("hashchange", function () {
    var id = pageFromHash();
    if (id !== activePageId) setActive(id);
  });

  function buildNav() {
    els.nav.textContent = "";
    NAV.forEach(function (item) {
      var b = document.createElement("button");
      b.type = "button";
      b.className = "nz-nav-btn";
      b.dataset.page = item.id;
      b.appendChild(NZ.icon(item.icon));
      var label = document.createElement("span");
      label.textContent = item.label;
      b.appendChild(label);
      b.addEventListener("click", function () { activate(item.id); });
      els.nav.appendChild(b);
    });
  }

  /* ----- Init ----- */

  function init() {
    els.nav = document.getElementById("nav");
    els.version = document.getElementById("version");
    els.pageTitle = document.getElementById("pageTitle");
    els.masterLabel = document.getElementById("masterLabel");
    els.masterSwitchHost = document.getElementById("masterSwitchHost");
    els.mutedUntil = document.getElementById("mutedUntil");
    els.muteBtn = document.getElementById("muteBtn");
    els.page = document.getElementById("page");
    els.toasts = document.getElementById("toasts");
    els.openLogBtn = document.getElementById("openLogBtn");

    buildNav();
    attachListener();

    els.masterSwitch = NZ.toggle({
      checked: true,
      labelledBy: "masterLabel",
      onchange: function (on) { api.setMute(!on); }
    });
    els.masterSwitchHost.appendChild(els.masterSwitch);

    els.muteBtn.addEventListener("click", function () {
      if (state && state.muted) api.setMute(false);
      else api.setMute(true, 60);
    });

    els.openLogBtn.addEventListener("click", function () { api.openLog(); });

    activate(pageFromHash());

    api.getState().then(function (reply) {
      if (reply && reply.ok && reply.data) {
        applyState(reply.data);
      } else if (reply && !reply.ok) {
        NZ.toast(reply.error || "Could not load the settings.", "bad");
      }
    });
  }

  /* When app.js runs as a defer script, the page modules after it in index.html
     run in the same batch before timers fire. The timeout defers init past that
     batch so every page has registered. */
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    window.setTimeout(init, 0);
  }
})();
