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
            artist = await db.Artists.FirstOrDefaultAsync(a => a.Mbid == mbid, cancellationToken);
        }

        artist ??= db.Artists.Local.FirstOrDefault(a => a.Name == name)
                   ?? await db.Artists.FirstOrDefaultAsync(a => a.Name == name, cancellationToken);
        if (artist is not null)
        {
            if (artist.Mbid is null && !string.IsNullOrWhiteSpace(mbid))
            {
                artist.Mbid = mbid;
                artist.UpdatedAt = DateTimeOffset.UtcNow;
            }

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
        await db.SaveChangesAsync(cancellationToken);
        return artist;
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
            album = await db.Albums.FirstOrDefaultAsync(a => a.Mbid == mbid, cancellationToken);
        }

        if (album is null && !string.IsNullOrWhiteSpace(title))
        {
            album = await db.Albums.FirstOrDefaultAsync(
                a => a.ArtistId == artist.Id && a.Title == title,
                cancellationToken);
        }

        if (album is not null)
        {
            if (album.Mbid is null && !string.IsNullOrWhiteSpace(mbid))
            {
                album.Mbid = mbid;
                album.UpdatedAt = DateTimeOffset.UtcNow;
            }

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
        await db.SaveChangesAsync(cancellationToken);
        return album;
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
        foreach (var entry in db.ChangeTracker.Entries<ArtistAlias>()
                     .Where(e => e.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task SaveChangesIgnoringDuplicateAliasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsArtistAliasUniqueViolation(ex))
        {
            DiscardUnsavedAliases();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsArtistAliasUniqueViolation(DbUpdateException ex)
    {
        for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
        {
            if (inner.Message.Contains("UNIQUE constraint failed: ArtistAliases", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

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
