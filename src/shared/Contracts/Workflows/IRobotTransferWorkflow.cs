using Temporalio.Workflows;

namespace XtsContracts.Workflows;

/// <summary>
/// Short-lived child workflow for each robot pick-and-place transfer.
/// Coordinates the 6-step robot state machine with timeout safety.
/// </summary>
[Workflow]
public interface IRobotTransferWorkflow
{
    [WorkflowRun]
    Task<RobotTransferResult> RunAsync(RobotTransferInput input);
}

public enum TransferDirection { MoverToMachine, MachineToMover }

public record RobotTransferInput(
    int RobotId,
    int MoverId,
    int MachineId,
    TransferDirection Direction,
    string PartTrackingNumber
);

public record RobotTransferResult(
    bool Success,
    string? FailureReason,
    TimeSpan Duration
);
