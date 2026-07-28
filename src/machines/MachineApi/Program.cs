using MachineApi.Hubs;
using Microsoft.OpenApi.Models;
using Temporalio.Client;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Machine API", Version = "v1" }));

builder.Services.AddCors(opts => opts.AddPolicy("AllowAll", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddSingleton<ITemporalClient>(sp =>
    TemporalClient.ConnectAsync(new TemporalClientConnectOptions(
        builder.Configuration["Temporal:Address"] ?? "temporal:7233")
    { Namespace = "xts-system" }).GetAwaiter().GetResult());

builder.Services.AddHttpClient("MachineService", c =>
    c.BaseAddress = new Uri(builder.Configuration["MachineService:BaseUrl"] ?? "http://machine-service:8080"));

builder.Services.AddHostedService<MachineStatusBroadcaster>();

var app = builder.Build();

app.UseCors("AllowAll");
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpMetrics();
app.MapControllers();
app.MapHub<MachineHub>("/hubs/machine");
app.MapMetrics("/metrics");
app.Run();
