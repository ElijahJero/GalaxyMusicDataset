using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Normalization;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class TrackEditInput
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public string? AlbumTitle { get; set; }
    public string? TrackMbid { get; set; }
    public string? ArtistMbid { get; set; }
    public string? AlbumMbid { get; set; }
    public double? DurationSeconds { get; set; }
    public string? Isrc { get; set; }
    public string? Summary { get; set; }
    public string? MusicVideoUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? DiscogsReleaseId { get; set; }
    public string? TheAudioDbTrackId { get; set; }
    public bool ResetEnrichment { get; set; }
    public bool LookupFromMbid { get; set; }
}

public sealed record TrackEditResult(long TrackId, string Message, bool Merged, bool LookupQueued = false);

public sealed class TrackEditService(AppDbContext db, CatalogService catalog)
{
    public async Task<TrackEditResult> SaveAsync(TrackEditInput input, CancellationToken cancellationToken)
    {
        var title = (input.Title ?? "").Trim();
        var artistName = (input.ArtistName ?? "").Trim();
        if (title.Length == 0 || artistName.Length == 0)
        {
            throw new InvalidOperationException("Artist and title are required.");
        }

        var track = await db.Tracks
            .Include(t => t.Artist)
            .Include(t => t.Album)
            .Include(t => t.SourcePayloads)
            .FirstOrDefaultAsync(t => t.Id == input.Id, cancellationToken)
            ?? throw new InvalidOperationException("Track not found.");

        var artist = await catalog.GetOrCreateArtistAsync(artistName, EmptyToNull(input.ArtistMbid), cancellationToken);
        track.ArtistId = artist.Id;
        track.Artist = artist;

        var albumTitle = EmptyToNull(input.AlbumTitle);
        if (albumTitle is null)
        {
            track.AlbumId = null;
            track.Album = null;
        }
        else
        {
            var album = await catalog.GetOrCreateAlbumAsync(artist, albumTitle, EmptyToNull(input.AlbumMbid), cancellationToken);
            if (album is not null)
            {
                CatalogService.SetCoverIfEmpty(album, EmptyToNull(input.CoverUrl));
                if (!string.IsNullOrWhiteSpace(input.CoverUrl))
                {
                    album.CoverUrl = input.CoverUrl.Trim();
                }
            }

            track.AlbumId = album?.Id;
            track.Album = album;
        }

        track.Title = title;
        var previousMbid = track.Mbid;
        track.Mbid = EmptyToNull(input.TrackMbid);
        track.Isrc = EmptyToNull(input.Isrc);
        track.Summary = EmptyToNull(input.Summary);
        track.MusicVideoUrl = EmptyToNull(input.MusicVideoUrl);
        track.DiscogsReleaseId = EmptyToNull(input.DiscogsReleaseId);
        track.TheAudioDbTrackId = EmptyToNull(input.TheAudioDbTrackId);
        track.DurationMs = input.DurationSeconds is > 0
            ? (int)Math.Round(input.DurationSeconds.Value * 1000)
            : null;
        track.UpdatedAt = DateTimeOffset.UtcNow;

        if (track.Album is not null && !string.IsNullOrWhiteSpace(input.CoverUrl))
        {
            track.Album.CoverUrl = input.CoverUrl.Trim();
        }

        var merged = false;
        if (!string.IsNullOrWhiteSpace(track.Mbid))
        {
            var mbidTwin = await db.Tracks.FirstOrDefaultAsync(
                t => t.Mbid == track.Mbid && t.Id != track.Id,
                cancellationToken);
            if (mbidTwin is not null)
            {
                await AbsorbAsync(track, mbidTwin, cancellationToken);
                merged = true;
            }
        }

        var fingerprint = TrackFingerprint.Compute(artistName, title);
        if (!string.Equals(track.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            var fpTwin = await db.Tracks.FirstOrDefaultAsync(
                t => t.Fingerprint == fingerprint && t.Id != track.Id,
                cancellationToken);
            if (fpTwin is not null)
            {
                await AbsorbAsync(track, fpTwin, cancellationToken);
                merged = true;
            }

            var lookup = await db.TrackLookups.FirstOrDefaultAsync(l => l.Fingerprint == track.Fingerprint, cancellationToken);
            var destLookup = await db.TrackLookups.FirstOrDefaultAsync(l => l.Fingerprint == fingerprint, cancellationToken);
            track.Fingerprint = fingerprint;
            if (destLookup is not null && lookup is not null && destLookup.Id != lookup.Id)
            {
                destLookup.ArtistName = artistName;
                destLookup.TrackName = title;
                destLookup.AlbumName = albumTitle;
                destLookup.TrackId = track.Id;
                db.TrackLookups.Remove(lookup);
            }
            else if (destLookup is not null)
            {
                destLookup.ArtistName = artistName;
                destLookup.TrackName = title;
                destLookup.AlbumName = albumTitle;
                destLookup.TrackId = track.Id;
            }
            else if (lookup is not null)
            {
                lookup.Fingerprint = fingerprint;
                lookup.ArtistName = artistName;
                lookup.TrackName = title;
                lookup.AlbumName = albumTitle;
                lookup.TrackId = track.Id;
            }
        }
        else
        {
            var lookup = await db.TrackLookups.FirstOrDefaultAsync(l => l.Fingerprint == track.Fingerprint, cancellationToken);
            if (lookup is not null)
            {
                lookup.ArtistName = artistName;
                lookup.TrackName = title;
                lookup.AlbumName = albumTitle;
            }
        }

        if (input.ResetEnrichment)
        {
            foreach (var payload in track.SourcePayloads)
            {
                payload.Status = SourceFetchStatus.NotStarted;
                payload.ErrorMessage = "Reset from library edit.";
                payload.FetchedAt = null;
                payload.PayloadJson = null;
                payload.ExternalId = null;
            }

            var lookup = await db.TrackLookups.FirstOrDefaultAsync(l => l.Fingerprint == track.Fingerprint, cancellationToken);
            if (lookup is not null && string.IsNullOrWhiteSpace(track.Mbid))
            {
                lookup.Status = LookupStatus.Pending;
                lookup.ErrorMessage = null;
                lookup.LastAttemptUtc = null;
                lookup.MatchedMbid = null;
            }
        }

        var mbidChanged = !string.Equals(previousMbid, track.Mbid, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(track.Mbid) && (input.LookupFromMbid || mbidChanged))
        {
            MetadataEnrichmentService.QueueRecordingDetails(
                track,
                input.LookupFromMbid
                    ? "Queued from manual MBID lookup."
                    : "Queued because the MBID changed.");
            var lookup = await db.TrackLookups.FirstOrDefaultAsync(l => l.Fingerprint == track.Fingerprint, cancellationToken);
            if (lookup is not null)
            {
                lookup.Status = LookupStatus.ManualMatched;
                lookup.MatchedMbid = track.Mbid;
                lookup.LastAttemptUtc = DateTimeOffset.UtcNow;
                lookup.ErrorMessage = null;
                lookup.TrackId = track.Id;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var lookupQueued = !string.IsNullOrWhiteSpace(track.Mbid) && (input.LookupFromMbid || mbidChanged);
        var message = merged
            ? $"Saved and merged a duplicate into track #{track.Id}."
            : "Saved.";
        if (input.LookupFromMbid)
        {
            message += " Fetching MusicBrainz recording details.";
        }
        else if (mbidChanged && !string.IsNullOrWhiteSpace(track.Mbid))
        {
            message += " MusicBrainz details queued for this MBID.";
        }

        return new TrackEditResult(track.Id, message, merged, lookupQueued);
    }

    private async Task AbsorbAsync(Track keep, Track drop, CancellationToken cancellationToken)
    {
        drop.Fingerprint = TrackFingerprint.Compute($"__merged_{drop.Id}", Guid.NewGuid().ToString("N"));
        drop.Mbid = null;
        var dropLookup = await db.TrackLookups.FirstOrDefaultAsync(l => l.TrackId == drop.Id, cancellationToken);
        if (dropLookup is not null)
        {
            dropLookup.Fingerprint = drop.Fingerprint;
        }

        await db.SaveChangesAsync(cancellationToken);
        await catalog.MergeTracksAsync(keep, drop, cancellationToken);
        if (dropLookup is not null)
        {
            var leftover = await db.TrackLookups.FirstOrDefaultAsync(l => l.Id == dropLookup.Id, cancellationToken);
            if (leftover is not null)
            {
                db.TrackLookups.Remove(leftover);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        await db.Entry(keep).ReloadAsync(cancellationToken);
        await db.Entry(keep).Collection(t => t.SourcePayloads).LoadAsync(cancellationToken);
        await db.Entry(keep).Reference(t => t.Artist).LoadAsync(cancellationToken);
        await db.Entry(keep).Reference(t => t.Album).LoadAsync(cancellationToken);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
