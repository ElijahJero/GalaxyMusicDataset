using GalaxyMusicDataset.Services.Api;
using Microsoft.AspNetCore.Http;

namespace GalaxyMusicDataset.Tests;

public class ApiKeyStoreTests
{
    [Fact]
    public void Create_hashes_token_and_verifies()
    {
        using var dir = new TempDir();
        var store = new ApiKeyStore(Path.Combine(dir.Path, "api-keys.json"));
        var created = store.Create("Phone", read: true, write: true);

        Assert.StartsWith(ApiKeyStore.TokenPrefix, created.Token);
        Assert.Equal("Phone", created.Info.Name);
        Assert.Contains(ApiKeyScopes.Read, created.Info.Scopes);
        Assert.Contains(ApiKeyScopes.Write, created.Info.Scopes);
        Assert.True(store.TryAuthenticate(created.Token, out var info));
        Assert.Equal(created.Info.Id, info.Id);
        Assert.False(store.TryAuthenticate("gmk_not-a-real-key-value-at-all", out _));
        Assert.False(store.TryAuthenticate(null, out _));
    }

    [Fact]
    public void Write_scope_implies_read()
    {
        using var dir = new TempDir();
        var store = new ApiKeyStore(Path.Combine(dir.Path, "api-keys.json"));
        var created = store.Create("Jobs", read: false, write: true);
        Assert.Contains(ApiKeyScopes.Read, created.Info.Scopes);
        Assert.Contains(ApiKeyScopes.Write, created.Info.Scopes);
    }

    [Fact]
    public void Revoke_rejects_the_token()
    {
        using var dir = new TempDir();
        var store = new ApiKeyStore(Path.Combine(dir.Path, "api-keys.json"));
        var created = store.Create("Old", true, false);
        Assert.True(store.Revoke(created.Info.Id));
        Assert.False(store.TryAuthenticate(created.Token, out _));
        Assert.Empty(store.List());
        Assert.False(store.Revoke(created.Info.Id));
    }

    [Fact]
    public void Reload_from_disk_still_authenticates()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "api-keys.json");
        var created = new ApiKeyStore(path).Create("Tablet", true, true);
        var reloaded = new ApiKeyStore(path);
        Assert.True(reloaded.TryAuthenticate(created.Token, out var info));
        Assert.Equal("Tablet", info.Name);
        Assert.Single(reloaded.List());
    }

    [Fact]
    public void Create_requires_name_and_scope()
    {
        using var dir = new TempDir();
        var store = new ApiKeyStore(Path.Combine(dir.Path, "api-keys.json"));
        Assert.Throws<InvalidOperationException>(() => store.Create("  ", true, true));
        Assert.Throws<InvalidOperationException>(() => store.Create("x", false, false));
    }

    [Fact]
    public void ExtractToken_reads_headers()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer gmk_abc";
        Assert.Equal("gmk_abc", ApiKeyStore.ExtractToken(http.Request));

        http = new DefaultHttpContext();
        http.Request.Headers["X-Api-Key"] = "gmk_hdr";
        Assert.Equal("gmk_hdr", ApiKeyStore.ExtractToken(http.Request));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gmk-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // ignore leftover temp files
            }
        }
    }
}
