# Blockers

## 2026-10-10: windows-screenshots CI job fails on ui-v2

What failed: the `windows-screenshots` job in `ci.yml` fails at the "Capture settings screenshots" step on both `877a0cb` (the integrator head this review started from) and `b03d17c` (the review commit). `Noizes.exe --screenshot-settings docs/screenshots/v1.3/windows` starts, exits with code 1 after about 8 seconds, and no PNGs are produced. The other two jobs (`ui-tests` Playwright and `test` dotnet build plus self-test) pass on both commits.

Why it cannot be diagnosed from CI today: the workflow step launches the exe with `Start-Process` and no output redirection, so the lines the app prints when it fails (`screenshot run failed: ...` on stderr) are thrown away. `Logger` writes to `%APPDATA%\Noizes\log.txt` on the runner, which is never uploaded. The same selftest.log check in the step reads a file the app does not create in the working directory.

What you (the owner or the fixer) must do:

1. Land the diagnosability fix in `.github/workflows/ci.yml` (UI-REVIEW.md fix 4): redirect the app's stdout and stderr to files, print them, then exit with the app's code. One CI run then names the real cause.
2. If the cause is the screen copy, land the code fix in `src/Noizes/SettingsWindow.cs` `SaveScreenshot` (UI-REVIEW.md fix 5): capture the WebView2 itself instead of copying the screen, because a runner without an interactive desktop can fail `CopyFromScreen`.

This blocks the finisher: brief section 7 requires the Windows screenshots as a CI artifact and committed copies under `docs/screenshots/v1.3/windows/`, and the release gate expects CI green.

### Update (finisher, same day)

Fix landed on ui-v2: ci.yml now installs the WebView2 Runtime, redirects the exe's stdout and stderr in the capture and self-test steps (build.yml too), and SaveScreenshot falls back to capturing the WebView2 page itself when the desktop copy is unavailable. Verification pending; this entry gets its final status (with the CI run link) once the run completes.
