using System.Linq.Expressions;
using Temporalio.Client;
using Temporalio.Exceptions;
using OrchestrationWorker.Workflows;
using XtsContracts.Workflows;

namespace OrchestrationWorker;

/// <summary>
/// Starts the long-running singleton workflows (MoverScheduler, SafetyOrchestration)
/// on startup. Idempotent — safe to restart the worker pod.
/// </summary>
public sealed class OrchestrationStartupService : IHostedService
{
    private const string TaskQueue = "xts-orchestration-queue";

    private readonly ITemporalClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<OrchestrationStartupService> _logger;

    public OrchestrationStartupService(
        ITemporalClient client,
        IConfiguration config,
        ILogger<OrchestrationStartupService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var machineIds = _config.GetSection("Orchestration:MachineIds").Get<int[]>() ?? new[] { 0, 1, 2, 3 };
        var moverCount = _config.GetValue("Orchestration:MoverCount", 10);
        var maxWip = _config.GetValue("Orchestration:MaxWipParts", 6);

        var safetyInput = new SafetyOrchestrationInput(machineIds.ToList().AsReadOnly());
        var moverInput = new MoverSchedulerInput(moverCount, maxWip);

        await StartSingletonAsync<SafetyOrchestrationWorkflow>(
            "xts-safety-orchestration",
            w => w.RunAsync(safetyInput));

        await StartSingletonAsync<MoverSchedulerWorkflow>(
            "xts-mover-scheduler",
            w => w.RunAsync(moverInput));
    }

    private async Task StartSingletonAsync<TWorkflow>(
        string workflowId,
        Expression<Func<TWorkflow, Task>> workflowExpr)
    {
        try
        {
            await _client.StartWorkflowAsync(workflowExpr, new WorkflowOptions
            {
                Id = workflowId,
                TaskQueue = TaskQueue,
                IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.AllowDuplicateFailedOnly
            });
            _logger.LogInformation("Singleton workflow started: {WorkflowId}", workflowId);
        }
        catch (WorkflowAlreadyStartedException)
        {
            _logger.LogInformation("Singleton workflow already running: {WorkflowId}", workflowId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start singleton workflow: {WorkflowId}", workflowId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
