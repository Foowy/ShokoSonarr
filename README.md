# Shoko Sonarr

A ShokoServer plugin that scans your anime collection for missing episodes and bridges them to Sonarr for automated download.

## Features

- Scans all inventoried Shoko series for missing episodes (on-demand or on a configurable schedule).
- Single-pane-of-glass dashboard embedded in Shoko's WebUI showing missing episodes grouped by series.
- Resolves each series to a Sonarr match via its TMDB-linked TVDB ID, falling back to a confirmable title search when no TVDB ID is available.
- Adds new series to Sonarr (without triggering a full-series download) and/or triggers a targeted `EpisodeSearch` for just the missing episodes.

## Building

```bash
dotnet build
dotnet test
```

## Installing

Copy the built plugin output into your Shoko Server plugins directory, or install via Shoko's plugin manager once a manifest/release is published.

## Dashboard

Open the "Missing Episodes" page from Shoko's WebUI plugin pages list, or go directly to `http://<your-shoko-host>:<port>/api/plugin/ShokoSonarr/dashboard`.

Click Settings and enter your Sonarr base URL and API key (found in Sonarr under Settings > General), then Test Connection to populate the Quality Profile and Root Folder dropdowns before saving. The Sonarr base URL must be directly reachable from the Shoko host — if Sonarr sits behind a reverse proxy with authentication (e.g. an SSO forward-auth layer), use Sonarr's internal address instead, since API-key requests won't pass through an SSO login redirect.
