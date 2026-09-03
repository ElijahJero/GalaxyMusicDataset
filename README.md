# GalaxyMusicDataset

Personal Last.fm history warehouse: pull scrobbles, resolve MusicBrainz IDs, attach tags and metadata, inspect progress, and browse listening analytics.

## Run

.NET 10 SDK.

```bash
cd GalaxyMusicDataset
dotnet run --launch-profile http
```

Open http://localhost:5107

## Docker

GitHub Actions builds the image and publishes it to GitHub Container Registry on pushes to `master` and on `v*.*.*` tags: `ghcr.io/elijahjero/galaxymusicdataset`.

The repo is private, so pull with a GitHub token that can read packages:

```bash
echo "$GITHUB_TOKEN" | docker login ghcr.io -u YOUR_GITHUB_USERNAME --password-stdin
docker compose up -d
```

Or build locally:

```bash
docker compose up -d --build
```

Open http://localhost:8080. SQLite and Settings (`user-settings.json`) live in the `galaxy-data` volume. API keys can also be passed as environment variables (`LASTFM_API_KEY`, `LASTFM_USERNAME`, `DISCOGS_TOKEN`, `THEAUDIODB_API_KEY`, `MUSICBRAINZ_BASE_URL`).

- **Progress** — ingest/enrichment status, per-database coverage, job log, API stats
- **Analytics** — overview, tops, **genres/tags**, discovery, time patterns, deep cuts, sessions, wrapped year, artist/track detail ([spec](docs/ANALYTICS_PAGES.md))
- **Library** (`/Recent`) — all unique tracks, 50 per page, with filters (including has/missing tags) and inline editing
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
| `MusicBrainz:BaseUrl` | MusicBrainz Server origin. Default `https://musicbrainz.org`. Point this at a mirror you already host (e.g. [musicbrainz-docker](https://github.com/metabrainz/musicbrainz-docker) at `http://localhost:5000`) for much faster lookups. This app does not run Docker for you. |
| `MusicBrainz:CoverArtBaseUrl` | Cover Art Archive origin. Default `https://coverartarchive.org`. musicbrainz-docker’s website is not CAA — leave this unless you host a CAA mirror. |
| `MusicBrainz:MinIntervalMs` | Optional Web Service throttle. Unset = 1200ms on the public API, 50ms on a self-hosted mirror. `0` = no extra delay. Public Cover Art Archive stays at 1200ms. |

User secrets / env vars: `LastFm__ApiKey`, `LastFm__Username`, `Discogs__Token`, `TheAudioDb__ApiKey`, `MusicBrainz__BaseUrl`.

Get a Last.fm API key at https://www.last.fm/api/account/create. History export needs “Hide recent listening information” **off** on Last.fm.

## Self-hosted MusicBrainz

The public `musicbrainz.org` web service is rate-limited to about 1 request/second, which is too slow to resolve a large backlog. Host a mirror **separately** with [musicbrainz-docker](https://github.com/metabrainz/musicbrainz-docker) (website + `/ws/2` listen on port 5000 by default), then set **Server URL** on Settings to that origin (`http://localhost:5000`, or `MusicBrainz__BaseUrl`). This app only calls the Web Service; it does not vendor or start that Docker stack.

Cover art still uses `https://coverartarchive.org` unless you also host a Cover Art Archive mirror and set `MusicBrainz:CoverArtBaseUrl`.

## How ingest works

1. **Incremental** (hourly, and on startup): `user.getRecentTracks` from the newest stored timestamp minus a small overlap. Unique `UnixTimestamp` drops duplicates.
2. **Backfill**: UTC-day windows walking backward toward the account registration date (same idea as [lastfm-export](https://github.com/Tyainss/lastfm-export) verified mode), so Last.fm page gaps are less likely on a full history pull.
3. Each play attaches to a **Track** keyed by fingerprint (`normalized artist + title`). Ten plays of one song are ten scrobbles and one track row.
4. If Last.fm already sent an MBID, identity is done. Otherwise a **TrackLookup** row is queued once per fingerprint.
5. MusicBrainz search auto-links high-confidence hits and caches the rest for Review. The public API is about 1 req/s; a self-hosted `MusicBrainz:BaseUrl` uses a much smaller gap (50ms by default, or `MinIntervalMs`). After an MBID exists, a second pass loads recording tags, ISRCs, genres, and Cover Art Archive front images.
6. Then Last.fm `track.getInfo` (duration, crowd tags, wiki, album art, artist URL — by MBID when present), VocaDB / UtaiteDB / TouhouDB (song search: tags, duration, PVs, aliases, optional MBID), Discogs (search + release detail: year, cover, genres/styles), and TheAudioDB (duration, genre/mood, biography, thumb, music video) fill catalog fields, `TrackSourcePayloads`, and `TrackTags`.

SQLite file: `GalaxyMusicDataset/App_Data/galaxy.db`.
