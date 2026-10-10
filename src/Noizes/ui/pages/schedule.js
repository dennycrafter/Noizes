// Noizes UI, Schedule page (lane F). Plain JS, no framework, no build step.
//
// Contract with the shell (lane D): this file registers
//   window.Pages.schedule = { id, label, render(host), onHide(), onShow() }
// and fires the 'noizes:page-registered' event after registering. The shell builds the
// sidebar from window.Pages and calls render(container) when the page is shown; onHide
// lets the page stop its countdown ticker when the shell swaps pages (optional for the shell).
//
// Bridge (section 2 of the brief): the page sends window.chrome.webview.postMessage({id, type, ...payload});
// the app replies {id, ok, data?, error?} and pushes {type: 'state', state} when anything
// changes outside the page. Messages used here: getState, setQuietHours {enabled, start, end,
// allowAlarms}, setUptime {urls}, setCountdown {enabled, targetLocal}.
//
// Expected state fields (lane B maps config.json; camelCase, all optional, defaults below):
//   quiet {enabled, start "HH:MM", end "HH:MM", allowAlarms}, uptimeUrls [string],
//   uptimeStatus {url: 'up' | 'down', booleans tolerated}, countdown {enabled, targetLocal}.
// Quiet fields map to QuietHoursConfig (Start, End, AllowAlarms); allowAlarms is the
// "Still play the uptime alarm during quiet hours" switch.

(function () {
  'use strict';

  // ---- shared lane F helpers: keep this block identical in every ui/pages file ----

  var NZF_CSS =
    '.nzf-box{background:var(--surface,#121215);border:1px solid var(--line,#2a2a30);border-radius:8px;padding:4px 0;margin:0 0 16px}' +
    '.nzf-box-title{font-size:13px;color:var(--muted,#9a9aa3);padding:12px 16px 4px}' +
    '.nzf-row{display:flex;align-items:center;gap:12px;padding:12px 16px;min-height:56px;box-sizing:border-box;flex-wrap:wrap}' +
    '.nzf-row+.nzf-row{border-top:1px solid var(--line,#2a2a30)}' +
    '.nzf-row:hover{background:var(--surface-2,#1c1c20)}' +
    '.nzf-main{flex:1 1 220px;min-width:200px}' +
    '.nzf-name{font-weight:600;color:var(--fg,#f2f2f4)}' +
    '.nzf-sub{color:var(--muted,#9a9aa3);font-size:13px;margin-top:2px}' +
    '.nzf-end{margin-left:auto;display:flex;align-items:center;gap:12px}' +
    '.nzf-inline{display:flex;align-items:center;gap:10px;flex-wrap:wrap}' +
    '.nzf-btn{background:var(--surface-2,#1c1c20);color:var(--fg,#f2f2f4);border:1px solid var(--line,#2a2a30);border-radius:6px;padding:7px 14px;font:inherit;font-size:14px;white-space:nowrap;cursor:pointer}' +
    '.nzf-btn:hover{border-color:var(--muted,#9a9aa3)}' +
    '.nzf-btn:focus-visible{outline:2px solid var(--info,#6ab8ff);outline-offset:2px}' +
    '.nzf-btn-accent{background:var(--accent,#7c5cff);border-color:var(--accent,#7c5cff);color:#fff}' +
    '.nzf-btn-accent:hover{background:var(--accent-ink,#9d85ff);border-color:var(--accent-ink,#9d85ff)}' +
    '.nzf-icon-btn{background:transparent;border:none;color:var(--muted,#9a9aa3);cursor:pointer;padding:6px;border-radius:6px;display:inline-flex;align-items:center}' +
    '.nzf-icon-btn:hover{color:var(--fg,#f2f2f4);background:var(--surface-2,#1c1c20)}' +
    '.nzf-icon-btn:focus-visible{outline:2px solid var(--info,#6ab8ff);outline-offset:2px}' +
    '.nzf-status{display:inline-flex;align-items:center;gap:8px;font-size:13px;color:var(--muted,#9a9aa3);white-space:nowrap}' +
    '.nzf-dot{width:8px;height:8px;border-radius:50%;background:var(--muted,#9a9aa3);flex:none}' +
    '.nzf-dot.nzf-ok{background:var(--ok,#34d65c)}' +
    '.nzf-dot.nzf-warn{background:var(--warn,#ffb547)}' +
    '.nzf-dot.nzf-bad{background:var(--bad,#ff6f66)}' +
    '.nzf-input{background:var(--bg,#0b0b0d);color:var(--fg,#f2f2f4);border:1px solid var(--line,#2a2a30);border-radius:6px;padding:7px 10px;font:inherit;font-size:14px}' +
    '.nzf-input:focus-visible{outline:2px solid var(--info,#6ab8ff);outline-offset:1px}' +
    '.nzf-switch{width:36px;height:20px;border-radius:10px;background:var(--surface-2,#1c1c20);border:1px solid var(--line,#2a2a30);position:relative;cursor:pointer;padding:0;flex:none;transition:background .15s ease,border-color .15s ease}' +
    '.nzf-switch .nzf-knob{position:absolute;top:2px;left:2px;width:16px;height:16px;border-radius:50%;background:var(--muted,#9a9aa3);transition:transform .15s ease,background .15s ease}' +
    '.nzf-switch.nzf-on{background:var(--accent,#7c5cff);border-color:var(--accent,#7c5cff)}' +
    '.nzf-switch.nzf-on .nzf-knob{transform:translateX(16px);background:#fff}' +
    '.nzf-switch:focus-visible{outline:2px solid var(--info,#6ab8ff);outline-offset:2px}' +
    '.nzf-field{display:flex;flex-direction:column;gap:6px}' +
    '.nzf-field-label{font-size:13px;color:var(--muted,#9a9aa3)}' +
    '.nzf-hint{color:var(--muted,#9a9aa3);font-size:13px}' +
    '.nzf-step-num{width:20px;height:20px;border-radius:50%;background:var(--surface-2,#1c1c20);border:1px solid var(--line,#2a2a30);color:var(--muted,#9a9aa3);font-size:12px;display:inline-flex;align-items:center;justify-content:center;flex:none}' +
    '.nzf-code{background:var(--bg,#0b0b0d);border:1px solid var(--line,#2a2a30);border-radius:6px;padding:4px 8px;font-family:Consolas,monospace;font-size:13px;color:var(--silver,#e8e8ea)}' +
    '.nzf-count-num{font-family:"Segoe UI Variable Display","Segoe UI",system-ui,sans-serif;font-weight:600;font-size:40px;color:var(--fg,#f2f2f4);font-variant-numeric:tabular-nums;padding:8px 16px 16px}' +
    '.nzf-backdrop{position:fixed;inset:0;background:rgba(0,0,0,.45);display:flex;align-items:center;justify-content:center;z-index:60}' +
    '.nzf-panel{background:var(--surface-2,#1c1c20);border:1px solid var(--line,#2a2a30);box-shadow:0 6px 16px rgba(0,0,0,.3);border-radius:8px;width:min(520px,calc(100vw - 48px));max-height:calc(100vh - 96px);overflow:auto}' +
    '.nzf-panel-sm{width:min(380px,calc(100vw - 48px))}' +
    '.nzf-panel-head{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:14px 16px;border-bottom:1px solid var(--line,#2a2a30)}' +
    '.nzf-panel-title{font-weight:600;color:var(--fg,#f2f2f4)}' +
    '.nzf-panel-body{padding:16px;display:flex;flex-direction:column;gap:14px}' +
    '.nzf-toast{position:fixed;left:50%;bottom:24px;transform:translateX(-50%);background:var(--surface-2,#1c1c20);color:var(--fg,#f2f2f4);border:1px solid var(--line,#2a2a30);box-shadow:0 6px 16px rgba(0,0,0,.3);border-radius:8px;padding:10px 16px;z-index:70;max-width:calc(100vw - 48px)}' +
    '@media (prefers-reduced-motion:reduce){.nzf-switch,.nzf-switch .nzf-knob{transition:none}}';

  function ensureStyle() {
    if (!document.getElementById('nzf-style')) {
      var st = document.createElement('style');
      st.id = 'nzf-style';
      st.textContent = NZF_CSS;
      document.head.appendChild(st);
    }
  }

  function h(tag, attrs) {
    var el = document.createElement(tag);
    if (attrs) {
      Object.keys(attrs).forEach(function (k) {
        var v = attrs[k];
        if (v === null || v === undefined || v === false) return; // false means "no attribute"
        if (k === 'class') el.className = v;
        else if (k === 'text') el.textContent = v;
        else if (k.slice(0, 2) === 'on') el.addEventListener(k.slice(2), v);
        else el.setAttribute(k, v);
      });
    }
    for (var i = 2; i < arguments.length; i++) {
      var c = arguments[i];
      if (c === null || c === undefined) continue;
      if (typeof c === 'string') el.appendChild(document.createTextNode(c));
      else el.appendChild(c);
    }
    return el;
  }

  function svgIcon(paths, size) {
    var NS = 'http://www.w3.org/2000/svg';
    var svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('width', String(size || 16));
    svg.setAttribute('height', String(size || 16));
    svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', '2');
    svg.setAttribute('stroke-linecap', 'round');
    svg.setAttribute('stroke-linejoin', 'round');
    svg.setAttribute('aria-hidden', 'true');
    paths.forEach(function (d) {
      var p = document.createElementNS(NS, 'path');
      p.setAttribute('d', d);
      svg.appendChild(p);
    });
    return svg;
  }

  var ICON_TRASH = ['M4 7h16', 'M9 7V4h6v3', 'M6 7l1 13h10l1-13'];
  var ICON_X = ['M6 6l12 12', 'M18 6L6 18'];

  // ---- bridge: replies {id, ok, data?, error?} plus state pushes ----

  var msgSeq = 0;
  var pendingReplies = {};
  var webview = (window.chrome && window.chrome.webview) || null;

  function post(type, payload) {
    if (!webview || typeof webview.postMessage !== 'function') {
      return Promise.resolve({ id: 0, ok: true, data: null }); // plain browser without the stub
    }
    var id = ++msgSeq;
    return new Promise(function (resolve) {
      pendingReplies[id] = resolve;
      var msg = { id: id, type: type };
      Object.keys(payload || {}).forEach(function (k) { msg[k] = payload[k]; });
      webview.postMessage(msg);
    });
  }

  function normalizeState(raw) {
    raw = raw || {};
    var gh = raw.github || {};
    var q = raw.quiet || {};
    var cd = raw.countdown || {};
    var ft = raw.features || {};
    var cn = raw.connectors || {};
    var urls = Array.isArray(raw.uptimeUrls) ? raw.uptimeUrls : [];
    return {
      port: typeof raw.port === 'number' ? raw.port : 7351,
      startWithWindows: !!raw.startWithWindows,
      onlyWhenUnfocused: raw.onlyWhenUnfocused !== false,
      quiet: {
        enabled: !!q.enabled,
        start: q.start || '22:00',
        end: q.end || '07:00',
        allowAlarms: q.allowAlarms !== false
      },
      github: {
        tokenSet: !!gh.tokenSet,
        username: gh.username || '',
        pollSeconds: typeof gh.pollSeconds === 'number' ? gh.pollSeconds : 10,
        repos: Array.isArray(gh.repos) ? gh.repos : [],
        lastError: gh.lastError || (gh.status === 'error' ? 'The last check did not work.' : '')
      },
      uptimeUrls: urls.map(function (u) {
        if (typeof u === 'string') return u;
        return (u && u.url) || '';
      }).filter(function (u) { return !!u; }),
      // values: 'up' or 'down' (booleans tolerated); missing = not checked yet
      uptimeStatus: raw.uptimeStatus || raw.uptimeStatuses || {},
      countdown: { enabled: !!cd.enabled, targetLocal: cd.targetLocal || '' },
      features: { claudeDesktopWatcher: !!ft.claudeDesktopWatcher },
      connectors: {
        claudeCode: !!cn.claudeCode,
        cursor: !!cn.cursor,
        extension: !!cn.extension
      }
    };
  }

  var state = normalizeState(null);
  var currentHost = null;
  var panelOpen = false;
  var pendingRender = false;

  function applyStateRender() {
    if (!currentHost) return;
    if (panelOpen) { pendingRender = true; return; }
    render(currentHost);
  }

  function refreshFromApp() {
    return post('getState').then(function (reply) {
      if (reply && reply.ok && reply.data) state = normalizeState(reply.data);
      applyStateRender();
    });
  }

  if (webview && typeof webview.addEventListener === 'function') {
    webview.addEventListener('message', function (e) {
      var d = e.data;
      if (!d || typeof d !== 'object') return;
      if (d.id !== undefined && pendingReplies[d.id]) {
        var r = pendingReplies[d.id];
        delete pendingReplies[d.id];
        r(d);
      } else if (d.type === 'state' && d.state) {
        state = normalizeState(d.state);
        applyStateRender();
      }
    });
  }

  function makeSwitch(checked, label, onChange) {
    var on = !!checked;
    var btn = h('button', {
      class: 'nzf-switch' + (on ? ' nzf-on' : ''),
      type: 'button',
      role: 'switch',
      'aria-checked': String(on),
      'aria-label': label
    }, h('span', { class: 'nzf-knob' }));
    btn.addEventListener('click', function () {
      on = !on;
      btn.classList.toggle('nzf-on', on);
      btn.setAttribute('aria-checked', String(on));
      onChange(on);
    });
    return btn;
  }

  function toast(msg) {
    // one toast at a time: all toasts share the same spot, so replace instead of stack
    var existing = document.querySelector('.nzf-toast');
    if (existing) existing.remove();
    var t = h('div', { class: 'nzf-toast', role: 'status', text: msg });
    document.body.appendChild(t);
    setTimeout(function () { if (t.parentNode === document.body) t.remove(); }, 3000);
  }

  // Floating panel: closes on Escape, on the X button, or on a click outside (section 5).
  function openPanel(title, small, buildBody, onClose) {
    panelOpen = true;
    var closed = false;
    var backdrop = h('div', { class: 'nzf-backdrop' });
    var panel = h('div', {
      class: 'nzf-panel' + (small ? ' nzf-panel-sm' : ''),
      role: 'dialog', 'aria-modal': 'true', 'aria-label': title
    });
    function close() {
      if (closed) return;
      closed = true;
      document.removeEventListener('keydown', onKey);
      backdrop.remove();
      panelOpen = false;
      if (onClose) onClose();
      if (pendingRender && currentHost) { pendingRender = false; render(currentHost); }
    }
    function onKey(e) {
      if (e.key === 'Escape') { e.stopPropagation(); close(); }
    }
    var closeBtn = h('button', { class: 'nzf-icon-btn', type: 'button', 'aria-label': 'Close', onclick: close }, svgIcon(ICON_X));
    backdrop.addEventListener('mousedown', function (e) { if (e.target === backdrop) close(); });
    document.addEventListener('keydown', onKey);
    panel.appendChild(h('div', { class: 'nzf-panel-head' }, h('div', { class: 'nzf-panel-title', text: title }), closeBtn));
    var body = h('div', { class: 'nzf-panel-body' });
    buildBody(body, close);
    panel.appendChild(body);
    backdrop.appendChild(panel);
    document.body.appendChild(backdrop);
    var f = body.querySelector('input, textarea, select, button');
    if (f) f.focus();
    return { close: close };
  }

  // ---- page ----

  var tickTimer = null;
  var timeLeftEl = null;

  function pad2(n) { return (n < 10 ? '0' : '') + n; }

  function defaultTarget() {
    var d = new Date();
    d.setDate(d.getDate() + 1);
    d.setHours(12, 0, 0, 0);
    return d;
  }

  function parseTarget() {
    var t = state.countdown.targetLocal ? new Date(state.countdown.targetLocal) : null;
    if (!t || isNaN(t.getTime())) t = defaultTarget();
    return t;
  }

  function toDateValue(d) { return d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate()); }
  function toTimeValue(d) { return pad2(d.getHours()) + ':' + pad2(d.getMinutes()); }

  function remainingText() {
    if (!state.countdown.enabled) return '';
    var t = parseTarget();
    var s = Math.floor((t.getTime() - Date.now()) / 1000);
    if (s <= 0) return 'Time is up.';
    var d = Math.floor(s / 86400); s -= d * 86400;
    var hh = Math.floor(s / 3600); s -= hh * 3600;
    var mm = Math.floor(s / 60);
    var ss = s - mm * 60;
    return (d > 0 ? d + 'd ' : '') + pad2(hh) + ':' + pad2(mm) + ':' + pad2(ss);
  }

  function startTick() {
    if (tickTimer) clearInterval(tickTimer);
    tickTimer = setInterval(function () {
      if (timeLeftEl) timeLeftEl.textContent = remainingText();
    }, 1000);
  }

  function stopTick() {
    if (tickTimer) { clearInterval(tickTimer); tickTimer = null; }
  }

  function quietBox() {
    var fromInput = h('input', {
      class: 'nzf-input', type: 'time', value: state.quiet.start, 'aria-label': 'Quiet hours start'
    });
    var toInput = h('input', {
      class: 'nzf-input', type: 'time', value: state.quiet.end, 'aria-label': 'Quiet hours end'
    });
    function sendQuiet() {
      post('setQuietHours', {
        enabled: state.quiet.enabled,
        start: fromInput.value || state.quiet.start,
        end: toInput.value || state.quiet.end,
        allowAlarms: state.quiet.allowAlarms
      });
    }
    fromInput.addEventListener('change', sendQuiet);
    toInput.addEventListener('change', sendQuiet);
    var onOff = makeSwitch(state.quiet.enabled, 'Quiet hours', function (v) {
      state.quiet.enabled = v;
      sendQuiet();
    });
    var alarmSwitch = makeSwitch(state.quiet.allowAlarms, 'Still play the uptime alarm during quiet hours', function (v) {
      state.quiet.allowAlarms = v;
      sendQuiet();
    });
    var box = h('div', { class: 'nzf-box' });
    box.appendChild(h('div', { class: 'nzf-row' },
      h('div', { class: 'nzf-main' }, h('div', { class: 'nzf-name', text: 'Quiet hours' })),
      h('div', { class: 'nzf-end' }, onOff)));
    box.appendChild(h('div', { class: 'nzf-row' },
      h('div', { class: 'nzf-main' }, h('div', { class: 'nzf-inline' },
        h('span', { class: 'nzf-hint', text: 'From' }), fromInput,
        h('span', { class: 'nzf-hint', text: 'To' }), toInput))));
    box.appendChild(h('div', { class: 'nzf-row' },
      h('div', { class: 'nzf-main' }, h('div', { class: 'nzf-sub', text: 'Still play the uptime alarm during quiet hours' })),
      h('div', { class: 'nzf-end' }, alarmSwitch)));
    return box;
  }

  function removeUrl(url) {
    state.uptimeUrls = state.uptimeUrls.filter(function (u) { return u !== url; });
    post('setUptime', { urls: state.uptimeUrls });
    applyStateRender();
  }

  function addSitePanel() {
    openPanel('Add a site', true, function (body, closePanel) {
      var input = h('input', {
        class: 'nzf-input', type: 'url', name: 'nzf-url',
        placeholder: 'https://example.com', 'aria-label': 'Site address'
      });
      function add() {
        var url = (input.value || '').trim();
        if (!url) return;
        var dup = state.uptimeUrls.some(function (u) { return u.toLowerCase() === url.toLowerCase(); });
        if (!dup) state.uptimeUrls.push(url);
        post('setUptime', { urls: state.uptimeUrls });
        closePanel();
        applyStateRender();
      }
      input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') { e.preventDefault(); add(); }
      });
      body.appendChild(input);
      body.appendChild(h('div', { class: 'nzf-inline' },
        h('button', { class: 'nzf-btn nzf-btn-accent', type: 'button', text: 'Add', onclick: add }),
        h('button', { class: 'nzf-btn', type: 'button', text: 'Cancel', onclick: closePanel })));
    });
  }

  function uptimeBox() {
    var box = h('div', { class: 'nzf-box' }, h('div', { class: 'nzf-box-title', text: 'Uptime watch' }));
    if (!state.uptimeUrls.length) {
      box.appendChild(h('div', { class: 'nzf-row' },
        h('span', { class: 'nzf-hint', text: 'No sites watched yet. Add one and Noizes checks it every 3 minutes.' })));
    }
    state.uptimeUrls.forEach(function (url) {
      var st = state.uptimeStatus[url];
      var dotCls = 'nzf-dot';
      if (st === 'up' || st === true) dotCls += ' nzf-ok';
      else if (st === 'down' || st === false) dotCls += ' nzf-bad';
      box.appendChild(h('div', { class: 'nzf-row' },
        h('div', { class: 'nzf-main' }, h('div', { class: 'nzf-inline' },
          h('span', { class: dotCls }),
          h('span', { text: url }))),
        h('div', { class: 'nzf-end' },
          h('button', {
            class: 'nzf-icon-btn', type: 'button', 'aria-label': 'Remove ' + url,
            onclick: function () { removeUrl(url); }
          }, svgIcon(ICON_TRASH)))));
    });
    box.appendChild(h('div', { class: 'nzf-row' },
      h('button', { class: 'nzf-btn', type: 'button', text: '+ Add a site', 'aria-label': 'Add a site to watch', onclick: addSitePanel })));
    return box;
  }

  function countdownBox() {
    var t = parseTarget();
    var dateInput = h('input', {
      class: 'nzf-input', type: 'date', value: toDateValue(t), 'aria-label': 'Countdown date'
    });
    var timeInput = h('input', {
      class: 'nzf-input', type: 'time', value: toTimeValue(t), 'aria-label': 'Countdown time'
    });
    function sendTarget(enabled) {
      var v = (dateInput.value || '') + 'T' + (timeInput.value || '12:00') + ':00';
      post('setCountdown', { enabled: enabled, targetLocal: v });
    }
    dateInput.addEventListener('change', function () { sendTarget(state.countdown.enabled); });
    timeInput.addEventListener('change', function () { sendTarget(state.countdown.enabled); });
    var onOff = makeSwitch(state.countdown.enabled, 'Countdown', function (v) {
      state.countdown.enabled = v;
      sendTarget(v);
    });
    var box = h('div', { class: 'nzf-box' });
    box.appendChild(h('div', { class: 'nzf-row' },
      h('div', { class: 'nzf-main' },
        h('div', { class: 'nzf-name', text: 'Countdown' }),
        h('div', { class: 'nzf-sub', text: 'Plays a sound when the date and time arrive.' })),
      h('div', { class: 'nzf-end' }, onOff)));
    box.appendChild(h('div', { class: 'nzf-row' },
      h('div', { class: 'nzf-main' }, h('div', { class: 'nzf-inline' }, dateInput, timeInput))));
    if (state.countdown.enabled) {
      timeLeftEl = h('div', { class: 'nzf-count-num', text: remainingText() });
      box.appendChild(timeLeftEl);
    }
    return box;
  }

  function copyCmd() {
    var text = 'noizes run <command>';
    function done() { toast('Copied.'); }
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(done, function () {
        // clipboard API refused (WebView2 permission or plain file page); fall back to execCommand
        fallbackCopy(text);
        done();
      });
    } else {
      fallbackCopy(text);
      done();
    }
  }

  function fallbackCopy(text) {
    var ta = document.createElement('textarea');
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand('copy'); } catch (e) { /* clipboard unavailable; the code text stays selectable */ }
    ta.remove();
  }

  function longCmdBox() {
    var box = h('div', { class: 'nzf-box' }, h('div', { class: 'nzf-box-title', text: 'Long commands' }));
    box.appendChild(h('div', { class: 'nzf-row' },
      h('div', { class: 'nzf-main' },
        h('div', { class: 'nzf-sub', text: 'Run a command through Noizes and get a sound when it finishes.' }),
        h('div', { class: 'nzf-inline' },
          h('code', { class: 'nzf-code', text: 'noizes run <command>' }),
          h('button', { class: 'nzf-btn', type: 'button', text: 'Copy', 'aria-label': 'Copy the noizes run command', onclick: copyCmd })))));
    return box;
  }

  function render(host) {
    currentHost = host;
    host.textContent = '';
    timeLeftEl = null;
    host.appendChild(quietBox());
    host.appendChild(uptimeBox());
    host.appendChild(countdownBox());
    host.appendChild(longCmdBox());
    startTick();
  }

  function onHide() {
    stopTick();
    currentHost = null;
  }

  function onShow() {
    if (currentHost) render(currentHost);
  }

  ensureStyle();
  refreshFromApp();

  window.Pages = window.Pages || {};
  window.Pages.schedule = { id: 'schedule', label: 'Schedule', render: render, onHide: onHide, onShow: onShow };
  document.dispatchEvent(new CustomEvent('noizes:page-registered', { detail: { id: 'schedule' } }));
})();
