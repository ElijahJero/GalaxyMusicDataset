# GalaxyMusicDataset

Personal Last.fm history warehouse: pull scrobbles, resolve MusicBrainz IDs, attach tags and metadata, inspect progress. Analytics dashboards are specified in [docs/ANALYTICS_PAGES.md](docs/ANALYTICS_PAGES.md) and are not built yet.

## Run

.NET 10 SDK.

```bash
cd GalaxyMusicDataset
dotnet run --launch-profile http
```

Open http://localhost:5107

- **Progress** — ingest/enrichment status, job log, API stats
- **Library** (`/Recent`) — all unique tracks, 50 per page, with filters and inline editing
- **Lookups** — fingerprint cache (one MusicBrainz search per unique song)
- **Review** — accept/reject low-confidence MusicBrainz matches
- **Settings** — API keys (written to `App_Data/user-settings.json`, gitignored)

Development seeds 14 sample scrobbles when the database is empty (`Aggregation:SeedSampleData`).

## Configuration

| Key | Purpose |
| --- | --- |
| `LastFm:ApiKey` / `LastFm:Username` | Required for live ingest (`user.getRecentTracks`, `track.getInfo`) |
| `Discogs:Token` | Optional release search + `/releases/{id}` metadata |
| `TheAudioDb:ApiKey` | Optional track metadata (duration, genre, cover, video) |
| `MusicBrainz:Contact` | Included in the MusicBrainz User-Agent |

User secrets / env vars: `LastFm__ApiKey`, `LastFm__Username`, `Discogs__Token`, `TheAudioDb__ApiKey`.

Get a Last.fm API key at https://www.last.fm/api/account/create. History export needs “Hide recent listening information” **off** on Last.fm.

## How ingest works

1. **Incremental** (hourly, and on startup): `user.getRecentTracks` from the newest stored timestamp minus a small overlap. Unique `UnixTimestamp` drops duplicates.
2. **Backfill**: UTC-day windows walking backward toward the account registration date (same idea as [lastfm-export](https://github.com/Tyainss/lastfm-export) verified mode), so Last.fm page gaps are less likely on a full history pull.
3. Each play attaches to a **Track** keyed by fingerprint (`normalized artist + title`). Ten plays of one song are ten scrobbles and one track row.
4. If Last.fm already sent an MBID, identity is done. Otherwise a **TrackLookup** row is queued once per fingerprint.
5. MusicBrainz search (about 1 req/s) auto-links high-confidence hits and caches the rest for Review. After an MBID exists, a second pass loads recording tags, ISRCs, genres, and Cover Art Archive front images.
6. Then Last.fm `track.getInfo` (duration, crowd tags, wiki, album art, artist URL — by MBID when present), Discogs (search + release detail: year, cover, genres/styles), and TheAudioDB (duration, genre/mood, biography, thumb, music video) fill catalog fields, `TrackSourcePayloads`, and `TrackTags`.

SQLite file: `GalaxyMusicDataset/App_Data/galaxy.db`.
