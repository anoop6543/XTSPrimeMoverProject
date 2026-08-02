using Temporalio.Workflows;

namespace XtsContracts.Workflows;

[Workflow]
public interface ISafetyOrchestrationWorkflow
{
    [WorkflowRun]
    Task RunAsync(SafetyOrchestrationInput input);

    [WorkflowSignal("EmergencyStop")]
    Task SignalEmergencyStopAsync(string sourceId, string reason, string triggeredBy);

    [WorkflowSignal("LockoutTagoutConfirmed")]
    Task SignalLockoutTagoutConfirmedAsync(int machineId, string technicianId, string lockTag);

    [WorkflowSignal("SafetyResetRequested")]
    Task SignalSafetyResetAsync(string authorizedBy, string verificationCode);

    [WorkflowSignal("Shutdown")]
    Task SignalShutdownAsync();

    [WorkflowQuery("GetSafetyState")]
    SafetySystemState GetSafetyState();
}

public record SafetyOrchestrationInput(IReadOnlyList<int> MachineIds);

public record SafetySystemState(
    bool EmergencyStopActive,
    string? EStopReason,
    string? EStopSource,
    DateTime? EStopTriggeredAt,
    IReadOnlyList<int> LockedOutMachines,
    IReadOnlyList<int> PendingResetMachines,
    bool AllClear
);
