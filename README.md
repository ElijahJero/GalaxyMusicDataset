# AI Usage Disclosure 

This project was made with AI for experimentation purposes. It is not intended to be a production capable application. Use at your own risk.

# GalaxyMusicDataset

Personal listening-history warehouse: pull scrobbles from Last.fm, Maloja, ListenBrainz, or Libre.fm; resolve MusicBrainz IDs; attach tags and metadata; inspect progress; and browse listening analytics.

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

Open http://localhost:8080. SQLite and Settings (`user-settings.json`) live in the `galaxy-data` volume. API keys can also be passed as environment variables (`LASTFM_API_KEY`, `LASTFM_USERNAME`, `MALOJA_BASE_URL`, `MALOJA_API_KEY`, `LISTENBRAINZ_USERNAME`, `LISTENBRAINZ_TOKEN`, `LIBREFM_API_KEY`, `LIBREFM_USERNAME`, `PRIMARY_SCROBBLE_SOURCE`, `DISCOGS_TOKEN`, `THEAUDIODB_API_KEY`, `MUSICBRAINZ_BASE_URL`, `VGMDB_BASE_URL`). Analytics use Eastern Time by default; set `ANALYTICS_TIMEZONE` (IANA id or `EST` / `UTC`) in docker compose to change it.

Analytics pages (`/Dashboard`, tops, genres, discovery, patterns, deep cuts, sessions, wrapped, artist/track detail) are public — anyone who can reach the host can browse them. Progress, Library, Lookups, Review, and Settings require an admin cookie. Set `AUTH_USERNAME` (default `admin`) and `AUTH_PASSWORD`. If the password is empty, analytics stay public and nobody can sign in. Locally: `dotnet user-secrets set Auth:Password "your-password"` (or `Auth__Username` / `Auth__Password` env vars). Anonymous visits to `/` redirect to `/Dashboard`.

- **Progress** — ingest/enrichment status, per-database coverage, job log, API stats
- **Analytics** — overview, tops, **genres/tags**, **audio profile**, discovery, time patterns, deep cuts, sessions, wrapped year, artist/track detail ([spec](docs/ANALYTICS_PAGES.md))
- **Library** (`/Recent`) — all unique tracks, 50 per page, with Lucene.NET search (case-insensitive, partial tokens, light typo tolerance), filters (including has/missing tags, Essentia genre folders, BPM range, and key), and inline editing
- **Lookups** — fingerprint cache (one MusicBrainz search per unique song)
- **Review** — accept/reject low-confidence MusicBrainz matches
- **Settings** — primary scrobble source (Last.fm / Maloja / ListenBrainz / Libre.fm), enrichment keys, and **REST API keys** for mobile clients (written to `App_Data/user-settings.json` and `App_Data/api-keys.json`, gitignored)

## REST API

`/api/v1` exposes the same features as the site (analytics, library, lookups, review, ingest jobs, settings, Essentia audio profiles). Create keys on Settings; send `Authorization: Bearer gmk_…` or `X-Api-Key`. Docs: [docs/API.md](docs/API.md), in-app `/api-docs`, or `GET /api/docs`.

A local Essentia client (not this server) can pull unprofiled tracks and upload BPM/mood/genre features: [tools/essentia-client](tools/essentia-client).

Development seeds 14 sample scrobbles when the database is empty (`Aggregation:SeedSampleData`).

## Configuration

| Key | Purpose |
| --- | --- |
| `Aggregation:PrimaryScrobbleSource` | Active history source: `LastFm` (default), `Maloja`, `ListenBrainz`, or `LibreFm`. Only this source runs incremental sync / backfill / reconcile. |
| `Aggregation:CrossProviderMatchSeconds` | When switching sources, same track within this many seconds of an existing play is treated as a duplicate (default `2`). Exact Unix timestamps always dedupe. |
| `LastFm:ApiKey` / `LastFm:Username` | Last.fm ingest + optional `track.getInfo` enrichment |
| `Maloja:BaseUrl` / `Maloja:ApiKey` | Self-hosted Maloja origin (e.g. `http://localhost:42010`). Optional API key. Also used for local album-art enrichment with a short throttle (50ms on localhost/LAN). |
| `Maloja:MinIntervalMs` | Optional Maloja throttle. Unset = 50ms local / 250ms remote. `0` = no delay. |
| `ListenBrainz:Username` / `ListenBrainz:Token` | ListenBrainz username (MusicBrainz ID). Token optional but raises rate limits. |
| `LibreFm:ApiKey` / `LibreFm:Username` | Libre.fm (Audioscrobbler 2.0). Optional `LibreFm:BaseUrl` (default `https://libre.fm/2.0/`). |
| `Discogs:Token` | Optional release search + `/releases/{id}` metadata |
| `TheAudioDb:ApiKey` | Optional track metadata (duration, genre, cover, video) |
| `Vgmdb:BaseUrl` | Unofficial VGMdb JSON proxy origin. Default `https://vgmdb.info` ([hufman/vgmdb](https://github.com/hufman/vgmdb)). Point this at a proxy you already host. This app does not run Docker for you. |
| `MusicBrainz:Contact` | Included in the MusicBrainz User-Agent |
| `MusicBrainz:BaseUrl` | MusicBrainz Server origin. Default `https://musicbrainz.org`. Point this at a mirror you already host (e.g. [musicbrainz-docker](https://github.com/metabrainz/musicbrainz-docker) at `http://localhost:5000`) for much faster lookups. This app does not run Docker for you. |
| `MusicBrainz:CoverArtBaseUrl` | Cover Art Archive origin. Default `https://coverartarchive.org`. musicbrainz-docker’s website is not CAA — leave this unless you host a CAA mirror. |
| `MusicBrainz:MinIntervalMs` | Optional Web Service throttle. Unset = 1200ms on the public API, 50ms on a self-hosted mirror. `0` = no extra delay. Public Cover Art Archive stays at 1200ms. |
| `Auth:Username` / `Auth:Password` | Cookie login for admin pages. Default username `admin`. Leave password empty to disable sign-in (analytics remain public). |
| `Analytics:TimeZone` | Zone for day/hour aggregations and timestamps. Default `America/New_York` (EST/EDT). Aliases: `EST`, `ET`, `UTC`. |
| `Ui:DefaultTheme` | Fallback UI theme when a browser has no local preference: `dark` (cockpit), `maloja` (lightweight), or `light`. Clients still choose and persist their own theme. |

User secrets / env vars: `LastFm__ApiKey`, `LastFm__Username`, `Maloja__BaseUrl`, `ListenBrainz__Username`, `Aggregation__PrimaryScrobbleSource`, `Discogs__Token`, `TheAudioDb__ApiKey`, `MusicBrainz__BaseUrl`, `Vgmdb__BaseUrl`, `Auth__Username`, `Auth__Password`, `Analytics__TimeZone`.

Get a Last.fm API key at https://www.last.fm/api/account/create. History export needs “Hide recent listening information” **off** on Last.fm.

### Switching scrobble services (e.g. Last.fm → Maloja)

1. Keep your existing warehouse (do not wipe `galaxy.db`).
2. Configure Maloja’s base URL and set **Primary scrobble source** to `Maloja` on Settings.
3. Sync/backfill from Maloja. Plays that share a Unix timestamp (typical when you imported Last.fm history into Maloja) or the same track within the match window are skipped as duplicates.
4. Reconcile deletes only remove plays that were originally ingested from the current primary source, so Last.fm-era rows are not wiped when Maloja’s day window differs.

## Self-hosted MusicBrainz

The public `musicbrainz.org` web service is rate-limited to about 1 request/second, which is too slow to resolve a large backlog. Host a mirror **separately** with [musicbrainz-docker](https://github.com/metabrainz/musicbrainz-docker) (website + `/ws/2` listen on port 5000 by default), then set **Server URL** on Settings to that origin (`http://localhost:5000`, or `MusicBrainz__BaseUrl`). This app only calls the Web Service; it does not vendor or start that Docker stack.

Cover art still uses `https://coverartarchive.org` unless you also host a Cover Art Archive mirror and set `MusicBrainz:CoverArtBaseUrl`.

## Self-hosted VGMdb proxy

VGMdb.net has no official API. This app talks to the unofficial JSON proxy from [hufman/vgmdb](https://github.com/hufman/vgmdb). The public default `https://vgmdb.info` is often unreachable (`No route to host`, failed TLS, HTTP 502). When that happens, VGMdb enrichment pauses for 30 minutes instead of retrying every track.

Self-host the proxy (it needs a logged-in VGMdb `Cookie` header because of Cloudflare), then set **Proxy URL** on Settings (or `Vgmdb__BaseUrl`):

```bash
# Cookie value from a logged-in vgmdb.net request (includes cf_clearance and vgmsessionhash)
export VGMDB_USER_COOKIE='cf_clearance=...; vgmsessionhash=...'
export VGMDB_BASE_URL=http://vgmdb
docker compose --profile vgmdb up -d
```

For `dotnet run` on the host, start the proxy on port 8731 and set Proxy URL to `http://localhost:8731`. Album links in the UI still go to `vgmdb.net`.

## How ingest works

1. **Incremental** (hourly, and on startup): pulls from the **primary** scrobble source from the newest stored timestamp minus a small overlap. Unique `UnixTimestamp` drops duplicates; cross-provider soft-match also skips the same track within `CrossProviderMatchSeconds`.
2. **Nightly 7-day check** (pausable on the aggregation page): re-fetches the last 7 UTC days from the primary source, inserts missing plays, and deletes local plays that came from that same source and are no longer returned (ListenBrainz skips deletes). Incomplete remote windows skip deletes.
3. **Full resync** (Settings): the same check walking back toward the account registration date (or 20 years when the source has no registration date).
4. **Backfill**: UTC-day windows walking backward so page gaps are less likely on a full history pull.
5. Each play attaches to a **Track** keyed by fingerprint (`normalized artist + title`). Ten plays of one song are ten scrobbles and one track row. Each scrobble stores its `Source` provider.
6. If the source already sent an MBID, identity is done. Otherwise a **TrackLookup** row is queued once per fingerprint.
7. MusicBrainz search auto-links high-confidence hits and caches the rest for Review. The public API is about 1 req/s; a self-hosted `MusicBrainz:BaseUrl` uses a much smaller gap (50ms by default, or `MinIntervalMs`). After an MBID exists, a second pass loads recording tags, ISRCs, genres, and Cover Art Archive front images.
8. Then Maloja album art (when configured — preferred local cover source), Last.fm `track.getInfo`, VocaDB / UtaiteDB / TouhouDB, VGMdb, Discogs, and TheAudioDB fill catalog fields, `TrackSourcePayloads`, and `TrackTags`.

SQLite file: `GalaxyMusicDataset/App_Data/galaxy.db`.
