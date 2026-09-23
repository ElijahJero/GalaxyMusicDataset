using GalaxyMusicDataset.Services;

namespace GalaxyMusicDataset.Tests;

public class DatabaseSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(200L * 1024 * 1024, "200 MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    [InlineData(1536L * 1024 * 1024, "1.5 GB")]
    public void Format_uses_the_largest_fitting_unit(long bytes, string expected)
    {
        Assert.Equal(expected, DatabaseSize.Format(bytes));
    }

    [Fact]
    public void OnDiskBytes_sums_the_database_and_wal_files()
    {
        var dir = Directory.CreateTempSubdirectory("galaxy-db-size");
        try
        {
            var db = Path.Combine(dir.FullName, "galaxy.db");
            File.WriteAllBytes(db, new byte[100]);
            File.WriteAllBytes(db + "-wal", new byte[40]);
            File.WriteAllBytes(db + "-shm", new byte[10]);

            Assert.Equal(150, DatabaseSize.OnDiskBytes(db));
            Assert.Equal(0, DatabaseSize.OnDiskBytes(":memory:"));
            Assert.Equal(0, DatabaseSize.OnDiskBytes(null));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
