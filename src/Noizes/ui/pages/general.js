// Noizes UI, General page (lane F). Plain JS, no framework, no build step.
//
// Shell contract (lane D, pages/README.md): NZ.registerPage({ id, render(el, ctx) }).
// Without an update() hook the shell re-runs render on every state push, so render
// always rebuilds from scratch.
//
// State shape this page reads (see dev-stub.js for the canonical demo):
//   general {startWithWindows, onlyWhenUnfocused, port, portRestartNeeded}
// Bridge calls used: setGeneral {field: value}, openLog, and openFolder via
// NZ.request (the shell wrapper drops the which argument from section 2).
//
// The port hint also honors state.general.portRestartNeeded, which the app sets
// when a saved port still needs a restart to take effect.

(function () {
  'use strict';

  if (!window.NZ || typeof window.NZ.registerPage !== 'function') return;

  var LOCAL_CSS = '.nz-row.nzf-wrap{flex-wrap:wrap}.nz-row.nzf-wrap .nz-row-actions{margin-left:auto}' +
    '@media (max-width:640px){.nz-row.nzf-wrap .nz-row-main{flex:1 1 100%}' +
    '.nz-row.nzf-wrap .nz-row-actions{flex-basis:100%;justify-content:flex-end;padding-top:8px}}';

  function ensureStyle() {
    if (!document.getElementById('nzf-style-general')) {
      var st = document.createElement('style');
      st.id = 'nzf-style-general';
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

  // Module level so the hint survives state-push rerenders; a real app restart
  // reloads the page and clears it.
  var portChanged = false;

  function row(title, sub, control) {
    return h('div', { class: 'nz-row nzf-wrap' },
      h('div', { class: 'nz-row-main' },
        h('div', { class: 'nz-row-title', text: title }),
        sub ? h('div', { class: 'nz-row-sub', text: sub }) : null),
      h('div', { class: 'nz-row-actions' }, control));
  }

  function render(el, ctx) {
    ensureStyle();
    var st = ctx.state || {};
    var g = st.general || {};

    var portInput = h('input', {
      class: 'nz-input', type: 'number', id: 'nzf-port', min: '1', max: '65535',
      value: String(typeof g.port === 'number' ? g.port : 7351),
      'aria-label': 'Local server port'
    });
    var portHint = h('span', { class: 'nz-hint nzf-restart-hint', text: 'Restart Noizes to apply.' });
    portHint.hidden = true;

    function sendPort() {
      var v = Math.round(Number(portInput.value));
      if (!isFinite(v) || v < 1 || v > 65535) {
        portInput.value = String(typeof g.port === 'number' ? g.port : 7351);
        return;
      }
      if (v !== g.port) {
        portChanged = true;
        portHint.hidden = false;
        ctx.api.setGeneral({ port: v }); // the state push refreshes the page
      }
    }
    portInput.addEventListener('change', sendPort);

    function refreshHint() {
      portHint.hidden = !portChanged && !g.portRestartNeeded;
    }
    refreshHint();

    var startup = ctx.toggle({
      checked: !!g.startWithWindows, label: 'Start Noizes when Windows starts',
      onchange: function (on) {
        ctx.api.setGeneral({ startWithWindows: on });
      }
    });

    var focus = ctx.toggle({
      checked: g.onlyWhenUnfocused !== false,
      label: 'Only play when I am not looking at the app that finished',
      onchange: function (on) {
        ctx.api.setGeneral({ onlyWhenUnfocused: on });
      }
    });

    var openSettingsBtn = h('button', {
      class: 'nz-btn', type: 'button', text: 'Open settings file folder',
      'aria-label': 'Open settings file folder',
      onclick: function () {
        NZ.request({ type: 'openFolder', which: 'settings' }).then(function (reply) {
          if (!reply || !reply.ok) ctx.toast('Could not open the settings folder. ' + errorText(reply), 'bad');
        });
      }
    });

    var openLogBtn = h('button', {
      class: 'nz-btn', type: 'button', text: 'Open log',
      'aria-label': 'Open log',
      onclick: function () {
        ctx.api.openLog().then(function (reply) {
          if (!reply || !reply.ok) ctx.toast('Could not open the log. ' + errorText(reply), 'bad');
        });
      }
    });

    var page = h('div');
    page.appendChild(h('div', { class: 'nz-card' },
      row('Start Noizes when Windows starts', '', startup)));
    page.appendChild(h('div', { class: 'nz-card' },
      row('Only play when I am not looking at the app that finished',
        'Skips the sound while the app that finished is already in front of you.',
        focus)));
    page.appendChild(h('div', { class: 'nz-card' },
      row('Local server port', 'The hooks, the CLI and the Chrome extension call Noizes on this port.', portInput),
      h('div', { class: 'nz-card-pad' }, portHint)));
    page.appendChild(h('div', { class: 'nz-card' },
      h('div', { class: 'nz-row nzf-wrap' },
        h('div', { class: 'nz-row-main' },
          h('div', { class: 'nz-row-title', text: 'Files' })),
        h('div', { class: 'nz-row-actions' }, openSettingsBtn, openLogBtn))));

    el.textContent = '';
    el.appendChild(page);
  }

  NZ.registerPage({ id: 'general', render: render });
})();
