/*
 * Noizes v1.3.0 bridge contract mock (lane G).
 *
 * Fakes window.chrome.webview with realistic made-up state so the UI specs run
 * before the native host (lane A) and the C# bridge (lane B) merge. The specs
 * code against the message contract from section 2 of the master brief, not
 * against the C# implementation.
 *
 * CONTRACT
 *   page to app : window.chrome.webview.postMessage({ id, type, payload? })
 *   app to page : a 'message' MessageEvent on window.chrome.webview carrying
 *                   - reply  { id, ok: true, data? } or { id, ok: false, error }
 *                   - push   { type: 'state', state }   (changes made outside the page)
 *
 * Every event below comes from src/Noizes/EventRegistry.cs with its real group.
 * GitHub is connected; the other integrations are not. A few events carry
 * custom sound names. The Claude desktop event is off and the desktop watcher
 * is off, because the brief makes those two switches one source of truth.
 *
 * Test seam: window.__noizesMock.calls logs every page-to-app message as
 * { id, type, payload, at } and pushState() simulates app-side changes.
 */
(function () {
  'use strict';

  if (window.chrome && window.chrome.webview) return; // never fight the real host

  var GROUPS = ['Coding & AI', 'GitHub', 'Apps', 'Browser', 'Deploys', 'Extras'];

  var EVENTS = [
    // id,                   display name,                        group,        enabled, volume, soundPath, focusApps
    ['claude-code-done',     'Claude Code: finished',             'Coding & AI', true,  80, 'C:\\Users\\denis\\Sounds\\victory-choir.mp3', []],
    ['claude-code-input',    'Claude Code: needs your input',     'Coding & AI', true,  60, '', []],
    ['cursor-done',          'Cursor: agent finished',            'Coding & AI', false, 40, '', []],
    ['gh-push',              'GitHub: new push',                  'GitHub',      true,  50, '', []],
    ['gh-pr-opened',         'GitHub: PR opened',                 'GitHub',      false, 40, '', []],
    ['gh-pr-merged',         'GitHub: PR merged',                 'GitHub',      true,  75, 'C:\\Users\\denis\\Sounds\\Victory Royale.mp3', []],
    ['gh-checks-failed',     'GitHub: checks failed',             'GitHub',      true,  90, '', []],
    ['gh-star',              'GitHub: new star',                  'GitHub',      false, 40, '', []],
    ['gh-issue',             'GitHub: new issue',                 'GitHub',      false, 40, '', []],
    ['gh-comment',           'GitHub: new comment',               'GitHub',      true,  55, '', []],
    ['claude-desktop-done',  'Claude desktop app: finished',      'Apps',        false, 40, '', ['claude']],
    ['browser-claude-done',  'Browser: claude.ai finished',       'Browser',     true,  70, '', ['chrome']],
    ['browser-obvious-done', 'Browser: Obvious finished',         'Browser',     false, 40, '', ['chrome']],
    ['browser-watch-done',   'Browser: watched tab finished',     'Browser',     true,  45, '', ['chrome']],
    ['browser-download-done','Browser: download finished',        'Browser',     false, 40, '', ['chrome']],
    ['deploy-succeeded',     'Deploy: succeeded',                 'Deploys',     true,  65, 'C:\\Users\\denis\\Sounds\\airhorn.mp3', []],
    ['deploy-failed',        'Deploy: failed',                    'Deploys',     true,  85, 'C:\\Users\\denis\\Sounds\\sad-trombone.mp3', []],
    ['uptime-alarm',         'Uptime: a site is down',            'Extras',      true,  100, '', []],
    ['long-command-done',    'Long command finished',             'Extras',      true,  60, '', []],
    ['countdown-1h',         'Countdown: 1 hour left',            'Extras',      false, 40, '', []],
    ['countdown-30m',        'Countdown: 30 minutes left',        'Extras',      true,  70, '', []],
    ['countdown-10m',        'Countdown: 10 minutes left',        'Extras',      false, 40, '', []]
  ];

  function freshState() {
    var events = {};
    EVENTS.forEach(function (row) {
      events[row[0]] = {
        id: row[0],
        name: row[1],
        group: row[2],
        enabled: row[3],
        volume: row[4],
        soundPath: row[5],
        focusApps: row[6].slice()
      };
    });
    return {
      version: '1.3.0',
      page: 'sounds',
      mute: { muted: false, mutedUntil: null },
      groups: GROUPS.slice(),
      events: events,
      connections: {
        github: { connected: true, tokenSet: true, username: 'denis-codes', pollSeconds: 15, repos: ['denis-craft/noizes', 'denis-craft/poker-analytics'] },
        claudeCode: { connected: false },
        cursor: { connected: false },
        desktopWatcher: { enabled: false },
        extension: { installed: false },
        deploys: { connected: false }
      },
      quiet: { enabled: false, start: '22:00', end: '07:00', allowAlarms: true },
      uptime: { urls: ['https://starpool.global'] },
      countdown: { enabled: false, targetLocal: '2026-10-11T12:00:00' },
      general: { startWithWindows: true, onlyWhenUnfocused: true, port: 7351 }
    };
  }

  var state = freshState();
  var calls = [];
  var nextId = 1;
  var listeners = [];

  function fileName(p) {
    return String(p || '').split('\\').pop();
  }

  function dispatch(data) {
    listeners.forEach(function (fn) {
      try { fn({ data: data }); } catch (e) {
        // surface instead of swallow: a listener crash must be visible in tests
        if (typeof console !== 'undefined' && console.error) console.error('mock listener error:', e && e.stack || String(e));
      }
    });
  }

  function reply(id, ok, data, error) {
    setTimeout(function () {
      var msg = { id: id, ok: ok };
      if (data !== undefined) msg.data = data;
      if (error !== undefined) msg.error = error;
      dispatch(msg);
    }, 0);
  }

  var handlers = {
    getState: function () { return { data: state }; },

    setEvent: function (p) {
      var ev = state.events[p.eventId];
      if (!ev) return { error: 'unknown event ' + p.eventId };
      if (p.enabled !== undefined) ev.enabled = !!p.enabled;
      if (p.volume !== undefined) ev.volume = Math.max(0, Math.min(100, Math.round(p.volume)));
      if (p.soundPath !== undefined) ev.soundPath = p.soundPath || '';
      if (p.focusApps !== undefined) ev.focusApps = (p.focusApps || []).slice();
      // one source of truth: the Claude desktop event switch and the watcher agree
      if (p.eventId === 'claude-desktop-done' && p.enabled !== undefined)
        state.connections.desktopWatcher.enabled = ev.enabled;
      return { data: { eventId: ev.id, enabled: ev.enabled, volume: ev.volume, soundPath: ev.soundPath } };
    },

    setGroup: function (p) {
      var changed = [];
      Object.keys(state.events).forEach(function (id) {
        if (state.events[id].group === p.group) {
          state.events[id].enabled = !!p.enabled;
          changed.push(id);
        }
      });
      if (p.group === 'Apps') state.connections.desktopWatcher.enabled = !!p.enabled;
      return { data: { group: p.group, enabled: !!p.enabled, count: changed.length } };
    },

    setMute: function (p) {
      state.mute.muted = !!p.muted;
      state.mute.mutedUntil = p.muted && p.minutes ? new Date(Date.now() + p.minutes * 60000).toISOString() : null;
      return { data: { muted: state.mute.muted, mutedUntil: state.mute.mutedUntil } };
    },

    testSound: function (p) {
      var ev = state.events[p.eventId];
      if (!ev) return { error: 'unknown event ' + p.eventId };
      return { data: { playing: fileName(ev.soundPath) || 'default chime' } };
    },

    chooseSound: function (p) {
      // the real bridge opens the native picker; the mock returns a made-up pick
      var ev = state.events[p.eventId];
      if (!ev) return { error: 'unknown event ' + p.eventId };
      ev.soundPath = 'C:\\Users\\denis\\Sounds\\chosen-radar.mp3';
      return { data: { eventId: ev.id, soundPath: ev.soundPath } };
    },

    resetSound: function (p) {
      var ev = state.events[p.eventId];
      if (!ev) return { error: 'unknown event ' + p.eventId };
      ev.soundPath = '';
      return { data: { eventId: ev.id, soundPath: '' } };
    },

    setupClaudeCode: function () {
      state.connections.claudeCode.connected = true;
      return { data: { connected: true } };
    },

    setupCursor: function () {
      state.connections.cursor.connected = true;
      return { data: { connected: true } };
    },

    saveGitHub: function (p) {
      var gh = state.connections.github;
      gh.username = p.username;
      gh.pollSeconds = p.pollSeconds;
      gh.repos = (p.repos || []).slice();
      if (p.token) gh.tokenSet = true;
      return { data: { saved: true, tokenSet: gh.tokenSet } };
    },

    testGitHub: function () {
      return { data: { ok: true, message: 'Token works. 2 repos watched, polling every 15 seconds.' } };
    },

    setDesktopWatcher: function (p) {
      state.connections.desktopWatcher.enabled = !!p.enabled;
      state.events['claude-desktop-done'].enabled = !!p.enabled;
      return { data: { enabled: !!p.enabled } };
    },

    setGeneral: function (p) {
      Object.assign(state.general, p || {});
      return { data: state.general };
    },

    setQuietHours: function (p) {
      Object.assign(state.quiet, p || {});
      return { data: state.quiet };
    },

    setUptime: function (p) {
      state.uptime.urls = (p.urls || []).slice();
      return { data: state.uptime };
    },

    setCountdown: function (p) {
      state.countdown.enabled = !!p.enabled;
      if (p.targetLocal) state.countdown.targetLocal = p.targetLocal;
      return { data: state.countdown };
    },

    openLog: function () { return { data: { opened: 'log' } }; },
    openFolder: function () { return { data: { opened: 'folder' } }; },
    openUrl: function (p) { return { data: { opened: p.url } }; }
  };

  window.chrome = window.chrome || {};
  window.chrome.webview = {
    postMessage: function (raw) {
      var msg = typeof raw === 'string' ? JSON.parse(raw) : raw;
      calls.push({ id: msg.id, type: msg.type, payload: msg.payload || {}, at: Date.now() });
      var h = handlers[msg.type];
      if (!h) { reply(msg.id, false, undefined, 'unknown message ' + msg.type); return; }
      try {
        var r = h(msg.payload || {});
        if (r && r.error) reply(msg.id, false, undefined, r.error);
        else reply(msg.id, true, r && r.data);
      } catch (e) {
        reply(msg.id, false, undefined, String(e && e.message || e));
      }
    },
    addEventListener: function (type, fn) {
      if (type === 'message') listeners.push(fn);
    },
    removeEventListener: function (type, fn) {
      if (type === 'message') listeners = listeners.filter(function (l) { return l !== fn; });
    }
  };

  // Test seam: inspect every page-to-app message and simulate app-side changes.
  window.__noizesMock = {
    ready: true,
    calls: calls,
    state: function () { return JSON.parse(JSON.stringify(state)); },
    pushState: function () {
      dispatch({ type: 'state', state: JSON.parse(JSON.stringify(state)) });
    },
    setState: function (patch) {
      patch = patch || {};
      if (patch.mute) state.mute = patch.mute;
      if (patch.events) Object.keys(patch.events).forEach(function (id) { Object.assign(state.events[id], patch.events[id]); });
      if (patch.connections) Object.keys(patch.connections).forEach(function (k) { Object.assign(state.connections[k], patch.connections[k]); });
      dispatch({ type: 'state', state: JSON.parse(JSON.stringify(state)) });
    },
    reset: function () {
      state = freshState();
      calls.length = 0;
      dispatch({ type: 'state', state: JSON.parse(JSON.stringify(state)) });
    },
    nextId: function () { return nextId++; }
  };
})();
