using OrchestrationWorker;
using OrchestrationWorker.Workflows;
using OrchestrationWorker.Activities;
using Temporalio.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var temporalAddress = builder.Configuration["Temporal:Address"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "xts-system";

builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = temporalAddress;
    opts.Namespace = temporalNamespace;
});

// HTTP clients for inter-service communication
builder.Services.AddHttpClient("MesService", c =>
    c.BaseAddress = new Uri(builder.Configuration["MesService:BaseUrl"] ?? "http://mes-service:8100"));

builder.Services.AddHttpClient("WorkforceService", c =>
    c.BaseAddress = new Uri(builder.Configuration["WorkforceService:BaseUrl"] ?? "http://workforce-service:8200"));

builder.Services.AddHttpClient("KnowledgeBaseService", c =>
    c.BaseAddress = new Uri(builder.Configuration["KnowledgeBaseService:BaseUrl"] ?? "http://kb-service:8300"));

builder.Services.AddHttpClient("MaintenanceService", c =>
    c.BaseAddress = new Uri(builder.Configuration["MaintenanceService:BaseUrl"] ?? "http://maintenance-service:8400"));

builder.Services.AddHttpClient("PrimeMoverApi", c =>
    c.BaseAddress = new Uri(builder.Configuration["PrimeMoverApi:BaseUrl"] ?? "http://prime-mover-api:8082"));

// Register all 4 machine API clients
for (int i = 0; i < 4; i++)
{
    var idx = i;
    builder.Services.AddHttpClient($"MachineApi-{idx}", c =>
        c.BaseAddress = new Uri(builder.Configuration[$"MachineApis:Machine{idx}"] ?? $"http://machine-api-{idx}:8081"));
}

// Register Temporal worker on the dedicated orchestration task queue
builder.Services.AddHostedTemporalWorker("xts-orchestration-queue")
    .AddWorkflow<QualityGateWorkflow>()
    .AddWorkflow<FaultResolutionWorkflow>()
    .AddWorkflow<MoverSchedulerWorkflow>()
    .AddWorkflow<SafetyOrchestrationWorkflow>()
    .AddWorkflow<ShiftTransitionWorkflow>()
    .AddWorkflow<ProductionOrderWorkflow>()
    .AddScopedActivities<OrchestrationActivities>();

// Auto-start long-running singleton workflows
builder.Services.AddHostedService<OrchestrationStartupService>();

var host = builder.Build();
await host.RunAsync();
