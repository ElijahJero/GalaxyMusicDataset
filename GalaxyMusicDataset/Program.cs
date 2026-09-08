using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services;
using GalaxyMusicDataset.Services.Aggregation;
using GalaxyMusicDataset.Services.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var appData = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appData);
builder.Configuration.AddJsonFile(
    Path.Combine(appData, "user-settings.json"),
    optional: true,
    reloadOnChange: true);

var dataProtectionKeys = Path.Combine(appData, "keys");
Directory.CreateDirectory(dataProtectionKeys);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeys))
    .SetApplicationName("GalaxyMusicDataset");

builder.Services.AddRazorPages();
builder.Services.AddGalaxyAggregation(builder.Configuration, builder.Environment);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.Cookie.Name = "galaxy.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");

    var aggregation = scope.ServiceProvider.GetRequiredService<IOptions<AggregationOptions>>().Value;
    if (aggregation.SeedSampleData)
    {
        var seeder = scope.ServiceProvider.GetRequiredService<SampleDataSeeder>();
        await seeder.SeedIfEmptyAsync(SampleDataSeeder.DefaultSample(), CancellationToken.None);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    // docker-compose exposes HTTP only. Skip redirect unless an HTTPS port is set
    // (otherwise HttpsRedirectionMiddleware logs "Failed to determine the https port").
    if (HttpsPortConfigured())
    {
        app.UseHttpsRedirection();
    }
}
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (AnonymousHomeRedirect.ShouldRedirectToDashboard(
            context.Request.Method,
            context.Request.Path.Value,
            context.User.Identity?.IsAuthenticated == true))
    {
        context.Response.Redirect("/Dashboard");
        return;
    }

    await next();
});
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.Run();

static bool HttpsPortConfigured()
{
    return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORTS"))
           || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT"))
           || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HTTPS_PORT"));
}
