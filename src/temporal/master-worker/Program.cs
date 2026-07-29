using MasterWorker.Workflows;
using MasterWorker.Activities;
using MasterWorker;
using Temporalio.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var temporalAddress = builder.Configuration["Temporal:Address"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "xts-system";

builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = temporalAddress;
    opts.Namespace = temporalNamespace;
});

// Named HTTP client used by PrimeMoverActivities to call the Prime Mover API
builder.Services.AddHttpClient("PrimeMoverApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["PrimeMoverApi:BaseUrl"] ?? "http://prime-mover-api:8082");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Named HTTP client for direct MachineService callbacks (used by robot/mover assignment activities)
builder.Services.AddHttpClient("MachineApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["MachineApi:BaseUrl"] ?? "http://machine-api:8080");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedTemporalWorker("xts-prime-mover-queue")
    .AddWorkflow<XTSPrimeMoverWorkflow>()
    .AddWorkflow<PartLifecycleWorkflow>()
    .AddWorkflow<RobotTransferWorkflow>()
    .AddScopedActivities<PrimeMoverActivities>();

// Auto-start the XTSPrimeMoverWorkflow on startup (idempotent — safe to restart)
builder.Services.AddHostedService<XTSPrimeMoverWorkflowStarter>();

var host = builder.Build();
await host.RunAsync();
