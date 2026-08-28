using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.MusicBrainz;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Pages;

public class ReviewModel(AppDbContext db, MusicBrainzLookupService lookups) : PageModel
{
    public IReadOnlyList<ReviewRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var pending = await db.TrackLookups
            .AsNoTracking()
            .Where(l => l.Status == LookupStatus.NeedsReview)
            .OrderByDescending(l => l.BestScore)
            .Take(50)
            .ToListAsync(cancellationToken);

        Rows = pending.Select(l => new ReviewRow(l, MusicBrainzLookupService.ParseCandidates(l.CandidateJson))).ToList();
    }

    public async Task<IActionResult> OnPostAcceptAsync(long id, string mbid, CancellationToken cancellationToken)
    {
        await lookups.AcceptCandidateAsync(id, mbid, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostNotFoundAsync(long id, CancellationToken cancellationToken)
    {
        await lookups.MarkNotFoundAsync(id, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAsync(long id, CancellationToken cancellationToken)
    {
        await lookups.RetryAsync(id, cancellationToken);
        return RedirectToPage();
    }
}

public sealed record ReviewRow(TrackLookup Lookup, IReadOnlyList<RecordingCandidate> Candidates);
