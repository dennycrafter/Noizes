/*
 * TEST FIXTURE behavior (lane G): the minimal page logic the ui-tests suite
 * needs before the real UI (lanes D, E, F) lands. Mirrors the bridge contract
 * in ui-tests/mock-bridge.js. Do not ship this fixture in the app.
 */
(function () {
  'use strict';

  // ---------- bridge client ----------
  var pending = {};
  var seq = 0;

  function send(type, payload) {
    return new Promise(function (resolve, reject) {
      var id = 'm' + (++seq);
      pending[id] = { resolve: resolve, reject: reject };
      window.chrome.webview.postMessage({ id: id, type: type, payload: payload || {} });
    });
  }

  window.chrome.webview.addEventListener('message', function (e) {
    var msg = e.data || {};
    if (msg.type === 'state' && msg.state) { applyState(msg.state); return; }
    var p = pending[msg.id];
    if (!p) return;
    delete pending[msg.id];
    if (msg.ok) p.resolve(msg.data);
    else p.reject(new Error(msg.error || 'bridge error'));
  });

  // ---------- state ----------
  var state = null;
  var page = 'sounds';
  var expandedEventId = null;
  var query = '';
  var volumeTimers = {};

  function applyState(next) {
    state = next;
    renderAll();
  }

  // ---------- helpers ----------
  function el(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text !== undefined) n.textContent = text;
    return n;
  }

  function fileName(p) { return String(p || '').split('\\').pop(); }

  function subLine(ev) {
    var sound = ev.soundPath ? fileName(ev.soundPath) : 'Default chime';
    return sound + ' · ' + ev.volume + '%';
  }

  function muteButtonText() {
    var m = state.mute;
    if (m.mutedUntil) {
      var t = new Date(m.mutedUntil).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
      return 'Muted until ' + t;
    }
    if (m.muted) return 'Muted until you unmute';
    return 'Mute 1 hour';
  }

  function groupState(group) {
    var rows = Object.keys(state.events).map(function (id) { return state.events[id]; })
      .filter(function (ev) { return ev.group === group; });
    var on = rows.filter(function (ev) { return ev.enabled; }).length;
    if (on === 0) return 'false';
    if (on === rows.length) return 'true';
    return 'mixed';
  }

  var CONNECTION_FOR_GROUP = {
    'Coding & AI': function (c) { return c.claudeCode.connected || c.cursor.connected; },
    'GitHub': function (c) { return c.github.connected; },
    'Apps': function (c) { return c.desktopWatcher.enabled; },
    'Browser': function (c) { return c.extension.installed; },
    'Deploys': function (c) { return c.deploys.connected; },
    'Extras': null
  };

  function toast(text) {
    var root = document.getElementById('toast-root');
    var t = el('div', 'toast', text);
    root.appendChild(t);
    setTimeout(function () { t.remove(); }, 3000);
  }

  // ---------- header ----------
  function renderHeader() {
    var muted = state.mute.muted;
    var master = document.getElementById('master-switch');
    master.setAttribute('aria-checked', muted ? 'false' : 'true');
    document.getElementById('master-label').textContent = muted ? 'Sounds off' : 'Sounds on';
    document.getElementById('mute-1h').textContent = muteButtonText();
  }

  // ---------- sounds page ----------
  function renderGroups() {
    var root = document.getElementById('groups');
    root.textContent = '';
    state.groups.forEach(function (group) {
      var evs = Object.keys(state.events).map(function (id) { return state.events[id]; })
        .filter(function (ev) { return ev.group === group; });
      var q = query.trim().toLowerCase();
      var visible = evs.filter(function (ev) {
        return !q || ev.name.toLowerCase().indexOf(q) >= 0 || group.toLowerCase().indexOf(q) >= 0;
      });
      if (q && visible.length === 0) return;

      var box = el('div', 'group-box');
      box.dataset.group = group;
      var head = el('div', 'group-head');
      var name = el('span', 'group-name', group);
      var sw = el('button', 'switch');
      sw.setAttribute('role', 'switch');
      var gs = groupState(group); // 'true' | 'false' | 'mixed'
      // aria-checked stays binary for the switch role; the middle state is
      // carried by data-state (drawn as a dash) and said in the label
      sw.setAttribute('aria-checked', gs === 'true' ? 'true' : 'false');
      sw.setAttribute('data-state', gs);
      sw.setAttribute('aria-label', group + ' sounds, ' + (gs === 'mixed' ? 'some on' : gs === 'true' ? 'all on' : 'all off'));
      sw.addEventListener('click', function () {
        var turnOn = groupState(group) !== 'true'; // mixed or off turns all on, all on turns off
        send('setGroup', { group: group, enabled: turnOn }).then(function () {
          evs.forEach(function (ev) { ev.enabled = turnOn; });
          renderGroups();
        });
      });
      head.appendChild(name);
      head.appendChild(sw);
      box.appendChild(head);

      var connCheck = CONNECTION_FOR_GROUP[group];
      if (connCheck && !connCheck(state.connections)) {
        var empty = el('p', 'empty-line');
        empty.appendChild(document.createTextNode('Not connected yet. Set it up in '));
        var link = el('a', null, 'Connections');
        link.href = '#';
        link.addEventListener('click', function (e2) { e2.preventDefault(); showPage('connections'); });
        empty.appendChild(link);
        box.appendChild(empty);
      }

      evs.forEach(function (ev) { box.appendChild(renderRow(ev, visible.indexOf(ev) >= 0)); });
      root.appendChild(box);
    });
  }

  function renderRow(ev, visible) {
    var row = el('div', 'event-row');
    row.dataset.event = ev.id;
    row.dataset.disabled = ev.enabled ? 'false' : 'true';
    if (!visible) row.hidden = true;

    var line = el('div', 'event-line');
    var main = el('div', 'event-main');
    main.appendChild(el('div', 'event-name', ev.name));
    main.appendChild(el('div', 'event-sub', subLine(ev)));
    line.appendChild(main);

    var play = el('button', 'play-btn');
    play.setAttribute('aria-label', 'Test ' + ev.name);
    play.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14l11-7z" fill="currentColor"/></svg>';
    play.addEventListener('click', function () {
      send('testSound', { eventId: ev.id }).then(function (d) {
        toast('Test: ' + d.playing);
      });
    });
    line.appendChild(play);

    var sw = el('button', 'switch');
    sw.setAttribute('role', 'switch');
    sw.setAttribute('aria-checked', ev.enabled ? 'true' : 'false');
    sw.setAttribute('aria-label', ev.name);
    sw.addEventListener('click', function () {
      send('setEvent', { eventId: ev.id, enabled: !ev.enabled }).then(function (d) {
        ev.enabled = d.enabled;
        row.dataset.disabled = d.enabled ? 'false' : 'true';
        sw.setAttribute('aria-checked', d.enabled ? 'true' : 'false');
      });
    });
    line.appendChild(sw);

    line.addEventListener('click', function (e2) {
      if (e2.target.closest('button')) return; // the switch and play button have their own jobs
      expandedEventId = expandedEventId === ev.id ? null : ev.id;
      renderGroups();
    });
    row.appendChild(line);

    if (expandedEventId === ev.id) {
      var detail = el('div', 'event-detail');

      var vline = el('div', 'volume-line');
      var slider = document.createElement('input');
      slider.type = 'range';
      slider.min = '0'; slider.max = '100'; slider.step = '1'; slider.value = String(ev.volume);
      slider.setAttribute('aria-label', 'Volume for ' + ev.name);
      var num = el('span', 'volume-num', ev.volume + '%');
      slider.addEventListener('input', function () {
        num.textContent = slider.value + '%';
        clearTimeout(volumeTimers[ev.id]);
        volumeTimers[ev.id] = setTimeout(function () { // debounce: one save per 300ms
          send('setEvent', { eventId: ev.id, volume: Number(slider.value) }).then(function (d) {
            ev.volume = d.volume;
            main.querySelector('.event-sub').textContent = subLine(ev);
          });
        }, 300);
      });
      vline.appendChild(slider);
      vline.appendChild(num);
      detail.appendChild(vline);

      var sline = el('div', 'sound-line');
      sline.appendChild(el('span', 'sound-file', ev.soundPath ? fileName(ev.soundPath) : 'Default chime'));
      var choose = el('button', 'btn', 'Choose file');
      choose.addEventListener('click', function () {
        send('chooseSound', { eventId: ev.id }).then(function (d) {
          ev.soundPath = d.soundPath;
          renderGroups();
          toast('Sound set to ' + fileName(d.soundPath));
        });
      });
      var reset = el('button', 'btn', 'Use default');
      reset.addEventListener('click', function () {
        send('resetSound', { eventId: ev.id }).then(function () {
          ev.soundPath = '';
          renderGroups();
        });
      });
      sline.appendChild(choose);
      sline.appendChild(reset);
      detail.appendChild(sline);

      var adv = document.createElement('details');
      adv.className = 'advanced-line';
      var sum = el('summary', null, 'Advanced');
      var wrap = el('div');
      var lbl = el('label', null, 'Stay quiet while these apps are in front');
      var input = document.createElement('input');
      input.className = 'focus-apps';
      input.placeholder = 'claude, chrome';
      input.value = ev.focusApps.join(', ');
      lbl.appendChild(input);
      wrap.appendChild(lbl);
      adv.appendChild(sum);
      adv.appendChild(wrap);
      detail.appendChild(adv);

      row.appendChild(detail);
    }
    return row;
  }

  // search filter is live
  function bindSearch() {
    document.getElementById('search').addEventListener('input', function (e) {
      query = e.target.value || '';
      renderGroups();
    });
  }

  // ---------- connections page ----------
  var CONN_DEFS = [
    { key: 'claudeCode', name: 'Claude Code', desc: 'Plays sounds when Claude Code finishes or needs your input.', on: function (c) { return c.claudeCode.connected; }, btn: 'Set up', act: function () { return send('setupClaudeCode').then(function () { toast('Claude Code connected. Open a new Claude Code session.'); }); } },
    { key: 'cursor', name: 'Cursor', desc: 'Plays a sound when a Cursor agent run finishes.', on: function (c) { return c.cursor.connected; }, btn: 'Set up', act: function () { return send('setupCursor').then(function () { toast('Cursor connected. Open a new Cursor session.'); }); } },
    { key: 'github', name: 'GitHub', desc: 'Watches your GitHub feed for pushes, pull requests, stars and more.', on: function (c) { return c.github.connected; }, btn: 'Edit', act: function () { openGitHubPanel(); } },
    { key: 'desktopWatcher', name: 'Claude desktop app', desc: 'Watches the Claude desktop app for finished answers.', on: function (c) { return c.desktopWatcher.enabled; }, btn: 'Turn on', act: function () { return send('setDesktopWatcher', { enabled: true }).then(function () { state.connections.desktopWatcher.enabled = true; state.events['claude-desktop-done'].enabled = true; renderAll(); }); } },
    { key: 'extension', name: 'Chrome extension', desc: 'Plays sounds for browser tabs and downloads in Chrome.', on: function (c) { return c.extension.installed; }, btn: 'How to add it', act: function () { openExtensionPanel(); } }
  ];

  function renderConnections() {
    var box = document.getElementById('connections-box');
    box.textContent = '';
    CONN_DEFS.forEach(function (def) {
      var connected = def.on(state.connections);
      var row = el('div', 'conn-row');
      var main = el('div', 'conn-main');
      main.appendChild(el('div', 'conn-name', def.name));
      main.appendChild(el('div', 'conn-desc', def.desc));
      row.appendChild(main);
      var st = el('span', 'status ' + (connected ? 'connected' : 'off'));
      st.appendChild(el('span', 'dot'));
      st.appendChild(document.createTextNode(connected ? 'Connected' : 'Not connected'));
      row.appendChild(st);
      var btn = el('button', 'btn', connected ? 'Edit' : def.btn);
      btn.addEventListener('click', function () { def.act(); });
      row.appendChild(btn);
      box.appendChild(row);
    });
  }

  // ---------- floating panel plumbing ----------
  var panelEscBound = false;

  function closePanel() {
    document.getElementById('panel-root').textContent = '';
  }

  function bindEscape() {
    if (panelEscBound) return;
    panelEscBound = true;
    document.addEventListener('keydown', function (e) {
      if (e.key !== 'Escape') return;
      if (document.querySelector('.overlay')) { closePanel(); return; }
      if (expandedEventId) { expandedEventId = null; renderGroups(); }
    });
  }

  function openPanel(title, build) {
    closePanel();
    var overlay = el('div', 'overlay');
    var panel = el('div', 'panel');
    var head = el('div', 'panel-head');
    head.appendChild(el('h2', null, title));
    var x = el('button', 'panel-x', 'X');
    x.setAttribute('aria-label', 'Close');
    x.addEventListener('click', closePanel);
    head.appendChild(x);
    panel.appendChild(head);
    build(panel);
    overlay.appendChild(panel);
    overlay.addEventListener('click', function (e) { if (e.target === overlay) closePanel(); });
    document.getElementById('panel-root').appendChild(overlay);
    bindEscape();
  }

  function openGitHubPanel() {
    var gh = state.connections.github;
    openPanel('GitHub', function (panel) {
      var tokenSet = gh.tokenSet;

      var tokenField = el('span', 'field');
      function drawToken() {
        tokenField.textContent = '';
        var label = el('span', null, 'Token');
        tokenField.appendChild(label);
        if (tokenSet) {
          var row = el('span', 'row');
          row.appendChild(el('span', 'token-saved', 'Token saved'));
          var rep = el('button', 'btn', 'Replace');
          rep.addEventListener('click', function () { tokenSet = false; drawToken(); });
          row.appendChild(rep);
          tokenField.appendChild(row);
        } else {
          var inp = document.createElement('input');
          inp.type = 'password';
          inp.setAttribute('aria-label', 'GitHub token');
          inp.id = 'gh-token';
          tokenField.appendChild(inp);
        }
      }
      drawToken();
      panel.appendChild(tokenField);

      function field(labelText, inputEl) {
        var f = el('label', 'field');
        f.appendChild(el('span', null, labelText));
        f.appendChild(inputEl);
        return f;
      }

      var user = document.createElement('input');
      user.type = 'text'; user.id = 'gh-user'; user.value = gh.username;
      panel.appendChild(field('Username', user));

      var poll = document.createElement('input');
      poll.type = 'number'; poll.id = 'gh-poll'; poll.min = '5'; poll.max = '300'; poll.value = String(gh.pollSeconds);
      panel.appendChild(field('Check every N seconds', poll));

      var repos = document.createElement('textarea');
      repos.id = 'gh-repos'; repos.rows = '3';
      repos.value = gh.repos.join('\n');
      panel.appendChild(field('Repos, one per line', repos));

      var testRow = el('div', 'row');
      var testBtn = el('button', 'btn', 'Test');
      var result = el('p', 'test-result', '');
      testBtn.addEventListener('click', function () {
        send('testGitHub').then(function (d) { result.textContent = d.message; });
      });
      testRow.appendChild(testBtn);
      panel.appendChild(testRow);
      panel.appendChild(result);

      var saveRow = el('div', 'row');
      saveRow.appendChild(el('span', 'spacer'));
      var save = el('button', 'btn accent', 'Save');
      save.addEventListener('click', function () {
        var payload = {
          username: user.value,
          pollSeconds: Number(poll.value),
          repos: repos.value.split('\n').map(function (s) { return s.trim(); }).filter(Boolean)
        };
        var tokenInput = panel.querySelector('#gh-token');
        if (tokenInput && tokenInput.value) payload.token = tokenInput.value;
        send('saveGitHub', payload).then(function (d) {
          gh.tokenSet = d.tokenSet;
          gh.username = payload.username;
          gh.pollSeconds = payload.pollSeconds;
          gh.repos = payload.repos;
          closePanel();
          renderConnections();
          toast('GitHub settings saved');
        });
      });
      saveRow.appendChild(save);
      panel.appendChild(saveRow);
    });
  }

  function openExtensionPanel() {
    openPanel('Chrome extension', function (panel) {
      var steps = el('ol');
      ['Open chrome://extensions', 'Turn on Developer mode', 'Choose Load unpacked', 'Pick the extension folder'].forEach(function (s) {
        steps.appendChild(el('li', null, s));
      });
      panel.appendChild(steps);
      var row = el('div', 'row');
      var btn = el('button', 'btn', 'Open extension folder');
      btn.addEventListener('click', function () { send('openFolder', {}); });
      row.appendChild(btn);
      panel.appendChild(row);
    });
  }

  // ---------- schedule page ----------
  function renderSchedule() {
    setSwitch('quiet-switch', state.quiet.enabled);
    document.getElementById('quiet-start').value = state.quiet.start;
    document.getElementById('quiet-end').value = state.quiet.end;
    setSwitch('quiet-alarms', state.quiet.allowAlarms);

    var list = document.getElementById('uptime-list');
    list.textContent = '';
    state.uptime.urls.forEach(function (url, i) {
      var row = el('div', 'url-row');
      row.appendChild(el('span', 'url', url));
      var del = el('button', 'icon-btn', 'Delete');
      del.setAttribute('aria-label', 'Delete ' + url);
      del.addEventListener('click', function () {
        var urls = state.uptime.urls.slice();
        urls.splice(i, 1);
        send('setUptime', { urls: urls }).then(function () { state.uptime.urls = urls; renderSchedule(); });
      });
      row.appendChild(del);
      list.appendChild(row);
    });

    setSwitch('countdown-switch', state.countdown.enabled);
    var left = el('p', 'countdown-big');
    left.id = 'countdown-left';
    var target = new Date(state.countdown.targetLocal);
    var ms = target - new Date();
    left.textContent = state.countdown.enabled && ms > 0
      ? Math.floor(ms / 3600000) + ' hours ' + Math.floor((ms % 3600000) / 60000) + ' minutes left'
      : 'No countdown running';
    var old = document.getElementById('countdown-left');
    old.parentNode.replaceChild(left, old);
  }

  function setSwitch(id, on) {
    document.getElementById(id).setAttribute('aria-checked', on ? 'true' : 'false');
  }

  function bindSchedule() {
    document.getElementById('quiet-switch').addEventListener('click', function () {
      var enabled = state.quiet.enabled = !state.quiet.enabled;
      send('setQuietHours', { enabled: enabled }).then(function () { setSwitch('quiet-switch', enabled); });
    });
    document.getElementById('quiet-alarms').addEventListener('click', function () {
      var allow = state.quiet.allowAlarms = !state.quiet.allowAlarms;
      send('setQuietHours', { allowAlarms: allow }).then(function () { setSwitch('quiet-alarms', allow); });
    });
    ['quiet-start', 'quiet-end'].forEach(function (id) {
      document.getElementById(id).addEventListener('change', function (e) {
        state.quiet[id === 'quiet-start' ? 'start' : 'end'] = e.target.value;
        send('setQuietHours', state.quiet);
      });
    });
    document.getElementById('add-site').addEventListener('click', function () {
      openPanel('Add a site', function (panel) {
        var input = document.createElement('input');
        input.type = 'text';
        input.setAttribute('aria-label', 'Site URL');
        input.placeholder = 'https://example.com';
        panel.appendChild(input);
        var row = el('div', 'row');
        row.appendChild(el('span', 'spacer'));
        var add = el('button', 'btn accent', 'Add');
        add.addEventListener('click', function () {
          var v = input.value.trim();
          if (!v) return;
          var urls = state.uptime.urls.concat([v]);
          send('setUptime', { urls: urls }).then(function () {
            state.uptime.urls = urls;
            closePanel();
            renderSchedule();
          });
        });
        row.appendChild(add);
        panel.appendChild(row);
      });
    });
    document.getElementById('countdown-switch').addEventListener('click', function () {
      state.countdown.enabled = !state.countdown.enabled;
      send('setCountdown', state.countdown).then(function () { renderSchedule(); });
    });
    document.getElementById('copy-cmd').addEventListener('click', function () {
      navigator.clipboard.writeText('noizes run <command>').then(function () { toast('Copied'); });
    });
  }

  // ---------- general page ----------
  function bindGeneral() {
    document.getElementById('startup-switch').addEventListener('click', function () {
      state.general.startWithWindows = !state.general.startWithWindows;
      send('setGeneral', { startWithWindows: state.general.startWithWindows }).then(function () {
        setSwitch('startup-switch', state.general.startWithWindows);
      });
    });
    document.getElementById('unfocused-switch').addEventListener('click', function () {
      state.general.onlyWhenUnfocused = !state.general.onlyWhenUnfocused;
      send('setGeneral', { onlyWhenUnfocused: state.general.onlyWhenUnfocused }).then(function () {
        setSwitch('unfocused-switch', state.general.onlyWhenUnfocused);
      });
    });
    document.getElementById('port-input').addEventListener('change', function (e) {
      state.general.port = Number(e.target.value);
      send('setGeneral', { port: state.general.port }).then(function () {
        document.getElementById('port-hint').hidden = false;
      });
    });
    document.getElementById('open-folder').addEventListener('click', function () { send('openFolder', {}); });
    document.getElementById('open-log-2').addEventListener('click', function () { send('openLog', {}); });
  }

  // ---------- master mute ----------
  function bindHeader() {
    document.getElementById('master-switch').addEventListener('click', function () {
      var muted = !state.mute.muted;
      send('setMute', { muted: muted }).then(function (d) {
        state.mute = d;
        renderHeader();
      });
    });
    document.getElementById('mute-1h').addEventListener('click', function () {
      if (state.mute.muted) { // any muted state: this button unmutes
        send('setMute', { muted: false }).then(function (d) {
          state.mute = d;
          renderHeader();
        });
        return;
      }
      send('setMute', { muted: true, minutes: 60 }).then(function (d) {
        state.mute = d;
        renderHeader();
      });
    });
    document.getElementById('open-log').addEventListener('click', function () { send('openLog', {}); });
  }

  // ---------- pages ----------
  var TITLES = { sounds: 'Sounds', connections: 'Connections', schedule: 'Schedule', general: 'General' };

  function showPage(next) {
    page = next;
    ['sounds', 'connections', 'schedule', 'general'].forEach(function (p) {
      document.getElementById('page-' + p).hidden = p !== page;
      var btn = document.querySelector('.nav-item[data-page="' + p + '"]');
      if (btn) { if (p === page) btn.setAttribute('aria-current', 'page'); else btn.removeAttribute('aria-current'); }
    });
    document.getElementById('page-title').textContent = TITLES[page];
    renderAll();
  }

  function bindNav() {
    document.querySelectorAll('.nav-item').forEach(function (btn) {
      btn.addEventListener('click', function () { showPage(btn.dataset.page); });
    });
  }

  function renderAll() {
    if (!state) return;
    renderHeader();
    renderGroups();
    renderConnections();
    renderSchedule();
    document.getElementById('version').textContent = 'v' + state.version;
  }

  // ---------- boot ----------
  bindNav();
  bindSearch();
  bindHeader();
  bindSchedule();
  bindGeneral();
  bindEscape(); // Escape collapses an open row even when no panel ever opened
  send('getState').then(applyState);
})();
