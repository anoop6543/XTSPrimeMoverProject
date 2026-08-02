using PrimeMoverApi.Hubs;
using PrimeMoverApi.Services;
using Microsoft.OpenApi.Models;
using Temporalio.Client;
using StackExchange.Redis;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Prime Mover API", Version = "v1" }));

builder.Services.AddCors(opts => opts.AddPolicy("AllowAll", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:Connection"] ?? "redis:6379"));

builder.Services.AddSingleton<ITemporalClient>(_ =>
    TemporalClient.ConnectAsync(new TemporalClientConnectOptions(
        builder.Configuration["Temporal:Address"] ?? "temporal:7233")
    { Namespace = "xts-system" }).GetAwaiter().GetResult());

builder.Services.AddHttpClient("PrimeMoverService", c =>
    c.BaseAddress = new Uri(builder.Configuration["PrimeMoverService:BaseUrl"] ?? "http://prime-mover-service:8080"));

// One HTTP client per machine
for (int i = 0; i < 4; i++)
{
    builder.Services.AddHttpClient($"MachineApi-{i}", c =>
        c.BaseAddress = new Uri(builder.Configuration[$"MachineApis:Machine{i}"] ?? $"http://machine-api-{i}:8081"));
}

builder.Services.AddSingleton<MachineAggregatorService>();
builder.Services.AddHostedService<SystemStatusBroadcaster>();

var app = builder.Build();

app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpMetrics();
app.MapControllers();
app.MapHub<SystemHub>("/hubs/system");
app.MapMetrics("/metrics");
app.MapHealthChecks("/health");
app.Run();
