using GalaxyMusicDataset.Data;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Search;

public sealed class LibrarySearchService : IDisposable
{
    private readonly LibrarySearchEngine _engine = new();
    private readonly object _gate = new();
    private int _indexedCount = -1;
    private DateTimeOffset _indexedStamp = DateTimeOffset.MinValue;

    public async Task EnsureCurrentAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var count = await db.Tracks.CountAsync(cancellationToken);
        var stamp = new[]
        {
            (await db.Tracks.AsNoTracking().Select(t => t.UpdatedAt).ToListAsync(cancellationToken)).DefaultIfEmpty().Max(),
            (await db.Artists.AsNoTracking().Select(a => a.UpdatedAt).ToListAsync(cancellationToken)).DefaultIfEmpty().Max(),
            (await db.Albums.AsNoTracking().Select(a => a.UpdatedAt).ToListAsync(cancellationToken)).DefaultIfEmpty().Max()
        }.Max();

        lock (_gate)
        {
            if (_indexedCount == count && _indexedStamp >= stamp)
            {
                return;
            }
        }

        var tracks = await db.Tracks.AsNoTracking()
            .Include(t => t.Artist).ThenInclude(a => a.Aliases)
            .Include(t => t.Album)
            .ToListAsync(cancellationToken);
        var documents = tracks.Select(t => new LibrarySearchDocument(
            t.Id,
            t.Title,
            t.Artist.Name,
            t.Album?.Title,
            t.Artist.Aliases.Select(a => a.Name).ToList())).ToList();

        _engine.Rebuild(documents);
        lock (_gate)
        {
            _indexedCount = count;
            _indexedStamp = stamp;
        }
    }

    public IReadOnlyList<long> Search(string? q, string? artist, string? title, string? album) =>
        _engine.Search(q, artist, title, album);

    public void Dispose() => _engine.Dispose();
}
