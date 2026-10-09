# Noizes

Noizes plays a sound when your AI coding agents finish or need your input, when GitHub things happen, and more - so you can leave the tab and hear when it's your turn again. Windows tray app (.NET 8) + Chrome companion; free, open source, everything stays on your machine. Fresh installs are silent: nothing plays until you turn it on.

## Set up in one command

1. Grab `Noizes-<version>-setup.exe` from [Releases](../../releases) and run it (unsigned, so click **More info → Run anyway**).
2. From any terminal: `noizes setup`

That's it. It connects Claude Code and Cursor (their config files are backed up first, nothing else is touched), turns on exactly four events at 40% volume (Claude Code done / needs input, Cursor agent done, long command done), and is safe to run twice. Restart Claude Code / Cursor so the hooks load.

## Tell your AI agent instead

Paste this into Claude Code, Cursor, or any agent - it can do the rest:

```
Set up Noizes for me:
1. Download the newest Noizes-*-setup.exe from https://github.com/dennycrafter/Noizes/releases
2. Unblock-File .\Noizes-*-setup.exe   (unsigned installer; skips the SmartScreen prompt)
3. Run .\Noizes-*-setup.exe /VERYSILENT /NORESTART
4. Run `noizes setup`; it must print its steps and exit 0
5. Restart Claude Code / Cursor so their hooks load
```

## Turning things on and off

Everything is off by default at 40% volume. Tray icon → Settings: per-tool setup on **Integrations** (first tab), sounds/volumes on **Events**, quiet hours on **General**. Every skipped event logs its reason to `%APPDATA%\Noizes\log.txt`.

## GitHub activity (optional)

Paste a **read-only** fine-grained token (create at https://github.com/settings/personal-access-tokens, public repos read-only) plus your username in Settings. User-events poll every 10 s (5-300 configurable, cheap 304s when quiet); deploys every 3 minutes. GitHub's user-events feed can lag ~30-60 s behind reality server-side - Noizes sees activity the moment GitHub does; sub-second would need webhooks + a tunnel (out of scope).

## Chrome extension

`chrome://extensions` → Developer mode → **Load unpacked** → select the `extension/` folder from this repo. Adds Claude/Obvious in the browser, watched tabs, and download sounds (off until enabled).

## CLI

- `noizes setup` - hooks + the four core events; idempotent, agent-friendly
- `noizes run <command...>` - sound if the command took over 30 seconds
- `noizes test [eventId]` - play any event's sound now

## Reference

Events post to `http://127.0.0.1:7351/event/<event-id>` (port configurable, localhost only). Config: `%APPDATA%\Noizes\config.json`; no telemetry. Headless: `Noizes.exe --setup` (what `noizes setup` runs) and `Noizes.exe --selftest` (writes `selftest.log`). Build: `dotnet build src/Noizes/Noizes.csproj -c Release`.

## License

MIT - see [LICENSE](LICENSE).
