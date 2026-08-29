# Analytics pages

Interactive dashboards over the SQLite warehouse. Queries read scrobbles joined to tracks, artists, and albums. They do not call Last.fm or MusicBrainz.

## Data

Normalized tables (not one row of duplicated metadata per play):

| Table | Role |
| --- | --- |
| `Scrobbles` | One row per listen (`UnixTimestamp` unique, `PlayedAt`, original Last.fm names) |
| `Tracks` | Canonical song (`Fingerprint` = SHA-256 of normalized artist+title, optional `Mbid`, `DurationMs`) |
| `Artists` / `ArtistAliases` | Names plus MusicBrainz aliases (JP / romaji / English) |
| `Albums` | Optional album attached when known |
| `TrackLookups` | Identity-resolution cache so the same song is not searched four times |
| `Tags` / `TrackTags` | Crowd tags with `Source` (`LastFm`, `MusicBrainz`, `Discogs`, `TheAudioDb`) |
| `TrackSourcePayloads` | Raw JSON from each API |

## Shared UI chrome

Razor Pages + Bootstrap + Chart.js (sparklines and monthly bars). Heatmaps use CSS grid.

**Global filters** (query string, sticky in `_TimeRange.cshtml`):

- Presets: `7d`, `30d`, `90d`, `1y`, `all`, plus `custom` (`from`, `to` ISO dates)
- Timezone: UTC for aggregations and display (local zone can land in settings later)
- Search: artist / track / album, matching `Name`, `Title`, and `ArtistAliases`

Every chart page takes the same `TimeRange` record:

```csharp
public sealed record TimeRange(DateTimeOffset From, DateTimeOffset To, string Preset);
```

Helper: `AnalyticsQuery.ApplyRange(IQueryable<Scrobble> scrobbles, TimeRange range)`.

## Pages

### 1. Overview (`/Dashboard`)

At-a-glance numbers for the selected range (and all-time in a secondary line).

| Metric | How |
| --- | --- |
| Total scrobbles | `COUNT(*)` on ranged scrobbles |
| Unique tracks / artists / albums | `COUNT(DISTINCT TrackId / ArtistId / AlbumId)` |
| Listening time | `SUM(Tracks.DurationMs)` where duration is known; also show `% of plays missing duration` |
| Days tracked | distinct UTC dates with ≥1 scrobble (all-time, not the filter) |
| Average scrobbles / day | total / calendar days in range (or days tracked for all-time) |
| Current streak | consecutive UTC days with ≥1 scrobble, ending today or yesterday |
| Now playing / most recent | latest `Scrobbles` row with artist, track, album, timestamp |

Layout: stat cards on top, sparkline of daily volume, most recent track card linking to `/Tracks/{id}`.

### 2. Top lists (`/Tops`)

Tabs: Artists, Tracks, Albums. Same time range.

- Rank, name, play count, optional listening time
- Click through to detail pages
- **Movers:** the same ranking for the previous window of equal length. `delta`, `%`, and **new**
- Limit 50 with “show more”

### 3. Discovery (`/Discovery`)

“First time you played X” in the selected range.

- For each track/artist, `MIN(PlayedAt)` as first-heard (all-time)
- Filter `first-heard` ∈ range
- Sort newest discovery first

### 4. Time patterns (`/Patterns`)

**Heatmap (hour × weekday)** — 7 rows (Mon–Sun) × 24 columns. Cell = scrobble count. UTC.

**Time of day buckets** — Morning 5–11, afternoon 11–17, evening 17–22, night 22–5.

**Monthly / yearly volume** — bar chart of scrobbles and minutes by month.

**Year in review (`/Wrapped/{year}`)** — overview + tops + discovery + heatmap for that calendar year, plus new artists, most replayed track, longest streak, busiest hour.

### 5. Artist detail (`/Artists/{id}`)

Name, aliases, MBID, tags rolled up from tracks. Plays, unique tracks, first/last, timeline, top tracks. MusicBrainz link when MBID exists.

### 6. Track detail (`/Tracks/{id}`)

Artist, album, MBID, duration, fingerprint, lookup status. Play count, first/last, timestamp strip, tags by source, collapsed source payloads.

### 7. Deep cuts (`/DeepCuts`)

Tracks with `play_count = 1` vs tracks with `play_count >= N` (default 10). Two tables, same range.

### 8. Sessions (`/Sessions`)

Cluster scrobbles into sessions on read (no `Sessions` table):

1. Order scrobbles by `PlayedAt`
2. Start a new session when gap from previous play > `G` minutes (default 30)
3. Session length = last – first (or sum of durations when that span is zero)

Shows session list, average length, median tracks/session, **repeat rate** (consecutive same `TrackId`), and **skip-adjacent** (gap < 30% of known duration).

## Query service

`Services/Analytics/AnalyticsQueries.cs`:

- `GetOverview(TimeRange)`
- `GetTopArtists/Tracks/Albums(TimeRange, previousRange)`
- `GetDiscoveries(TimeRange)`
- `GetHeatmap(TimeRange)`
- `GetStreak()`
- `GetArtistDetail(id, TimeRange)`
- `GetTrackDetail(id)`
- `GetDeepCuts(TimeRange, heavyThreshold)`
- `GetSessions(gap)`

Aggregations stay in SQL (EF `GroupBy`) so large histories stay on the server. Indexes already present: `Scrobbles.PlayedAt`, `Scrobbles.TrackId`.

## Japanese / aliases

Search and labels prefer:

1. User-facing Last.fm name stored on `Artist.Name` / `Track.Title`
2. Alias list for matching typed romaji or English
3. Identity stays on MBID / fingerprint — names are not merged just because they look similar

## Out of scope until audio extraction

Skip detection beyond timestamp gaps, “true” listening time for untimed tracks, AcousticBrainz features. Duration still comes from Last.fm / MusicBrainz / TheAudioDB when those enrichers have run.
