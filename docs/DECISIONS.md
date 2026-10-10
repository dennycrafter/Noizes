# Decisions

Per the brief: when the spec is silent, pick the simplest option that fits and log it here.

- 2026-10-10 - Lane B (ui-v2/lane-b-bridge) - Config file name stays `config.json` in `%APPDATA%\Noizes` - the never-rename rule outranks any other name; renaming would orphan v1.2.0 settings.
- 2026-10-10 - Lane B - `testSound` bypasses mute, quiet hours and focus rules - it is an explicit preview the owner just clicked; mute gates automatic playback only.
- 2026-10-10 - Lane B - `saveGitHub` with an absent or empty `token` keeps the stored token - the page never holds the token, and a token-clearing control is not in the brief.
- 2026-10-10 - Lane B - `testGitHub` tests the saved token (no params in the message) - it answers ok:false with a plain-words error when no token is saved yet.
- 2026-10-10 - Lane B - A port change saves to config and takes effect at the next app start; the bridge replies `restartRequired: true` - no live rebind, so a busy port can never take down the working server.
- 2026-10-10 - Lane B - `Window` defaults: X and Y of -1 (Windows places the window), 1080x720, not maximized - matches the brief default window size.
- 2026-10-10 - Lane B - Pushed state uses the shape `{type: "state", data: state}`, and `getState` replies `{id, ok, data: state}` with the same state object - one render path for both.
- 2026-10-10 - Lane B - The bridge also coalesces volume-only saves on a 300ms timer, and `UiBridge.FlushPendingSave()` exists for the window host to call on exit - belt and braces with the page-side debounce.
- 2026-10-10 - Lane B - Tray mute belongs in `UiBridge.SetMuted` / `UiBridge.NotifyStateChanged` and the tray icon listens on `UiBridge.StateChanged` - one mute code path for page and tray. Lane C wires `UiBridge.Services.*` (`RunClaudeDesktopWatcher`, `RestartGitHubPoller`, `ListeningPort`) from TrayApplicationContext.
- 2026-10-10 - Lane B - `setupClaudeCode` / `setupCursor` embed `UiBridge.Services.ListeningPort` when wired (config port otherwise) - hooks must target the port the server is actually on, which can differ from config until a port change restarts the app.
- 2026-10-10 - Lane B - SelfTest.cs edits live in this lane, not the test lane - new config fields must state their defaults in the coverage sweep or the sweep fails, and the section 7 mute and sync tests need the bridge's seams.
- 2026-10-10 - Lane B - `chooseSound` cancel replies `ok: true` with `data: {path: null, cancelled: true}` - a cancel is not an error and the page keeps the previous sound.
- 2026-10-10 - Lane B - `openUrl` only opens http and https links - a page-level XSS must not be able to launch arbitrary schemes.
