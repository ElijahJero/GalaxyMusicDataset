using System.Text.Json;
using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Http;
using GalaxyMusicDataset.Services.MusicBrainz;
using GalaxyMusicDataset.Services.Normalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class MusicBrainzLookupService(
    AppDbContext db,
    CatalogService catalog,
    ExternalClientFactory clients,
    AggregationProgress progress,
    IOptionsMonitor<AggregationOptions> options)
{
    private static readonly LookupStatus[] Terminal =
    [
        LookupStatus.AutoMatched,
        LookupStatus.NeedsReview,
        LookupStatus.NotFound,
        LookupStatus.Rejected,
        LookupStatus.ManualMatched
    ];

    public async Task<int> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.EnableMusicBrainz)
        {
            return 0;
        }

        var lookup = await PickNextLookupAsync(cancellationToken);
        if (lookup is null)
        {
            return 0;
        }

        if (lookup.TrackId is not null)
        {
            var existingMbid = await db.Tracks.Where(t => t.Id == lookup.TrackId).Select(t => t.Mbid).FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(existingMbid))
            {
                lookup.Status = LookupStatus.AutoMatched;
                lookup.MatchedMbid = existingMbid;
                lookup.BestScore = 1;
                lookup.QueryUsed = "existing track mbid";
                lookup.LastAttemptUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return 1;
            }
        }

        lookup.Status = LookupStatus.InProgress;
        lookup.AttemptCount++;
        lookup.LastAttemptUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        progress.SetPhase("MusicBrainz lookup", $"{lookup.ArtistName} – {lookup.TrackName}");
        var mb = clients.CreateMusicBrainz();
        try
        {
            var candidates = await mb.SearchRecordingsAsync(
                lookup.ArtistName,
                lookup.TrackName,
                lookup.AlbumName,
                cancellationToken);

            lookup.CandidateJson = JsonSerializer.Serialize(candidates);
            lookup.QueryUsed = "recording+artist(+release, +romaji if kana)";

            if (candidates.Count == 0)
            {
                lookup.Status = LookupStatus.NotFound;
                lookup.BestScore = 0;
                lookup.ErrorMessage = "No MusicBrainz recordings returned.";
                await db.SaveChangesAsync(cancellationToken);
                progress.Log($"MB not found: {lookup.ArtistName} – {lookup.TrackName}");
                return 1;
            }

            var best = candidates[0];
            lookup.BestScore = best.Score;
            var artistScore = StringSimilarity.Ratio(
                TextNormalizer.Normalize(lookup.ArtistName),
                TextNormalizer.Normalize(best.Artist));
            var decision = RecordingMatchScorer.Decide(
                best.Score,
                artistScore,
                settings.AutoMatchThreshold,
                settings.ReviewThreshold);

            switch (decision)
            {
                case LookupDecision.AutoMatch:
                    lookup.Status = LookupStatus.AutoMatched;
                    lookup.MatchedMbid = best.Mbid;
                    lookup.ErrorMessage = null;
                    if (lookup.TrackId is not null)
                    {
                        await ApplyMatchAsync(lookup.TrackId.Value, best, cancellationToken);
                    }

                    progress.Log($"MB auto-match {best.Score:0.00}: {lookup.ArtistName} – {lookup.TrackName}");
                    break;
                case LookupDecision.NeedsReview:
                    lookup.Status = LookupStatus.NeedsReview;
                    progress.Log($"MB needs review {best.Score:0.00}: {lookup.ArtistName} – {lookup.TrackName}");
                    break;
                default:
                    lookup.Status = LookupStatus.NotFound;
                    lookup.ErrorMessage = $"Best score {best.Score:0.00} below review threshold.";
                    progress.Log($"MB low confidence {best.Score:0.00}: {lookup.ArtistName} – {lookup.TrackName}");
                    break;
            }

            await db.SaveChangesAsync(cancellationToken);
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 503/429 are "server busy", not "this recording does not exist".
            // Leave the row pending so we retry after cooldown instead of
            // skipping the song as NotFound.
            lookup.Status = LookupStatus.Pending;
            lookup.LastAttemptUtc = DateTimeOffset.UtcNow;
            lookup.ErrorMessage = ex.Message;
            if (ex is JsonApiException api && HttpResponseHelpers.IsTransientStatus(api.StatusCode))
            {
                lookup.ErrorMessage = $"MusicBrainz busy (HTTP {api.StatusCode}); will retry after cooldown.";
                progress.Log($"MB busy for {lookup.ArtistName} – {lookup.TrackName}; backing off.");
            }
            else
            {
                progress.Error($"MB lookup failed for {lookup.ArtistName} – {lookup.TrackName}: {ex.Message}");
            }

            await db.SaveChangesAsync(cancellationToken);
            return 1;
        }
    }

    public async Task AcceptCandidateAsync(long lookupId, string mbid, CancellationToken cancellationToken)
    {
        var lookup = await db.TrackLookups.FirstOrDefaultAsync(l => l.Id == lookupId, cancellationToken)
                     ?? throw new InvalidOperationException("Lookup not found.");
        var candidates = ParseCandidates(lookup.CandidateJson);
        var match = candidates.FirstOrDefault(c => string.Equals(c.Mbid, mbid, StringComparison.OrdinalIgnoreCase))
                    ?? new RecordingCandidate(mbid, lookup.TrackName, lookup.ArtistName, lookup.AlbumName, null, null, null, null, 1);
        lookup.Status = LookupStatus.ManualMatched;
        lookup.MatchedMbid = mbid;
        lookup.BestScore = match.Score;
        lookup.LastAttemptUtc = DateTimeOffset.UtcNow;
        lookup.ErrorMessage = null;
        if (lookup.TrackId is not null)
        {
            await ApplyMatchAsync(lookup.TrackId.Value, match, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkNotFoundAsync(long lookupId, CancellationToken cancellationToken)
    {
        var lookup = await db.TrackLookups.FirstOrDefaultAsync(l => l.Id == lookupId, cancellationToken)
                     ?? throw new InvalidOperationException("Lookup not found.");
        lookup.Status = LookupStatus.NotFound;
        lookup.LastAttemptUtc = DateTimeOffset.UtcNow;
        lookup.ErrorMessage = "Marked not in MusicBrainz by user.";
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RetryAsync(long lookupId, CancellationToken cancellationToken)
    {
        var lookup = await db.TrackLookups.FirstOrDefaultAsync(l => l.Id == lookupId, cancellationToken)
                     ?? throw new InvalidOperationException("Lookup not found.");
        if (Terminal.Contains(lookup.Status) && lookup.Status != LookupStatus.NotFound && lookup.Status != LookupStatus.Rejected && lookup.Status != LookupStatus.Failed)
        {
            return;
        }

        lookup.Status = LookupStatus.Pending;
        lookup.ErrorMessage = null;
        lookup.LastAttemptUtc = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RetryFailedAsync(CancellationToken cancellationToken)
    {
        await db.TrackLookups
            .Where(l => l.Status == LookupStatus.Failed
                        || l.Status == LookupStatus.NotFound
                        || l.Status == LookupStatus.InProgress)
            .ExecuteUpdateAsync(
                s => s.SetProperty(l => l.Status, LookupStatus.Pending)
                    .SetProperty(l => l.ErrorMessage, (string?)null)
                    .SetProperty(l => l.LastAttemptUtc, (DateTimeOffset?)null),
                cancellationToken);
    }

    /// <summary>
    /// Requeue lookups that were previously marked NotFound because MusicBrainz
    /// was busy (503), not because the recording is missing.
    /// </summary>
    public async Task<int> RequeueTransientFailuresAsync(CancellationToken cancellationToken)
    {
        var candidates = await db.TrackLookups
            .Where(l => l.Status == LookupStatus.NotFound
                        || l.Status == LookupStatus.Failed
                        || l.Status == LookupStatus.InProgress)
            .Select(l => new { l.Id, l.ErrorMessage, l.Status })
            .ToListAsync(cancellationToken);

        var ids = candidates
            .Where(l => l.Status == LookupStatus.InProgress || IsTransientFailureMessage(l.ErrorMessage))
            .Select(l => l.Id)
            .ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        await db.TrackLookups
            .Where(l => ids.Contains(l.Id))
            .ExecuteUpdateAsync(
                s => s.SetProperty(l => l.Status, LookupStatus.Pending)
                    .SetProperty(l => l.ErrorMessage, (string?)null)
                    .SetProperty(l => l.LastAttemptUtc, (DateTimeOffset?)null),
                cancellationToken);
        return ids.Count;
    }

    public static bool IsTransientFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("Gave up after", StringComparison.OrdinalIgnoreCase)
               || message.Contains("busy", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 503", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
               || message.Contains("HTTP 502", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Service Temporarily Unavailable", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLookupDue(TrackLookup lookup, DateTimeOffset now)
    {
        if (lookup.Status is not (LookupStatus.Pending or LookupStatus.Failed))
        {
            return false;
        }

        if (lookup.LastAttemptUtc is null)
        {
            return true;
        }

        var cooldown = lookup.ErrorMessage is { } message
                       && message.Contains("busy", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(30)
            : TimeSpan.FromSeconds(10);
        return now - lookup.LastAttemptUtc.Value >= cooldown;
    }

    private async Task<TrackLookup?> PickNextLookupAsync(CancellationToken cancellationToken)
    {
        var batch = await db.TrackLookups
            .Include(l => l.Track)
            .Where(l => l.Status == LookupStatus.Pending || l.Status == LookupStatus.Failed)
            .OrderBy(l => l.Id)
            .Take(50)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return batch.FirstOrDefault(l => IsLookupDue(l, now));
    }

    private async Task ApplyMatchAsync(long trackId, RecordingCandidate match, CancellationToken cancellationToken)
    {
        var track = await db.Tracks
            .Include(t => t.Artist)
            .Include(t => t.SourcePayloads)
            .FirstAsync(t => t.Id == trackId, cancellationToken);

        var duplicate = await db.Tracks.FirstOrDefaultAsync(
            t => t.Mbid == match.Mbid && t.Id != track.Id,
            cancellationToken);
        if (duplicate is not null)
        {
            var keepId = duplicate.Id;
            await catalog.MergeTracksAsync(duplicate, track, cancellationToken);
            track = await db.Tracks
                .Include(t => t.Artist)
                .Include(t => t.SourcePayloads)
                .FirstAsync(t => t.Id == keepId, cancellationToken);
            progress.Log($"Merged duplicate track into {track.Id} (shared MBID).");
        }

        track.Mbid = match.Mbid;
        if (track.DurationMs is null && match.LengthMs is > 0)
        {
            track.DurationMs = match.LengthMs;
        }

        if (!string.IsNullOrWhiteSpace(match.ArtistMbid) && track.Artist.Mbid is null)
        {
            track.Artist.Mbid = match.ArtistMbid;
        }

        if (!string.IsNullOrWhiteSpace(match.Album) && track.AlbumId is null)
        {
            var album = await catalog.GetOrCreateAlbumAsync(track.Artist, match.Album, match.ReleaseMbid, cancellationToken);
            track.AlbumId = album?.Id;
        }

        track.UpdatedAt = DateTimeOffset.UtcNow;
        await UpsertPayloadAsync(track, EnrichmentSource.MusicBrainz, match.Mbid, JsonSerializer.Serialize(match), cancellationToken);

        if (!string.IsNullOrWhiteSpace(match.ArtistMbid))
        {
            try
            {
                var mb = clients.CreateMusicBrainz();
                using var artistDoc = await mb.GetArtistAsync(match.ArtistMbid, cancellationToken);
                if (artistDoc.RootElement.TryGetProperty("aliases", out var aliases))
                {
                    foreach (var alias in aliases.EnumerateArray())
                    {
                        var name = alias.GetPropertyString("name");
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        await catalog.AddAliasIfMissingAsync(
                            track.Artist,
                            name,
                            "MusicBrainz",
                            alias.GetPropertyString("locale"),
                            cancellationToken);
                    }
                }

                var sortName = artistDoc.RootElement.GetPropertyString("sort-name");
                if (!string.IsNullOrWhiteSpace(sortName))
                {
                    track.Artist.SortName = sortName;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress.Log($"Artist alias fetch failed for {match.ArtistMbid}: {ex.Message}");
            }
        }
    }

    private async Task UpsertPayloadAsync(Track track, EnrichmentSource source, string? externalId, string json, CancellationToken cancellationToken)
    {
        var payload = track.SourcePayloads.FirstOrDefault(p => p.Source == source)
                      ?? await db.TrackSourcePayloads.FirstOrDefaultAsync(
                          p => p.TrackId == track.Id && p.Source == source,
                          cancellationToken);
        if (payload is null)
        {
            payload = new TrackSourcePayload { TrackId = track.Id, Source = source };
            db.TrackSourcePayloads.Add(payload);
        }

        payload.Status = SourceFetchStatus.Success;
        payload.ExternalId = externalId;
        payload.PayloadJson = json;
        payload.FetchedAt = DateTimeOffset.UtcNow;
        payload.ErrorMessage = null;
    }

    public static IReadOnlyList<RecordingCandidate> ParseCandidates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<RecordingCandidate>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
