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
        if (string.IsNullOrWhiteSpace(alias) ||
            string.Equals(alias, artist.Name, StringComparison.OrdinalIgnoreCase))
        {
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
}
