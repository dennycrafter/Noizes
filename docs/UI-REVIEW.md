# UI review: screenshots vs the brief (sections 1, 3 and 5)

Reviewer: Agent H (reviewed fresh, wrote none of this UI)
Date: 2026-10-10
Branch reviewed: `ui-v2` at `877a0cb`
Verdict: **3 findings, all small and mechanical. Not ready for the finisher until fixes 1 and 2 land; fix 3 is test-only.** The 10 section 7 screenshots pass every rule they can show. All three failures live in interactive or test-harness states, listed exactly below for the fixers.

## Method

- Ran the full Playwright suite (38 of 38 pass), which regenerated the section 7 shots: `docs/screenshots/v1.3/`, every page plus the GitHub panel at 1080x720, device scale 1 and 1.5.
- Viewed every screenshot at full size, then re-inspected suspect regions with 4x pixel crops and measured colors and geometry against section 5.
- Static shots cannot show hover, focus, mute or expansion, so `ui-tests/review-evidence.js` drove the mock bridge and captured evidence to `docs/screenshots/v1.3/review/` (muted header, timed mute, expanded row, row hover, button hover, keyboard focus, toast, Claude desktop sync).
- Palette check: read `src/Noizes/ui/app.css` `:root` and sampled rendered pixels in the PNGs.

## Results by screen (screen, rule, verdict)

Verdicts are `pass` or `fail`. No rule was marked fixed; the orchestrator dispatches fixers for the fails, so nothing here was repaired by the reviewer.

### Sounds (`sounds-1x.png`, `sounds-1_5x.png`)

| Rule | Verdict | Evidence |
|---|---|---|
| No truncation anywhere | pass | Every row, button and the search box show full text at scale 1 and 1.5. `layout.spec` is green at 960 and 1400 px and device scale 1, 1.25, 1.5 and 2. |
| One click on or off, no Save button | pass | Toggle on the right of every row sends `setEvent` and dims the row (`bridge.spec`); no Save button on any main page. |
| Master mute in the header | pass | "Sounds on" switch plus a quiet "Mute 1 hour" text button, on every page. |
| Group switches with middle state | pass | GitHub group (one row on, three off) renders the knob centered: measured knob center 996.5 px vs pill center 996 px, distinct from off (knob left) and on (knob right, purple). Implemented as `.snd-switch.is-mixed .snd-knob { transform: translateX(8px) }`. |
| Exact palette | pass | `app.css` `:root` matches section 5 value for value (all four greys, `#7c5cff` accent, `--r: 8px`, popup shadow). Rendered pixels confirm: page `#121215`, inputs `#0b0b0d`, ON toggles `#7c5cff`. |
| Accent discipline | **fail** | Accent appears in three unsanctioned places, all visible only in interactive states (see fix 2): the volume slider fill and thumb are accent purple, the search box border turns accent on focus, and every focusable element in `.snd-page` gets an accent-ink focus ring. The static shots show only sanctioned accent: ON toggles, the 2 px sidebar bar. |
| 8 px grid | pass | Rows measure 56 px collapsed (7 x 8); paddings and gaps step in 8 px. |
| Hover states | pass | Row hover renders `--surface-2` (captured in `review/row-hover-1x.png`); button hover background measured `rgb(28, 28, 32)` = `#1c1c20`. 16 hover rules cover rows, nav links, buttons, icon buttons, switches and inputs, all neutral. |
| Focus states | **fail** (color only) | Outline is visible (2 px, offset 2) and switches are keyboard operable (`a11y.spec`, axe-core green). But on the Sounds page the ring is accent-ink purple on everything, while the app-wide rule (`app.css` line 50) correctly uses silver. Part of fix 2. |
| Muted-state rendering | **fail** | Master off renders "Sounds off" with the switch grey and rows unchanged (correct: mute is a layer over per-event state). Timed mute renders "Muted until 5:59 AM" plus "Unmute" (captured). Failure: while master-muted, the text button still reads "Mute 1 hour" but clicking it unmutes. Label and behavior disagree. Fix 1. |

### Connections (`connections-1x.png`, `connections-1_5x.png`)

| Rule | Verdict | Evidence |
|---|---|---|
| No truncation anywhere | pass | "How to add it" and "Open settings file folder" size to their text at both scales. |
| One button per row, allowed labels | pass | Set up, Set up, Edit, Turn on, How to add it. Buttons sit right of the text. |
| Status dots and pills | pass | Green "Connected", grey "Not connected", amber "Needs attention". Pills used for status only. |
| Claude desktop one source of truth | pass | Clicking "Turn on" flips `watcherEnabled`, `connections.claudeDesktop.enabled` and the event row to true together; the row re-renders to "Connected" with "Turn off" (captured in `review/claude-desktop-on-1x.png`). Production `UiBridge.cs` lines 504 to 506 sync the event switch both ways. Harness gap in fix 3. |
| Master mute in the header | pass | Present on this page too. |

### GitHub panel (`github-panel-1x.png`, `github-panel-1_5x.png`)

| Rule | Verdict | Evidence |
|---|---|---|
| Panel chrome | pass | `--surface-2` box, 1 px `--line` outline, shadow `0 6px 16px rgba(0,0,0,.3)`. |
| Token privacy | pass | Shows "Token saved." with a neutral Replace button; no token text reaches the page. |
| One accent action | pass | Save is the only accent button in the panel; Test is neutral. |
| No truncation anywhere | pass | At both scales, all labels complete. |

### Schedule (`schedule-1x.png`, `schedule-1_5x.png`)

| Rule | Verdict | Evidence |
|---|---|---|
| No truncation anywhere | pass | Pickers, "noizes run <command>" line and Copy all complete. |
| Accent discipline | pass | Purple only on the ON toggles (uptime alarm override, master). |
| Countdown in big numbers | pass | Large bold "2d 12:05:06". |
| 8 px grid | pass | |

### General (`general-1x.png`, `general-1_5x.png`)

| Rule | Verdict | Evidence |
|---|---|---|
| No truncation anywhere | pass | |
| Accent discipline | pass | Purple only on the ON toggles. |
| Muted explainer lines | pass | "Skips the sound while the app that finished is already in front of you." under its switch; muted hint under the port field. |

### Cross-cutting

| Rule | Verdict | Evidence |
|---|---|---|
| Toasts bottom center | pass | Measured box: center x 540 of 1080, y 658 of 720 (captured in `review/toast-1x.png`). |
| Reduced motion | pass | `@media (prefers-reduced-motion: reduce)` in `app.css` line 484 and the Sounds page styles. |
| Aria labels on icon buttons | pass | Close, Set up + name, token and interval fields all labelled (`a11y.spec` green). |
| No em dash, en dash or double space | pass | `layout.spec` text rules green on every page and the panel. |

## Fix list for the fixers

1. **Mute button label must match its click (src/Noizes/ui/app.js).** In `renderHeader()` (lines 172 to 186) the button reads "Unmute" only for a timed mute. While muted with no timer (master switch off), it reads "Mute 1 hour" while the click handler (lines 545 to 548) sends `setMute(false)` and unmutes. A control that says "Mute 1 hour" and then unmutes is the same dishonesty section 1 bans in the old UI. Fix: set the label from the muted state, not the timed state. After the `if (mutedForTime)` block that manages the "Muted until" pill, always run `els.muteBtn.textContent = muted ? "Unmute" : "Mute 1 hour";` and drop the label assignment from both branches. The "Muted until" pill logic stays exactly as is. Rejected alternative: keeping "Mute 1 hour" as a timed mute while muted and adding a second "Unmute" link, because section 3 specifies one switch plus one quiet text button in the header. Update `bridge.spec.js` to also cover the master-muted case: label reads "Unmute" and the click unmutes.
2. **Accent only for the sanctioned states (src/Noizes/ui/pages/sounds.js injected styles).** Section 5: accent only for the toggle-on state and the one panel action; everything else neutral. Three lines, one root cause:
   - Line 109 `.snd-search:focus { border-color: var(--accent-ink) }`: use `var(--muted, #9a9aa3)` for the border.
   - Line 171 `.snd-range { accent-color: var(--accent) }`: use `accent-color: var(--silver, #e8e8ea)` so the volume slider is neutral.
   - Line 202 `.snd-page :focus-visible { outline: 2px solid var(--accent-ink) }`: delete the override so the app-wide silver ring (`app.css` line 50) applies to the Sounds page like everywhere else.
   Then re-run the suite and the evidence script; the focus and expanded-row shots must show no purple outside toggle-on states.
3. **Mock bridge must mirror the Claude desktop sync (ui-tests/mock-bridge.js).** Production `UiBridge.cs` (lines 504 to 506) turns `Features.ClaudeDesktopWatcher` (and the watcher) off when `setEvent` disables `claude-desktop-done`. The mock's `setEvent` handler does not, so in the harness the Connections row can read "Turn off" while the event row is off. Production cannot reach that state; the test fake can. Fix in `handlers.setEvent`, after `ev.enabled` is set: if `ev.id === 'claude-desktop-done'`, also set `state.watcherEnabled = ev.enabled` and `state.connections.claudeDesktop.enabled = ev.enabled`. Add a spec: flip the event switch off on Sounds, assert both the watcher flag and the Connections flag go false.

## Readiness for the finisher

- The section 7 screenshot set is complete and committed at both scales.
- Fixes 1 and 2 touch `app.js` and `sounds.js` only; fix 3 touches `ui-tests/mock-bridge.js` and one spec. None restructures pages or the bridge.
- The branch is ready for the finisher as soon as a fixer lands fixes 1 and 2 and the full suite is re-run green. Fix 3 can land in the same commit; it only makes the harness honest.

## Environment caveats (not failures)

- The suite runs on Linux Chromium, so "Segoe UI Variable Text" is absent and system-ui renders instead. On Windows the brief's font stack applies. The `scrollWidth` checks bound the truncation risk across font swaps.
- The schedule PNGs differ slightly between runs because the countdown ticks live.
- Windows-side evidence (`dotnet build`, `--selftest`, `--screenshot-settings` at 150 percent) is produced by `build.yml` and `ci.yml` runners and cannot be verified from this sandbox.
