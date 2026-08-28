using System.Text.Json;
using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Discogs;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.MusicBrainz;
using GalaxyMusicDataset.Services.TheAudioDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class MetadataEnrichmentService(
    AppDbContext db,
    ExternalClientFactory clients,
    TagService tags,
    CatalogService catalog,
    AggregationProgress progress,
    IOptionsMonitor<AggregationOptions> options)
{
    public async Task<int> EnrichNextAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;

        if (settings.EnableMusicBrainz)
        {
            var n = await EnrichMusicBrainzDetailsAsync(cancellationToken);
            if (n > 0)
            {
                return n;
            }
        }

        if (settings.EnableLastFmTrackInfo && clients.TryCreateLastFm() is not null)
        {
            var n = await EnrichLastFmAsync(cancellationToken);
            if (n > 0)
            {
                return n;
            }
        }

        if (settings.EnableDiscogs && clients.TryCreateDiscogs() is not null)
        {
            var n = await EnrichDiscogsAsync(cancellationToken);
            if (n > 0)
            {
                return n;
            }
        }

        if (settings.EnableTheAudioDb && clients.TryCreateTheAudioDb() is not null)
        {
            var n = await EnrichAudioDbAsync(cancellationToken);
            if (n > 0)
            {
                return n;
            }
        }

        return 0;
    }

    private async Task<int> EnrichMusicBrainzDetailsAsync(CancellationToken cancellationToken)
    {
        var batch = await db.Tracks
            .Include(t => t.Artist)
            .Include(t => t.Album)
            .Include(t => t.SourcePayloads)
            .Where(t => t.Mbid != null && t.Mbid != "" &&
                        !t.SourcePayloads.Any(p =>
                            p.Source == EnrichmentSource.MusicBrainz &&
                            p.Status == SourceFetchStatus.Success &&
                            p.PayloadJson != null &&
                            p.PayloadJson.Contains("\"isrcs\"")))
            .OrderBy(t => t.Id)
            .Take(25)
            .ToListAsync(cancellationToken);
        var track = batch.FirstOrDefault(t =>
        {
            var payload = t.SourcePayloads.FirstOrDefault(p => p.Source == EnrichmentSource.MusicBrainz);
            return payload?.FetchedAt is null
                   || payload.ErrorMessage is null
                   || !payload.ErrorMessage.StartsWith("Recording details", StringComparison.Ordinal)
                   || DateTimeOffset.UtcNow - payload.FetchedAt.Value >= TimeSpan.FromMinutes(2);
        });
        if (track is null)
        {
            return 0;
        }

        var payload = await EnsurePayloadAsync(track.Id, EnrichmentSource.MusicBrainz, cancellationToken);
        progress.SetPhase("MusicBrainz recording", $"{track.Artist.Name} – {track.Title}");
        var mb = clients.CreateMusicBrainz();
        try
        {
            var json = await mb.GetRecordingJsonAsync(track.Mbid!, cancellationToken);
            var details = MusicBrainzClient.ParseRecordingDetails(json);
            if (details is null)
            {
                payload.Status = SourceFetchStatus.NotFound;
                payload.ErrorMessage = "Recording lookup returned no usable body.";
                payload.FetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return 1;
            }

            payload.Status = SourceFetchStatus.Success;
            payload.ExternalId = details.Mbid;
            payload.PayloadJson = details.RawJson;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            payload.ErrorMessage = null;

            ApplyRecordingDetails(track, details);

            if (track.AlbumId is null && !string.IsNullOrWhiteSpace(details.AlbumTitle))
            {
                var album = await catalog.GetOrCreateAlbumAsync(track.Artist, details.AlbumTitle, details.ReleaseMbid, cancellationToken);
                track.AlbumId = album?.Id;
                track.Album = album;
            }

            if (track.Album is not null && track.Album.ReleaseYear is null && details.ReleaseYear is > 0)
            {
                track.Album.ReleaseYear = details.ReleaseYear;
            }

            if (!string.IsNullOrWhiteSpace(details.ReleaseMbid) && string.IsNullOrWhiteSpace(track.Album?.CoverUrl))
            {
                try
                {
                    var cover = await mb.GetReleaseFrontCoverUrlAsync(details.ReleaseMbid, cancellationToken);
                    CatalogService.SetCoverIfEmpty(track.Album, cover);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    progress.Log($"Cover Art Archive skipped for {details.ReleaseMbid}: {ex.Message}");
                }
            }

            var tagPairs = details.Genres.Select(g => (g, 80))
                .Concat(details.Tags.Select(t => (t.Name, Math.Max(1, t.Count))));
            await tags.ApplyTagsAsync(track.Id, EnrichmentSource.MusicBrainz, tagPairs, cancellationToken);
            track.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            progress.Log($"MB details: {track.Artist.Name} – {track.Title}");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            payload.ErrorMessage = "Recording details: " + ex.Message;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (ex is JsonApiException api && HttpResponseHelpers.IsTransientStatus(api.StatusCode))
            {
                progress.Log($"MB recording busy for {track.Artist.Name} – {track.Title}; will retry.");
            }
            else
            {
                progress.Error($"MB recording failed for {track.Artist.Name} – {track.Title}: {ex.Message}");
            }

            return 1;
        }
    }

    private async Task<int> EnrichLastFmAsync(CancellationToken cancellationToken)
    {
        var track = await NextTrackNeedingAsync(EnrichmentSource.LastFm, cancellationToken);
        if (track is null)
        {
            return 0;
        }

        var client = clients.TryCreateLastFm();
        if (client is null)
        {
            return 0;
        }

        progress.SetPhase("Last.fm track.getInfo", $"{track.Artist.Name} – {track.Title}");
        var payload = await EnsurePayloadAsync(track.Id, EnrichmentSource.LastFm, cancellationToken);
        try
        {
            var info = await client.GetTrackInfoAsync(track.Artist.Name, track.Title, track.Mbid, cancellationToken);
            if (info is null)
            {
                payload.Status = SourceFetchStatus.NotFound;
                payload.FetchedAt = DateTimeOffset.UtcNow;
                payload.ErrorMessage = "track.getInfo returned no match.";
                await db.SaveChangesAsync(cancellationToken);
                return 1;
            }

            payload.Status = SourceFetchStatus.Success;
            payload.PayloadJson = info.RawJson;
            payload.ExternalId = info.Mbid;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            payload.ErrorMessage = null;
            await ApplyLastFmAsync(track, info, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            progress.Log($"Last.fm info: {track.Artist.Name} – {track.Title}");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            payload.Status = SourceFetchStatus.Error;
            payload.ErrorMessage = ex.Message;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            progress.Error($"Last.fm info failed: {ex.Message}");
            return 1;
        }
    }

    private async Task ApplyLastFmAsync(Track track, LastFmTrackInfo info, CancellationToken cancellationToken)
    {
        if (track.DurationMs is null && info.DurationMs is > 0)
        {
            track.DurationMs = info.DurationMs;
        }

        track.Mbid = CatalogService.Coalesce(track.Mbid, info.Mbid);
        track.Summary = CatalogService.Coalesce(track.Summary, StripWikiMarkup(info.WikiSummary));
        track.Artist.Mbid = CatalogService.Coalesce(track.Artist.Mbid, info.ArtistMbid);
        track.Artist.LastFmUrl = CatalogService.Coalesce(track.Artist.LastFmUrl, info.ArtistUrl);

        if (track.AlbumId is null && !string.IsNullOrWhiteSpace(info.AlbumTitle))
        {
            var album = await catalog.GetOrCreateAlbumAsync(track.Artist, info.AlbumTitle, info.AlbumMbid, cancellationToken);
            track.AlbumId = album?.Id;
            track.Album = album;
        }

        CatalogService.SetCoverIfEmpty(track.Album, info.AlbumImageUrl);
        track.UpdatedAt = DateTimeOffset.UtcNow;
        await tags.ApplyTagsAsync(
            track.Id,
            EnrichmentSource.LastFm,
            info.Tags.Select(t => (t.Name, t.Weight)),
            cancellationToken);
    }

    private async Task<int> EnrichDiscogsAsync(CancellationToken cancellationToken)
    {
        var track = await NextTrackNeedingAsync(EnrichmentSource.Discogs, cancellationToken);
        if (track is null)
        {
            return 0;
        }

        var client = clients.TryCreateDiscogs();
        if (client is null)
        {
            await SkipAsync(track.Id, EnrichmentSource.Discogs, "Discogs token not configured.", cancellationToken);
            return 1;
        }

        progress.SetPhase("Discogs search", $"{track.Artist.Name} – {track.Title}");
        var payload = await EnsurePayloadAsync(track.Id, EnrichmentSource.Discogs, cancellationToken);
        try
        {
            var result = await client.SearchAsync(track.Artist.Name, track.Title, track.Album?.Title, cancellationToken);
            if (result is null || result.Value.Best is null || string.IsNullOrWhiteSpace(result.Value.Best.Id))
            {
                payload.Status = SourceFetchStatus.NotFound;
                payload.PayloadJson = result?.RawJson;
                payload.FetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return 1;
            }

            var hit = result.Value.Best;
            DiscogsRelease? release = null;
            try
            {
                release = await client.GetReleaseAsync(hit.Id!, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress.Log($"Discogs release {hit.Id} failed: {ex.Message}");
            }

            payload.Status = SourceFetchStatus.Success;
            payload.ExternalId = release?.Id ?? hit.Id;
            payload.PayloadJson = release?.RawJson ?? result.Value.RawJson;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            payload.ErrorMessage = null;

            track.DiscogsReleaseId = CatalogService.Coalesce(track.DiscogsReleaseId, release?.Id ?? hit.Id);
            var year = release?.Year ?? (int.TryParse(hit.Year, out var searchYear) ? searchYear : null);
            var cover = release?.CoverUrl ?? hit.CoverUrl;
            var extraTags = new List<(string, int)>();
            extraTags.AddRange((release?.Genres ?? []).Select(g => (g, 50)));
            extraTags.AddRange((release?.Styles ?? []).Select(g => (g, 40)));
            if (extraTags.Count == 0)
            {
                using var doc = JsonDocument.Parse(result.Value.RawJson);
                if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    extraTags.AddRange(ReadStringArray(results[0], "genre").Select(g => (g, 50)));
                    extraTags.AddRange(ReadStringArray(results[0], "style").Select(g => (g, 40)));
                }
            }

            if (track.AlbumId is null && !string.IsNullOrWhiteSpace(release?.Title ?? hit.Title))
            {
                var albumTitle = release?.Title ?? hit.Title;
                if (albumTitle is not null && albumTitle.Contains(" - ", StringComparison.Ordinal))
                {
                    albumTitle = albumTitle[(albumTitle.IndexOf(" - ", StringComparison.Ordinal) + 3)..];
                }

                var album = await catalog.GetOrCreateAlbumAsync(track.Artist, albumTitle, null, cancellationToken);
                track.AlbumId = album?.Id;
                track.Album = album;
            }

            if (track.Album is not null && track.Album.ReleaseYear is null && year is > 0)
            {
                track.Album.ReleaseYear = year;
            }

            CatalogService.SetCoverIfEmpty(track.Album, cover);
            await tags.ApplyTagsAsync(track.Id, EnrichmentSource.Discogs, extraTags, cancellationToken);
            track.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            progress.Log($"Discogs: {track.Artist.Name} – {track.Title}");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            payload.Status = SourceFetchStatus.Error;
            payload.ErrorMessage = ex.Message;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            progress.Error($"Discogs failed: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> EnrichAudioDbAsync(CancellationToken cancellationToken)
    {
        var track = await NextTrackNeedingAsync(EnrichmentSource.TheAudioDb, cancellationToken);
        if (track is null)
        {
            return 0;
        }

        var client = clients.TryCreateTheAudioDb();
        if (client is null)
        {
            await SkipAsync(track.Id, EnrichmentSource.TheAudioDb, "TheAudioDB API key not configured.", cancellationToken);
            return 1;
        }

        progress.SetPhase("TheAudioDB search", $"{track.Artist.Name} – {track.Title}");
        var payload = await EnsurePayloadAsync(track.Id, EnrichmentSource.TheAudioDb, cancellationToken);
        try
        {
            var result = await client.SearchTrackAsync(track.Artist.Name, track.Title, cancellationToken);
            if (result is null || result.Value.Best is null)
            {
                payload.Status = SourceFetchStatus.NotFound;
                payload.PayloadJson = result?.RawJson;
                payload.FetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return 1;
            }

            var hit = result.Value.Best;
            var thumb = hit.ThumbUrl;
            if (string.IsNullOrWhiteSpace(thumb) && !string.IsNullOrWhiteSpace(hit.AlbumId))
            {
                try
                {
                    thumb = await client.LookupAlbumThumbAsync(hit.AlbumId, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    progress.Log($"TheAudioDB album thumb failed: {ex.Message}");
                }
            }

            payload.Status = SourceFetchStatus.Success;
            payload.ExternalId = hit.Id;
            payload.PayloadJson = result.Value.RawJson;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            payload.ErrorMessage = null;

            track.TheAudioDbTrackId = CatalogService.Coalesce(track.TheAudioDbTrackId, hit.Id);
            track.Mbid = CatalogService.Coalesce(track.Mbid, hit.MusicBrainzId);
            track.Summary = CatalogService.Coalesce(track.Summary, hit.Description);
            track.MusicVideoUrl = CatalogService.Coalesce(track.MusicVideoUrl, hit.MusicVideoUrl);
            if (track.DurationMs is null && hit.DurationMs is > 0)
            {
                track.DurationMs = hit.DurationMs;
            }

            if (track.AlbumId is null && !string.IsNullOrWhiteSpace(hit.Album))
            {
                var album = await catalog.GetOrCreateAlbumAsync(track.Artist, hit.Album, null, cancellationToken);
                track.AlbumId = album?.Id;
                track.Album = album;
            }

            CatalogService.SetCoverIfEmpty(track.Album, thumb);

            var extraTags = new List<(string, int)>();
            if (!string.IsNullOrWhiteSpace(hit.Genre))
            {
                extraTags.Add((hit.Genre, 50));
            }

            if (!string.IsNullOrWhiteSpace(hit.Style))
            {
                extraTags.Add((hit.Style, 40));
            }

            if (!string.IsNullOrWhiteSpace(hit.Mood))
            {
                extraTags.Add((hit.Mood, 30));
            }

            if (!string.IsNullOrWhiteSpace(hit.Theme))
            {
                extraTags.Add((hit.Theme, 20));
            }

            if (extraTags.Count > 0)
            {
                await tags.ApplyTagsAsync(track.Id, EnrichmentSource.TheAudioDb, extraTags, cancellationToken);
            }

            track.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            progress.Log($"TheAudioDB: {track.Artist.Name} – {track.Title}");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            payload.Status = SourceFetchStatus.Error;
            payload.ErrorMessage = ex.Message;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            progress.Error($"TheAudioDB failed: {ex.Message}");
            return 1;
        }
    }

    public static void ApplyRecordingDetails(Track track, MusicBrainzRecordingDetails details)
    {
        if (track.DurationMs is null && details.LengthMs is > 0)
        {
            track.DurationMs = details.LengthMs;
        }

        track.Isrc = CatalogService.Coalesce(track.Isrc, details.FirstIsrc);
        track.Mbid = CatalogService.Coalesce(track.Mbid, details.Mbid);
    }

    public static string? StripWikiMarkup(string? wiki)
    {
        if (string.IsNullOrWhiteSpace(wiki))
        {
            return null;
        }

        var cut = wiki.IndexOf("<a href", StringComparison.OrdinalIgnoreCase);
        var text = cut >= 0 ? wiki[..cut] : wiki;
        return string.IsNullOrWhiteSpace(text) ? wiki.Trim() : text.Trim();
    }

    private async Task<Track?> NextTrackNeedingAsync(EnrichmentSource source, CancellationToken cancellationToken)
    {
        return await db.Tracks
            .Include(t => t.Artist)
            .Include(t => t.Album)
            .Include(t => t.SourcePayloads)
            .Where(t => !t.SourcePayloads.Any(p =>
                p.Source == source &&
                (p.Status == SourceFetchStatus.Success ||
                 p.Status == SourceFetchStatus.NotFound ||
                 p.Status == SourceFetchStatus.Skipped)))
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<TrackSourcePayload> EnsurePayloadAsync(long trackId, EnrichmentSource source, CancellationToken cancellationToken)
    {
        var payload = await db.TrackSourcePayloads
            .FirstOrDefaultAsync(p => p.TrackId == trackId && p.Source == source, cancellationToken);
        if (payload is not null)
        {
            return payload;
        }

        payload = new TrackSourcePayload
        {
            TrackId = trackId,
            Source = source,
            Status = SourceFetchStatus.NotStarted
        };
        db.TrackSourcePayloads.Add(payload);
        await db.SaveChangesAsync(cancellationToken);
        return payload;
    }

    private async Task SkipAsync(long trackId, EnrichmentSource source, string reason, CancellationToken cancellationToken)
    {
        var payload = await EnsurePayloadAsync(trackId, source, cancellationToken);
        payload.Status = SourceFetchStatus.Skipped;
        payload.ErrorMessage = reason;
        payload.FetchedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
            }
        }
    }
}
