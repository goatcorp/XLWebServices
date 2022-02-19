using Microsoft.AspNetCore.HttpOverrides;
using Prometheus;
using XLWebServices.Services;
using XLWebServices.Services.PluginData;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<GitHubService>();
builder.Services.AddSingleton<FileCacheService>();
builder.Services.AddSingleton<PluginDataService>();
builder.Services.AddSingleton<ReleaseDataService>();
builder.Services.AddSingleton<DiscordHookService>();

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

app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapMetrics();
});

// Initialize services
app.Services.GetRequiredService<RedisService>();
app.Services.GetRequiredService<GitHubService>();

var pds = app.Services.GetRequiredService<PluginDataService>();
await pds.ClearCache();

var rds = app.Services.GetRequiredService<ReleaseDataService>();
await rds.ClearCache();

app.Run();