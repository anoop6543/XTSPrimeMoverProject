using Temporalio.Client;
using Temporalio.Exceptions;
using XtsContracts.Workflows;
using MasterWorker.Workflows;

namespace MasterWorker;

/// <summary>
/// Hosted service that starts (or reconnects to) the single long-running
/// XTSPrimeMoverWorkflow on master-worker startup.
/// Uses workflow ID collision policy AllowDuplicateFailedOnly so that normal
/// restarts of the worker pod do NOT create a duplicate workflow instance.
/// </summary>
public sealed class XTSPrimeMoverWorkflowStarter : IHostedService
{
    private const string WorkflowId = "xts-prime-mover-main";
    private const string TaskQueue = "xts-prime-mover-queue";

    private readonly ITemporalClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<XTSPrimeMoverWorkflowStarter> _logger;

    public XTSPrimeMoverWorkflowStarter(
        ITemporalClient client,
        IConfiguration configuration,
        ILogger<XTSPrimeMoverWorkflowStarter> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        int moverCount = _configuration.GetValue("PrimeMover:MoverCount", 10);
        double speedFactor = _configuration.GetValue("PrimeMover:SimulationSpeedFactor", 1.0);
        var machineIds = _configuration
            .GetSection("PrimeMover:MachineIds")
            .Get<int[]>() ?? new[] { 0, 1, 2, 3 };

        var input = new PrimeMoverInput(moverCount, machineIds.ToList().AsReadOnly(), speedFactor);

        try
        {
            await _client.StartWorkflowAsync(
                (XTSPrimeMoverWorkflow w) => w.RunAsync(input),
                new WorkflowOptions
                {
                    Id = WorkflowId,
                    TaskQueue = TaskQueue,
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.AllowDuplicateFailedOnly
                });

            _logger.LogInformation(
                "XTSPrimeMoverWorkflow started. WorkflowId={Id}, Movers={Count}, Machines=[{Machines}]",
                WorkflowId, moverCount, string.Join(",", machineIds));
        }
        catch (WorkflowAlreadyStartedException)
        {
            _logger.LogInformation(
                "XTSPrimeMoverWorkflow already running (WorkflowId={Id}) — worker reconnected.",
                WorkflowId);
        }
        catch (Exception ex)
        {
            // Log but do not crash the host — the workflow may already be running under a
            // different execution, and activities will still be dispatched correctly.
            _logger.LogError(ex,
                "Failed to start XTSPrimeMoverWorkflow (WorkflowId={Id}). Check Temporal connectivity.",
                WorkflowId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
