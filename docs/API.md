# Galaxy Music REST API

JSON API at `/api/v1` so a mobile app (or any client) can match the website: public analytics, library editing, MusicBrainz review, ingest jobs, and settings.

Human-readable page: `/api-docs`. Machine catalog: `GET /api` (no key).

## Authentication

Create keys on **Settings** while signed in as admin. The full token is shown once (`gmk_…`). Hashes are stored in `App_Data/api-keys.json` (not git).

Send the token on every `/api/v1` request:

```
Authorization: Bearer gmk_…
```

or

```
X-Api-Key: gmk_…
```

| Scope | Access |
| --- | --- |
| `read` | All GET routes (analytics, library list, lookups, review queue, status, settings, key list) |
| `write` | POST / PUT / DELETE (edits, review actions, jobs, settings save, create/revoke keys) |

A write key always includes `read`. Website analytics stay cookie-free; the REST API always requires a key.

Errors: `401` `{"error":"Invalid or missing API key."}` · `403` missing scope · `404` unknown id · `400` validation.

JSON is camelCase. Enums are strings. Timestamps are ISO-8601 UTC offsets.

## Shared analytics query string

Same meaning as the site’s time-range chrome.

| Param | Values |
| --- | --- |
| `range` | `7d`, `30d` (default), `90d`, `1y`, `all`, `custom` |
| `from`, `to` | ISO dates (`yyyy-MM-dd`) when `range=custom` |
| `q` | Artist / track / album search (names and aliases) |
| `take` | List length (default 50; clamped 1–500 on most lists) |

Responses that use a window include `range: { preset, from, to, q }`.

---

## Catalog

### `GET /api` and `GET /api/v1`

No key. Name, version, documentation path, and endpoint map.

### `GET /api/v1/me`

Current key: `id`, `name`, `prefix`, `scopes`.

---

## Analytics (website Analytics menu)

### `GET /api/v1/overview`

Dashboard: scrobble counts, unique tracks/artists/albums, listening time, streak, most recent play, daily volume, all-time extras, top genres (8), available years.

### `GET /api/v1/tops/{kind}`

`kind`: `artists` | `tracks` | `albums`. Ranked list with movers (`delta`, `percentChange`, `isNew`) unless `range=all`.

### `GET /api/v1/tags`

Play-weighted genre and crowd-tag clouds. Extra: `take`.

### `GET /api/v1/tags/{name}`

Tracks and artists for one tag. `404` if the tag does not exist.

### `GET /api/v1/discovery`

First-heard tracks and artists in the window (`kind` on the site is a UI tab; the API returns both lists).

### `GET /api/v1/patterns`

Hour × weekday heatmap, time-of-day buckets, monthly volume.

### `GET /api/v1/deep-cuts`

One-play tracks vs heavy rotation. Query: `n` (heavy threshold, default 10).

### `GET /api/v1/sessions`

Listening sessions clustered by gap. Query: `gap` minutes (default 30).

### `GET /api/v1/wrapped/{year}`

Year in review (overview, tops, genres, discoveries, heatmap, new artists, most replayed, streak, busiest hour).

### `GET /api/v1/wrapped/{year}/html`

Downloadable click-through HTML (`wrapped-{year}.html`), same as the site’s download handler.

### `GET /api/v1/years`

`{ "years": [2024, 2023, …] }`

### `GET /api/v1/artists/{id}`

Artist detail for the selected window: aliases, tags, plays, timeline, top tracks.

### `GET /api/v1/tracks/{id}`

Track detail: album, MBIDs, duration, tags by source, source payloads, play timestamps, Essentia `audio` profile when uploaded.

### `GET /api/v1/tracks/pending-audio`

Tracks with no Essentia profile, most-played first. Query: `take` (default 25, max 100). Used by `tools/essentia-client`.

### `PUT /api/v1/tracks/{id}/audio-profile` (write)

Upsert an Essentia `analyze()` payload. Genres/themes/instruments are stored as audio labels, **not** `TrackTags`.

```json
{
  "bpm": 85,
  "key": "G major",
  "danceability": 0.93,
  "voice": 0.95,
  "acoustic": 0.02,
  "electronic": 0.27,
  "timbre": "dark",
  "approachability": 0.72,
  "engagement": 0.84,
  "moods": { "party": 0.95, "happy": 0.78, "aggressive": 0.70, "relaxed": 0.22, "sad": 0.05 },
  "genres": ["Pop/J-pop", "Rock/Pop Rock"],
  "themes": [["energetic", 0.88]],
  "instruments": [{ "name": "drums", "score": 0.91 }]
}
```

`tools/essentia-client` sends camelCase. Raw `analyze()` JSON is also accepted (`key_strength`, `timbre_bright`, `genre_scores`).

### `PUT /api/v1/audio-profiles` (write)

Same body plus `artist` and `title`; matches the library fingerprint.

### `GET /api/v1/audio`

Play-weighted Essentia analytics for the selected range (BPM, key, moods, **primary genre folders** with subgenre children, themes, instruments). `audio.genres` is the primary-folder rollup used by the pie chart.

### `GET /api/v1/audio/labels`

Tracks and artists for one Essentia filter. Query: `kind` (`genre` | `theme` | `instrument` | `bpm` | `key`), `name` (e.g. `Electronic`, `Electronic/House`, `110-129`, `G major`). A primary genre name is a folder and includes every `Primary/Sub` leaf. BPM `name` values are `lt70`, `70-89`, `90-109`, `110-129`, `130-149`, `150plus` (display names such as `70–89` also work). Key `name` is `G major` or just `G` (both scales).

### `GET /api/v1/scrobbles`

Recent plays in the window, paginated (`page`, `take`).

---

## Library (website Library)

### `GET /api/v1/library`

Paged unique tracks. Query:

| Param | Meaning |
| --- | --- |
| `page`, `pageSize` | Default page size 50, max 200 |
| `q`, `artist`, `title`, `album` | Lucene search (same as the site) |
| `status` | Lookup status enum (`Pending`, `NeedsReview`, …) |
| `hasMbid`, `hasTags`, `hasAudio` | `yes` / `no` |
| `audioKind`, `audioLabel` | Essentia label filter (`genre` + `Electronic` or `Electronic/House`) |
| `bpmMin`, `bpmMax` | Inclusive BPM range |
| `bpmBucket` | Histogram bucket (`lt70`, `70-89`, `90-109`, `110-129`, `130-149`, `150plus`) |
| `audioKey` | Musical key (`G major`, or `G` for both scales) |
| `source` | Enrichment source that succeeded (`LastFm`, `MusicBrainz`, …) |
| `sort` | `recent` (default), `plays`, `artist`, `title` |

List items include source **status** only (`sources[].json` is omitted). Full payloads are on `GET /api/v1/tracks/{id}`.

### `PUT /api/v1/library/{id}` (write)

Body matches the library edit form:

```json
{
  "title": "Song",
  "artistName": "Artist",
  "albumTitle": "Album",
  "trackMbid": null,
  "artistMbid": null,
  "albumMbid": null,
  "durationSeconds": 210,
  "isrc": null,
  "summary": null,
  "musicVideoUrl": null,
  "coverUrl": null,
  "discogsReleaseId": null,
  "theAudioDbTrackId": null,
  "vocaDbSongId": null,
  "utaiteDbSongId": null,
  "touhouDbSongId": null,
  "resetEnrichment": false,
  "lookupFromMbid": false
}
```

### `POST /api/v1/tracks/{id}/enrich` (write)

Fetch MusicBrainz recording details when an MBID is present (same as “Fetch MusicBrainz details” on the track page).

---

## Lookups & review

### `GET /api/v1/lookups`

Fingerprint cache. Query: `status`, `take` (default 200). Includes per-status `counts`.

### `POST /api/v1/lookups/{id}/retry` (write)

Re-queue one lookup.

### `GET /api/v1/review`

Needs-review MusicBrainz candidates (`take` default 50).

### `POST /api/v1/review/{id}/accept` (write)

```json
{ "mbid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" }
```

### `POST /api/v1/review/{id}/not-found` (write)

Mark as not in MusicBrainz.

### `POST /api/v1/review/{id}/retry` (write)

Retry the search.

---

## Progress & jobs (website Progress)

### `GET /api/v1/status`

Ingest/enrichment status, coverage, job log, worker progress, recent outbound API calls.

### Jobs (write)

| Method | Path | Effect |
| --- | --- | --- |
| POST | `/api/v1/jobs/sync` | Incremental Last.fm sync |
| POST | `/api/v1/jobs/backfill` | Body `{ "days": 14 }` (1–365, default 14) |
| POST | `/api/v1/jobs/pause` | Pause enrichment |
| POST | `/api/v1/jobs/resume` | Resume enrichment |
| POST | `/api/v1/jobs/retry-lookups` | Retry failed lookups |
| POST | `/api/v1/jobs/seed` | Seed sample scrobbles if the database is empty |

Sync / backfill / retry-lookups return `202`. Pause / resume / seed return `200`. Body: `{ "queued": true, "command": "…" }`.

---

## Settings & keys

Secret values are never returned. `GET` reports whether a Last.fm / Discogs / TheAudioDB secret is already saved. `PUT` uses the same blank-means-keep rule as the Settings form.

### `GET /api/v1/settings`

### `PUT /api/v1/settings` (write)

```json
{
  "lastFmUsername": "user",
  "lastFmApiKey": "",
  "discogsToken": "",
  "theAudioDbApiKey": "",
  "musicBrainzContact": "https://example",
  "musicBrainzBaseUrl": "",
  "musicBrainzCoverArtBaseUrl": "",
  "musicBrainzMinIntervalMs": null,
  "enableMusicBrainz": true,
  "enableLastFmTrackInfo": true,
  "enableDiscogs": true,
  "enableTheAudioDb": true,
  "enableVocaDb": true,
  "enableUtaiteDb": true,
  "enableTouhouDb": true,
  "incrementalIntervalMinutes": 60,
  "seedSampleData": false
}
```

### `GET /api/v1/keys`

Active keys (id, name, prefix, scopes, created, last used). No secrets.

### `POST /api/v1/keys` (write)

```json
{ "name": "Phone", "read": true, "write": true }
```

`201` includes `token` once, plus `warning`.

### `DELETE /api/v1/keys/{id}` (write)

Revokes the key (`204`). Admin UI on Settings does the same.

---

## Example

```bash
# after creating a key on Settings
export KEY=gmk_…

curl -sS -H "Authorization: Bearer $KEY" http://localhost:5107/api/v1/me
curl -sS -H "Authorization: Bearer $KEY" "http://localhost:5107/api/v1/overview?range=30d"
curl -sS -H "Authorization: Bearer $KEY" "http://localhost:5107/api/v1/library?q=miku&page=1"
curl -sS -X POST -H "Authorization: Bearer $KEY" http://localhost:5107/api/v1/jobs/sync
```

Website pages remain available without an API key (analytics are public; admin pages use the cookie login).
