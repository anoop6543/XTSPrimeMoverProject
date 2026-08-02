using Temporalio.Workflows;
using XtsContracts.Dtos;
using XtsContracts.Workflows;
using MachineWorker.Activities;

namespace MachineWorker.Workflows;

/// <summary>
/// Always-running workflow for each physical machine.
/// Waits for parts, executes station sequences, signals prime mover on completion.
/// Temporal's retry policy replaces the custom watchdog escalation logic.
/// </summary>
[Workflow]
public class MachineCycleWorkflow : IMachineCycleWorkflow
{
    private MachineDto _machineStatus;
    private readonly Queue<PartArrivalSignal> _pendingParts = new();
    private bool _faultResetRequested;
    private bool _shutdown;

    public MachineCycleWorkflow()
    {
        _machineStatus = new MachineDto(0, "Unknown", "Unknown", "Init", true, false, string.Empty, 0, 0, new List<StationDto>(), 0, false, 0);
    }

    [WorkflowRun]
    public async Task RunAsync(MachineCycleInput input)
    {
        _machineStatus = _machineStatus with
        {
            MachineId = input.MachineId,
            Name = input.MachineName,
            Type = input.MachineType,
            SequencerState = "Ready"
        };

        Workflow.Logger.LogInformation("MachineCycleWorkflow started for Machine {Id} ({Name})", input.MachineId, input.MachineName);

        while (!_shutdown)
        {
            // Wait for a part to arrive or shutdown
            await Workflow.WaitConditionAsync(() => _pendingParts.Count > 0 || _shutdown);

            if (_shutdown) break;

            var partSignal = _pendingParts.Dequeue();
            Workflow.Logger.LogInformation("Machine {Id} received part {Tracking}", input.MachineId, partSignal.TrackingNumber);

            _machineStatus = _machineStatus with { SequencerState = "Run" };

            try
            {
                // Execute each station as a Temporal activity (with built-in timeout/retry)
                var stationResult = await Workflow.ExecuteActivityAsync(
                    (MachineActivities a) => a.ExecuteStationSequenceAsync(
                        input.MachineId, partSignal.PartId, partSignal.TrackingNumber, partSignal.HasDefect),
                    new ActivityOptions
                    {
                        StartToCloseTimeout = TimeSpan.FromSeconds(120),
                        RetryPolicy = new Temporalio.Common.RetryPolicy
                        {
                            MaximumAttempts = 3,
                            InitialInterval = TimeSpan.FromSeconds(2)
                        }
                    });

                _machineStatus = _machineStatus with
                {
                    PartsExitedCount = _machineStatus.PartsExitedCount + 1,
                    SequencerState = "Ready"
                };

                // Signal the calling PartLifecycleWorkflow that this machine is done
                if (!string.IsNullOrEmpty(partSignal.CallbackWorkflowId))
                {
                    var handle = Workflow.GetExternalWorkflowHandle<IPartLifecycleWorkflow>(partSignal.CallbackWorkflowId);
                    await handle.SignalAsync(w => w.SignalUnloadedFromMachineAsync(
                        input.MachineId, stationResult.HasDefect, stationResult.FinalStatus));
                }
            }
            catch (Exception ex)
            {
                Workflow.Logger.LogError("Machine {Id} station sequence failed: {Error}", input.MachineId, ex.Message);
                _machineStatus = _machineStatus with
                {
                    FaultActive = true,
                    FaultMessage = ex.Message,
                    SequencerState = "Fault"
                };

                // Wait for fault reset
                await Workflow.WaitConditionAsync(() => _faultResetRequested || _shutdown);
                _faultResetRequested = false;
                _machineStatus = _machineStatus with { FaultActive = false, FaultMessage = string.Empty, SequencerState = "Ready" };
            }
        }

        Workflow.Logger.LogInformation("MachineCycleWorkflow shutting down for Machine {Id}", input.MachineId);
    }

    [WorkflowSignal("PartArrived")]
    public Task SignalPartArrivedAsync(PartArrivalSignal signal)
    {
        _pendingParts.Enqueue(signal);
        _machineStatus = _machineStatus with { PartsEnteredCount = _machineStatus.PartsEnteredCount + 1 };
        return Task.CompletedTask;
    }

    [WorkflowSignal("ResetFault")]
    public Task SignalResetFaultAsync()
    {
        _faultResetRequested = true;
        return Task.CompletedTask;
    }

    [WorkflowSignal("Shutdown")]
    public Task SignalShutdownAsync()
    {
        _shutdown = true;
        return Task.CompletedTask;
    }

    [WorkflowQuery("GetMachineStatus")]
    public MachineDto GetMachineStatus() => _machineStatus;
}
