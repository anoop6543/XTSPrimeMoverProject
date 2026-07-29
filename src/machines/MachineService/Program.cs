using MachineService.PlcEngine;
using MachineService.GrpcServices;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

var machineId = int.Parse(builder.Configuration["Machine:Id"] ?? "0");
var machineType = builder.Configuration["Machine:Type"] ?? "LaserWelding";

builder.Services.AddGrpc();
builder.Services.AddSingleton<MachinePlcEngine>(sp =>
    new MachinePlcEngine(machineId, Enum.Parse<MachineType>(machineType)));
builder.Services.AddSingleton<MachineService.MachineCompletionTracker>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseRouting();
app.UseHttpMetrics();
app.MapGrpcService<MachineGrpcService>();
app.MapControllers();
app.MapMetrics("/metrics");
app.MapHealthChecks("/health");
app.Run();
