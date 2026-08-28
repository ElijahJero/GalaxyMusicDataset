using GalaxyMusicDataset.Services.Aggregation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GalaxyMusicDataset.Pages;

public class IndexModel(
    AggregationStatusService statusService,
    AggregationCoordinator coordinator,
    IServiceScopeFactory scopes,
    SampleDataSeeder seeder) : PageModel
{
    public AggregationStatusDto Status { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Status = await statusService.GetStatusAsync(cancellationToken);
    }

    public IActionResult OnPostSync()
    {
        coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.SyncIncremental));
        return RedirectToPage();
    }

    public IActionResult OnPostBackfill()
    {
        coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.Backfill, 14));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPauseAsync(CancellationToken cancellationToken)
    {
        await coordinator.SetPausedAsync(scopes, true, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResumeAsync(CancellationToken cancellationToken)
    {
        await coordinator.SetPausedAsync(scopes, false, cancellationToken);
        return RedirectToPage();
    }

    public IActionResult OnPostRetryLookups()
    {
        coordinator.TryEnqueue(new AggregationCommand(AggregationCommandKind.RetryFailedLookups));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSeedAsync(CancellationToken cancellationToken)
    {
        await seeder.SeedIfEmptyAsync(SampleDataSeeder.DefaultSample(), cancellationToken);
        return RedirectToPage();
    }
}
