using MaintenanceService.Database;
using MaintenanceService.Services;
using Microsoft.OpenApi.Models;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=postgresql;Database=maintenance_db;Username=xts;******";

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Maintenance Service API", Version = "v1" }));
builder.Services.AddHealthChecks();
builder.Services.AddCors(opts => opts.AddPolicy("AllowAll", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddSingleton<CmmsService>();
builder.Services.AddSingleton<ToolLifeService>();
builder.Services.AddSingleton<PredictiveAnalysisService>();

var app = builder.Build();
await MaintenanceDatabase.EnsureSchemaAsync(connectionString);

app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpMetrics();
app.MapControllers();
app.MapMetrics("/metrics");
app.MapHealthChecks("/health");
app.Run();
