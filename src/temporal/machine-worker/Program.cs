using MachineWorker.Workflows;
using MachineWorker.Activities;
using MachineWorker;
using Temporalio.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var machineId = int.Parse(builder.Configuration["Machine:Id"] ?? "0");
var machineName = builder.Configuration["Machine:Name"] ?? $"Machine-{machineId}";
var machineType = builder.Configuration["Machine:Type"] ?? "LaserWelding";
var temporalAddress = builder.Configuration["Temporal:Address"] ?? "localhost:7233";

builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = temporalAddress;
    opts.Namespace = "xts-system";
});

// Named HTTP client used by MachineActivities to call the MachineService REST API
builder.Services.AddHttpClient("MachineService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["MachineService:BaseUrl"] ?? "http://machine-service:8080");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHostedTemporalWorker($"xts-machine-{machineId}-queue")
    .AddWorkflow<MachineCycleWorkflow>()
    .AddScopedActivities<MachineActivities>();

// Auto-start the MachineCycleWorkflow when the worker starts
builder.Services.AddHostedService(sp =>
    new MachineCycleWorkflowStarter(
        sp.GetRequiredService<Temporalio.Client.ITemporalClient>(),
        machineId, machineName, machineType));

var host = builder.Build();
await host.RunAsync();
