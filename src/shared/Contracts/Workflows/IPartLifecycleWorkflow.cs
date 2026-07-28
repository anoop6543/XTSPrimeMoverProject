using Temporalio.Workflows;
using XtsContracts.Dtos;

namespace XtsContracts.Workflows;

/// <summary>
/// One Temporal workflow instance per part travelling through the XTS line.
/// The workflow history IS the traceability audit trail.
/// </summary>
[Workflow]
public interface IPartLifecycleWorkflow
{
    [WorkflowRun]
    Task<PartLifecycleResult> RunAsync(PartLifecycleInput input);

    [WorkflowSignal("MoverAssigned")]
    Task SignalMoverAssignedAsync(int moverId);

    [WorkflowSignal("LoadedToMachine")]
    Task SignalLoadedToMachineAsync(int machineId);

    [WorkflowSignal("StationComplete")]
    Task SignalStationCompleteAsync(int stationId, string stationName, bool hadDefect);

    [WorkflowSignal("UnloadedFromMachine")]
    Task SignalUnloadedFromMachineAsync(int machineId, bool hasDefect, string partStatus);

    [WorkflowSignal("Exited")]
    Task SignalExitedAsync(bool good);

    [WorkflowQuery("GetCurrentStatus")]
    PartDto GetCurrentStatus();

    [WorkflowQuery("GetStationHistory")]
    IReadOnlyList<StationEventRecord> GetStationHistory();
}

public record PartLifecycleInput(
    Guid PartId,
    string TrackingNumber,
    IReadOnlyList<int> MachineRouteIds
);

public record PartLifecycleResult(
    string TrackingNumber,
    bool Good,
    int StationsCompleted,
    TimeSpan TotalCycleTime
);

public record StationEventRecord(
    int MachineId,
    int StationId,
    string StationName,
    DateTime StartedAt,
    DateTime CompletedAt,
    bool HadDefect
);
