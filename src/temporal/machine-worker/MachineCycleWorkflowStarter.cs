using Temporalio.Client;
using XtsContracts.Workflows;
using MachineWorker.Workflows;

namespace MachineWorker;

/// <summary>
/// Starts or connects to the always-running MachineCycleWorkflow on worker startup.
/// Uses workflow ID collision (GetOrStartWorkflow) so restarts are idempotent.
/// </summary>
public class MachineCycleWorkflowStarter : IHostedService
{
    private readonly ITemporalClient _client;
    private readonly int _machineId;
    private readonly string _machineName;
    private readonly string _machineType;

    public MachineCycleWorkflowStarter(ITemporalClient client, int machineId, string machineName, string machineType)
    {
        _client = client;
        _machineId = machineId;
        _machineName = machineName;
        _machineType = machineType;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var workflowId = $"machine-cycle-{_machineId}";
        try
        {
            await _client.StartWorkflowAsync(
                (MachineCycleWorkflow w) => w.RunAsync(new MachineCycleInput(_machineId, _machineName, _machineType)),
                new WorkflowOptions
                {
                    Id = workflowId,
                    TaskQueue = $"xts-machine-{_machineId}-queue",
                    IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.AllowDuplicateFailedOnly
                });
        }
        catch (Temporalio.Exceptions.WorkflowAlreadyStartedException)
        {
            // Workflow already running — this is fine (idempotent)
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
