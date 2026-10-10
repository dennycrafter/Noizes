<div align="center">
  <img src="docs/banner.png" alt="#1 Noizes" width="820"/>
  <p><em>When the work is done, you'll know.</em></p>
  <p>
    <a href="https://github.com/dennycrafter/Noizes/actions/workflows/build.yml"><img src="https://github.com/dennycrafter/Noizes/actions/workflows/build.yml/badge.svg" alt="build"/></a>
    <a href="https://github.com/dennycrafter/Noizes/releases"><img src="https://img.shields.io/badge/latest-v1.1.0-blue" alt="release"/></a>
    <img src="https://img.shields.io/badge/platform-Windows-blueviolet" alt="windows"/>
    <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT"/></a>
  </p>
</div>

# 🏆 Victory Royale

You put your coding agents in the game to grind for you. But a build that finishes silently is a win nobody heard — and you were on the other side of the map.

**Noizes is the horn.** A Windows tray app (plus a Chrome companion) that plays a sound when something actually happens:

- 🤖 **Claude Code / Cursor / Obvious finish their work** — hook events hit a local HTTP endpoint and the sound plays
- 🐙 **GitHub PRs merge or get pushed** — including merges by **other actors** (yes, `obvious[bot]` counts now)
- 🚀 **Deployments succeed or fail** — poll results become noise, the useful kind
- 🌐 **Monitored sites change / go down** — uptime checks with a configurable cadence
- ⏬ **Downloads finish** and 👀 **watched tabs do something** — via the Chrome extension
- ⏳ **Countdowns hit zero** — for the "rest is 20 seconds, go" crowd

Everything lands on `localhost:7351`, dispatches through the event bus, and comes out your speakers via NAudio. Focus awareness and quiet hours keep it polite; logging keeps it honest.

<div align="center"><em>"A win is not a win until the horn plays."</em></div>

## What's new in v1.3.0

- **A brand new settings window.** Modern dark pages (Sounds, Connections, Schedule, General) in a native WebView2 window: crisp at 100% to 200% scaling, nothing cut off, and every change saves the moment you click it. No Save button anywhere.
- **One big mute switch.** "Sounds off" sits in the header of every page, "Mute 1 hour" sits next to it, and Mute items sit at the top of the tray menu. The tray icon wears a slash while muted, so you always know.
- **One-click toggles.** Every event row has its own switch on the right: one click turns that sound, or a whole group, on or off.

## 🎒 Loot table

| Rarity | Drop | What it does |
|---|---|---|
| 🟡 Legendary | `noizes setup` | One command. Zero prompts. Agent-runnable. Wires hooks, base events, config. Adds itself to PATH. |
| 🟣 Epic | Integrations tab | Every integration's on/off in one place — the master surface, first thing you see. |
| 🔵 Rare | GitHub repo feeds | Personal feed **plus** explicitly configured repos, deduped, ETag-cached, rate-limit cooldown. |
| 🟢 Uncommon | Quiet hours | 22:00–07:00 by default, midnight-safe, alarms still allowed. |
| ⚪ Common | Silent by default | Fresh install: every event **off** at 40% volume. No 3 AM jump scares. Your settings survive upgrades. |

## 🎯 Kill feed

```
[Noizes]  Claude Code finished the refactor        🔔   hook → done
[Noizes]  obvious[bot] merged PR #42               🔔   repo feed (bots count now)
[Noizes]  Deploy FAILED                            📯   poll result
[Noizes]  quiet hours 22:00–07:00 — suppressed (2) 💤   alarms allowed
[Noizes]  port 7351 taken — staying alive anyway   🛡️   tray balloon says hi
```

And when a notification is suppressed, Noizes writes down **why** — skip-reason logging is part of the contract.

## ⌨️ Controls

| Input | Move |
|---|---|
| `noizes setup` | The whole lobby: hooks, base events, config — zero prompts |
| `noizes run` | Drop in and start listening |
| Tray right-click | Settings, pause, exit |
| Settings → Events | Per-event sound, volume, focus rules |
| Settings → Integrations | Master on/off, GitHub credentials + test, Claude desktop watcher |

## 📦 Drop in

1. Grab `Noizes-1.1.0-setup.exe` from [Releases](https://github.com/dennycrafter/Noizes/releases/latest)
2. Run it (Inno Setup, adds the CLI to PATH)
3. `noizes setup` — or open Settings → Integrations and flip things on

No account, no cloud, no telemetry. Sounds come from localhost and your own speakers.

## 🗺️ The map

```
hooks (Claude/Cursor) · Chrome extension · GitHub polling · deploy/uptime/countdown checks
        └──────────────►  localhost:7351  ──►  event bus  ──►  NAudio  ──►  🔊
```

## 🤝 Squad

- **.NET 8 WinForms** — the tray app
- **NAudio** — the noise itself
- **Chrome MV3** — the browser companion
- **Inno Setup** — the installer
- **Python stdlib** — generated every default sound and the icon (yes, really)

## 📜 License

MIT. Play responsibly.
