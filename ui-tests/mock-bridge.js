// Mirror of the real UiBridge (src/Noizes/UiBridge.cs) for the Playwright suite:
// flat messages over window.chrome.webview.postMessage, replies {id, ok, data}
// or {id, ok:false, error}, state pushes {type:'state', data:state}, and a state
// shape that matches BuildState exactly (one sounds array, one connections view).
//
// The fixture page (fixture/index.html) bootstraps this before any page module
// loads. Test seams live on window.__noizesMock:
//   .calls         every received message, as {type, payload} (payload = message minus id/type)
//   .postMessage() what the C# side receives from the page
//   .setState()    patch the state object before rendering (mute, github, connections, ...)
//   .reset()       restore the data story and replay the initial state push
//   .pushState()   send a fresh {type:'state', data} push
(function () {
  'use strict';

  // the 22 registry events, at the real ids, names, groups, default sounds and
  // default focus apps from src/Noizes/EventRegistry.cs
  var REGISTRY = [
    ['claude-code-done',     'Claude Code: finished',          '08-success.wav',  [],                'Coding & AI'],
    ['claude-code-input',    'Claude Code: needs your input',  '04-two-tone.wav', [],                'Coding & AI'],
    ['cursor-done',          'Cursor: agent finished',         '01-chime.wav',    [],                'Coding & AI'],
    ['gh-push',              'GitHub: new push',               '03-arp-up.wav',   [],                'GitHub'],
    ['gh-pr-opened',         'GitHub: PR opened',              '07-marimba.wav',  [],                'GitHub'],
    ['gh-pr-merged',         'GitHub: PR merged',              '08-success.wav',  [],                'GitHub'],
    ['gh-checks-failed',     'GitHub: checks failed',          '06-sparkle.wav',  [],                'GitHub'],
    ['claude-desktop-done',  'Claude desktop app: finished',   '01-chime.wav',    ['claude'],        'Apps'],
    ['browser-claude-done',  'Browser: claude.ai finished',    '05-triad.wav',    ['chrome'],        'Browser'],
    ['browser-obvious-done', 'Browser: Obvious finished',      '03-arp-up.wav',   ['chrome'],        'Browser'],
    ['browser-watch-done',   'Browser: watched tab finished',  '02-bell.wav',     ['chrome'],        'Browser'],
    ['browser-download-done','Browser: download finished',     '02-bell.wav',     ['chrome'],        'Browser'],
    ['deploy-succeeded',     'Deploy: succeeded',              '08-success.wav',  [],                'Deploys'],
    ['deploy-failed',        'Deploy: failed',                 '06-sparkle.wav',  [],                'Deploys'],
    ['gh-star',              'GitHub: new star',               '07-marimba.wav',  [],                'GitHub'],
    ['gh-issue',             'GitHub: new issue',              '04-two-tone.wav', [],                'GitHub'],
    ['gh-comment',           'GitHub: new comment',            '05-triad.wav',    [],                'GitHub'],
    ['uptime-alarm',         'Uptime: a site is down',         '06-sparkle.wav',  [],                'Extras'],
    ['long-command-done',    'Long command finished',          '05-triad.wav',    [],                'Extras'],
    ['countdown-1h',         'Countdown: 1 hour left',         '04-two-tone.wav', [],                'Extras'],
    ['countdown-30m',        'Countdown: 30 minutes left',     '04-two-tone.wav', [],                'Extras'],
    ['countdown-10m',        'Countdown: 10 minutes left',     '03-arp-up.wav',   [],                'Extras']
  ];

  var DEFAULTS = {
    version: '1.3.0',
    muted: false,
    mutedUntil: null,
    // the story: two custom sounds, a few events live, everything else fresh-install.
    // two of four Browser events are on (the mixed-group case), both Deploys are on.
    overrides: {
      'claude-code-done':    { enabled: true, volume: 80, soundPath: 'C:\\Users\\you\\Music\\victory.wav' },
      'gh-pr-merged':        { enabled: true, volume: 60, soundPath: 'C:\\Users\\you\\Music\\fanfare.wav' },
      'deploy-succeeded':    { enabled: true },
      'deploy-failed':       { enabled: true },
      'browser-claude-done': { enabled: true },
      'browser-watch-done':  { enabled: true }
    },
    github: { tokenSet: true, username: 'den', pollSeconds: 10, repos: ['den/noizes'] },
    connections: {
      claudeCode:     { connected: true },   // the mock hook file exists
      cursor:         { connected: false },
      claudeDesktop:  { enabled: false },
      chrome:         { connected: false },
      chromeExtension:{ connected: false },
      deploy:         { connected: true },
      extras:         { connected: true }
    },
    quiet: { enabled: false, start: '23:00', end: '07:00', allowAlarms: true },
    uptimeUrls: ['https://den.dev'],
    countdown: { enabled: false, targetLocal: '2026-10-12T17:00:00' },
    general: { port: 7352, startWithWindows: false, onlyWhenUnfocused: true }
  };

  var state = null;
  var calls = [];
  var seq = 0;

  function freshState() {
    var sounds = REGISTRY.map(function (r) {
      var o = DEFAULTS.overrides[r[0]] || {};
      var soundPath = o.soundPath || '';
      return {
        id: r[0],
        name: r[1],
        group: r[4],
        enabled: !!o.enabled,
        volume: typeof o.volume === 'number' ? o.volume : 40,
        soundPath: soundPath,
        hasCustomSound: !!soundPath,
        defaultSound: r[2],
        focusApps: o.focusApps !== undefined ? o.focusApps : r[3],
        defaultFocusApps: r[3]
      };
    });
    var watcherEnabled = DEFAULTS.connections.claudeDesktop.enabled;
    return {
      version: DEFAULTS.version,
      muted: DEFAULTS.muted,
      mutedUntil: DEFAULTS.mutedUntil,
      sounds: sounds,
      github: JSON.parse(JSON.stringify(DEFAULTS.github)),
      watcherEnabled: watcherEnabled,
      connections: Object.assign({ github: JSON.parse(JSON.stringify(DEFAULTS.github)) },
        JSON.parse(JSON.stringify(DEFAULTS.connections))),
      quiet: JSON.parse(JSON.stringify(DEFAULTS.quiet)),
      uptimeUrls: DEFAULTS.uptimeUrls.slice(),
      countdown: JSON.parse(JSON.stringify(DEFAULTS.countdown)),
      general: JSON.parse(JSON.stringify(DEFAULTS.general))
    };
  }

  function findEvent(id) {
    for (var i = 0; i < state.sounds.length; i++) if (state.sounds[i].id === id) return state.sounds[i];
    return null;
  }

  function pushState() {
    // a frozen snapshot, like the real bridge serializing state over the wire
    dispatch({ type: 'state', data: JSON.parse(JSON.stringify(state)) });
  }

  function reply(id, data) {
    dispatch({ id: id, ok: true, data: data === undefined ? null : data });
  }

  function replyError(id, error) {
    dispatch({ id: id, ok: false, error: error });
  }

  // handler table: each mirrors the same-named case in UiBridge.Handle
  var handlers = {
    getState: function (payload, id) { reply(id, state); },
    setEvent: function (payload, id) {
      var ev = findEvent(payload.eventId);
      if (!ev) { replyError(id, 'unknown eventId'); return; }
      if (typeof payload.enabled === 'boolean') ev.enabled = payload.enabled;
      if (typeof payload.volume === 'number') ev.volume = Math.min(100, Math.max(0, Math.round(payload.volume)));
      pushState();
      reply(id, { eventId: ev.id, enabled: ev.enabled, volume: ev.volume });
    },
    setGroup: function (payload, id) {
      (payload.group ? [payload.group] : Object.keys(GROUPS(payload))).forEach(function (g) {
        state.sounds.forEach(function (ev) {
          if (ev.group.toLowerCase() === String(g).toLowerCase()) ev.enabled = payload.enabled !== false;
        });
      });
      pushState();
      reply(id, { enabled: payload.enabled !== false });
    },
    setMute: function (payload, id) {
      state.muted = !!payload.muted;
      state.mutedUntil = payload.muted && payload.minutes
        ? new Date(Date.now() + payload.minutes * 60000).toISOString()
        : (payload.muted ? state.mutedUntil : null);
      pushState();
      reply(id, { muted: state.muted, mutedUntil: state.mutedUntil });
    },
    testSound: function (payload, id) {
      var ev = findEvent(payload.eventId);
      if (!ev) { replyError(id, 'unknown eventId'); return; }
      reply(id, { played: true, reason: 'played ' + (ev.hasCustomSound ? 'the chosen sound' : ev.defaultSound) + ' for ' + ev.id });
    },
    chooseSound: function (payload, id) {
      if (!findEvent(payload.eventId)) { replyError(id, 'unknown eventId'); return; }
      reply(id, { path: null, cancelled: true }); // no native dialog in the browser
    },
    resetSound: function (payload, id) {
      var ev = findEvent(payload.eventId);
      if (!ev) { replyError(id, 'unknown eventId'); return; }
      ev.soundPath = '';
      ev.hasCustomSound = false;
      pushState();
      reply(id, { eventId: ev.id, soundPath: '' });
    },
    saveGitHub: function (payload, id) {
      var gh = state.github;
      if (typeof payload.token === 'string' && payload.token.trim() !== '') gh.token = payload.token.trim();
      if (typeof payload.username === 'string') gh.username = payload.username.trim();
      if (typeof payload.pollSeconds === 'number') gh.pollSeconds = Math.min(300, Math.max(5, payload.pollSeconds));
      if (Array.isArray(payload.repos)) gh.repos = payload.repos;
      gh.tokenSet = typeof gh.token === 'string' && gh.token.trim() !== '';
      state.connections.github = JSON.parse(JSON.stringify(gh));
      pushState();
      reply(id, JSON.parse(JSON.stringify(gh)));
    },
    testGitHub: function (payload, id) {
      if (!state.github.tokenSet) {
        replyError(id, 'No token saved yet. Paste one and press Save first.');
        return;
      }
      reply(id, { login: state.github.username || 'you' });
    },
    setDesktopWatcher: function (payload, id) {
      if (typeof payload.enabled !== 'boolean') { replyError(id, 'setDesktopWatcher needs enabled'); return; }
      state.watcherEnabled = payload.enabled;
      state.connections.claudeDesktop.enabled = payload.enabled;
      // one toggle drives the claude-desktop event and the watcher, as in production
      var ev = findEvent('claude-desktop-done');
      if (ev) ev.enabled = payload.enabled;
      pushState();
      reply(id, { watcherEnabled: payload.enabled });
    },
    setQuietHours: function (payload, id) {
      if (typeof payload.enabled === 'boolean') state.quiet.enabled = payload.enabled;
      if (typeof payload.start === 'string') state.quiet.start = payload.start.trim();
      if (typeof payload.end === 'string') state.quiet.end = payload.end.trim();
      if (typeof payload.allowAlarms === 'boolean') state.quiet.allowAlarms = payload.allowAlarms;
      pushState();
      reply(id, { enabled: state.quiet.enabled, start: state.quiet.start, end: state.quiet.end, allowAlarms: state.quiet.allowAlarms });
    },
    setUptime: function (payload, id) {
      if (!Array.isArray(payload.urls)) { replyError(id, 'setUptime needs a urls array'); return; }
      state.uptimeUrls = payload.urls;
      pushState();
      reply(id, { uptimeUrls: state.uptimeUrls });
    },
    setCountdown: function (payload, id) {
      if (typeof payload.enabled === 'boolean') state.countdown.enabled = payload.enabled;
      if (typeof payload.targetLocal === 'string') state.countdown.targetLocal = payload.targetLocal;
      pushState();
      reply(id, { enabled: state.countdown.enabled, targetLocal: state.countdown.targetLocal });
    },
    setGeneral: function (payload, id) {
      var g = state.general;
      var restartRequired = false;
      if (typeof payload.startWithWindows === 'boolean') g.startWithWindows = payload.startWithWindows;
      if (typeof payload.onlyWhenUnfocused === 'boolean') g.onlyWhenUnfocused = payload.onlyWhenUnfocused;
      if (typeof payload.port === 'number' && payload.port >= 1024 && payload.port <= 65535 && payload.port !== g.port) {
        g.port = payload.port;
        restartRequired = true; // applies at the next app start
      }
      pushState();
      reply(id, { port: g.port, startWithWindows: g.startWithWindows, onlyWhenUnfocused: g.onlyWhenUnfocused, restartRequired: restartRequired });
    },
    openLog: function (payload, id) { reply(id, null); },
    openFolder: function (payload, id) { reply(id, null); },
    openUrl: function (payload, id) {
      var url = payload.url || '';
      if (url.indexOf('http://') !== 0 && url.indexOf('https://') !== 0) {
        replyError(id, 'only http and https links can be opened');
        return;
      }
      reply(id, null);
    }
  };

  // group membership by group name, mirroring GROUP_CONNECTION_KEYS usage on the
  // Sounds page: only groups with a connection state can be "not connected"
  function GROUPS() { return { 'github': 1, 'coding & ai': 1, 'apps': 1, 'browser': 1 }; }

  window.chrome = window.chrome || {};
  var listeners = [];
  function dispatch(data) {
    listeners.forEach(function (fn) {
      try { fn({ data: data }); } catch (e) {
        // surface instead of swallow: a listener crash must be visible in tests
        if (typeof console !== 'undefined' && console.error) console.error('mock listener error:', e && e.stack || String(e));
      }
    });
  }
  window.chrome.webview = {
    addEventListener: function (type, fn) {
      if (type === 'message') listeners.push(fn);
    },
    removeEventListener: function (type, fn) {
      if (type === 'message') listeners = listeners.filter(function (l) { return l !== fn; });
    },
    postMessage: function (msg) {
      if (!msg || typeof msg !== 'object') return;
      if (msg.type === 'state') return; // a push, not a request (never sent by pages)
      var type = msg.type;
      if (!type) return;
      var payload = {};
      for (var k in msg) if (k !== 'type' && k !== 'id') payload[k] = msg[k];
      calls.push({ type: type, payload: payload });
      var handler = handlers[type];
      if (!handler) { replyError(msg.id, 'unknown message type: ' + type); return; }
      handler(payload, msg.id);
    }
  };

  // test seams, same names as before the rewrite
  window.__noizesMock = {
    get calls() { return calls.slice(); },
    postMessage: function (msg) { window.chrome.webview.postMessage(msg); },
    setState: function (patch) {
      Object.keys(patch || {}).forEach(function (k) {
        state[k] = patch[k];
      });
      pushState();
    },
    reset: function () {
      state = freshState();
      calls.length = 0;
      pushState();
    },
    pushState: pushState,
    get state() { return state; }
  };

  state = freshState();
  pushState();
})();
