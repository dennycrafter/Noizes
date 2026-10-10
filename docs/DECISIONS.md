# Decisions

Per the brief: when the spec is silent, pick the simplest option that fits and log it here.

## 2026-10-10 - Lane A, native host (branch ui-v2/lane-a-native-host)

- WebView2 NuGet pinned to 1.0.2903.40: stable, restores and builds clean on net8.0-windows.
- Window size and position memory reads and writes AppConfig.Window through a small reflection shim in SettingsWindow.cs. Lane A must not edit AppConfig.cs and lane B adds the field, so the shim reaches it by name until the lanes merge. Expected shape: a Window property on AppConfig whose type exposes settable int properties X, Y, Width, Height. A missing field, a different shape or never-written values degrade to defaults (1080x720 centered on screen), never a crash. Lane B should match this shape or the shim must be updated at merge.
- Bridge seam for lane B: SettingsWindow.OnWebViewReady (Action holding the CoreWebView2) fires once the webview is ready, again on every window reopen. UiBridge assigns it once and uses it to subscribe WebMessageReceived and push state.
- Tray seam for lane C: SettingsWindow.LegacyFallback (Func returning Form) lets TrayService hand the fallback old form its live Server, GitHub and Watcher references. When unset, SettingsWindow bare-constructs SettingsForm, which null-guards those dependencies.
- The 150 percent screenshot pass passes --force-device-scale-factor=1.5 to WebView2 as an additional browser argument, because a CI runner has one 100 percent display. Capture is CopyFromScreen of the window bounds with the window topmost.
- No --settings flag was added: none exists in v1.2.0, and a second process would die on the single-instance mutex before a flag could help.
- Version bumped to 1.3.0 in Noizes.csproj in this lane because lane A owns that file (brief section 8).
- CI wiring to run --screenshot-settings and commit PNGs to docs/screenshots/v1.3/windows/ is not in this lane's file set; the integrator needs to add that workflow step.
- Meaningful screenshots require lane D's ui/ files. Until they merge, the window renders an empty page and extraction logs "no embedded ui resources in this build".

## 2026-10-10 - Lane B, bridge and config (branch ui-v2/lane-b-bridge)

- Config file name stays `config.json` in `%APPDATA%\Noizes` - the never-rename rule outranks any other name; renaming would orphan v1.2.0 settings.
- `testSound` bypasses mute, quiet hours and focus rules - it is an explicit preview the owner just clicked; mute gates automatic playback only.
- `saveGitHub` with an absent or empty `token` keeps the stored token - the page never holds the token, and a token-clearing control is not in the brief.
- `testGitHub` tests the saved token (no params in the message) - it answers ok:false with a plain-words error when no token is saved yet.
- A port change saves to config and takes effect at the next app start; the bridge replies `restartRequired: true` - no live rebind, so a busy port can never take down the working server.
- `Window` defaults: X and Y of -1 (Windows places the window), 1080x720, not maximized - matches the brief default window size.
- Pushed state uses the shape `{type: "state", data: state}`, and `getState` replies `{id, ok, data: state}` with the same state object - one render path for both.
- The bridge also coalesces volume-only saves on a 300ms timer, and `UiBridge.FlushPendingSave()` exists for the window host to call on exit - belt and braces with the page-side debounce.
- Tray mute belongs in `UiBridge.SetMuted` / `UiBridge.NotifyStateChanged` and the tray icon listens on `UiBridge.StateChanged` - one mute code path for page and tray. Lane C wires `UiBridge.Services.*` (`RunClaudeDesktopWatcher`, `RestartGitHubPoller`, `ListeningPort`) from TrayApplicationContext.
- `setupClaudeCode` / `setupCursor` embed `UiBridge.Services.ListeningPort` when wired (config port otherwise) - hooks must target the port the server is actually on, which can differ from config until a port change restarts the app.
- SelfTest.cs edits live in this lane, not the test lane - new config fields must state their defaults in the coverage sweep or the sweep fails, and the section 7 mute and sync tests need the bridge's seams.
- `chooseSound` cancel replies `ok: true` with `data: {path: null, cancelled: true}` - a cancel is not an error and the page keeps the previous sound.
- `openUrl` only opens http and https links - a page-level XSS must not be able to launch arbitrary schemes.

## 2026-10-10 - Integrator (branch ui-v2)

- Lane B's `WindowConfig` (settable int X, Y, Width, Height, plus Maximized) matches the shape lane A's reflection shim expects, so the shim stays as written. Note 3 checked and closed.
- Page contract reconciled on lane D's terms (note 4): every page registers with `NZ.registerPage` as documented in pages/README.md. The lane E module gets a small adapter inside sounds.js that maps the shell api to the bridge shape it expects and hands the shell its destroy function.
- The shell gained an optional page-destroy lifecycle: a page render may return a destroy function and the shell runs it before the next render. Needed because the lane E module's Escape listener stops propagation while a row is expanded; without a destroy hook a stale listener would swallow Escape on other pages after a page switch.
- The shell `api.openFolder(which)` wrapper now passes the which argument through (note 7); pages keep calling it via NZ.request, both paths carry which.
- One state shape for v1.3.0: UiBridge.BuildState is the source of truth. Pages read it (with one-line fallbacks for the standalone dev stub path), and the Playwright mock mirrors it exactly, so the suite tests the production contract rather than a parallel one.
- The Playwright suite targets the real UI at src/Noizes/ui (playwright.config.js already prefers it once it exists). The ui-tests/fixture stays only as the pre-shell bootstrap fallback and is no longer the spec target.
- The off-row dim went from 0.6 to 0.85 opacity - 0.6 dropped the 13px meta text under 4.5:1 contrast (axe color-contrast, serious); 0.85 keeps the dim cue and passes on both the page and surface backgrounds.
- The Chrome extension button's accessible name is now "How to add it - the Chrome extension" - the old name did not contain the visible text, which axe flags as label-content-name-mismatch (serious).
- The Playwright mock's default volume is 40 on the integer 0-100 scale, matching EventConfig.Volume in production - the old 0.4 float came from the pre-lane fixture shape.
- Mock story: two of four Browser events on (the mixed-group middle state) and both Deploys on (the all-on group switch case) - the group-switch tests need real mixed and all-on groups.
- Spec selectors mapped to the real shell DOM: snd-row with data-event-id, snd-rowname/snd-detail, .snd-search, .nz-panel, #muteBtn and #mutedUntil - the fixtures' pre-shell names (.event-row, data-event, #search) no longer exist in the real page.

## 2026-10-10 - Finisher (branch ui-v2)

- UI-REVIEW fixes 4 and 5 landed in the finisher pass instead of a separate fixer round: the finisher gate is a green PR and BLOCKERS.md marked the windows-screenshots failure as finisher-blocking, while the review document already held the exact fixes. Fixes 1 to 3 (mute label, Sounds accent, mock sync) stay with their queued fixer task; none of them affects CI.
- The windows-screenshots failure had two candidate causes with stderr discarded: the runner lacking an interactive desktop (CopyFromScreen) and the runner lacking the WebView2 Runtime (the runner image lists Edge, not the WebView2 Runtime). The fix space landed in one pass: ci.yml installs the WebView2 Runtime, redirects the exe's stdout and stderr for the capture and self-test steps (build.yml too) so the next failure names itself, and SaveScreenshot keeps CopyFromScreen first with a fallback that captures the WebView2 page itself (CoreWebView2.CapturePreviewAsync with CoreWebView2CapturePreviewImageFormat.Png, verified against the installed WebView2 1.0.2903.40 package docs; the review doc's guessed CaptureMode name was wrong). A page-only capture drops the title bar; acceptable UI evidence per the review.
- build.yml computed the prerelease flag from the tag dash but the Create release step never consumed it; wired prerelease: ${{ steps.rel.outputs.prerelease }} so v1.3.0-beta.1 publishes as a pre-release (brief section 8, step 3). Earlier v-tag releases (1.0.0 to 1.2.0) carry no dash and were correct as stable releases.
- No v1.2.0 settings-window screenshot exists for the PR's before/after pair: not in the repo tree, not in git history, not in any PR body or project file (the v1.2.0 PRs shipped with a visual-evidence exception). The PR says so in plain words and pairs the owner's own complaint list (brief section 1) with the new Sounds page. Never fake a pass.
- README gains a "What's new in v1.3.0" section with the three lines the brief asks for (new window, mute switch, one-click toggles). Existing README text keeps its older dashes; the no-em-dash rule applies to everything added from here on.
