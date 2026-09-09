namespace GalaxyMusicDataset.Services.Api;

public static class ApiKeyScopes
{
    public const string Read = "read";
    public const string Write = "write";
    public const string ClaimType = "scope";
}

public sealed class StoredApiKey
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Hash { get; set; } = "";
    public List<string> Scopes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class ApiKeyFile
{
    public List<StoredApiKey> Keys { get; set; } = [];
}

public sealed record ApiKeyInfo(
    string Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    bool Revoked);

public sealed record CreatedApiKey(ApiKeyInfo Info, string Token);
