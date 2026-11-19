using GalaxyMusicDataset.Data;

namespace GalaxyMusicDataset.Services.Scrobbling;

/// <summary>
/// Reads listening history from one scrobble service for sync/backfill/reconcile.
/// </summary>
public interface IScrobbleHistoryClient
{
    ScrobbleProvider Provider { get; }
    string DisplayName { get; }
    bool SupportsDeletionReconcile { get; }

    Task<ScrobbleUserInfo> GetUserInfoAsync(CancellationToken cancellationToken);

    Task<PlayWindowResult> GetPlaysWindowAsync(
        long? fromUnix,
        long? toUnix,
        int pageSize,
        CancellationToken cancellationToken);
}
