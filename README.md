# Noizes

Noizes plays a sound when your AI coding agents finish or need your input, when GitHub things happen, when a site goes down, and more - so you can leave the tab and hear when it's your turn again.

Windows tray app (.NET 8) + Chrome companion. Free, open source, everything stays on your machine.

## Install

1. Go to [Releases](../../releases) and download `Noizes-<version>-setup.exe` from the newest release.
2. Run it. Windows SmartScreen will warn because the installer is unsigned: click **More info**, then **Run anyway**.
3. Noizes starts minimized to your tray. Click the tray speaker icon to open Settings.

## What makes sounds

- **Claude Code finished / Claude Code needs input** - hooks Noizes adds to `~/.claude/settings.json` (Stop + Notification hooks).
- **Cursor agent finished** - a hook Noizes adds to `~/.cursor/hooks.json`.
- **GitHub activity** - pushes, PRs opened/merged, failed checks, stars, issues, comments on your account (read-only token required, see below).
- **Deploy succeeded / deploy failed** - GitHub deployment statuses (Vercel, Netlify, etc).
- **A site is down** - paste URLs in Settings; an alarm plays if one fails two checks in a row (3-minute interval). Alarms can optionally play during quiet hours.
- **Countdown** - sounds at 1 hour, 30 minutes and 10 minutes before a date/time you pick.
- **Long command finished** - the `noizes` command-line helper, see below.
- **Downloads finished / Claude or Obvious answered in the browser / a watched tab went idle** - the Chrome extension (below).

Every event can be turned off, given its own sound (.mp3 or .wav) and volume per-event. "Only play when the source app is not focused" keeps quiet when you're already looking at the thing.

## Connecting your tools

Open **Noizes settings -> Connections**:

- **Connect Claude Code** / **Connect Cursor** - adds the hook entries to the tool's own config file. The original file is backed up next to it (`*.noizes-backup-<timestamp>`) and nothing existing is removed. Restart the tool afterwards. These buttons are idempotent - clicking twice does not duplicate hooks.
- **GitHub token** - create a **read-only** personal access token at https://github.com/settings/personal-access-tokens ("Generate new token" -> fine-grained -> Public repositories (read-only), no extra permissions), paste it plus your GitHub username in Settings. The token stays in `%APPDATA%\Noizes\config.json` on your machine and is only sent to api.github.com.

## Chrome extension (Claude/Obvious in the browser, watched tabs, downloads)

1. Open `chrome://extensions`, enable **Developer mode**, click **Load unpacked**, and select the `extension/` folder from this repository.
2. Click the Noizes extension icon to see per-site toggles and pick a tab to watch.

## Long commands (`noizes run`)

Installed with the app. From any terminal:

```
noizes run npm run build
noizes run python train.py --epochs 50
noizes test claude-code-done
```

If the command takes over 30 seconds, Noizes plays the "long command finished" sound when it exits. `noizes test <eventId>` plays any event's sound so you can try settings.

## Quiet hours

Settings -> General: mute window (e.g. 22:00-07:00). The uptime alarm can be allowed through.

## Privacy

Noizes talks to: `api.github.com` (only if you add a token) and the URLs you paste for uptime checks. The local HTTP server listens on 127.0.0.1 only. No telemetry, no accounts.

## Building from source

```
git clone https://github.com/dennycrafter/Noizes
cd Noizes
dotnet build src/Noizes/Noizes.csproj -c Release
```

`Noizes.exe --selftest` runs a headless verification pass (used by CI) and writes `selftest.log`.

## License

MIT - see [LICENSE](LICENSE).
