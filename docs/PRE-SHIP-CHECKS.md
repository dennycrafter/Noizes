# Pre-ship checks

Nothing reaches a release unless every check below passes on a real Windows machine.
They run on every PR (`ci.yml`) and again inside the release build (`build.yml`), so a
failing check means no installer is built.

| Check | What it proves | Where |
|---|---|---|
| Self-test (`Noizes.exe --selftest`) | Config, sounds, hooks, HTTP server, bridge replies, page messages read correctly in both forms, window placement on every screen layout | `src/Noizes/SelfTest.cs` |
| UI suite (Playwright) | Every page, switch, row, mute state, layout, accessibility, and that a host that never answers shows Try again instead of loading forever | `ui-tests/` |
| Smoke test (`Noizes.exe --smoke-test`) | The shipping exe opens the real settings window over the real bridge (no demo data): settings load, a click lands in config.json, all four pages render, no script errors, window on screen | `SettingsWindow.RunSmokeTest` |
| Launch test | Opens Noizes the way a person does: first open, close to tray, reopen while in the tray, sign-in start stays quiet, Start menu after sign-in, saved spot off screen, saved size bigger than the screen | `scripts/launch-test.ps1` |

## Rules

- Screenshots and tests never use the demo data in the app. The demo data hid the v1.3.0-beta.2 "Loading settings" bug.
- Every bug a person finds gets a check here first, then the fix.
- Failures show as annotations on the PR checks page, named "Smoke test: ..." or "Launch test: ...".
