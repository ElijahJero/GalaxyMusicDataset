using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class CatalogService(AppDbContext db)
{
    public async Task<Artist> GetOrCreateArtistAsync(string name, string? mbid, CancellationToken cancellationToken)
    {
        Artist? artist = null;
        if (!string.IsNullOrWhiteSpace(mbid))
        {
            artist = db.Artists.Local.FirstOrDefault(a => a.Mbid == mbid)
                     ?? await db.Artists.FirstOrDefaultAsync(a => a.Mbid == mbid, cancellationToken);
        }

        artist ??= db.Artists.Local.FirstOrDefault(a => a.Name == name)
                   ?? await db.Artists.FirstOrDefaultAsync(a => a.Name == name, cancellationToken);
        if (artist is not null)
        {
            await TryCoalesceArtistMbidAsync(artist, mbid, cancellationToken);
            return artist;
        }

        artist = new Artist
        {
            Name = name,
            SortName = name,
            Mbid = string.IsNullOrWhiteSpace(mbid) ? null : mbid,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Artists.Add(artist);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return artist;
        }
        catch (DbUpdateException ex) when (!string.IsNullOrWhiteSpace(mbid) && IsSqliteUniqueConstraint(ex, "Artists.Mbid"))
        {
            db.Entry(artist).State = EntityState.Detached;
            return db.Artists.Local.FirstOrDefault(a => a.Mbid == mbid)
                   ?? await db.Artists.FirstAsync(a => a.Mbid == mbid, cancellationToken);
        }
    }

    public async Task<Album?> GetOrCreateAlbumAsync(Artist artist, string? title, string? mbid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(mbid))
        {
            return null;
        }

        Album? album = null;
        if (!string.IsNullOrWhiteSpace(mbid))
        {
            album = db.Albums.Local.FirstOrDefault(a => a.Mbid == mbid)
                    ?? await db.Albums.FirstOrDefaultAsync(a => a.Mbid == mbid, cancellationToken);
        }

        if (album is null && !string.IsNullOrWhiteSpace(title))
        {
            album = db.Albums.Local.FirstOrDefault(a => a.ArtistId == artist.Id && a.Title == title)
                    ?? await db.Albums.FirstOrDefaultAsync(
                        a => a.ArtistId == artist.Id && a.Title == title,
                        cancellationToken);
        }

        if (album is not null)
        {
            await TryCoalesceAlbumMbidAsync(album, mbid, cancellationToken);
            return album;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        album = new Album
        {
            ArtistId = artist.Id,
            Title = title,
            Mbid = string.IsNullOrWhiteSpace(mbid) ? null : mbid,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Albums.Add(album);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return album;
        }
        catch (DbUpdateException ex) when (!string.IsNullOrWhiteSpace(mbid) && IsSqliteUniqueConstraint(ex, "Albums.Mbid"))
        {
            db.Entry(album).State = EntityState.Detached;
            return db.Albums.Local.FirstOrDefault(a => a.Mbid == mbid)
                   ?? await db.Albums.FirstAsync(a => a.Mbid == mbid, cancellationToken);
        }
    }

    public async Task AddAliasIfMissingAsync(Artist artist, string alias, string source, string? locale, CancellationToken cancellationToken)
    {
        alias = alias.Trim();
        locale = string.IsNullOrWhiteSpace(locale) ? null : locale.Trim();
        if (alias.Length == 0 ||
            string.Equals(alias, artist.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // MusicBrainz repeats the same alias name with different locales
        // (e.g. "Ryūichi Sakamoto" for de/en/fr/nl). The unique index is
        // (ArtistId, Name), so check the change tracker as well as the
        // database — AnyAsync cannot see aliases queued in this SaveChanges.
        var pending = db.ArtistAliases.Local.FirstOrDefault(a =>
            a.ArtistId == artist.Id &&
            string.Equals(a.Name, alias, StringComparison.OrdinalIgnoreCase));
        if (pending is not null)
        {
            PreferLocale(pending, locale);
            return;
        }

        var exists = await db.ArtistAliases.AnyAsync(
            a => a.ArtistId == artist.Id && a.Name == alias,
            cancellationToken);
        if (exists)
        {
            return;
        }

        db.ArtistAliases.Add(new ArtistAlias
        {
            ArtistId = artist.Id,
            Name = alias,
            Source = source,
            Locale = locale
        });
    }

    public void DiscardUnsavedAliases()
    {
        DetachAdded<ArtistAlias>();
    }

    public void DiscardUnsavedTrackTags()
    {
        DetachAdded<TrackTag>();
    }

    public void DiscardUnsavedTags()
    {
        DetachAdded<Tag>();
    }

    public void DiscardUnsavedPayloads()
    {
        DetachAdded<TrackSourcePayload>();
    }

    public void DiscardUnsavedLookups()
    {
        DetachAdded<TrackLookup>();
    }

    public void DiscardConflictingCatalogInserts()
    {
        DiscardUnsavedAliases();
        DiscardUnsavedTrackTags();
        DiscardUnsavedTags();
        DiscardUnsavedPayloads();
        DiscardUnsavedLookups();
        RevertUnsavedMbidAssignments();
    }

    private void DetachAdded<T>()
        where T : class
    {
        foreach (var entry in db.ChangeTracker.Entries<T>()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    public Task SaveChangesIgnoringDuplicateAliasesAsync(CancellationToken cancellationToken) =>
        SaveChangesIgnoringDuplicateCatalogKeysAsync(cancellationToken);

    public async Task SaveChangesIgnoringDuplicateCatalogKeysAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt < 3 && IsDuplicateCatalogKey(ex))
            {
                if (IsUniqueViolation(ex, "ArtistAliases"))
                {
                    DiscardUnsavedAliases();
                }

                if (IsUniqueViolation(ex, "TrackTags"))
                {
                    DiscardUnsavedTrackTags();
                }

                if (IsUniqueViolation(ex, "Tags.NormalizedName"))
                {
                    DiscardUnsavedTags();
                }

                if (IsUniqueViolation(ex, "TrackSourcePayloads"))
                {
                    DiscardUnsavedPayloads();
                }

                if (IsUniqueViolation(ex, "TrackLookups.Fingerprint"))
                {
                    DiscardUnsavedLookups();
                }

                if (IsMbidUniqueViolation(ex))
                {
                    RevertUnsavedMbidAssignments();
                }
            }
        }
    }

    public async Task TryCoalesceArtistMbidAsync(Artist artist, string? mbid, CancellationToken cancellationToken)
    {
        var incoming = Coalesce(artist.Mbid, mbid);
        if (incoming is null || string.Equals(artist.Mbid, incoming, StringComparison.Ordinal))
        {
            return;
        }

        if (await IsArtistMbidTakenAsync(artist.Id, incoming, cancellationToken))
        {
            return;
        }

        artist.Mbid = incoming;
        artist.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task TryCoalesceAlbumMbidAsync(Album album, string? mbid, CancellationToken cancellationToken)
    {
        var incoming = Coalesce(album.Mbid, mbid);
        if (incoming is null || string.Equals(album.Mbid, incoming, StringComparison.Ordinal))
        {
            return;
        }

        if (await IsAlbumMbidTakenAsync(album.Id, incoming, cancellationToken))
        {
            return;
        }

        album.Mbid = incoming;
        album.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task TryCoalesceTrackMbidAsync(Track track, string? mbid, CancellationToken cancellationToken)
    {
        var incoming = Coalesce(track.Mbid, mbid);
        if (incoming is null || string.Equals(track.Mbid, incoming, StringComparison.Ordinal))
        {
            return;
        }

        if (await IsTrackMbidTakenAsync(track.Id, incoming, cancellationToken))
        {
            return;
        }

        track.Mbid = incoming;
    }

    public void RevertUnsavedMbidAssignments()
    {
        foreach (var entry in db.ChangeTracker.Entries<Artist>().ToList())
        {
            RevertMbidProperty(entry);
        }

        foreach (var entry in db.ChangeTracker.Entries<Album>().ToList())
        {
            RevertMbidProperty(entry);
        }

        foreach (var entry in db.ChangeTracker.Entries<Track>().ToList())
        {
            RevertMbidProperty(entry);
        }
    }

    private async Task<bool> IsArtistMbidTakenAsync(long exceptId, string mbid, CancellationToken cancellationToken)
    {
        if (db.Artists.Local.Any(a => a.Id != exceptId && a.Mbid == mbid))
        {
            return true;
        }

        return await db.Artists.AnyAsync(a => a.Id != exceptId && a.Mbid == mbid, cancellationToken);
    }

    private async Task<bool> IsAlbumMbidTakenAsync(long exceptId, string mbid, CancellationToken cancellationToken)
    {
        if (db.Albums.Local.Any(a => a.Id != exceptId && a.Mbid == mbid))
        {
            return true;
        }

        return await db.Albums.AnyAsync(a => a.Id != exceptId && a.Mbid == mbid, cancellationToken);
    }

    private async Task<bool> IsTrackMbidTakenAsync(long exceptId, string mbid, CancellationToken cancellationToken)
    {
        if (db.Tracks.Local.Any(t => t.Id != exceptId && t.Mbid == mbid))
        {
            return true;
        }

        return await db.Tracks.AnyAsync(t => t.Id != exceptId && t.Mbid == mbid, cancellationToken);
    }

    private static void RevertMbidProperty<T>(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> entry)
        where T : class
    {
        if (entry.State is not (EntityState.Modified or EntityState.Added))
        {
            return;
        }

        var property = entry.Property("Mbid");
        if (!property.IsModified)
        {
            return;
        }

        property.CurrentValue = entry.State == EntityState.Added ? null : property.OriginalValue;
    }

    private static bool IsDuplicateCatalogKey(DbUpdateException ex) =>
        IsUniqueViolation(ex, "ArtistAliases")
        || IsUniqueViolation(ex, "TrackTags")
        || IsUniqueViolation(ex, "Tags.NormalizedName")
        || IsUniqueViolation(ex, "TrackSourcePayloads")
        || IsUniqueViolation(ex, "TrackLookups.Fingerprint")
        || IsMbidUniqueViolation(ex);

    internal static bool IsSqliteUniqueConstraint(Exception ex, string constraint)
    {
        for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
        {
            if (inner.Message.Contains($"UNIQUE constraint failed: {constraint}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMbidUniqueViolation(DbUpdateException ex) =>
        IsUniqueViolation(ex, "Artists.Mbid")
        || IsUniqueViolation(ex, "Albums.Mbid")
        || IsUniqueViolation(ex, "Tracks.Mbid");

    private static bool IsUniqueViolation(DbUpdateException ex, string constraint) =>
        IsSqliteUniqueConstraint(ex, constraint);

    private static void PreferLocale(ArtistAlias existing, string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(existing.Locale) || LocaleRank(locale) > LocaleRank(existing.Locale))
        {
            existing.Locale = locale;
        }
    }

    private static int LocaleRank(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return 0;
        }

        return locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    public static string? Coalesce(string? current, string? incoming) =>
        string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(incoming) ? incoming.Trim() : current;

    public static void SetCoverIfEmpty(Album? album, string? url) =>
        CoverArtResolver.TrySetCover(album, url);

    public async Task MergeTracksAsync(Track keep, Track drop, CancellationToken cancellationToken)
    {
        if (keep.Id == drop.Id)
        {
            return;
        }

        foreach (var scrobble in db.ChangeTracker.Entries<Scrobble>()
                     .Select(e => e.Entity)
                     .Where(s => s.TrackId == drop.Id)
                     .ToList())
        {
            scrobble.TrackId = keep.Id;
        }

        foreach (var lookup in db.ChangeTracker.Entries<TrackLookup>()
                     .Select(e => e.Entity)
                     .Where(l => l.TrackId == drop.Id)
                     .ToList())
        {
            lookup.TrackId = keep.Id;
        }

        await db.Scrobbles.Where(s => s.TrackId == drop.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TrackId, keep.Id), cancellationToken);

        var keepTagKeys = await db.TrackTags
            .Where(t => t.TrackId == keep.Id)
            .Select(t => new { t.TagId, t.Source })
            .ToListAsync(cancellationToken);
        var keepSet = keepTagKeys.Select(k => (k.TagId, k.Source)).ToHashSet();
        var dropTags = await db.TrackTags.Where(t => t.TrackId == drop.Id).ToListAsync(cancellationToken);
        foreach (var tag in dropTags)
        {
            if (keepSet.Contains((tag.TagId, tag.Source)))
            {
                db.TrackTags.Remove(tag);
            }
            else
            {
                tag.TrackId = keep.Id;
            }
        }

        var keepSources = await db.TrackSourcePayloads
            .Where(p => p.TrackId == keep.Id)
            .Select(p => p.Source)
            .ToListAsync(cancellationToken);
        var keepSourceSet = keepSources.ToHashSet();
        var dropPayloads = await db.TrackSourcePayloads.Where(p => p.TrackId == drop.Id).ToListAsync(cancellationToken);
        foreach (var payload in dropPayloads)
        {
            if (keepSourceSet.Contains(payload.Source))
            {
                db.TrackSourcePayloads.Remove(payload);
            }
            else
            {
                payload.TrackId = keep.Id;
            }
        }

        await db.TrackLookups.Where(l => l.TrackId == drop.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TrackId, keep.Id), cancellationToken);

        keep.DurationMs ??= drop.DurationMs;
        keep.AlbumId ??= drop.AlbumId;
        keep.Mbid ??= drop.Mbid;
        keep.Isrc ??= drop.Isrc;
        keep.Summary ??= drop.Summary;
        keep.MusicVideoUrl ??= drop.MusicVideoUrl;
        keep.DiscogsReleaseId ??= drop.DiscogsReleaseId;
        keep.TheAudioDbTrackId ??= drop.TheAudioDbTrackId;
        keep.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (var entry in db.ChangeTracker.Entries<Scrobble>()
                     .Where(e => e.Entity.TrackId == drop.Id)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }

        db.Tracks.Remove(drop);
        await db.SaveChangesAsync(cancellationToken);
    }
}
