// Noizes UI, General page (lane F). Plain JS, no framework, no build step.
//
// Contract with the shell (lane D): this file registers
//   window.Pages.general = { id, label, render(host) }
// and fires the 'noizes:page-registered' event after registering. The shell builds the
// sidebar from window.Pages and calls render(container) when the page is shown.
//
// Bridge (section 2 of the brief): the page sends window.chrome.webview.postMessage({id, type, ...payload});
// the app replies {id, ok, data?, error?} and pushes {type: 'state', state} when anything
// changes outside the page. Messages used here: getState, setGeneral {startWithWindows?} or
// {onlyWhenUnfocused?} or {port?}, openFolder {which: 'settings'}, openLog.
// NZF_CSS stays identical across the lane F pages so the first file that loads still
// ships the styles the other pages need (style injection is guarded by an id).
//
// Expected state fields (lane B maps config.json; camelCase, all optional, defaults below):
//   port (number), startWithWindows (bool), onlyWhenUnfocused (bool).

(function () {
  'use strict';

  // ---- shared lane F helpers: keep NZF_CSS identical in every ui/pages file ----

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

  function refreshFromApp() {
    return post('getState').then(function (reply) {
      if (reply && reply.ok && reply.data) state = normalizeState(reply.data);
      if (currentHost) render(currentHost);
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
        if (currentHost) render(currentHost);
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

  // ---- page ----

  var currentHost = null;
  var portChanged = false;

  function startupBox() {
    var sw = makeSwitch(state.startWithWindows, 'Start Noizes when Windows starts', function (v) {
      state.startWithWindows = v;
      post('setGeneral', { startWithWindows: v });
    });
    return h('div', { class: 'nzf-box' },
      h('div', { class: 'nzf-row' },
        h('div', { class: 'nzf-main' }, h('div', { class: 'nzf-name', text: 'Start Noizes when Windows starts' })),
        h('div', { class: 'nzf-end' }, sw)));
  }

  function focusBox() {
    var sw = makeSwitch(state.onlyWhenUnfocused, 'Only play when I am not looking at the app that finished', function (v) {
      state.onlyWhenUnfocused = v;
      post('setGeneral', { onlyWhenUnfocused: v });
    });
    return h('div', { class: 'nzf-box' },
      h('div', { class: 'nzf-row' },
        h('div', { class: 'nzf-main' },
          h('div', { class: 'nzf-name', text: 'Only play when I am not looking at the app that finished' }),
          h('div', { class: 'nzf-sub', text: 'Skips the sound while the app that finished is already in front of you.' })),
        h('div', { class: 'nzf-end' }, sw)));
  }

  function serverBox() {
    var portInput = h('input', {
      class: 'nzf-input', type: 'number', min: '1', max: '65535',
      value: String(state.port), 'aria-label': 'Local server port'
    });
    // survives state-push rerenders; a real app restart reloads the page and clears it
    var restartHint = h('div', { class: 'nzf-hint', hidden: !portChanged, text: 'Restart Noizes to apply.' });
    portInput.addEventListener('change', function () {
      var p = Math.round(Number(portInput.value));
      if (!isFinite(p) || p < 1 || p > 65535) {
        portInput.value = String(state.port); // ignore anything that is not a real port
        return;
      }
      state.port = p;
      portChanged = true;
      restartHint.hidden = false;
      post('setGeneral', { port: p });
    });
    return h('div', { class: 'nzf-box' },
      h('div', { class: 'nzf-row' },
        h('div', { class: 'nzf-main' },
          h('div', { class: 'nzf-name', text: 'Local server port' }),
          h('div', { class: 'nzf-sub', text: 'The hooks, the CLI and the Chrome extension call Noizes on this port.' }),
          restartHint),
        h('div', { class: 'nzf-end' }, portInput)));
  }

  function filesBox() {
    return h('div', { class: 'nzf-box' },
      h('div', { class: 'nzf-row' },
        h('div', { class: 'nzf-main' }, h('div', { class: 'nzf-name', text: 'Files' })),
        h('div', { class: 'nzf-end' },
          h('button', {
            class: 'nzf-btn', type: 'button', text: 'Open settings file folder',
            onclick: function () { post('openFolder', { which: 'settings' }); }
          }),
          h('button', {
            class: 'nzf-btn', type: 'button', text: 'Open log',
            onclick: function () { post('openLog'); }
          }))));
  }

  function render(host) {
    currentHost = host;
    host.textContent = '';
    host.appendChild(startupBox());
    host.appendChild(focusBox());
    host.appendChild(serverBox());
    host.appendChild(filesBox());
  }

  ensureStyle();
  refreshFromApp();

  window.Pages = window.Pages || {};
  window.Pages.general = { id: 'general', label: 'General', render: render };
  document.dispatchEvent(new CustomEvent('noizes:page-registered', { detail: { id: 'general' } }));
})();
