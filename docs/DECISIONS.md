# Decisions

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
