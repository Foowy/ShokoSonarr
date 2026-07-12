# Shoko Sonarr

<img src="assets/master_shokosonarr.png" width="200" alt="Shoko Sonarr logo">

[![CI](https://github.com/Foowy/shoko_sonarr/actions/workflows/ci.yml/badge.svg)](https://github.com/Foowy/shoko_sonarr/actions/workflows/ci.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

A [ShokoServer](https://github.com/ShokoAnime/ShokoServer) plugin that scans your anime collection for missing episodes and bridges them to [Sonarr](https://sonarr.tv/) for automated download.

## ELI5

Shoko tracks what anime episodes you already have. Sonarr downloads episodes for you. This plugin sits between them: it scans your Shoko library for episodes you're missing, matches each series to the right one in Sonarr, and tells Sonarr "go find these specific episodes" — no manual searching, no adding whole seasons you already have.

## What it does

- **Scans** every already-inventoried Shoko series (on-demand from the dashboard, or on a configurable schedule) for episodes with no local file.
- **Matches** each series to Sonarr via its TMDB-linked TVDB ID, falling back to a confirmable title search when no TVDB link exists yet.
- **Bridges the gap**: adds the series to Sonarr if it isn't there yet (without triggering a full-series download), then triggers a targeted `EpisodeSearch` for just the missing episodes.
- **Reconciles automatically**: once Shoko confirms an episode was actually imported, the plugin unmonitors it in Sonarr so Sonarr's own RSS/automatic search stops re-fetching something you already have. A stale pending search that keeps failing (e.g. the Sonarr episode was deleted out-of-band) is dropped after 14 days instead of retrying forever.
- **Filters out noise**: optionally exclude specials (globally or per-series), and optionally hide episodes that haven't aired yet — since there's nothing for Sonarr to find until the air date passes.

## What it's not

v1 only scans series Shoko already has *some* files for. It doesn't discover completely unowned anime, and it doesn't manage Sonarr's quality/upgrade settings — bring your own quality profile.

## Dashboard

Single-pane-of-glass UI embedded in Shoko's WebUI. Open it from Shoko's plugin pages list, or go directly to:

```
http://<your-shoko-host>:<port>/api/plugin/ShokoSonarr/dashboard
```

- **Series list** — every series with missing episodes, expandable to see each episode and its status (`none` / `search-triggered`).
- **Specials toggle** — per-series Include/Exclude override for specials episodes; click the active button again to clear it back to the global default.
- **Pending Searches** — view and cancel any in-flight Sonarr search the plugin has triggered but Shoko hasn't confirmed as imported yet.
- **History** — a log of past outcomes (triggered, imported, cancelled, expired) for episodes that were previously pending, since Pending Searches only shows what's still in-flight.
- **Theme picker** — System (follows OS dark/light), Ember, Deep Teal, AniBlackle, or Paper.
- **Settings** — Sonarr base URL/API key, scan interval, quality profile, root folder, include-specials, hide-unaired, and an optional Discord-compatible notification webhook (pinged when a search is triggered or a stale pending entry expires after 14 days).

### First-time setup

1. Open Settings, enter your Sonarr base URL and API key (Sonarr → Settings → General).
2. Click **Test Connection** to populate the Quality Profile and Root Folder dropdowns.
3. Pick a profile and folder, then **Save**.

> Sonarr must be directly reachable from the Shoko host. If Sonarr sits behind a reverse proxy with SSO/forward-auth, use Sonarr's internal LAN address instead — API-key requests can't pass through an SSO login redirect.

## Installing

### Via Shoko's plugin manager (recommended)

This repo publishes a live [`manifest.json`](manifest.json) that ShokoServer's plugin manager can consume directly as a repository. It's kept up to date automatically: every tagged GitHub release adds its own entry (version, changelog, per-runtime archive + checksum) via CI.

1. In Shoko's WebUI, go to **Settings → Plugins** and add a new repository with:
   - **Name:** `Shoko Sonarr` (or anything you like)
   - **URL:** `https://raw.githubusercontent.com/Foowy/shoko_sonarr/master/manifest.json`
2. Shoko fetches the manifest and lists Shoko Sonarr as an installable package — pick a release and install it from there.
3. Once installed, enable **auto-upgrade** on the repository/package if you want future releases pulled in automatically; otherwise re-check the plugin manager after new releases.

### Manual deploy

```bash
dotnet publish ShokoSonarr -c Release -o <output-dir>
# copy <output-dir>/* into Shoko's plugins/ShokoSonarr/ directory, then restart Shoko Server
```

Plugin load only happens at ShokoServer startup — copying the DLL alone has no effect.

## Building & testing

```bash
dotnet build -c Release
dotnet test -c Release
```

46 tests cover missing-episode scanning (specials filtering, hidden/credits-type exclusion, unaired filtering), Sonarr search/reconciliation (success, failure, expiry, exception paths), the Sonarr HTTP client, series matching, and LiteDB settings persistence (including backward-compatible deserialization of pre-upgrade settings docs).

## License

[GPL-3.0](LICENSE)
