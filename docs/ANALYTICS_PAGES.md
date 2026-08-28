# Analytics pages (planned)

This document is the build spec for the interactive dashboard. **Do not implement these pages yet.** Aggregation (Last.fm ingest, MusicBrainz identity, Discogs / TheAudioDB / Last.fm tags) lands first. When that data is stable, these Razor Pages sit on top of the same SQLite database.

## What already exists

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

All analytics queries should **read scrobbles joined to tracks/artists/albums**. They must not call Last.fm or MusicBrainz.

## Shared UI chrome

Keep Razor Pages + Bootstrap (already in the app). Add Chart.js (or similar) only when these pages are built.

**Global filters** (query string, sticky in a partial `_TimeRange.cshtml`):

- Presets: `7d`, `30d`, `90d`, `1y`, `all`, plus `custom` (`from`, `to` ISO dates)
- Timezone: store display zone in settings later; compute in UTC first, convert in the view
- Search: artist / track / album, matching `Name`, `Title`, and `ArtistAliases`

Every chart page takes the same `TimeRange` record:

```csharp
public sealed record TimeRange(DateTimeOffset From, DateTimeOffset To, string Preset);
```

Helper: `AnalyticsQuery.ApplyRange(IQueryable<Scrobble> scrobbles, TimeRange range)`.

## Pages to add later

### 1. Overview (`/Dashboard`)

At-a-glance numbers for the selected range (and all-time in a secondary line).

| Metric | How |
| --- | --- |
| Total scrobbles | `COUNT(*)` on ranged scrobbles |
| Unique tracks / artists / albums | `COUNT(DISTINCT TrackId / ArtistId / AlbumId)` |
| Listening time | `SUM(Tracks.DurationMs)` where duration is known; also show `% of plays missing duration` |
| Days tracked | distinct UTC dates with ≥1 scrobble (all-time, not the filter) |
| Average scrobbles / day | total / days tracked (or days in range) |
| Current streak | walk distinct dates backward from today until a gap; “consecutive days with ≥1 scrobble” |
| Now playing / most recent | latest `Scrobbles` row with artist, track, album, timestamp |

Layout: stat cards on top, sparkline of daily volume, most recent track card linking to `/Tracks/{id}`.

### 2. Top lists (`/Tops`)

Tabs: Artists, Tracks, Albums. Same time range.

- Rank, name, play count, optional listening time
- Click through to detail pages
- **Movers:** compute the same ranking for the previous window of equal length (`To-From`). Join on artist/track/album id. Show `delta` and `%`. Highlight new entries as “new”
- Limit 50 with “show more”

SQL sketch: `GROUP BY TrackId ORDER BY COUNT(*) DESC`.

### 3. Discovery (`/Discovery`)

“First time you played X” in the selected range.

- For each track/artist, `MIN(PlayedAt)` as first-heard
- Filter `first-heard` ∈ range
- Sort newest discovery first
- Useful for a “this month I found…” list

### 4. Time patterns (`/Patterns`)

**Heatmap (hour × weekday)**  
GitHub-style grid: 7 rows (Mon–Sun) × 24 columns. Cell = scrobble count (or listening minutes). CSS grid is enough; no chart library required. Use UTC or local hour once timezone exists.

**Time of day buckets**  
Morning 5–11, afternoon 11–17, evening 17–22, night 22–5. Stacked bar or four stats.

**Monthly / yearly volume**  
Bar chart of scrobbles (and minutes) by month. Year selector.

**Year in review (`/Wrapped/{year}`)**  
Reuse overview + tops + discovery + heatmap constrained to that calendar year. Add: new artists that year, most replayed track, longest streak, busiest hour. Copy can wait; the queries are the same aggregations.

### 5. Artist detail (`/Artists/{id}`)

- Name, aliases, MBID, tags rolled up from tracks
- Total plays, unique tracks played, first/last played
- Play history timeline (scrobbles over time — line or heat strip)
- Top tracks table for this artist
- Link to MusicBrainz when MBID exists

### 6. Track detail (`/Tracks/{id}`)

- Artist, album, MBID, duration, fingerprint, lookup status
- Play count, first/last
- All timestamps plotted (strip of dots or vertical timeline)
- Tags by source
- Raw payloads remain on the debug Recent page; optional collapsed “sources” section

### 7. Deep cuts (`/DeepCuts`)

- Tracks with `play_count = 1` vs tracks with `play_count >= N` (default 10)
- Two tables, same range
- Helps separate one-off finds vs rinsed tracks (e.g. consecutive Lose-Lose Days)

### 8. Sessions (`/Sessions`)

Cluster scrobbles into sessions:

1. Order scrobbles by `PlayedAt`
2. Start a new session when gap from previous play > `G` minutes (default 30, setting later)
3. Session length = last – first (or sum of durations if richer)
4. Persist **nothing** at first — compute on read. If this is slow on a huge history, add a `Sessions` table in a later migration keyed by first/last scrobble id

Show:

- Session list (start, length, track count, first/last artist)
- Average session length, median tracks/session
- **Repeat rate:** consecutive scrobbles with the same `TrackId` / total consecutive pairs
- **Skip-adjacent:** gap from play N to N+1 is much smaller than `Tracks.DurationMs` of N (e.g. gap < 30% of duration). Only when duration is known. This is a hint, not a Last.fm skip flag

## Query service to add with the UI

Create `Services/Analytics/AnalyticsQueries.cs` (not now) with methods matching the pages:

- `GetOverview(TimeRange)`
- `GetTopArtists/Tracks/Albums(TimeRange, previousRange)`
- `GetDiscoveries(TimeRange)`
- `GetHeatmap(TimeRange)`
- `GetStreak()`
- `GetArtistDetail(id, TimeRange)`
- `GetTrackDetail(id)`
- `GetDeepCuts(TimeRange, heavyThreshold)`
- `GetSessions(gap)`

Keep aggregations in SQL (EF `GroupBy`) so 100k+ scrobbles stay on the server. Add indexes already present: `Scrobbles.PlayedAt`, `Scrobbles.TrackId`.

## Japanese / aliases

Search and labels should prefer:

1. User-facing Last.fm name stored on `Artist.Name` / `Track.Title`
2. Alias list for matching typed romaji or English
3. Never collapse two MusicBrainz MBIDs into one row just because names look similar — identity stays on MBID / fingerprint

Overview charts can show original script; a later toggle can prefer `SortName` or an English alias when present.

## Out of scope until audio extraction

Skip detection beyond timestamp gaps, “true” listening time for untimed tracks, AcousticBrainz features. Duration still comes from Last.fm / MusicBrainz / TheAudioDB when those enrichers have run.

## Suggested implementation order (when we build this)

1. `TimeRange` + Overview stats (no charts)
2. Top lists + movers
3. Artist/track detail
4. Heatmap + monthly volume
5. Discovery + deep cuts
6. Sessions + skip-adjacent + Wrapped
