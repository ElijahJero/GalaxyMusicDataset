using System.Security.Cryptography;
using System.Text;

namespace GalaxyMusicDataset.Services.Normalization;

public static class TrackFingerprint
{
    public static string Compute(string? artist, string? title)
    {
        var key = $"{TextNormalizer.Normalize(artist)}\n{TextNormalizer.Normalize(title)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
