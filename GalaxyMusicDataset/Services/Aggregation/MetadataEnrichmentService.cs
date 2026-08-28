using System.Text.Json;
using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
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
            var info = await client.GetTrackInfoAsync(track.Artist.Name, track.Title, cancellationToken);
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

            if (track.DurationMs is null && info.DurationMs is > 0)
            {
                track.DurationMs = info.DurationMs;
            }

            if (track.Mbid is null && !string.IsNullOrWhiteSpace(info.Mbid))
            {
                track.Mbid = info.Mbid;
            }

            if (track.Artist.Mbid is null && !string.IsNullOrWhiteSpace(info.ArtistMbid))
            {
                track.Artist.Mbid = info.ArtistMbid;
            }

            if (track.AlbumId is null && !string.IsNullOrWhiteSpace(info.AlbumTitle))
            {
                var album = await catalog.GetOrCreateAlbumAsync(track.Artist, info.AlbumTitle, info.AlbumMbid, cancellationToken);
                track.AlbumId = album?.Id;
            }

            track.UpdatedAt = DateTimeOffset.UtcNow;
            await tags.ApplyTagsAsync(
                track.Id,
                EnrichmentSource.LastFm,
                info.Tags.Select(t => (t.Name, t.Weight)),
                cancellationToken);
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
            if (result is null || result.Value.Best is null)
            {
                payload.Status = SourceFetchStatus.NotFound;
                payload.PayloadJson = result?.RawJson;
                payload.FetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return 1;
            }

            var hit = result.Value.Best;
            payload.Status = SourceFetchStatus.Success;
            payload.ExternalId = hit.Id;
            payload.PayloadJson = result.Value.RawJson;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            payload.ErrorMessage = null;

            if (int.TryParse(hit.Year, out var year) && track.Album is not null && track.Album.ReleaseYear is null)
            {
                track.Album.ReleaseYear = year;
            }

            var extraTags = new List<(string, int)>();
            using var doc = JsonDocument.Parse(result.Value.RawJson);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var first = results[0];
                extraTags.AddRange(ReadStringArray(first, "genre").Select(g => (g, 50)));
                extraTags.AddRange(ReadStringArray(first, "style").Select(g => (g, 40)));
            }

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
            payload.Status = SourceFetchStatus.Success;
            payload.ExternalId = hit.Id;
            payload.PayloadJson = result.Value.RawJson;
            payload.FetchedAt = DateTimeOffset.UtcNow;
            payload.ErrorMessage = null;
            if (track.DurationMs is null && hit.DurationMs is > 0)
            {
                track.DurationMs = hit.DurationMs;
            }

            if (!string.IsNullOrWhiteSpace(hit.Genre))
            {
                await tags.ApplyTagsAsync(track.Id, EnrichmentSource.TheAudioDb, [(hit.Genre, 50)], cancellationToken);
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
