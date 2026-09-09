using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GalaxyMusicDataset.Services.Api;

public sealed class ApiKeyStore
{
    public const string TokenPrefix = "gmk_";
    public const int PrefixDisplayLength = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly object _gate = new();
    private List<StoredApiKey> _keys = [];
    private DateTimeOffset _lastUsedFlushUtc = DateTimeOffset.MinValue;

    public ApiKeyStore(IWebHostEnvironment env)
        : this(Path.Combine(env.ContentRootPath, "App_Data", "api-keys.json"))
    {
    }

    public ApiKeyStore(string path)
    {
        _path = path;
        Load();
    }

    public IReadOnlyList<ApiKeyInfo> List(bool includeRevoked = false)
    {
        lock (_gate)
        {
            return _keys
                .Where(k => includeRevoked || k.RevokedAt is null)
                .OrderByDescending(k => k.CreatedAt)
                .Select(ToInfo)
                .ToList();
        }
    }

    public CreatedApiKey Create(string? name, bool read, bool write)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A name is required.");
        }

        if (trimmed.Length > 80)
        {
            throw new InvalidOperationException("Name must be 80 characters or fewer.");
        }

        if (!read && !write)
        {
            throw new InvalidOperationException("Select at least one scope (read or write).");
        }

        if (write)
        {
            read = true;
        }

        var token = TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(24));
        var now = DateTimeOffset.UtcNow;
        var stored = new StoredApiKey
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmed,
            Prefix = token.Length <= PrefixDisplayLength ? token : token[..PrefixDisplayLength],
            Hash = Hash(token),
            Scopes = BuildScopes(read, write),
            CreatedAt = now
        };

        lock (_gate)
        {
            _keys.Add(stored);
            PersistUnlocked();
        }

        return new CreatedApiKey(ToInfo(stored), token);
    }

    public bool Revoke(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        lock (_gate)
        {
            var key = _keys.FirstOrDefault(k =>
                string.Equals(k.Id, id, StringComparison.Ordinal) && k.RevokedAt is null);
            if (key is null)
            {
                return false;
            }

            key.RevokedAt = DateTimeOffset.UtcNow;
            PersistUnlocked();
            return true;
        }
    }

    public bool TryAuthenticate(string? token, out ApiKeyInfo info)
    {
        info = null!;
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var incoming = Convert.FromHexString(Hash(token.Trim()));
        lock (_gate)
        {
            foreach (var key in _keys)
            {
                if (key.RevokedAt is not null)
                {
                    continue;
                }

                var stored = Convert.FromHexString(key.Hash);
                if (stored.Length != incoming.Length
                    || !CryptographicOperations.FixedTimeEquals(stored, incoming))
                {
                    continue;
                }

                key.LastUsedAt = DateTimeOffset.UtcNow;
                info = ToInfo(key);
                if (DateTimeOffset.UtcNow - _lastUsedFlushUtc > TimeSpan.FromMinutes(1))
                {
                    PersistUnlocked();
                    _lastUsedFlushUtc = DateTimeOffset.UtcNow;
                }

                return true;
            }
        }

        return false;
    }

    public static string? ExtractToken(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Api-Key", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString().Trim();
        }

        var authorization = request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return authorization[bearer.Length..].Trim();
        }

        if (request.Query.TryGetValue("api_key", out var query) && !string.IsNullOrWhiteSpace(query))
        {
            return query.ToString().Trim();
        }

        return null;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<ApiKeyFile>(json, JsonOptions);
            _keys = file?.Keys ?? [];
        }
        catch (JsonException)
        {
            _keys = [];
        }
    }

    private void PersistUnlocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(new ApiKeyFile { Keys = _keys }, JsonOptions);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    private static ApiKeyInfo ToInfo(StoredApiKey key) =>
        new(
            key.Id,
            key.Name,
            key.Prefix,
            key.Scopes.ToArray(),
            key.CreatedAt,
            key.LastUsedAt,
            key.RevokedAt is not null);

    private static List<string> BuildScopes(bool read, bool write)
    {
        var scopes = new List<string>();
        if (read)
        {
            scopes.Add(ApiKeyScopes.Read);
        }

        if (write)
        {
            scopes.Add(ApiKeyScopes.Write);
        }

        return scopes;
    }

    internal static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
