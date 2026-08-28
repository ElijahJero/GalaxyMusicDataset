using GalaxyMusicDataset.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace GalaxyMusicDataset.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<ArtistAlias> ArtistAliases => Set<ArtistAlias>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Scrobble> Scrobbles => Set<Scrobble>();
    public DbSet<TrackLookup> TrackLookups => Set<TrackLookup>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TrackTag> TrackTags => Set<TrackTag>();
    public DbSet<TrackSourcePayload> TrackSourcePayloads => Set<TrackSourcePayload>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();
    public DbSet<AggregationJob> AggregationJobs => Set<AggregationJob>();
    public DbSet<ApiRequestLog> ApiRequestLogs => Set<ApiRequestLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Artist>(e =>
        {
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.Mbid).IsUnique();
            e.Property(x => x.Name).HasMaxLength(512);
            e.Property(x => x.SortName).HasMaxLength(512);
            e.Property(x => x.Mbid).HasMaxLength(36);
        });

        modelBuilder.Entity<ArtistAlias>(e =>
        {
            e.HasIndex(x => new { x.ArtistId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(512);
            e.HasOne(x => x.Artist).WithMany(x => x.Aliases).HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Album>(e =>
        {
            e.HasIndex(x => x.Mbid).IsUnique();
            e.HasIndex(x => new { x.ArtistId, x.Title });
            e.Property(x => x.Title).HasMaxLength(1024);
            e.Property(x => x.Mbid).HasMaxLength(36);
            e.HasOne(x => x.Artist).WithMany(x => x.Albums).HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Track>(e =>
        {
            e.HasIndex(x => x.Fingerprint).IsUnique();
            e.HasIndex(x => x.Mbid).IsUnique();
            e.HasIndex(x => x.Title);
            e.Property(x => x.Title).HasMaxLength(1024);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.Mbid).HasMaxLength(36);
            e.HasOne(x => x.Artist).WithMany(x => x.Tracks).HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Album).WithMany(x => x.Tracks).HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Scrobble>(e =>
        {
            e.HasIndex(x => x.UnixTimestamp).IsUnique();
            e.HasIndex(x => x.PlayedAt);
            e.HasIndex(x => x.TrackId);
            e.Property(x => x.OriginalArtist).HasMaxLength(512);
            e.Property(x => x.OriginalTitle).HasMaxLength(1024);
            e.Property(x => x.OriginalAlbum).HasMaxLength(1024);
            e.HasOne(x => x.Track).WithMany(x => x.Scrobbles).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TrackLookup>(e =>
        {
            e.HasIndex(x => x.Fingerprint).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.HasIndex(x => x.NormalizedName).IsUnique();
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.NormalizedName).HasMaxLength(256);
        });

        modelBuilder.Entity<TrackTag>(e =>
        {
            e.HasIndex(x => new { x.TrackId, x.TagId, x.Source }).IsUnique();
            e.HasOne(x => x.Track).WithMany(x => x.Tags).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tag).WithMany(x => x.TrackTags).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackSourcePayload>(e =>
        {
            e.HasIndex(x => new { x.TrackId, x.Source }).IsUnique();
            e.HasOne(x => x.Track).WithMany(x => x.SourcePayloads).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncState>(e =>
        {
            e.HasData(new SyncState { Id = 1 });
        });

        modelBuilder.Entity<AggregationJob>(e =>
        {
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => x.Kind);
        });

        modelBuilder.Entity<ApiRequestLog>(e =>
        {
            e.HasIndex(x => x.At);
            e.HasIndex(x => x.Source);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.Error).HasMaxLength(2000);
        });
    }
}
