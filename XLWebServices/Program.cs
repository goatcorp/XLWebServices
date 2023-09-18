using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using XLWebServices;
using XLWebServices.Data;
using XLWebServices.Services;
using XLWebServices.Services.JobQueue;
using XLWebServices.Services.PluginData;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<ConfigMasterService>();
builder.Services.AddSingleton<FallibleService<RedisService>>();
builder.Services.AddDbContext<WsDbContext>();

builder.Services.AddHostedService<QueuedHostedService>();
builder.Services.AddSingleton<IBackgroundTaskQueue>(ctx => 
{
    if (!int.TryParse(ctx.GetRequiredService<IConfiguration>()["QueueCapacity"], out var queueCapacity))
    {
        queueCapacity = 100;
    }

    return new DefaultBackgroundTaskQueue(queueCapacity);
});

builder.Services.AddSingleton<DiscordHookService>();
builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<FileCacheService>();
builder.Services.AddSingleton<FallibleService<PluginDataService>>();
builder.Services.AddSingleton<FallibleService<LauncherReleaseDataService>>();
builder.Services.AddSingleton<FallibleService<AssetCacheService>>();
builder.Services.AddSingleton<FallibleService<DalamudReleaseDataService>>();

builder.Services.AddResponseCaching();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("GithubAccess",
        policy =>
        {
            policy.WithOrigins("https://goatcorp.github.io", "https://tommadness.github.io")
                .WithMethods("GET")
                .AllowAnyHeader();
        });
});

builder.Services.AddRateLimiter(o => {
    o.AddPolicy("limitip", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(httpContext.Connection.RemoteIpAddress ?? IPAddress.None,
            _ => new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(1), PermitLimit = 10 }));
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.WebHost.UseSentry(sentryBuilder =>
{
    sentryBuilder.SendDefaultPii = false;
    sentryBuilder.Release = Util.GetGitHash();
});

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
app.UseHttpMetrics();
app.UseCors();

app.UseSentryTracing();

app.UseResponseCaching();

app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapMetrics();
});

// UseRateLimiter must be used after the routes are registered
app.UseRateLimiter();

// Initialize services
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting XLWebServices {Version}", Util.GetGitHash());

var discord = app.Services.GetRequiredService<DiscordHookService>();

var redis = app.Services.GetRequiredService<FallibleService<RedisService>>();
if (redis.HasFailed)
    await discord.AdminSendError("Couldn't connect to redis", "Redis");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WsDbContext>();
    await db.Database.MigrateAsync();
}

app.Services.GetRequiredService<GitHubService>();

var acs = app.Services.GetRequiredService<FallibleService<AssetCacheService>>();
await acs.RunFallibleAsync(s => s.ClearCache());

var drs = app.Services.GetRequiredService<FallibleService<DalamudReleaseDataService>>();
await drs.RunFallibleAsync(s => s.ClearCache());

var pds = app.Services.GetRequiredService<FallibleService<PluginDataService>>();
await pds.RunFallibleAsync(s => s.ClearCache());

var rds = app.Services.GetRequiredService<FallibleService<LauncherReleaseDataService>>();
await rds.RunFallibleAsync(s => s.ClearCache());

app.Run();