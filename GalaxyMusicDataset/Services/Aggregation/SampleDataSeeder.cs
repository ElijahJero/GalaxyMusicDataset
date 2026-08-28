using System.Text.Json;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Data.Entities;
using GalaxyMusicDataset.Services.LastFm;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Services.Aggregation;

public sealed class SampleDataSeeder(AppDbContext db, ScrobbleIngestService ingest, AggregationProgress progress)
{
    public async Task<int> SeedIfEmptyAsync(IReadOnlyList<LastFmRecentTrack> tracks, CancellationToken cancellationToken)
    {
        if (await db.Scrobbles.AnyAsync(cancellationToken))
        {
            return 0;
        }

        var job = new AggregationJob
        {
            Kind = JobKind.SeedSample,
            Status = JobStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        db.AggregationJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        var result = await ingest.IngestAsync(tracks, cancellationToken);
        job.ItemsProcessed = tracks.Count;
        job.ItemsSucceeded = result.Inserted;
        job.ItemsSkipped = result.Duplicates + result.Skipped;
        job.Status = JobStatus.Succeeded;
        job.FinishedAt = DateTimeOffset.UtcNow;
        job.Message = $"Seeded {result.Inserted} sample scrobbles.";
        await db.SaveChangesAsync(cancellationToken);
        progress.Log(job.Message);
        return result.Inserted;
    }

    public static IReadOnlyList<LastFmRecentTrack> DefaultSample()
    {
        var json = """
            [
              {"artist_name": "fourfolium", "track_name": "Yumeirokonpasu", "album_name": "JUMPin' JUMP UP!!!!", "timestamp_unix": 1787928809, "mbid": null},
              {"artist_name": "fourfolium", "track_name": "Now Loading!!!!", "album_name": "TVアニメ「NEW GAME!」エンディングテーマ", "timestamp_unix": 1787928554, "mbid": "3f309fb6-fed0-461e-bfd9-c6d7467a4bd4"},
              {"artist_name": "fourfolium", "track_name": "ススメRunner!!(instrumental)", "album_name": "TVアニメ「NEW GAME!!」オープニングテーマ", "timestamp_unix": 1787928521, "mbid": null},
              {"artist_name": "Mori Calliope", "track_name": "INSOMNIAC BLACK", "album_name": "DISASTERPIECE", "timestamp_unix": 1787928245, "mbid": null},
              {"artist_name": "Ouro Kronii", "track_name": "Way 2 U", "album_name": "Way 2 U", "timestamp_unix": 1787928125, "mbid": "f143144a-cbeb-40f7-8afd-4ab24f87a136"},
              {"artist_name": "nihmune", "track_name": "Shopping Malls", "album_name": "Neutral Front", "timestamp_unix": 1787927971, "mbid": "adaa9988-b612-43f0-8499-a451ce9b1da1"},
              {"artist_name": "Samuel Kim", "track_name": "I Really Want to Stay at Your House", "album_name": "I Really Want to Stay at Your House", "timestamp_unix": 1787927724, "mbid": null},
              {"artist_name": "Mori Calliope", "track_name": "Left For Dead Lullaby", "album_name": "JIGOKU 6", "timestamp_unix": 1787927423, "mbid": "6e2a6d20-63de-4c55-8544-f6980d0e6938"},
              {"artist_name": "Suu Usuwa", "track_name": "思い出とペトリコール - Omoide to Petrichor", "album_name": "Dive iN", "timestamp_unix": 1787927259, "mbid": null},
              {"artist_name": "nihmune", "track_name": "Brain Rot", "album_name": "Hard to Think", "timestamp_unix": 1787927088, "mbid": null},
              {"artist_name": "明石繆", "track_name": "Akashi Myu", "album_name": null, "timestamp_unix": 1787926962, "mbid": null},
              {"artist_name": "Mori Calliope", "track_name": "Lose-Lose Days", "album_name": "UnAlive", "timestamp_unix": 1787926717, "mbid": "81730195-b0f4-45e6-9974-5f198b356194"},
              {"artist_name": "Mori Calliope", "track_name": "Lose-Lose Days", "album_name": "UnAlive", "timestamp_unix": 1787926461, "mbid": "81730195-b0f4-45e6-9974-5f198b356194"},
              {"artist_name": "Kyasu", "track_name": "Lilac", "album_name": "Lilac", "timestamp_unix": 1787926280, "mbid": null}
            ]
            """;
        using var doc = JsonDocument.Parse(json);
        var list = new List<LastFmRecentTrack>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add(new LastFmRecentTrack(
                item.GetProperty("artist_name").GetString() ?? "",
                item.GetProperty("track_name").GetString() ?? "",
                item.GetProperty("album_name").ValueKind == JsonValueKind.Null ? null : item.GetProperty("album_name").GetString(),
                item.GetProperty("timestamp_unix").GetInt64(),
                item.GetProperty("mbid").ValueKind == JsonValueKind.Null ? null : item.GetProperty("mbid").GetString(),
                null,
                null,
                false,
                item.GetRawText()));
        }

        return list;
    }
}
