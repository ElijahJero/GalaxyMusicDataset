using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Pages;

public class RecentModel(AppDbContext db, TrackEditService editor, MetadataEnrichmentService enrichment) : PageModel
{
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Artist { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Title { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Album { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? HasMbid { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Sort { get; set; } = "recent";

    [BindProperty(SupportsGet = true)]
    public long? Edit { get; set; }

    [BindProperty]
    public TrackEditInput Input { get; set; } = new();

    public IReadOnlyList<LibraryRow> Rows { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }
    public string? Flash { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Flash = TempData["LibraryFlash"] as string;
        if (P < 1)
        {
            P = 1;
        }

        var query = db.Tracks.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(t =>
                t.Title.Contains(term) ||
                t.Artist.Name.Contains(term) ||
                (t.Album != null && t.Album.Title.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(Artist))
        {
            var term = Artist.Trim();
            query = query.Where(t => t.Artist.Name.Contains(term) || t.Artist.Aliases.Any(a => a.Name.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(Title))
        {
            var term = Title.Trim();
            query = query.Where(t => t.Title.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(Album))
        {
            var term = Album.Trim();
            query = query.Where(t => t.Album != null && t.Album.Title.Contains(term));
        }

        if (string.Equals(HasMbid, "yes", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.Mbid != null && t.Mbid != "");
        }
        else if (string.Equals(HasMbid, "no", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.Mbid == null || t.Mbid == "");
        }

        if (!string.IsNullOrWhiteSpace(Source) && Enum.TryParse<EnrichmentSource>(Source, out var source))
        {
            query = query.Where(t => t.SourcePayloads.Any(p =>
                p.Source == source && p.Status == SourceFetchStatus.Success));
        }

        if (!string.IsNullOrWhiteSpace(Status) && Enum.TryParse<LookupStatus>(Status, out var lookupStatus))
        {
            query = query.Where(t => db.TrackLookups.Any(l => l.Fingerprint == t.Fingerprint && l.Status == lookupStatus));
        }

        query = Sort switch
        {
            "title" => query.OrderBy(t => t.Title).ThenBy(t => t.Artist.Name),
            "artist" => query.OrderBy(t => t.Artist.Name).ThenBy(t => t.Title),
            "plays" => query.OrderByDescending(t => t.Scrobbles.Count()).ThenBy(t => t.Title),
            _ => query.OrderByDescending(t => t.Scrobbles.Max(s => (long?)s.UnixTimestamp)).ThenBy(t => t.Title)
        };

        TotalCount = await query.CountAsync(cancellationToken);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        if (P > TotalPages)
        {
            P = TotalPages;
        }

        var tracks = await query
            .Skip((P - 1) * PageSize)
            .Take(PageSize)
            .Include(t => t.Artist).ThenInclude(a => a.Aliases)
            .Include(t => t.Album)
            .Include(t => t.Tags).ThenInclude(t => t.Tag)
            .Include(t => t.SourcePayloads)
            .ToListAsync(cancellationToken);

        var ids = tracks.Select(t => t.Id).ToList();
        var stats = await db.Scrobbles.AsNoTracking()
            .Where(s => ids.Contains(s.TrackId))
            .GroupBy(s => s.TrackId)
            .Select(g => new { TrackId = g.Key, Count = g.Count(), Last = g.Max(x => x.UnixTimestamp) })
            .ToListAsync(cancellationToken);
        var statMap = stats.ToDictionary(x => x.TrackId);

        var fingerprints = tracks.Select(t => t.Fingerprint).Distinct().ToList();
        var lookups = await db.TrackLookups.AsNoTracking()
            .Where(l => fingerprints.Contains(l.Fingerprint))
            .ToListAsync(cancellationToken);
        var lookupMap = lookups.ToDictionary(l => l.Fingerprint);

        Rows = tracks.Select(t =>
        {
            statMap.TryGetValue(t.Id, out var st);
            return new LibraryRow(
                t,
                lookupMap.GetValueOrDefault(t.Fingerprint),
                st?.Count ?? 0,
                st?.Last);
        }).ToList();
    }

    public async Task<IActionResult> OnPostEditAsync(CancellationToken cancellationToken)
    {
        var result = await editor.SaveAsync(Input, cancellationToken);
        var flash = result.Message;
        if (Input.LookupFromMbid && !string.IsNullOrWhiteSpace(Input.TrackMbid))
        {
            flash += " " + await enrichment.EnrichTrackFromMbidAsync(result.TrackId, cancellationToken);
        }

        TempData["LibraryFlash"] = flash;
        return RedirectToPage(FilterRoute(result.TrackId));
    }

    public object FilterRoute(long? edit = null) => new
    {
        P,
        Q,
        Artist,
        Title,
        Album,
        Status,
        HasMbid,
        Source,
        Sort,
        Edit = edit
    };
}

public sealed record LibraryRow(Track Track, TrackLookup? Lookup, int PlayCount, long? LastPlayedUnix);
