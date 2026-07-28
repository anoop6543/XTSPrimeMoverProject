using Temporalio.Workflows;
using XtsContracts.Dtos;

namespace XtsContracts.Workflows;

/// <summary>
/// One always-running Temporal workflow per machine.
/// Waits for part arrivals, executes station sequence, signals completion.
/// </summary>
[Workflow]
public interface IMachineCycleWorkflow
{
    [WorkflowRun]
    Task RunAsync(MachineCycleInput input);

    [WorkflowSignal("PartArrived")]
    Task SignalPartArrivedAsync(PartArrivalSignal signal);

    [WorkflowSignal("ResetFault")]
    Task SignalResetFaultAsync();

    [WorkflowSignal("Shutdown")]
    Task SignalShutdownAsync();

    [WorkflowQuery("GetMachineStatus")]
    MachineDto GetMachineStatus();
}

public record MachineCycleInput(int MachineId, string MachineName, string MachineType);

public record PartArrivalSignal(
    string PartId,
    string TrackingNumber,
    string PartStatus,
    bool HasDefect,
    string CallbackWorkflowId
);
