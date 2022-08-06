using Microsoft.AspNetCore.HttpOverrides;
using Prometheus;
using XLWebServices;
using XLWebServices.Services;
using XLWebServices.Services.PluginData;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<DiscordHookService>();
builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<FileCacheService>();
builder.Services.AddSingleton<PluginDataService>();
builder.Services.AddSingleton<LauncherReleaseDataService>();
builder.Services.AddSingleton<AssetCacheService>();
builder.Services.AddSingleton<DalamudReleaseDataService>();

builder.Services.AddResponseCaching();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

//app.MapControllers();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseRouting();

app.UseResponseCaching();

app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapMetrics();
});

// Initialize services
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting XLWebServices {Version}", Util.GetGitHash());

app.Services.GetRequiredService<RedisService>();
app.Services.GetRequiredService<GitHubService>();

var acs = app.Services.GetRequiredService<AssetCacheService>();
await acs.ClearCache();

var drs = app.Services.GetRequiredService<DalamudReleaseDataService>();
await drs.ClearCache();

var pds = app.Services.GetRequiredService<PluginDataService>();
await pds.ClearCache();

var rds = app.Services.GetRequiredService<LauncherReleaseDataService>();
await rds.ClearCache();

app.Run();