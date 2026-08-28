using GalaxyMusicDataset.Configuration;
using GalaxyMusicDataset.Data;
using GalaxyMusicDataset.Services;
using GalaxyMusicDataset.Services.Aggregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Configuration.AddJsonFile(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data", "user-settings.json"),
    optional: true,
    reloadOnChange: true);

builder.Services.AddRazorPages();
builder.Services.AddGalaxyAggregation(builder.Configuration, builder.Environment);

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
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.Run();
