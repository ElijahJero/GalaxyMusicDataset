using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.LastFm;
using GalaxyMusicDataset.Services.Normalization;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class ScrobbleIngestService(AppDbContext db, CatalogService catalog)
{
    public async Task<IngestBatchResult> IngestAsync(
        IEnumerable<LastFmRecentTrack> tracks,
        CancellationToken cancellationToken)
    {
        var result = new IngestBatchResult();
        foreach (var incoming in tracks)
        {
            if (incoming.IsNowPlaying || incoming.TimestampUnix is null or <= 0)
            {
                result.Skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(incoming.ArtistName) || string.IsNullOrWhiteSpace(incoming.TrackName))
            {
                result.Skipped++;
                continue;
            }

            var exists = db.Scrobbles.Local.Any(s => s.UnixTimestamp == incoming.TimestampUnix)
                         || await db.Scrobbles.AnyAsync(s => s.UnixTimestamp == incoming.TimestampUnix, cancellationToken);
            if (exists)
            {
                result.Duplicates++;
                continue;
            }

            var fingerprint = TrackFingerprint.Compute(incoming.ArtistName, incoming.TrackName);
            var artist = await catalog.GetOrCreateArtistAsync(incoming.ArtistName, incoming.ArtistMbid, cancellationToken);
            var album = await catalog.GetOrCreateAlbumAsync(artist, incoming.AlbumName, incoming.AlbumMbid, cancellationToken);

            var track = await FindOrCreateTrackAsync(
                artist,
                album,
                incoming.TrackName,
                incoming.TrackMbid,
                fingerprint,
                cancellationToken);

            db.Scrobbles.Add(new Scrobble
            {
                TrackId = track.Id,
                PlayedAt = DateTimeOffset.FromUnixTimeSeconds(incoming.TimestampUnix.Value),
                UnixTimestamp = incoming.TimestampUnix.Value,
                OriginalArtist = incoming.ArtistName,
                OriginalTitle = incoming.TrackName,
                OriginalAlbum = incoming.AlbumName,
                LastFmTrackMbid = incoming.TrackMbid,
                LastFmArtistMbid = incoming.ArtistMbid,
                LastFmAlbumMbid = incoming.AlbumMbid
            });

            await EnsureLookupAsync(track, incoming, fingerprint, cancellationToken);
            result.Inserted++;
        }

        if (result.Inserted > 0)
        {
            await catalog.SaveChangesIgnoringDuplicateCatalogKeysAsync(cancellationToken);
        }

        return result;
    }

    private async Task<Track> FindOrCreateTrackAsync(
        Artist artist,
        Album? album,
        string title,
        string? mbid,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        Track? track = null;
        if (!string.IsNullOrWhiteSpace(mbid))
        {
            track = db.Tracks.Local.FirstOrDefault(t => t.Mbid == mbid)
                    ?? await db.Tracks.FirstOrDefaultAsync(t => t.Mbid == mbid, cancellationToken);
        }

        track ??= db.Tracks.Local.FirstOrDefault(t => t.Fingerprint == fingerprint)
                  ?? await db.Tracks.FirstOrDefaultAsync(t => t.Fingerprint == fingerprint, cancellationToken);
        if (track is not null)
        {
            var dirty = false;
            if (track.Mbid is null && !string.IsNullOrWhiteSpace(mbid))
            {
                var mbidOwner = db.Tracks.Local.FirstOrDefault(t => t.Id != track.Id && t.Mbid == mbid)
                                ?? await db.Tracks.FirstOrDefaultAsync(
                                    t => t.Id != track.Id && t.Mbid == mbid,
                                    cancellationToken);
                if (mbidOwner is not null)
                {
                    await catalog.MergeTracksAsync(mbidOwner, track, cancellationToken);
                    return mbidOwner;
                }

                await catalog.TryCoalesceTrackMbidAsync(track, mbid, cancellationToken);
                dirty = !string.IsNullOrWhiteSpace(track.Mbid);
            }

            if (track.AlbumId is null && album is not null)
            {
                track.AlbumId = album.Id;
                dirty = true;
            }

            if (dirty)
            {
                track.UpdatedAt = DateTimeOffset.UtcNow;
                await catalog.SaveChangesIgnoringDuplicateCatalogKeysAsync(cancellationToken);
            }

            return track;
        }

        track = new Track
        {
            ArtistId = artist.Id,
            AlbumId = album?.Id,
            Title = title,
            Mbid = string.IsNullOrWhiteSpace(mbid) ? null : mbid,
            Fingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Tracks.Add(track);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return track;
        }
        catch (DbUpdateException ex) when (!string.IsNullOrWhiteSpace(mbid) && CatalogService.IsSqliteUniqueConstraint(ex, "Tracks.Mbid"))
        {
            db.Entry(track).State = EntityState.Detached;
            return db.Tracks.Local.FirstOrDefault(t => t.Mbid == mbid)
                   ?? await db.Tracks.FirstAsync(t => t.Mbid == mbid, cancellationToken);
        }
        catch (DbUpdateException ex) when (CatalogService.IsSqliteUniqueConstraint(ex, "Tracks.Fingerprint"))
        {
            db.Entry(track).State = EntityState.Detached;
            return db.Tracks.Local.FirstOrDefault(t => t.Fingerprint == fingerprint)
                   ?? await db.Tracks.FirstAsync(t => t.Fingerprint == fingerprint, cancellationToken);
        }
    }

    private async Task EnsureLookupAsync(
        Track track,
        LastFmRecentTrack incoming,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var lookup = db.TrackLookups.Local.FirstOrDefault(l => l.Fingerprint == fingerprint)
                     ?? await db.TrackLookups.FirstOrDefaultAsync(l => l.Fingerprint == fingerprint, cancellationToken);
        if (lookup is null)
        {
            lookup = new TrackLookup
            {
                Fingerprint = fingerprint,
                TrackId = track.Id,
                ArtistName = incoming.ArtistName,
                TrackName = incoming.TrackName,
                AlbumName = incoming.AlbumName,
                Status = string.IsNullOrWhiteSpace(track.Mbid) ? LookupStatus.Pending : LookupStatus.AutoMatched,
                CreatedAt = DateTimeOffset.UtcNow,
                MatchedMbid = track.Mbid,
                BestScore = string.IsNullOrWhiteSpace(track.Mbid) ? null : 1,
                QueryUsed = string.IsNullOrWhiteSpace(track.Mbid) ? null : "last.fm scrobble mbid"
            };
            db.TrackLookups.Add(lookup);
            return;
        }

        lookup.TrackId ??= track.Id;
        if (lookup.Status == LookupStatus.Pending && !string.IsNullOrWhiteSpace(track.Mbid))
        {
            lookup.Status = LookupStatus.AutoMatched;
            lookup.MatchedMbid = track.Mbid;
            lookup.BestScore = 1;
            lookup.QueryUsed = "last.fm scrobble mbid";
        }
    }
}

public sealed class IngestBatchResult
{
    public int Inserted { get; set; }
    public int Duplicates { get; set; }
    public int Skipped { get; set; }
}
