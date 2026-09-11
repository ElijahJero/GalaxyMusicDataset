using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Analytics;
using GalaxyMusicDataset.Services.Audio;
using GalaxyMusicDataset.Services.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Pages;

[Authorize]
public class RecentModel(
    AppDbContext db,
    TrackEditService editor,
    MetadataEnrichmentService enrichment,
    LibrarySearchService search) : PageModel
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
    public string? HasTags { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? HasAudio { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? AudioKind { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? AudioLabel { get; set; }

    [BindProperty(SupportsGet = true)]
    public double? BpmMin { get; set; }

    [BindProperty(SupportsGet = true)]
    public double? BpmMax { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? BpmBucket { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? AudioKey { get; set; }

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
        if (LibraryFilters.HasTextFilter(Q, Artist, Title, Album))
        {
            await search.EnsureCurrentAsync(db, cancellationToken);
        }

        query = LibraryFilters.Apply(
            query, db, search, Q, Artist, Title, Album, HasMbid, HasTags, Source, Status,
            HasAudio, AudioKind, AudioLabel, BpmMin, BpmMax, BpmBucket, AudioKey);
        query = LibraryListQuery.ApplySort(query, Sort);

        var page = await LibraryListQuery.LoadPageAsync(db, query, P, PageSize, cancellationToken);
        P = page.Page;
        TotalCount = page.TotalCount;
        TotalPages = page.TotalPages;
        Rows = page.Items.Select(item => new LibraryRow(
            item.Track,
            item.Lookup,
            item.PlayCount,
            item.LastPlayedUnix,
            item.Audio,
            item.Sources)).ToList();
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

    public Dictionary<string, string?> FilterRoutes(long? edit = null, int? page = null)
    {
        var d = new Dictionary<string, string?>
        {
            ["p"] = (page ?? P).ToString(),
            ["q"] = Q,
            ["artist"] = Artist,
            ["title"] = Title,
            ["album"] = Album,
            ["status"] = Status,
            ["hasMbid"] = HasMbid,
            ["hasTags"] = HasTags,
            ["hasAudio"] = HasAudio,
            ["audioKind"] = AudioKind,
            ["audioLabel"] = AudioLabel,
            ["bpmMin"] = BpmMin?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bpmMax"] = BpmMax?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bpmBucket"] = BpmBucket,
            ["audioKey"] = AudioKey,
            ["source"] = Source,
            ["sort"] = Sort
        };
        if (edit is not null)
        {
            d["edit"] = edit.Value.ToString();
        }

        return d;
    }

    public string BpmFilterLabel
    {
        get
        {
            if (AudioBpm.TryResolve(BpmBucket, out var bucket))
            {
                return bucket.Name;
            }

            if (BpmMin is not null && BpmMax is not null)
            {
                return $"{BpmMin:0}–{BpmMax:0}";
            }

            if (BpmMin is not null)
            {
                return $"{BpmMin:0}+";
            }

            if (BpmMax is not null)
            {
                return $"≤{BpmMax:0}";
            }

            return "any";
        }
    }

    public object FilterRoute(long? edit = null) => FilterRoutes(edit);
}

public sealed record LibraryRow(
    Track Track,
    TrackLookup? Lookup,
    int PlayCount,
    long? LastPlayedUnix,
    AudioProfileView? Audio,
    IReadOnlyList<SourcePayloadInfo> Sources);

public static class LibraryFilters
{
    public static bool HasTextFilter(string? q, string? artist, string? title, string? album) =>
        !string.IsNullOrWhiteSpace(q)
        || !string.IsNullOrWhiteSpace(artist)
        || !string.IsNullOrWhiteSpace(title)
        || !string.IsNullOrWhiteSpace(album);

    public static IQueryable<Track> Apply(
        IQueryable<Track> query,
        AppDbContext db,
        LibrarySearchService? search,
        string? q,
        string? artist,
        string? title,
        string? album,
        string? hasMbid,
        string? hasTags,
        string? source,
        string? status,
        string? hasAudio = null,
        string? audioKind = null,
        string? audioLabel = null,
        double? bpmMin = null,
        double? bpmMax = null,
        string? bpmBucket = null,
        string? audioKey = null)
    {
        if (HasTextFilter(q, artist, title, album))
        {
            var ids = search?.Search(q, artist, title, album) ?? [];
            query = query.Where(t => ids.Contains(t.Id));
        }

        if (string.Equals(hasMbid, "yes", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.Mbid != null && t.Mbid != "");
        }
        else if (string.Equals(hasMbid, "no", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.Mbid == null || t.Mbid == "");
        }

        if (string.Equals(hasTags, "yes", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.Tags.Any());
        }
        else if (string.Equals(hasTags, "no", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => !t.Tags.Any());
        }

        if (string.Equals(hasAudio, "yes", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.AudioProfile != null);
        }
        else if (string.Equals(hasAudio, "no", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.AudioProfile == null);
        }

        if (!string.IsNullOrWhiteSpace(audioLabel) &&
            Enum.TryParse<AudioLabelKind>(audioKind, true, out var parsedKind))
        {
            query = ApplyAudioLabel(query, parsedKind, audioLabel);
        }

        query = ApplyBpm(query, bpmMin, bpmMax, bpmBucket);
        query = ApplyAudioKey(query, audioKey);

        if (!string.IsNullOrWhiteSpace(source) && Enum.TryParse<EnrichmentSource>(source, out var parsedSource))
        {
            query = query.Where(t => t.SourcePayloads.Any(p =>
                p.Source == parsedSource && p.Status == SourceFetchStatus.Success));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LookupStatus>(status, out var lookupStatus))
        {
            query = query.Where(t => db.TrackLookups.Any(l => l.Fingerprint == t.Fingerprint && l.Status == lookupStatus));
        }

        return query;
    }

    public static IQueryable<Track> ApplyAudioLabel(
        IQueryable<Track> query,
        AudioLabelKind kind,
        string label)
    {
        var needle = label.Trim();
        var folder = AudioGenrePath.IsFolderQuery(kind, needle);
        var lower = needle.ToLower();
        var prefix = lower + AudioGenrePath.DisplaySeparator;
        if (folder)
        {
            return query.Where(t => t.AudioProfile != null && t.AudioProfile.Labels.Any(l =>
                l.Kind == kind &&
                (l.Name.ToLower() == lower || l.Name.ToLower().StartsWith(prefix))));
        }

        return query.Where(t => t.AudioProfile != null && t.AudioProfile.Labels.Any(l =>
            l.Kind == kind && l.Name.ToLower() == lower));
    }

    public static IQueryable<Track> ApplyBpm(
        IQueryable<Track> query,
        double? bpmMin,
        double? bpmMax,
        string? bpmBucket)
    {
        if (AudioBpm.TryResolve(bpmBucket, out var bucket))
        {
            var min = bucket.MinInclusive;
            var maxExclusive = bucket.MaxExclusive;
            if (min is not null && maxExclusive is not null)
            {
                query = query.Where(t => t.AudioProfile != null && t.AudioProfile.Bpm != null &&
                    t.AudioProfile.Bpm >= min && t.AudioProfile.Bpm < maxExclusive);
            }
            else if (min is not null)
            {
                query = query.Where(t => t.AudioProfile != null && t.AudioProfile.Bpm != null &&
                    t.AudioProfile.Bpm >= min);
            }
            else if (maxExclusive is not null)
            {
                query = query.Where(t => t.AudioProfile != null && t.AudioProfile.Bpm != null &&
                    t.AudioProfile.Bpm < maxExclusive);
            }
        }

        if (bpmMin is double minInclusive)
        {
            query = query.Where(t => t.AudioProfile != null && t.AudioProfile.Bpm != null &&
                t.AudioProfile.Bpm >= minInclusive);
        }

        if (bpmMax is double maxInclusive)
        {
            query = query.Where(t => t.AudioProfile != null && t.AudioProfile.Bpm != null &&
                t.AudioProfile.Bpm <= maxInclusive);
        }

        return query;
    }

    public static IQueryable<Track> ApplyAudioKey(IQueryable<Track> query, string? audioKey)
    {
        if (string.IsNullOrWhiteSpace(audioKey))
        {
            return query;
        }

        var (key, scale) = AudioProfileMapper.SplitKey(audioKey, null);
        if (string.IsNullOrWhiteSpace(key))
        {
            return query;
        }

        var keyLower = key.ToLower();
        if (string.IsNullOrWhiteSpace(scale))
        {
            return query.Where(t => t.AudioProfile != null &&
                t.AudioProfile.Key != null &&
                t.AudioProfile.Key.ToLower() == keyLower);
        }

        var scaleLower = scale.ToLower();
        return query.Where(t => t.AudioProfile != null &&
            t.AudioProfile.Key != null &&
            t.AudioProfile.Key.ToLower() == keyLower &&
            t.AudioProfile.Scale != null &&
            t.AudioProfile.Scale.ToLower() == scaleLower);
    }
}
