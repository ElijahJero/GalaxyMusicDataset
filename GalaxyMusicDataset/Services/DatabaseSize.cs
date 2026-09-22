using System.Globalization;

namespace GalaxyMusicDataset.Services;

public static class DatabaseSize
{
    public static long OnDiskBytes(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
        {
            return 0;
        }

        long total = 0;
        foreach (var path in new[] { dataSource, dataSource + "-wal", dataSource + "-shm" })
        {
            if (File.Exists(path))
            {
                total += new FileInfo(path).Length;
            }
        }

        return total;
    }

    public static string Format(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;

        if (bytes >= gb)
        {
            return (bytes / gb).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
        }

        if (bytes >= mb)
        {
            return (bytes / mb).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }

        if (bytes >= kb)
        {
            return (bytes / kb).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        }

        return bytes.ToString("0", CultureInfo.InvariantCulture) + " B";
    }
}
