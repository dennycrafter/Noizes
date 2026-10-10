// Noizes UI, Schedule page (lane F). Plain JS, no framework, no build step.
//
// Shell contract (lane D, pages/README.md): NZ.registerPage({ id, render(el, ctx) }).
// Without an update() hook the shell re-runs render on every state push, so render
// always rebuilds from scratch and the page keeps no cross-render element refs.
//
// State shape this page reads (see dev-stub.js for the canonical demo):
//   quiet {enabled, start "HH:MM", end "HH:MM", allowAlarms}
//   uptimeUrls [{url, status: "up" | "down" | null}]  (null = not checked yet)
//   countdown {enabled, targetLocal "YYYY-MM-DDTHH:MM:SS"}
// Bridge calls used: setQuietHours, setUptime {urls: [string]}, setCountdown,
// openFolder via NZ.request (the shell wrapper drops the which argument).

(function () {
  'use strict';

  if (!window.NZ || typeof window.NZ.registerPage !== 'function') return;

  // Page-local styles: app.css is lane D's file. The countdown size composes
  // with the shell's .nz-num font; the wrap rule mirrors the brief's narrow
  // width behavior.
  var LOCAL_CSS =
    '.nz-row.nzf-wrap{flex-wrap:wrap}' +
    '.nz-row.nzf-wrap .nz-row-actions{margin-left:auto}' +
    // On narrow windows the button drops under the text instead of squeezing it.
    '@media (max-width:640px){' +
    '.nz-row.nzf-wrap .nz-row-main{flex:1 1 100%}' +
    '.nz-row.nzf-wrap .nz-row-actions{flex-basis:100%;justify-content:flex-end;padding-top:8px}}' +
    '.nzf-count{font-size:40px;font-variant-numeric:tabular-nums;padding:4px 16px 16px;display:block}' +
    '.nzf-dot{width:8px;height:8px;border-radius:50%;flex:none;display:inline-block}' +
    '.nzf-time{display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding:12px 16px}';

  function ensureStyle() {
    if (!document.getElementById('nzf-style-schedule')) {
      var st = document.createElement('style');
      st.id = 'nzf-style-schedule';
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

  // ---- countdown ticker: one module-level interval, refreshed by render ----

  var tickTimer = 0;
  var timeLeftEl = null;

  function two(n) { return (n < 10 ? '0' : '') + n; }

  function tick() {
    if (!timeLeftEl || !timeLeftEl.isConnected) return;
    var target = Date.parse(timeLeftEl.dataset.target || '');
    if (isNaN(target)) { timeLeftEl.textContent = ''; return; }
    var left = target - Date.now();
    if (left <= 0) { timeLeftEl.textContent = 'Time is up.'; return; }
    var s = Math.floor(left / 1000);
    var d = Math.floor(s / 86400);
    var hh = Math.floor((s % 86400) / 3600);
    var mm = Math.floor((s % 3600) / 60);
    var ss = s % 60;
    timeLeftEl.textContent = (d > 0 ? d + 'd ' : '') + two(hh) + ':' + two(mm) + ':' + two(ss);
  }

  function startTicker(el) {
    timeLeftEl = el;
    if (tickTimer) window.clearInterval(tickTimer);
    tickTimer = window.setInterval(tick, 1000);
    tick();
  }

  // ---- quiet hours ----

  function quietBox(ctx, q) {
    function sendQuiet() {
      ctx.api.setQuietHours({
        enabled: quietOn,
        start: fromInput.value || q.start || '22:00',
        end: toInput.value || q.end || '07:00',
        allowAlarms: alarmOn
      }); // the state push refreshes the page
    }

    var quietOn = !!q.enabled;
    var alarmOn = q.allowAlarms !== false;

    var fromInput = h('input', {
      class: 'nz-input', type: 'time', 'aria-label': 'Quiet hours start',
      value: q.start || '22:00', onchange: sendQuiet
    });
    var toInput = h('input', {
      class: 'nz-input', type: 'time', 'aria-label': 'Quiet hours end',
      value: q.end || '07:00', onchange: sendQuiet
    });

    return h('div', { class: 'nz-card' },
      h('div', { class: 'nz-row' },
        h('div', { class: 'nz-row-main' },
          h('div', { class: 'nz-row-title', text: 'Quiet hours' })),
        ctx.toggle({ checked: quietOn, label: 'Quiet hours', onchange: function (on) { quietOn = on; sendQuiet(); } })),
      h('div', { class: 'nzf-time' },
        h('span', { class: 'nz-muted', text: 'From' }), fromInput,
        h('span', { class: 'nz-muted', text: 'to' }), toInput),
      h('div', { class: 'nz-row' },
        h('div', { class: 'nz-row-main' },
          h('div', { class: 'nz-row-sub', text: 'Still play the uptime alarm during quiet hours' })),
        ctx.toggle({ checked: alarmOn, label: 'Still play the uptime alarm during quiet hours', onchange: function (on) { alarmOn = on; sendQuiet(); } })));
  }

  // ---- uptime watch ----

  function uptimeBox(ctx, urls) {
    var list = h('div');
    (urls || []).forEach(function (u) {
      var url = u && u.url ? u.url : '';
      if (!url) return;
      var row = h('div', { class: 'nz-row' });
      if (u.status === 'up' || u.status === 'down') {
        var dot = h('span', { class: 'nzf-dot' });
        dot.style.background = u.status === 'up' ? 'var(--ok)' : 'var(--bad)';
        row.appendChild(dot);
      } else {
        row.appendChild(h('span', { class: 'nzf-dot' })); // status not known yet: grey dot
      }
      row.appendChild(h('div', { class: 'nz-row-main' }, h('span', { text: url })));
      row.appendChild(h('button', {
        class: 'nz-iconbtn', type: 'button', 'aria-label': 'Remove ' + url,
        title: 'Remove ' + url,
        onclick: function () {
          var left = (urls || []).map(function (x) { return x.url; }).filter(function (x) { return x && x !== url; });
          ctx.api.setUptime(left); // the state push refreshes the page
        }
      }, ctx.icon('trash', 16)));
      list.appendChild(row);
    });

    var addBtn = h('button', {
      class: 'nz-btn', type: 'button', text: '+ Add a site',
      'aria-label': 'Add a site to watch',
      onclick: function () {
        ctx.panel({
          title: 'Add a site',
          build: function (body, panelApi) {
            var input = h('input', {
              class: 'nz-input', type: 'url', id: 'nzf-add-url',
              placeholder: 'https://example.com', 'aria-label': 'Address to watch'
            });
            var hint = h('span', { class: 'nz-hint', text: '' });
            body.appendChild(h('div', { class: 'nz-field' },
              h('label', { class: 'nz-label', for: 'nzf-add-url', text: 'Address to watch' }),
              input, hint));
            body.appendChild(h('button', {
              class: 'nz-btn nz-btn-primary', type: 'button', text: 'Add',
              onclick: function () {
                var v = (input.value || '').trim();
                if (!/^https?:\/\//i.test(v)) {
                  hint.textContent = 'Use a full address like https://example.com.';
                  return;
                }
                var all = (urls || []).map(function (x) { return x.url; }).filter(Boolean);
                if (all.indexOf(v) !== -1) {
                  hint.textContent = 'That site is already on the list.';
                  return;
                }
                ctx.api.setUptime(all.concat([v]));
                panelApi.close(); // the state push refreshes the page
              }
            }));
          }
        });
      }
    });

    return h('div', { class: 'nz-card' },
      h('div', { class: 'nz-group-head', text: 'Uptime watch' }),
      list,
      h('div', { class: 'nz-card-pad' }, addBtn));
  }

  // ---- countdown ----

  function countdownBox(ctx, cd) {
    var targetLocal = cd.targetLocal || '';
    var datePart = targetLocal ? targetLocal.slice(0, 10) : '';
    var timePart = targetLocal ? targetLocal.slice(11, 16) : '';

    function sendCountdown() {
      if (!dateInput.value || !timeInput.value) return;
      ctx.api.setCountdown({
        enabled: cdOn,
        targetLocal: dateInput.value + 'T' + timeInput.value + ':00'
      }); // the state push refreshes the page
    }

    var cdOn = !!cd.enabled;
    var dateInput = h('input', {
      class: 'nz-input', type: 'date', 'aria-label': 'Countdown date',
      value: datePart, onchange: sendCountdown
    });
    var timeInput = h('input', {
      class: 'nz-input', type: 'time', 'aria-label': 'Countdown time',
      value: timePart, onchange: sendCountdown
    });

    var num = h('span', { class: 'nz-num nzf-count' });
    num.dataset.target = targetLocal;

    return h('div', { class: 'nz-card' },
      h('div', { class: 'nz-row' },
        h('div', { class: 'nz-row-main' },
          h('div', { class: 'nz-row-title', text: 'Countdown' }),
          h('div', { class: 'nz-row-sub', text: 'Plays a sound when the date and time arrive.' })),
        ctx.toggle({ checked: cdOn, label: 'Countdown', onchange: function (on) { cdOn = on; sendCountdown(); } })),
      h('div', { class: 'nzf-time' }, dateInput, timeInput),
      num);
  }

  // ---- long commands ----

  function longCommandsBox(ctx) {
    var cmdText = 'noizes run <command>';
    var input = h('input', {
      class: 'nz-input', type: 'text', readonly: 'readonly',
      value: cmdText, 'aria-label': 'Command to run', onclick: function (e) { e.target.select(); }
    });
    return h('div', { class: 'nz-card' },
      h('div', { class: 'nz-group-head', text: 'Long commands' }),
      h('div', { class: 'nz-card-pad' },
        h('div', { class: 'nz-row-sub', text: 'Run a command through Noizes and get a sound when it finishes.' }),
        h('div', { class: 'nz-actions', style: 'margin-top:8px' },
          input,
          h('button', {
            class: 'nz-btn', type: 'button', text: 'Copy',
            'aria-label': 'Copy the command',
            onclick: function () {
              function done(ok) {
                ctx.toast(ok ? 'Copied.' : 'Could not copy. Select the text and copy it by hand.', ok ? 'ok' : 'bad');
              }
              if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(cmdText).then(function () { done(true); }, function () { fallback(); });
              } else {
                fallback();
              }
              function fallback() {
                input.select();
                try { done(document.execCommand('copy')); } catch (e) { done(false); }
              }
            }
          }))));
  }

  // ---- page ----

  function render(el, ctx) {
    ensureStyle();
    var st = ctx.state || {};

    el.textContent = '';
    el.appendChild(quietBox(ctx, st.quiet || {}));
    el.appendChild(uptimeBox(ctx, Array.isArray(st.uptimeUrls) ? st.uptimeUrls : []));
    el.appendChild(countdownBox(ctx, st.countdown || {}));
    el.appendChild(longCommandsBox(ctx));
    startTicker(el.querySelector('.nzf-count')); // after append, so the first tick renders
  }

  NZ.registerPage({ id: 'schedule', render: render });
})();
