using PrimeMoverService.TrackEngine;
using PrimeMoverService.GrpcServices;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddControllers();
builder.Services.AddSingleton<XtsTrackEngine>(_ =>
    new XtsTrackEngine(10, new[] { 0, 1, 2, 3 }));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseRouting();
app.UseHttpMetrics();
app.MapGrpcService<PrimeMoverGrpcService>();
app.MapControllers();
app.MapMetrics("/metrics");
app.MapHealthChecks("/health");
app.Run();
