using WorkforceService.Database;
using WorkforceService.Services;
using Microsoft.OpenApi.Models;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=postgresql;Database=workforce_db;Username=xts;******";

builder.Services.AddControllers();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Workforce Service API", Version = "v1" }));
builder.Services.AddHealthChecks();
builder.Services.AddCors(opts => opts.AddPolicy("AllowAll", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddSingleton<EmployeeService>();
builder.Services.AddSingleton<ShiftService>();
builder.Services.AddSingleton<TrainingService>();

var app = builder.Build();
await WorkforceDatabase.EnsureSchemaAsync(connectionString);

app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpMetrics();
app.MapControllers();
app.MapMetrics("/metrics");
app.MapHealthChecks("/health");
app.Run();
