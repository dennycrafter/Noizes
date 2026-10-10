// Noizes UI, Connections page (lane F). Plain JS, no framework, no build step.
//
// Shell contract (lane D, pages/README.md): this module registers one page via
// NZ.registerPage({ id, render(el, ctx) }) and renders into the #page container.
// ctx carries state, api (section 2 bridge calls as promises), toast, panel,
// closePanel, toggle, icon and onEscape. Without an update() hook the shell
// re-runs render on every state push, so render always rebuilds from scratch.
//
// State shape this page reads (see dev-stub.js for the canonical demo):
//   connections.github {connected, tokenSet, username, pollSeconds, repos}
//   connections.claudeCode {connected} | cursor {connected} | chrome {connected}
//   connections.claudeDesktop {enabled}
// The GitHub token never comes back to the page: only tokenSet is exposed.
//
// openFolder carries a which value in section 2 (settings vs extension folder);
// the shell's api.openFolder() wrapper takes no argument, so those two buttons
// use NZ.request directly until the wrapper grows the parameter.

(function () {
  'use strict';

  if (!window.NZ || typeof window.NZ.registerPage !== 'function') return;

  // Page-local styles: app.css is lane D's file. The wrap rule and the step
  // badges are small enough to ship with the page; the integrator can lift
  // them into app.css if lane E needs the same.
  var LOCAL_CSS =
    '.nz-row.nzf-wrap{flex-wrap:wrap}' +
    '.nz-row.nzf-wrap .nz-row-actions{margin-left:auto}' +
    // On narrow windows the button drops under the text instead of squeezing it.
    '@media (max-width:640px){' +
    '.nz-row.nzf-wrap .nz-row-main{flex:1 1 100%}' +
    '.nz-row.nzf-wrap .nz-row-actions{flex-basis:100%;justify-content:flex-end;padding-top:8px}}' +
    '.nzf-step{display:flex;align-items:flex-start;gap:10px;padding:4px 0}' +
    '.nzf-step-num{width:20px;height:20px;border-radius:50%;background:var(--surface-2);border:1px solid var(--line);color:var(--muted);font-size:12px;display:inline-flex;align-items:center;justify-content:center;flex:none;margin-top:1px}';

  function ensureStyle() {
    if (!document.getElementById('nzf-style-connections')) {
      var st = document.createElement('style');
      st.id = 'nzf-style-connections';
      st.textContent = LOCAL_CSS;
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

  function errorText(reply) {
    if (reply && typeof reply.error === 'string' && reply.error) return reply.error;
    if (reply && reply.data && typeof reply.data.error === 'string' && reply.data.error) return reply.data.error;
    return 'Something did not work. Try again.';
  }

  // ---- rerender hook for actions that should refresh before a state push ----

  var lastEl = null;
  var lastCtx = null;

  function rerender() {
    if (lastEl && lastCtx) render(lastEl, lastCtx);
  }

  // ---- building blocks ----

  // Status pill: green Connected, grey Not connected, amber Needs attention.
  function statusPill(kind, text) {
    var cls = 'nz-pill';
    if (kind === 'ok') cls += ' nz-pill-ok';
    else if (kind === 'warn') cls += ' nz-pill-warn';
    var pill = h('span', { class: cls });
    pill.appendChild(h('span', { class: 'nz-pill-dot' }));
    pill.appendChild(document.createTextNode(text));
    return pill;
  }

  function githubStatus(gh) {
    if (!gh.tokenSet) return statusPill('', 'Not connected');
    if (!gh.connected) return statusPill('warn', 'Needs attention');
    return statusPill('ok', 'Connected');
  }

  function rowFor(name, desc, pill, action) {
    return h('div', { class: 'nz-row nzf-wrap' },
      h('div', { class: 'nz-row-main' },
        h('div', { class: 'nz-row-title', text: name }),
        h('div', { class: 'nz-row-sub', text: desc })),
      h('div', { class: 'nz-row-actions' }, pill, action));
  }

  function setupBtn(ctx, kind, name) {
    return h('button', {
      class: 'nz-btn', type: 'button', text: 'Set up',
      'aria-label': 'Set up ' + name,
      onclick: function () {
        var call = kind === 'claudeCode' ? ctx.api.setupClaudeCode() : ctx.api.setupCursor();
        call.then(function (reply) {
          if (reply && reply.ok) {
            ctx.toast(name + ' connected. Open a new ' + name + ' session.', 'ok');
          } else {
            ctx.toast('Could not connect ' + name + '. ' + errorText(reply), 'bad');
          }
          rerender(); // picks up pushed state when the app sends one
        });
      }
    });
  }

  // ---- GitHub panel ----

  function openGithubPanel(ctx, gh) {
    ctx.panel({
      title: 'GitHub settings',
      build: function (body, panelApi) {
        var replacing = !gh.tokenSet;

        var tokenRow = h('div', { class: 'nz-field' });
        var tokenInput = null;
        function buildTokenField() {
          tokenRow.textContent = '';
          tokenInput = null;
          if (!replacing) {
            tokenRow.appendChild(h('div', { class: 'nz-row-sub', text: 'Token saved.' }));
            tokenRow.appendChild(h('button', {
              class: 'nz-btn', type: 'button', text: 'Replace',
              onclick: function () { replacing = true; buildTokenField(); }
            }));
          } else {
            tokenInput = h('input', {
              class: 'nz-input', type: 'password', name: 'nzf-token',
              placeholder: 'Paste your GitHub token', 'aria-label': 'GitHub token'
            });
            tokenRow.appendChild(tokenInput);
            tokenRow.appendChild(h('span', {
              class: 'nz-hint',
              text: gh.tokenSet
                ? 'Type a new token to replace the saved one.'
                : 'Your token stays on this computer and is only sent to api.github.com.'
            }));
          }
        }
        buildTokenField();

        var username = h('input', {
          class: 'nz-input', type: 'text', id: 'nzf-gh-user', name: 'nzf-username',
          value: gh.username || '', 'aria-label': 'GitHub username'
        });
        var poll = h('input', {
          class: 'nz-input', type: 'number', id: 'nzf-gh-poll', name: 'nzf-poll',
          min: '5', max: '300',
          value: String(typeof gh.pollSeconds === 'number' ? gh.pollSeconds : 10),
          'aria-label': 'Check interval in seconds'
        });
        var repos = h('textarea', {
          class: 'nz-textarea', rows: '4', id: 'nzf-gh-repos', name: 'nzf-repos',
          placeholder: 'owner/repo, one per line',
          'aria-label': 'Repos to watch, one per line'
        });
        repos.value = (gh.repos || []).join('\n');

        var testResult = h('span', { class: 'nz-hint nzf-test-result', text: '' });
        var testBtn = h('button', {
          class: 'nz-btn', type: 'button', text: 'Test',
          onclick: function () {
            testBtn.disabled = true;
            testResult.textContent = 'Checking.';
            ctx.api.testGitHub().then(function (reply) {
              testBtn.disabled = false;
              if (reply && reply.ok) {
                testResult.textContent = reply.data && reply.data.message
                  ? reply.data.message
                  : 'It works.';
              } else {
                testResult.textContent = errorText(reply);
              }
            });
          }
        });

        function clampPoll() {
          var n = Math.round(Number(poll.value));
          if (!isFinite(n)) return 10;
          if (n < 5) return 5;
          if (n > 300) return 300;
          return n;
        }

        var saveBtn = h('button', {
          class: 'nz-btn nz-btn-primary', type: 'button', text: 'Save',
          onclick: function () {
            var fields = {
              username: (username.value || '').trim(),
              pollSeconds: clampPoll(),
              repos: (repos.value || '').split('\n').map(function (s) { return s.trim(); }).filter(Boolean)
            };
            if (tokenInput && tokenInput.value) fields.token = tokenInput.value;
            saveBtn.disabled = true;
            ctx.api.saveGitHub(fields).then(function (reply) {
              saveBtn.disabled = false;
              if (reply && reply.ok) {
                panelApi.close(); // the state push refreshes the row
              } else {
                ctx.toast('Could not save. ' + errorText(reply), 'bad');
              }
            });
          }
        });

        body.appendChild(h('div', { class: 'nz-field' },
          h('span', { class: 'nz-label', text: 'Token' }), tokenRow));
        body.appendChild(h('div', { class: 'nz-field' },
          h('label', { class: 'nz-label', for: 'nzf-gh-user', text: 'Username' }), username));
        body.appendChild(h('div', { class: 'nz-field' },
          h('label', { class: 'nz-label', for: 'nzf-gh-poll', text: 'Check every' }), poll,
          h('span', { class: 'nz-hint', text: 'Seconds, 5 to 300.' })));
        body.appendChild(h('div', { class: 'nz-field' },
          h('label', { class: 'nz-label', for: 'nzf-gh-repos', text: 'Repos to watch' }), repos));
        body.appendChild(h('div', { class: 'nz-actions' }, testBtn, testResult));
        body.appendChild(saveBtn);
      }
    });
  }

  // ---- Chrome extension panel ----

  function openExtensionPanel(ctx) {
    ctx.panel({
      title: 'Add the Chrome extension',
      build: function (body) {
        var steps = [
          'Open Chrome and go to chrome://extensions.',
          'Turn on Developer mode.',
          'Click Load unpacked.',
          'Choose the Noizes extension folder and click Select.'
        ];
        steps.forEach(function (s, i) {
          body.appendChild(h('div', { class: 'nzf-step' },
            h('span', { class: 'nzf-step-num', text: String(i + 1) }),
            h('span', { text: s })));
        });
        body.appendChild(h('button', {
          class: 'nz-btn', type: 'button', text: 'Open extension folder',
          onclick: function () { NZ.request({ type: 'openFolder', which: 'extension' }); }
        }));
      }
    });
  }

  // ---- page ----

  function render(el, ctx) {
    lastEl = el;
    lastCtx = ctx;
    ensureStyle();
    var st = ctx.state || {};
    var cn = st.connections || {};
    // BuildState puts github at the top level and exposes the watcher as
    // watcherEnabled; keep the connections.* fallback so the dev stub path works.
    var gh = st.github || cn.github || {};

    var box = h('div', { class: 'nz-card' });

    box.appendChild(rowFor('Claude Code',
      'Plays sounds when Claude Code starts working and when it finishes.',
      cn.claudeCode && cn.claudeCode.connected ? statusPill('ok', 'Connected') : statusPill('', 'Not connected'),
      setupBtn(ctx, 'claudeCode', 'Claude Code')));

    box.appendChild(rowFor('Cursor',
      'Plays sounds when Cursor finishes a task.',
      cn.cursor && cn.cursor.connected ? statusPill('ok', 'Connected') : statusPill('', 'Not connected'),
      setupBtn(ctx, 'cursor', 'Cursor')));

    box.appendChild(rowFor('GitHub',
      'Sounds for pushes, pull requests, failed checks, stars, issues and comments.',
      githubStatus(gh),
      h('button', {
        class: 'nz-btn', type: 'button', text: 'Edit',
        'aria-label': 'Edit GitHub settings',
        onclick: function () { openGithubPanel(ctx, gh); }
      })));

    var watcherOn = !!(cn.claudeDesktop && cn.claudeDesktop.enabled) || st.watcherEnabled === true;
    box.appendChild(rowFor('Claude desktop app',
      'Plays a sound when the Claude desktop app finishes answering.',
      watcherOn ? statusPill('ok', 'Connected') : statusPill('', 'Not connected'),
      h('button', {
        class: 'nz-btn', type: 'button', text: watcherOn ? 'Turn off' : 'Turn on',
        'aria-label': (watcherOn ? 'Turn off ' : 'Turn on ') + 'the Claude desktop app watcher',
        onclick: function () {
          ctx.api.setDesktopWatcher(!watcherOn); // the state push refreshes the row
        }
      })));

    var chromeOn = !!(cn.chrome && cn.chrome.connected);
    box.appendChild(rowFor('Chrome extension',
      'Connects Chrome so Noizes can play sounds for browser events.',
      chromeOn ? statusPill('ok', 'Connected') : statusPill('', 'Not connected'),
      h('button', {
        class: 'nz-btn', type: 'button', text: 'How to add it',
        'aria-label': 'How to add the Chrome extension',
        onclick: function () { openExtensionPanel(ctx); }
      })));

    el.textContent = '';
    el.appendChild(box);
  }

  NZ.registerPage({ id: 'connections', render: render });
})();
