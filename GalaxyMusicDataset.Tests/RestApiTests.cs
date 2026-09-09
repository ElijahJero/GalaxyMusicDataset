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
