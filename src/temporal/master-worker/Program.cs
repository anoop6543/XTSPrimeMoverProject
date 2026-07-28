using MasterWorker.Workflows;
using MasterWorker.Activities;
using Temporalio.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var temporalAddress = builder.Configuration["Temporal:Address"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "xts-system";

builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = temporalAddress;
    opts.Namespace = temporalNamespace;
});

builder.Services.AddHostedTemporalWorker("xts-prime-mover-queue")
    .AddWorkflow<XTSPrimeMoverWorkflow>()
    .AddWorkflow<PartLifecycleWorkflow>()
    .AddWorkflow<RobotTransferWorkflow>()
    .AddScopedActivities<PrimeMoverActivities>();

builder.Services.AddHttpClient("MachineApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["MachineApi:BaseUrl"] ?? "http://machine-api:8080");
});

var host = builder.Build();
await host.RunAsync();
