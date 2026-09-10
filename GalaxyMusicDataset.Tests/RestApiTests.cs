using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GalaxyMusicDataset.Tests;

[CollectionDefinition("RestApi", DisableParallelization = true)]
public class RestApiCollection : ICollectionFixture<GalaxyWebFactory>;

public sealed class GalaxyWebFactory : WebApplicationFactory<Program>
{
    public string ContentRoot { get; } = Path.Combine(Path.GetTempPath(), "galaxy-web-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(ContentRoot);
        var web = WebProjectDir();
        File.Copy(Path.Combine(web, "appsettings.json"), Path.Combine(ContentRoot, "appsettings.json"), true);
        var dev = Path.Combine(web, "appsettings.Development.json");
        if (File.Exists(dev))
        {
            File.Copy(dev, Path.Combine(ContentRoot, "appsettings.Development.json"), true);
        }

        builder.UseEnvironment("Development");
        builder.UseContentRoot(ContentRoot);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Password"] = "test-password",
                ["Aggregation:SeedSampleData"] = "true"
            });
        });
        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ImplementationType == typeof(AggregationHostedService)).ToList())
            {
                services.Remove(descriptor);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(ContentRoot, true);
        }
        catch
        {
            // ignore leftover temp files
        }
    }

    private static string WebProjectDir()
    {
        var dir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "GalaxyMusicDataset"));
    }
}

[Collection("RestApi")]
public class RestApiTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly GalaxyWebFactory _factory;

    public RestApiTests(GalaxyWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Catalog_and_docs_are_anonymous()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var catalog = await client.GetAsync("/api");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        var body = await catalog.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("v1", body.GetProperty("version").GetString());

        var docs = await client.GetAsync("/api/docs");
        Assert.Equal(HttpStatusCode.OK, docs.StatusCode);
        var markdown = await docs.Content.ReadAsStringAsync();
        Assert.Contains("Galaxy Music REST API", markdown);

        var page = await client.GetAsync("/api-docs");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Authentication", html);
        Assert.Contains("/api/v1/overview", html);
    }

    [Fact]
    public async Task Overview_requires_a_key()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.GetAsync("/api/v1/overview");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Read_key_can_load_overview_and_not_enqueue_jobs()
    {
        var created = _factory.Services.GetRequiredService<ApiKeyStore>().Create("tests-read", true, false);
        var client = Authenticated(created.Token);

        var overview = await client.GetAsync("/api/v1/overview?range=all");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        var json = await overview.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(json.GetProperty("overview").GetProperty("scrobbleCount").GetInt32() > 0);

        var me = await client.GetAsync("/api/v1/me");
        me.EnsureSuccessStatusCode();
        var identity = await me.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("tests-read", identity.GetProperty("name").GetString());

        var job = await client.PostAsync("/api/v1/jobs/sync", null);
        Assert.Equal(HttpStatusCode.Forbidden, job.StatusCode);
    }

    [Fact]
    public async Task Write_key_can_enqueue_sync()
    {
        var created = _factory.Services.GetRequiredService<ApiKeyStore>().Create("tests-write", true, true);
        var client = Authenticated(created.Token);
        var job = await client.PostAsync("/api/v1/jobs/sync", null);
        Assert.Equal(HttpStatusCode.Accepted, job.StatusCode);
    }

    [Fact]
    public async Task Audio_profile_put_requires_write_and_does_not_create_tags()
    {
        var store = _factory.Services.GetRequiredService<ApiKeyStore>();
        var read = Authenticated(store.Create("audio-read", true, false).Token);
        var write = Authenticated(store.Create("audio-write", true, true).Token);

        var pending = await write.GetAsync("/api/v1/tracks/pending-audio?take=5");
        pending.EnsureSuccessStatusCode();
        var pendingJson = await pending.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(pendingJson.GetProperty("items").GetArrayLength() > 0);
        var trackId = pendingJson.GetProperty("items")[0].GetProperty("id").GetInt64();

        var denied = await read.PutAsJsonAsync($"/api/v1/tracks/{trackId}/audio-profile", SampleAudioBody());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var put = await write.PutAsJsonAsync($"/api/v1/tracks/{trackId}/audio-profile", SampleAudioBody());
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var detail = await write.GetAsync($"/api/v1/tracks/{trackId}");
        detail.EnsureSuccessStatusCode();
        var json = await detail.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(85, json.GetProperty("audio").GetProperty("bpm").GetDouble());
        Assert.Equal("G", json.GetProperty("audio").GetProperty("key").GetString());
        Assert.DoesNotContain(json.GetProperty("tags").EnumerateArray(), t => t.GetProperty("name").GetString() == "Pop/J-pop");
        Assert.True(json.GetProperty("audio").GetProperty("genres").GetArrayLength() > 0);

        var library = await write.GetAsync("/api/v1/library?hasAudio=yes");
        library.EnsureSuccessStatusCode();
        var libraryJson = await library.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains(
            libraryJson.GetProperty("items").EnumerateArray(),
            t => t.GetProperty("id").GetInt64() == trackId && t.GetProperty("hasAudio").GetBoolean());

        var leftover = await write.GetAsync("/api/v1/tracks/pending-audio?take=50");
        leftover.EnsureSuccessStatusCode();
        var leftoverJson = await leftover.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.DoesNotContain(
            leftoverJson.GetProperty("items").EnumerateArray(),
            t => t.GetProperty("id").GetInt64() == trackId);
    }

    [Fact]
    public async Task Audio_label_detail_and_library_filter_use_genre_folders()
    {
        var store = _factory.Services.GetRequiredService<ApiKeyStore>();
        var client = Authenticated(store.Create("audio-labels", true, false).Token);

        var audio = await client.GetAsync("/api/v1/audio?range=all");
        audio.EnsureSuccessStatusCode();
        var audioJson = await audio.Content.ReadFromJsonAsync<JsonElement>(Json);
        var folders = audioJson.GetProperty("audio").GetProperty("genreFolders");
        Assert.True(folders.GetArrayLength() > 0);
        Assert.Contains(
            folders.EnumerateArray(),
            f => f.GetProperty("name").GetString() == "Electronic" && f.GetProperty("children").GetArrayLength() > 0);

        var folder = await client.GetAsync("/api/v1/audio/labels?kind=genre&name=Electronic&range=all");
        folder.EnsureSuccessStatusCode();
        var folderJson = await folder.Content.ReadFromJsonAsync<JsonElement>(Json);
        var detail = folderJson.GetProperty("detail");
        Assert.True(detail.GetProperty("isFolder").GetBoolean());
        Assert.True(detail.GetProperty("tracks").GetArrayLength() >= 2);
        Assert.True(detail.GetProperty("children").GetArrayLength() > 0);

        var leaf = await client.GetAsync("/api/v1/audio/labels?kind=genre&name=Electronic%2FSynth-pop&range=all");
        leaf.EnsureSuccessStatusCode();
        var leafJson = await leaf.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(leafJson.GetProperty("detail").GetProperty("isFolder").GetBoolean());
        Assert.Equal("Electronic", leafJson.GetProperty("detail").GetProperty("parent").GetString());

        var library = await client.GetAsync("/api/v1/library?audioKind=genre&audioLabel=Electronic&sort=title");
        library.EnsureSuccessStatusCode();
        var libraryJson = await library.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(libraryJson.GetProperty("total").GetInt32() >= 2);
        Assert.Contains(
            libraryJson.GetProperty("items").EnumerateArray(),
            t => t.GetProperty("title").GetString() == "Way 2 U");
    }

    private static object SampleAudioBody() => new
    {
        bpm = 85,
        key = "G major",
        danceability = 0.93,
        voice = 0.95,
        acoustic = 0.02,
        electronic = 0.27,
        timbre = "dark",
        approachability = 0.72,
        engagement = 0.84,
        moods = new
        {
            party = 0.95,
            happy = 0.78,
            aggressive = 0.70,
            relaxed = 0.22,
            sad = 0.05
        },
        genres = new[] { "Pop/J-pop", "Rock/Pop Rock" },
        themes = new object[] { new[] { "energetic", (object)0.88 } },
        instruments = new[] { new { name = "drums", score = 0.91 } }
    };

    [Fact]
    public async Task X_Api_Key_header_works()
    {
        var created = _factory.Services.GetRequiredService<ApiKeyStore>().Create("header-key", true, false);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", created.Token);
        var res = await client.GetAsync("/api/v1/years");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private HttpClient Authenticated(string token)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
