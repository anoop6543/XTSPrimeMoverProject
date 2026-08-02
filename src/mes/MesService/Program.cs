using MesService.Database;
using MesService.Services;
using Microsoft.OpenApi.Models;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=postgresql;Database=mes_db;Username=xts;******";

builder.Services.AddControllers();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MES Service API", Version = "v1" }));
builder.Services.AddHealthChecks();

builder.Services.AddSingleton<WorkOrderService>();
builder.Services.AddSingleton<RecipeService>();
builder.Services.AddSingleton<QualityService>();
builder.Services.AddSingleton<SchedulingService>();

builder.Services.AddCors(opts => opts.AddPolicy("AllowAll", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Ensure DB schema on startup
await MesDatabase.EnsureSchemaAsync(connectionString);

app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpMetrics();
app.MapControllers();
app.MapMetrics("/metrics");
app.MapHealthChecks("/health");
app.Run();
