/*
 * Noizes v1.3.0 - Sounds page (lane E)
 *
 * Renders the Sounds page from section 4 of the UI brief: search box, one box
 * per event group, rows with one-click toggles, in-place row expansion.
 * Plain JavaScript, no framework, no build step.
 *
 * Integration contract for the shell (lane D):
 *   <script src="pages/sounds.js"></script>   (plain script, then)
 *   NoizesPages.sounds.render(container, bridge)
 *   - bridge is optional. When omitted, this module builds its own bridge on
 *     window.chrome.webview. A custom bridge only needs:
 *       send(type, payload) -> Promise<data>   resolves with reply data
 *       onState(cb)         -> unsubscribe     app pushes, called with state
 *       navigate(page)      -> optional        sidebar jump, Connections etc.
 *   - render returns a destroy() function (removes listeners and styles).
 *   - Navigation links on this page call bridge.navigate('connections') when
 *     available, else dispatch CustomEvent 'noizes:navigate' {page: 'connections'}
 *     on document, else set location.hash = '#connections'.
 *
 * Wire protocol implemented by the built-in bridge (section 2 of the brief):
 *   page to app: window.chrome.webview.postMessage({id, type, ...payload})
 *   app to page: reply {id, ok, data?, error?} and pushes {type: 'state', state}
 *
 * State shape this page reads (lane B produces it; every field has a fallback):
 *   {
 *     events: [ { id, name, group, defaultSound, defaultFocusApps,
 *                 enabled, volume, soundPath, focusApps } ],   // or a map by id
 *     connections: { claudeCode, cursor, github, claudeDesktop,
 *                    chromeExtension, deploy, extras },         // true = connected
 *     groupConnections: { "Group name": bool },                 // optional override
 *     muted, mutedUntil
 *   }
 * Group names and event names always come from state, never hardcoded here.
 */

(function (root, factory) {
  'use strict';
  var api = factory(root);
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.NoizesPages = root.NoizesPages || {};
  root.NoizesPages.sounds = api;
})(typeof self !== 'undefined' ? self : this, function (root) {
  'use strict';

  /* Shell contract adapter (lane D, pages/README.md). Inside the lane D shell
     this module registers with NZ.registerPage like every other page, and the
     shell api is adapted to the bridge shape it expects: send resolves with
     reply data, onState forwards the shell's per-push update hook, navigate
     moves the shell hash. The destroy function returned by render is handed
     back so the shell unsubscribes listeners and timers on page switch. */
  if (root.NZ && typeof root.NZ.registerPage === 'function') {
    var nzDestroy = null;
    var nzPush = null;
    root.NZ.registerPage({
      id: 'sounds',
      render: function (container, ctx) {
        nzPush = null;
        var bridge = {
          send: function (type, payload) {
            var msg = Object.assign({ type: type }, payload || {});
            return ctx.api.request(msg).then(function (reply) {
              if (!reply || reply.ok !== true) {
                throw new Error(reply && reply.error ? reply.error : 'The app did not answer this request.');
              }
              return reply.data;
            });
          },
          onState: function (cb) {
            nzPush = cb;
            return function () { if (nzPush === cb) nzPush = null; };
          },
          navigate: function (page) { location.hash = '#' + page; }
        };
        nzDestroy = render(container, bridge);
        return function () {
          if (nzDestroy) { nzDestroy(); nzDestroy = null; }
          nzPush = null;
        };
      },
      update: function (state) {
        if (nzPush) nzPush(state);
      }
    });
  }

  var SVG_NS = 'http://www.w3.org/2000/svg';

  /* ------------------------------------------------------------------ *
   * Styles (scoped to .snd-page, uses the shell tokens when they exist) *
   * ------------------------------------------------------------------ */

  var SND_CSS = [
    '.snd-page {',
    '  font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;',
    '  color: var(--fg, #f2f2f4);',
    '  background: var(--bg, #0b0b0d);',
    '  padding: 16px 24px 32px;',
    '  box-sizing: border-box;',
    '}',
    '.snd-page *, .snd-page *::before, .snd-page *::after { box-sizing: border-box; }',
    '.snd-search {',
    '  display: block; width: 100%; max-width: 480px; margin: 0 0 16px;',
    '  background: var(--bg, #0b0b0d); color: var(--fg, #f2f2f4);',
    '  border: 1px solid var(--line, #2a2a30); border-radius: 6px;',
    '  padding: 10px 12px; font: inherit; font-size: 14px;',
    '}',
    '.snd-search::placeholder { color: var(--muted, #9a9aa3); }',
    '.snd-search:focus { outline: none; border-color: var(--accent-ink, #9d85ff); }',
    '.snd-group {',
    '  background: var(--surface, #121215);',
    '  border: 1px solid var(--line, #2a2a30);',
    '  border-radius: 8px;',
    '  margin: 0 0 16px;',
    '}',
    '.snd-group-head { display: flex; align-items: center; gap: 12px; padding: 12px 16px; }',
    '.snd-group-name {',
    '  flex: 1; margin: 0; font-size: 13px; font-weight: 600;',
    '  color: var(--muted, #9a9aa3); letter-spacing: 0.02em;',
    '}',
    '.snd-notconnected { margin: 0; padding: 0 16px 10px; font-size: 13px; color: var(--muted, #9a9aa3); }',
    '.snd-navlink { color: var(--info, #6ab8ff); }',
    '.snd-navlink:hover { text-decoration: underline; }',
    '.snd-rows { list-style: none; margin: 0; padding: 0; }',
    '.snd-row {',
    '  display: flex; align-items: center; flex-wrap: wrap; gap: 12px;',
    '  padding: 10px 16px; min-height: 56px;',
    '}',
    '.snd-row + .snd-row { border-top: 1px solid var(--line, #2a2a30); }',
    '.snd-row:hover { background: var(--surface-2, #1c1c20); }',
    '.snd-row.is-off .snd-rowmain { opacity: 0.6; }',
    '.snd-rowmain { flex: 1 1 200px; min-width: 0; }',
    '.snd-rowname {',
    '  display: block; background: none; border: 0; padding: 0; margin: 0;',
    '  font: inherit; font-size: 14px; font-weight: 600; color: var(--fg, #f2f2f4);',
    '  text-align: left; cursor: pointer; overflow-wrap: anywhere;',
    '}',
    '.snd-rowname:hover { text-decoration: underline; }',
    '.snd-rowmeta {',
    '  margin-top: 2px; font-size: 13px; color: var(--muted, #9a9aa3);',
    '  overflow-wrap: anywhere;',
    '}',
    '.snd-play {',
    '  flex: 0 0 auto; width: 32px; height: 32px; padding: 0;',
    '  display: inline-flex; align-items: center; justify-content: center;',
    '  background: var(--surface-2, #1c1c20); color: var(--silver, #e8e8ea);',
    '  border: 1px solid var(--line, #2a2a30); border-radius: 50%; cursor: pointer;',
    '}',
    '.snd-play:hover { border-color: var(--muted, #9a9aa3); }',
    '.snd-icon { width: 14px; height: 14px; display: block; }',
    '.snd-switch {',
    '  flex: 0 0 auto; position: relative; width: 36px; height: 20px; padding: 0;',
    '  border: 1px solid var(--line, #2a2a30); border-radius: 10px;',
    '  background: var(--surface-2, #1c1c20); cursor: pointer;',
    '  transition: background 150ms ease, border-color 150ms ease;',
    '}',
    '.snd-switch .snd-knob {',
    '  position: absolute; top: 2px; left: 2px; width: 14px; height: 14px;',
    '  border-radius: 50%; background: var(--muted, #9a9aa3);',
    '  transition: transform 150ms ease, background 150ms ease;',
    '}',
    '.snd-switch[aria-checked="true"] { background: var(--accent, #7c5cff); border-color: transparent; }',
    '.snd-switch[aria-checked="true"] .snd-knob { transform: translateX(16px); background: #ffffff; }',
    '.snd-switch.is-mixed { border-color: var(--line, #2a2a30); }',
    '.snd-switch.is-mixed .snd-knob { transform: translateX(8px); }',
    '.snd-detail { flex: 1 1 100%; padding: 4px 0 10px; }',
    '.snd-volrow, .snd-soundrow { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; margin: 8px 0; }',
    '.snd-volrow > label { font-size: 13px; color: var(--muted, #9a9aa3); }',
    '.snd-range { width: 220px; max-width: 100%; margin: 0; accent-color: var(--accent, #7c5cff); }',
    '.snd-volout { font-size: 13px; color: var(--silver, #e8e8ea); min-width: 44px; }',
    '.snd-filename { flex: 1 1 120px; min-width: 120px; font-size: 13px; color: var(--muted, #9a9aa3); overflow-wrap: anywhere; }',
    '.snd-btn {',
    '  font: inherit; font-size: 13px; color: var(--fg, #f2f2f4); cursor: pointer;',
    '  background: var(--surface-2, #1c1c20); border: 1px solid var(--line, #2a2a30);',
    '  border-radius: 6px; padding: 6px 12px;',
    '}',
    '.snd-btn:hover { border-color: var(--muted, #9a9aa3); }',
    '.snd-advbtn {',
    '  background: none; border: 0; padding: 4px 0; margin: 4px 0 0; cursor: pointer;',
    '  font: inherit; font-size: 13px; color: var(--muted, #9a9aa3); text-align: left;',
    '}',
    '.snd-advbtn:hover { color: var(--silver, #e8e8ea); }',
    '.snd-advpanel { margin: 8px 0 0; }',
    '.snd-advpanel label { display: block; font-size: 13px; margin: 0 0 6px; }',
    '.snd-advinput {',
    '  display: block; width: 100%; max-width: 420px; padding: 8px 10px;',
    '  background: var(--bg, #0b0b0d); color: var(--fg, #f2f2f4);',
    '  border: 1px solid var(--line, #2a2a30); border-radius: 6px; font: inherit; font-size: 13px;',
    '}',
    '.snd-advinput::placeholder { color: var(--muted, #9a9aa3); }',
    '.snd-advhint { margin: 4px 0 0; font-size: 12px; color: var(--muted, #9a9aa3); }',
    '.snd-empty { margin: 0; padding: 8px 4px; font-size: 13px; color: var(--muted, #9a9aa3); }',
    '.snd-notice { margin: 0 0 12px; font-size: 13px; color: var(--muted, #9a9aa3); }',
    '.snd-alert {',
    '  position: fixed; left: 50%; bottom: 16px; transform: translateX(-50%);',
    '  max-width: 90%; margin: 0; padding: 8px 14px; z-index: 30;',
    '  background: var(--surface-2, #1c1c20); color: var(--fg, #f2f2f4);',
    '  border: 1px solid var(--bad, #ff6f66); border-radius: 6px; font-size: 13px;',
    '}',
    '.snd-page :focus-visible { outline: 2px solid var(--accent-ink, #9d85ff); outline-offset: 2px; }',
    '@media (prefers-reduced-motion: reduce) {',
    '  .snd-page, .snd-page * { transition: none !important; }',
    '}'
  ].join('\n');

  /* ------------------------ tiny DOM helper ------------------------ */

  function el(tag, attrs) {
    var node = root.document.createElement(tag);
    if (attrs) {
      Object.keys(attrs).forEach(function (k) {
        var v = attrs[k];
        if (v === null || v === undefined || v === false) return;
        if (k === 'class') node.className = v;
        else if (k === 'text') node.textContent = v;
        else node.setAttribute(k, v === true ? '' : v);
      });
    }
    return node;
  }

  function playIcon() {
    var svg = root.document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('viewBox', '0 0 16 16');
    svg.setAttribute('class', 'snd-icon');
    svg.setAttribute('aria-hidden', 'true');
    var path = root.document.createElementNS(SVG_NS, 'path');
    path.setAttribute('d', 'M5 3.5v9l7.5-4.5z');
    path.setAttribute('fill', 'currentColor');
    svg.appendChild(path);
    return svg;
  }

  /* ----------------------- pure formatting ------------------------- */

  // "08-success.wav" -> "success", "04-two-tone.wav" -> "two tone"
  function prettyDefaultSound(fileName) {
    if (!fileName) return 'sound';
    var s = String(fileName)
      .replace(/\.[^.]+$/, '')
      .replace(/^\d+[-_]/, '')
      .replace(/[-_]+/g, ' ')
      .trim()
      .toLowerCase();
    return s || 'sound';
  }

  function baseName(path) {
    var parts = String(path).split(/[\\/]/);
    return parts[parts.length - 1] || '';
  }

  // Second line of a row: "Victory Royale.mp3" or "Default chime"
  function soundLabel(ev) {
    if (ev.soundPath) return baseName(ev.soundPath);
    return 'Default ' + prettyDefaultSound(ev.defaultSound);
  }

  function metaText(ev) {
    return soundLabel(ev) + ' \u00B7 ' + ev.volume + '%';
  }

  // "claude-code-done" -> "Claude code done" (fallback when state has no name)
  function labelFromId(id) {
    var s = String(id).replace(/[-_]+/g, ' ').trim();
    return s ? s.charAt(0).toUpperCase() + s.slice(1) : 'Unnamed event';
  }

  // " claude ,  chrome " -> ["claude", "chrome"]
  function parseFocusApps(text) {
    return String(text)
      .split(',')
      .map(function (t) { return t.trim(); })
      .filter(function (t) { return t.length > 0; });
  }

  /* ----------------------- state normalization --------------------- */

  // Group name (lowercased) -> connection keys; connected if any key is true.
  // Keys documented in the header contract for lane B.
  var GROUP_CONNECTION_KEYS = {
    'coding & ai': ['claudeCode', 'cursor'],
    'github': ['github'],
    'apps': ['claudeDesktop'],
    'browser': ['chromeExtension'],
    'deploys': ['deploy'],
    'extras': ['extras']
  };

  function groupConnected(groupName, state) {
    if (state.groupConnections && Object.prototype.hasOwnProperty.call(state.groupConnections, groupName)) {
      return state.groupConnections[groupName] !== false;
    }
    var keys = GROUP_CONNECTION_KEYS[String(groupName).toLowerCase()];
    if (!keys) return true;
    function connOn(v) {
      if (typeof v === 'boolean') return v;
      if (v && typeof v === 'object') return !!(v.connected || v.enabled || v.tokenSet);
      return undefined;
    }
    var known = keys
      .map(function (k) { return state.connections ? connOn(state.connections[k]) : undefined; })
      .filter(function (v) { return typeof v === 'boolean'; });
    if (!known.length) return true; // unknown status: never nag the user
    return known.some(Boolean);
  }

  function normalizeState(raw) {
    var out = { events: [], connections: {}, groupConnections: {}, muted: false, mutedUntil: null };
    if (!raw || typeof raw !== 'object') return out;
    out.muted = !!raw.muted;
    out.mutedUntil = raw.mutedUntil || null;
    if (raw.connections && typeof raw.connections === 'object') out.connections = raw.connections;
    if (raw.groupConnections && typeof raw.groupConnections === 'object') out.groupConnections = raw.groupConnections;
    var list = [];
    if (Array.isArray(raw.sounds)) list = raw.sounds;
    else if (Array.isArray(raw.events)) list = raw.events;
    else if (raw.events && typeof raw.events === 'object') {
      Object.keys(raw.events).forEach(function (k) {
        list.push(Object.assign({ id: k }, raw.events[k]));
      });
    }
    out.events = list.map(function (ev) {
      var id = String(ev.id || '');
      return {
        id: id,
        name: String(ev.name || ev.displayName || labelFromId(id)),
        group: String(ev.group || ev.category || 'Other'),
        defaultSound: String(ev.defaultSound || ''),
        defaultFocusApps: Array.isArray(ev.defaultFocusApps) ? ev.defaultFocusApps.slice() : [],
        enabled: !!ev.enabled,
        volume: isFinite(ev.volume) ? Math.max(0, Math.min(100, Math.round(ev.volume))) : 40,
        soundPath: typeof ev.soundPath === 'string' ? ev.soundPath : '',
        focusApps: Array.isArray(ev.focusApps) ? ev.focusApps.slice() : []
      };
    });
    return out;
  }

  /* --------------------- built-in bridge (WebView2) ---------------- */

  function defaultBridge() {
    var wv = root.chrome && root.chrome.webview;
    var seq = 0;
    var pending = {};
    var stateCbs = [];
    if (wv && typeof wv.addEventListener === 'function') {
      wv.addEventListener('message', function (e) {
        var msg = e.data || {};
        if (msg.type === 'state') {
          var st = msg.state !== undefined ? msg.state : msg.payload;
          stateCbs.forEach(function (cb) { cb(st); });
          return;
        }
        if (msg.id !== undefined && pending[msg.id]) {
          var p = pending[msg.id];
          delete pending[msg.id];
          if (msg.ok) p.resolve(msg.data);
          else p.reject(new Error(msg.error || 'Bridge reply was not ok'));
        }
      });
    }
    return {
      send: function (type, payload) {
        if (!wv || typeof wv.postMessage !== 'function') {
          return Promise.reject(new Error('The Noizes bridge is not available'));
        }
        var id = ++seq;
        var msg = Object.assign({ id: id, type: type }, payload || {});
        return new Promise(function (resolve, reject) {
          pending[id] = { resolve: resolve, reject: reject };
          wv.postMessage(msg);
        });
      },
      onState: function (cb) {
        stateCbs.push(cb);
        return function () {
          var i = stateCbs.indexOf(cb);
          if (i >= 0) stateCbs.splice(i, 1);
        };
      },
      navigate: null // shell may override; fallback lives in render()
    };
  }

  function goConnections(bridge) {
    if (bridge && typeof bridge.navigate === 'function') {
      bridge.navigate('connections');
      return;
    }
    root.document.dispatchEvent(new CustomEvent('noizes:navigate', { detail: { page: 'connections' } }));
    try { root.location.hash = '#connections'; } catch (e) { /* hashless environments */ }
  }

  /* ---------------------------- the page --------------------------- */

  function render(container, bridgeArg) {
    var doc = root.document;
    var bridge = bridgeArg || defaultBridge();
    var model = normalizeState(null);
    var search = '';
    var expandedId = null;
    var advancedOpen = false;
    var suppressRender = false;
    var pendingPush = null;
    var volTimers = {};
    var destroyed = false;

    // inject styles once
    var oldStyle = doc.querySelector('style[data-snd-styles]');
    if (oldStyle) oldStyle.parentNode.removeChild(oldStyle);
    var style = el('style', { 'data-snd-styles': true });
    style.textContent = SND_CSS;
    doc.head.appendChild(style);

    var page = el('div', { class: 'snd-page' });
    container.textContent = '';
    container.appendChild(page);

    var notice = el('p', { class: 'snd-notice', role: 'alert', hidden: true });
    page.appendChild(notice);

    var searchInput = el('input', {
      class: 'snd-search', type: 'search', placeholder: 'Search sounds',
      'aria-label': 'Search sounds', 'data-testid': 'sounds-search', autocomplete: 'off'
    });
    searchInput.addEventListener('input', function () {
      search = searchInput.value;
      refresh();
    });
    page.appendChild(searchInput);

    var groupsBox = el('div', { class: 'snd-groups' });
    page.appendChild(groupsBox);

    var emptyMsg = el('p', { class: 'snd-empty', hidden: true });
    page.appendChild(emptyMsg);

    var alert = el('p', { class: 'snd-alert', role: 'alert', hidden: true });
    page.appendChild(alert);
    var alertTimer = null;
    function showAlert(msg) {
      alert.textContent = String(msg); // textContent: app strings never become markup
      alert.hidden = false;
      if (alertTimer) clearTimeout(alertTimer);
      alertTimer = setTimeout(function () { alert.hidden = true; }, 3000);
    }

    function fail(err) {
      showAlert('Action failed: ' + (err && err.message ? err.message : err));
    }

    function send(msg, payload) {
      return bridge.send(msg, payload).catch(fail);
    }

    function findScrollers() {
      var list = [];
      if (doc.scrollingElement) list.push(doc.scrollingElement);
      var node = container;
      while (node && node !== doc.body) {
        if (node.scrollHeight > node.clientHeight + 1) list.push(node);
        node = node.parentElement;
      }
      return list;
    }

    function refresh(refocusSel) {
      if (suppressRender) { pendingPush = pendingPush || 'refresh'; return; }
      var scrollers = findScrollers();
      var tops = scrollers.map(function (s) { return s.scrollTop; });
      renderGroups();
      scrollers.forEach(function (s, i) { s.scrollTop = tops[i]; });
      if (refocusSel) {
        var target = groupsBox.querySelector(refocusSel);
        if (target) target.focus();
      }
    }

    function toggleExpand(id) {
      if (expandedId === id) {
        expandedId = null;
        advancedOpen = false;
      } else {
        expandedId = id;
        advancedOpen = false;
      }
      refresh();
    }

    function onEscape(e) {
      if (e.key !== 'Escape' || !expandedId) return;
      e.preventDefault();
      e.stopPropagation();
      expandedId = null;
      advancedOpen = false;
      refresh();
    }
    doc.addEventListener('keydown', onEscape, true);

    function applyState(raw) {
      model = normalizeState(raw);
      refresh();
    }

    function unsuppress() {
      suppressRender = false;
      if (pendingPush) {
        var raw = pendingPush === 'refresh' ? null : pendingPush;
        pendingPush = null;
        if (raw) model = normalizeState(raw);
        refresh();
      }
    }

    /* -------------------------- builders --------------------------- */

    function buildSwitch(on, label, onToggle) {
      var sw = el('button', {
        class: 'snd-switch', type: 'button', role: 'switch',
        'aria-checked': on ? 'true' : 'false', 'aria-label': label
      });
      sw.appendChild(el('span', { class: 'snd-knob' }));
      sw.addEventListener('click', function (e) {
        e.stopPropagation();
        onToggle(sw);
      });
      return sw;
    }

    function buildRow(ev) {
      var expanded = expandedId === ev.id;
      var li = el('li', {
        class: 'snd-row' + (ev.enabled ? '' : ' is-off') + (expanded ? ' is-expanded' : ''),
        'data-event-id': ev.id
      });

      var main = el('div', { class: 'snd-rowmain' });
      var nameBtn = el('button', {
        class: 'snd-rowname', type: 'button', text: ev.name,
        'aria-expanded': String(expanded),
        'aria-controls': expanded ? 'snd-detail-' + safeId(ev.id) : null
      });
      nameBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        toggleExpand(ev.id);
      });
      var meta = el('div', { class: 'snd-rowmeta', text: metaText(ev) });
      main.appendChild(nameBtn);
      main.appendChild(meta);
      li.appendChild(main);

      var testBtn = el('button', {
        class: 'snd-play', type: 'button', title: 'Test',
        'aria-label': 'Test ' + ev.name, 'data-testid': 'event-test'
      });
      testBtn.appendChild(playIcon());
      testBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        send('testSound', { eventId: ev.id });
      });
      li.appendChild(testBtn);

      var sw = buildSwitch(ev.enabled, 'Sound switch for ' + ev.name, function () {
        var next = !ev.enabled;
        ev.enabled = next; // one click applies and saves instantly
        refresh('[data-event-id="' + attrSel(ev.id) + '"] [role="switch"]');
        send('setEvent', { eventId: ev.id, enabled: next });
      });
      sw.setAttribute('data-testid', 'event-switch');
      li.appendChild(sw);

      if (expanded) li.appendChild(buildDetail(ev, function () { meta.textContent = metaText(ev); }));

      li.addEventListener('click', function (e) {
        if (e.target.closest('button, input, a, [role="switch"]')) return;
        if (e.target.closest('.snd-detail')) return; // clicks inside detail are not collapses
        toggleExpand(ev.id);
      });
      return li;
    }

    function buildDetail(ev, onVolumeLive) {
      var detail = el('div', { class: 'snd-detail', id: 'snd-detail-' + safeId(ev.id), 'data-testid': 'event-detail' });

      // volume: number updates live, saves are debounced to one per 300ms
      var volId = 'snd-vol-' + safeId(ev.id);
      var volRow = el('div', { class: 'snd-volrow' });
      volRow.appendChild(el('label', { for: volId, text: 'Volume' }));
      var range = el('input', {
        class: 'snd-range', type: 'range', id: volId, min: '0', max: '100', step: '1',
        value: String(ev.volume), 'aria-label': 'Volume for ' + ev.name
      });
      var out = el('span', { class: 'snd-volout', text: ev.volume + '%' });
      range.addEventListener('input', function () {
        ev.volume = Math.round(parseFloat(range.value) || 0);
        out.textContent = ev.volume + '%';
        if (onVolumeLive) onVolumeLive(); // second line stays in sync with the slider
        if (volTimers[ev.id]) clearTimeout(volTimers[ev.id]);
        volTimers[ev.id] = setTimeout(function () {
          delete volTimers[ev.id];
          send('setEvent', { eventId: ev.id, volume: ev.volume });
        }, 300);
      });
      range.addEventListener('focus', function () { suppressRender = true; });
      range.addEventListener('blur', unsuppress);
      volRow.appendChild(range);
      volRow.appendChild(out);
      detail.appendChild(volRow);

      // sound line: file name plus choose, default and test actions
      var soundRow = el('div', { class: 'snd-soundrow' });
      soundRow.appendChild(el('span', { class: 'snd-filename', text: soundLabel(ev) }));
      var chooseBtn = el('button', { class: 'snd-btn', type: 'button', text: 'Choose file', 'data-testid': 'event-choose' });
      chooseBtn.addEventListener('click', function () {
        bridge.send('chooseSound', { eventId: ev.id }).then(function (data) {
          if (data && typeof data.path === 'string') ev.soundPath = data.path;
          else return bridge.send('getState').then(applyState);
          refresh();
        }).catch(fail);
      });
      soundRow.appendChild(chooseBtn);
      var defaultBtn = el('button', { class: 'snd-btn', type: 'button', text: 'Use default', 'data-testid': 'event-default' });
      defaultBtn.addEventListener('click', function () {
        ev.soundPath = '';
        refresh();
        send('resetSound', { eventId: ev.id });
      });
      soundRow.appendChild(defaultBtn);
      var testBtn = el('button', { class: 'snd-btn', type: 'button', text: 'Test', 'data-testid': 'event-test-inline' });
      testBtn.addEventListener('click', function () {
        send('testSound', { eventId: ev.id });
      });
      soundRow.appendChild(testBtn);
      detail.appendChild(soundRow);

      // advanced, collapsed by default
      var advId = 'snd-adv-' + safeId(ev.id);
      var advBtn = el('button', {
        class: 'snd-advbtn', type: 'button', text: 'Advanced',
        'aria-expanded': String(advancedOpen), 'aria-controls': advId
      });
      var advPanel = el('div', { class: 'snd-advpanel', id: advId, hidden: !advancedOpen });
      advBtn.addEventListener('click', function () {
        advancedOpen = !advancedOpen;
        advBtn.setAttribute('aria-expanded', String(advancedOpen));
        advPanel.hidden = !advancedOpen;
      });
      detail.appendChild(advBtn);

      var focusLabelId = 'snd-focus-' + safeId(ev.id);
      advPanel.appendChild(el('label', { id: focusLabelId, for: 'snd-focus-input-' + safeId(ev.id), text: 'Stay quiet while these apps are in front' }));
      var focusInput = el('input', {
        class: 'snd-advinput', type: 'text', id: 'snd-focus-input-' + safeId(ev.id),
        'aria-labelledby': focusLabelId,
        value: ev.focusApps.join(', '),
        placeholder: ev.defaultFocusApps.length ? ev.defaultFocusApps.join(', ') : 'None'
      });
      function commitFocus() {
        var next = parseFocusApps(focusInput.value);
        var before = ev.focusApps.join('\u0000');
        if (next.join('\u0000') === before) return;
        ev.focusApps = next;
        send('setEvent', { eventId: ev.id, focusApps: next });
      }
      focusInput.addEventListener('change', commitFocus);
      focusInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
          e.preventDefault();
          commitFocus();
        }
      });
      advPanel.appendChild(focusInput);
      advPanel.appendChild(el('p', { class: 'snd-advhint', text: 'Comma-separated app names' }));
      detail.appendChild(advPanel);

      return detail;
    }

    function buildGroup(groupName, events) {
      var section = el('section', { class: 'snd-group', 'data-group': groupName, 'aria-label': groupName + ' group' });
      var head = el('div', { class: 'snd-group-head' });
      head.appendChild(el('h3', { class: 'snd-group-name', text: groupName }));

      var allOn = events.every(function (ev) { return ev.enabled; });
      var anyOn = events.some(function (ev) { return ev.enabled; });
      var sw = buildSwitch(allOn, 'Sounds for ' + groupName + ' group', function () {
        var next = !allOn; // mixed or off turns the whole group on
        events.forEach(function (ev) { ev.enabled = next; });
        refresh('[data-group="' + attrSel(groupName) + '"] [role="switch"]');
        send('setGroup', { group: groupName, enabled: next });
      });
      if (!allOn && anyOn) {
        sw.classList.add('is-mixed'); // middle state: knob centered
        sw.setAttribute('data-state', 'mixed');
      }
      sw.setAttribute('data-testid', 'group-switch');
      head.appendChild(sw);
      section.appendChild(head);

      if (!groupConnected(groupName, model)) {
        var nc = el('p', { class: 'snd-notconnected' });
        nc.appendChild(doc.createTextNode('Not connected yet. Set it up in '));
        var link = el('a', { class: 'snd-navlink', href: '#connections' });
        link.textContent = 'Connections'; // group names can contain & and stay text
        link.addEventListener('click', function (e) {
          e.preventDefault();
          goConnections(bridge);
        });
        nc.appendChild(link);
        section.appendChild(nc);
      }

      var ul = el('ul', { class: 'snd-rows' });
      events.forEach(function (ev) { ul.appendChild(buildRow(ev)); });
      section.appendChild(ul);
      return section;
    }

    function renderGroups() {
      groupsBox.textContent = '';
      var q = search.trim().toLowerCase();
      var byGroup = [];
      var index = {};
      model.events.forEach(function (ev) {
        if (q) {
          var hay = (ev.name + ' ' + ev.group + ' ' + soundLabel(ev) + ' ' + ev.id).toLowerCase();
          if (hay.indexOf(q) < 0) return;
        }
        if (!index[ev.group]) {
          index[ev.group] = [];
          byGroup.push({ name: ev.group, events: index[ev.group] });
        }
        index[ev.group].push(ev);
      });

      byGroup.forEach(function (g) {
        groupsBox.appendChild(buildGroup(g.name, g.events));
      });

      var nothing = !byGroup.length;
      emptyMsg.hidden = !nothing;
      if (nothing) emptyMsg.textContent = q ? 'No sounds match "' + search.trim() + '"' : 'No sounds yet';
    }

    /* --------------------------- wire up --------------------------- */

    var unsubState = bridge.onState ? bridge.onState(function (raw) {
      if (destroyed) return;
      if (suppressRender) { pendingPush = raw; return; }
      applyState(raw);
    }) : null;

    if (bridge.onState) {
      bridge.send('getState').then(applyState).catch(function () {
        notice.textContent = 'The Noizes bridge is not available. Open this page inside the Noizes app.';
        notice.hidden = false;
      });
    } else {
      notice.textContent = 'This page expects a bridge with getState support.';
      notice.hidden = false;
    }

    return function destroy() {
      destroyed = true;
      doc.removeEventListener('keydown', onEscape, true);
      if (unsubState) unsubState();
      if (style.parentNode) style.parentNode.removeChild(style);
      Object.keys(volTimers).forEach(function (k) { clearTimeout(volTimers[k]); });
      if (alertTimer) clearTimeout(alertTimer);
    };
  }

  function safeId(id) {
    return String(id).replace(/[^a-zA-Z0-9_-]/g, '_');
  }

  function attrSel(value) {
    return String(value).replace(/\\/g, '\\\\').replace(/"/g, '\\"');
  }

  return { id: 'sounds', title: 'Sounds', render: render, normalizeState: normalizeState };
});
