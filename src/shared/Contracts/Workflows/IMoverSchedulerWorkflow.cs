using Temporalio.Workflows;

namespace XtsContracts.Workflows;

[Workflow]
public interface IMoverSchedulerWorkflow
{
    [WorkflowRun]
    Task RunAsync(MoverSchedulerInput input);

    [WorkflowSignal("MoverAvailable")]
    Task SignalMoverAvailableAsync(int moverId);

    [WorkflowSignal("MoverRequested")]
    Task SignalMoverRequestedAsync(MoverRequest request);

    [WorkflowSignal("MoverReleased")]
    Task SignalMoverReleasedAsync(int moverId, string reason);

    [WorkflowSignal("Shutdown")]
    Task SignalShutdownAsync();

    [WorkflowQuery("GetPoolStatus")]
    MoverPoolStatus GetPoolStatus();
}

public record MoverSchedulerInput(int TotalMoverCount, int MaxWipParts);

public record MoverRequest(
    string RequestId,
    int MachineId,
    string PartTrackingNumber,
    int Priority,
    string CallbackWorkflowId
);

public record MoverPoolStatus(
    int TotalMovers,
    int AvailableMovers,
    int AssignedMovers,
    int WipCount,
    int MaxWip,
    IReadOnlyList<MoverAssignment> CurrentAssignments
);

public record MoverAssignment(int MoverId, int MachineId, string PartTracking, DateTime AssignedAt);
