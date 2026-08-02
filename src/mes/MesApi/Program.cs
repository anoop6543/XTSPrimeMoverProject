using MesService.Database;
using MesService.Services;
using MesApi.Hubs;
using Microsoft.OpenApi.Models;
using Temporalio.Client;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MES API", Version = "v1" }));
builder.Services.AddHealthChecks();

builder.Services.AddCors(opts => opts.AddPolicy("AllowAll", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Internal MES services
builder.Services.AddSingleton<WorkOrderService>();
builder.Services.AddSingleton<RecipeService>();
builder.Services.AddSingleton<QualityService>();
builder.Services.AddSingleton<SchedulingService>();

// Temporal client for triggering ProductionOrderWorkflow
builder.Services.AddSingleton<ITemporalClient>(_ =>
    TemporalClient.ConnectAsync(new TemporalClientConnectOptions(
        builder.Configuration["Temporal:Address"] ?? "temporal:7233")
    { Namespace = "xts-system" }).GetAwaiter().GetResult());

// Background broadcaster for MES real-time dashboard
builder.Services.AddHostedService<MesDashboardBroadcaster>();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=postgresql;Database=mes_db;Username=xts;******";

var app = builder.Build();

await MesDatabase.EnsureSchemaAsync(connectionString);

app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpMetrics();
app.MapControllers();
app.MapHub<MesHub>("/hubs/mes");
app.MapMetrics("/metrics");
app.MapHealthChecks("/health");
app.Run();
