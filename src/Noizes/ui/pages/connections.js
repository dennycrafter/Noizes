// Noizes UI, Connections page (lane F). Plain JS, no framework, no build step.
//
// Contract with the shell (lane D): this file registers
//   window.Pages.connections = { id, label, render(host) }
// and fires the 'noizes:page-registered' event after registering. The shell builds the
// sidebar from window.Pages and calls render(container) when the page is shown.
//
// Bridge (section 2 of the brief): the page sends window.chrome.webview.postMessage({id, type, ...payload});
// the app replies {id, ok, data?, error?} and pushes {type: 'state', state} when anything
// changes outside the page. Messages used here: getState, setupClaudeCode, setupCursor,
// saveGitHub {token?, username, pollSeconds, repos}, testGitHub, setDesktopWatcher {enabled},
// openFolder {which: 'extension'}.
//
// Expected state fields (lane B maps config.json; camelCase, all optional, defaults below):
//   connectors {claudeCode, cursor, extension} (bool), features {claudeDesktopWatcher} (bool),
//   github {tokenSet, username, pollSeconds, repos[], lastError}.
// The GitHub token is never in state, only tokenSet.

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

  function errorText(reply) {
    if (reply && typeof reply.error === 'string' && reply.error) return reply.error;
    if (reply && reply.data && typeof reply.data.error === 'string' && reply.data.error) return reply.data.error;
    return 'Something went wrong.';
  }

  // Floating panel: closes on Escape, on the X button, or on a click outside (section 4).
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

  function connectedStatus(isConnected) {
    return isConnected ? { cls: ' nzf-ok', text: 'Connected' } : { cls: '', text: 'Not connected' };
  }

  function rowFor(name, desc, st, button) {
    var end = h('div', { class: 'nzf-end' },
      h('span', { class: 'nzf-status' },
        h('span', { class: 'nzf-dot' + st.cls }),
        h('span', { text: st.text })));
    if (button) end.appendChild(button);
    return h('div', { class: 'nzf-row' },
      h('div', { class: 'nzf-main' },
        h('div', { class: 'nzf-name', text: name }),
        h('div', { class: 'nzf-sub', text: desc })),
      end);
  }

  function setupBtn(kind) {
    var name = kind === 'claudeCode' ? 'Claude Code' : 'Cursor';
    return h('button', {
      class: 'nzf-btn', type: 'button', text: 'Set up',
      'aria-label': 'Set up ' + name,
      onclick: function () { runSetup(kind, name); }
    });
  }

  function runSetup(kind, name) {
    post(kind === 'claudeCode' ? 'setupClaudeCode' : 'setupCursor').then(function (reply) {
      if (reply && reply.ok) {
        state.connectors[kind] = true;
        applyStateRender();
        toast(kind === 'claudeCode'
          ? 'Claude Code connected. Open a new Claude Code session.'
          : 'Cursor connected. Open a new Cursor session.');
      } else {
        toast('Could not connect ' + name + '. ' + errorText(reply));
      }
    });
  }

  function githubStatus() {
    if (!state.github.tokenSet) return { cls: '', text: 'Not connected' };
    if (state.github.lastError) return { cls: ' nzf-warn', text: 'Needs attention' };
    return { cls: ' nzf-ok', text: 'Connected' };
  }

  function fieldWrap(label, input, hint) {
    var w = h('div', { class: 'nzf-field' }, h('div', { class: 'nzf-field-label', text: label }), input);
    if (hint) w.appendChild(h('span', { class: 'nzf-hint', text: hint }));
    return w;
  }

  function openGithubPanel() {
    var gh = state.github;
    var replacing = false;
    openPanel('GitHub settings', false, function (body, closePanel) {
      var tokenInput = null;

      // Token: the token itself never comes back to the page, only tokenSet (section 2).
      var tokenField = h('div', { class: 'nzf-field' });
      function buildTokenField() {
        tokenField.textContent = '';
        tokenField.appendChild(h('div', { class: 'nzf-field-label', text: 'Token' }));
        if (gh.tokenSet && !replacing) {
          tokenField.appendChild(h('div', { class: 'nzf-inline' },
            h('span', { class: 'nzf-hint', text: 'Token saved' }),
            h('button', {
              class: 'nzf-btn', type: 'button', text: 'Replace',
              onclick: function () { replacing = true; buildTokenField(); }
            })));
        } else {
          tokenInput = h('input', {
            class: 'nzf-input', type: 'password', name: 'nzf-token',
            placeholder: 'Paste your GitHub token', 'aria-label': 'GitHub token'
          });
          tokenField.appendChild(tokenInput);
          tokenField.appendChild(h('span', {
            class: 'nzf-hint',
            text: gh.tokenSet
              ? 'Type a new token to replace the saved one.'
              : 'Your token stays on this computer and is only sent to api.github.com.'
          }));
        }
      }
      buildTokenField();

      var userInput = h('input', {
        class: 'nzf-input', type: 'text', name: 'nzf-username',
        value: gh.username, 'aria-label': 'GitHub username'
      });
      var pollInput = h('input', {
        class: 'nzf-input', type: 'number', name: 'nzf-poll', min: '5', max: '300',
        value: String(gh.pollSeconds), 'aria-label': 'Check interval in seconds'
      });
      var reposInput = h('textarea', {
        class: 'nzf-input', rows: '4', name: 'nzf-repos',
        placeholder: 'owner/repo, one per line', 'aria-label': 'Repos to watch, one per line'
      });
      reposInput.value = gh.repos.join('\n');

      var testResult = h('span', { class: 'nzf-hint nzf-test-result', text: '' });
      var testBtn = h('button', {
        class: 'nzf-btn', type: 'button', text: 'Test',
        onclick: function () {
          testBtn.disabled = true;
          testResult.textContent = 'Checking...';
          post('testGitHub').then(function (reply) {
            testBtn.disabled = false;
            if (reply && reply.ok) {
              testResult.textContent = (reply.data && reply.data.message) ? reply.data.message : 'It works.';
            } else {
              testResult.textContent = errorText(reply);
            }
          });
        }
      });

      var saveBtn = h('button', {
        class: 'nzf-btn nzf-btn-accent', type: 'button', text: 'Save',
        onclick: function () {
          saveBtn.disabled = true;
          var poll = Math.round(Number(pollInput.value));
          if (!isFinite(poll) || poll <= 0) poll = gh.pollSeconds;
          poll = Math.min(300, Math.max(5, poll));
          var payload = {
            username: userInput.value.trim(),
            pollSeconds: poll,
            repos: reposInput.value.split('\n').map(function (s) { return s.trim(); }).filter(Boolean)
          };
          if (tokenInput && tokenInput.value.trim()) payload.token = tokenInput.value.trim();
          post('saveGitHub', payload).then(function (reply) {
            saveBtn.disabled = false;
            if (reply && reply.ok) {
              if (payload.token) state.github.tokenSet = true;
              state.github.username = payload.username;
              state.github.pollSeconds = poll;
              state.github.repos = payload.repos;
              state.github.lastError = '';
              closePanel();
              applyStateRender();
              toast('GitHub settings saved.');
            } else {
              toast('Could not save GitHub settings. ' + errorText(reply));
            }
          });
        }
      });

      body.appendChild(tokenField);
      body.appendChild(fieldWrap('Username', userInput));
      body.appendChild(fieldWrap('Check every', pollInput, 'Seconds, 5 to 300.'));
      body.appendChild(fieldWrap('Repos to watch', reposInput));
      body.appendChild(h('div', { class: 'nzf-inline' }, testBtn, testResult));
      body.appendChild(saveBtn);
    });
  }

  function openExtensionPanel() {
    openPanel('Add the Chrome extension', false, function (body) {
      var steps = [
        'Open Chrome and go to chrome://extensions.',
        'Turn on Developer mode.',
        'Click Load unpacked.',
        'Choose the Noizes extension folder and click Select.'
      ];
      steps.forEach(function (s, i) {
        body.appendChild(h('div', { class: 'nzf-inline' },
          h('span', { class: 'nzf-step-num', text: String(i + 1) }),
          h('span', { text: s })));
      });
      body.appendChild(h('button', {
        class: 'nzf-btn', type: 'button', text: 'Open extension folder',
        onclick: function () { post('openFolder', { which: 'extension' }); }
      }));
    });
  }

  function render(host) {
    currentHost = host;
    host.textContent = '';
    var box = h('div', { class: 'nzf-box' });
    box.appendChild(rowFor('Claude Code',
      'Plays sounds when Claude Code starts working and when it finishes.',
      connectedStatus(state.connectors.claudeCode),
      setupBtn('claudeCode')));
    box.appendChild(rowFor('Cursor',
      'Plays sounds when Cursor finishes a task.',
      connectedStatus(state.connectors.cursor),
      setupBtn('cursor')));
    box.appendChild(rowFor('GitHub',
      'Sounds for pushes, pull requests, failed checks, stars, issues and comments.',
      githubStatus(),
      h('button', { class: 'nzf-btn', type: 'button', text: 'Edit', 'aria-label': 'Edit GitHub settings', onclick: openGithubPanel })));
    box.appendChild(rowFor('Claude desktop app',
      'Plays a sound when the Claude desktop app finishes answering.',
      connectedStatus(state.features.claudeDesktopWatcher),
      state.features.claudeDesktopWatcher ? null : h('button', {
        class: 'nzf-btn', type: 'button', text: 'Turn on', 'aria-label': 'Turn on the Claude desktop app watcher',
        onclick: function () {
          post('setDesktopWatcher', { enabled: true }).then(function (reply) {
            if (reply && reply.ok) {
              state.features.claudeDesktopWatcher = true;
              applyStateRender();
            } else {
              toast('Could not turn on the Claude desktop app. ' + errorText(reply));
            }
          });
        }
      })));
    box.appendChild(rowFor('Chrome extension',
      'Connects Chrome so Noizes can play sounds for browser events.',
      connectedStatus(state.connectors.extension),
      h('button', {
        class: 'nzf-btn', type: 'button', text: 'How to add it', 'aria-label': 'How to add the Chrome extension',
        onclick: openExtensionPanel
      })));
    host.appendChild(box);
  }

  ensureStyle();
  refreshFromApp();

  window.Pages = window.Pages || {};
  window.Pages.connections = { id: 'connections', label: 'Connections', render: render };
  document.dispatchEvent(new CustomEvent('noizes:page-registered', { detail: { id: 'connections' } }));
})();
