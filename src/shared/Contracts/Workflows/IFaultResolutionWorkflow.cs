using Temporalio.Workflows;

namespace XtsContracts.Workflows;

[Workflow]
public interface IFaultResolutionWorkflow
{
    [WorkflowRun]
    Task<FaultResolutionResult> RunAsync(FaultResolutionInput input);

    [WorkflowSignal("StepAcknowledged")]
    Task SignalStepAcknowledgedAsync(int stepIndex, string operatorId);

    [WorkflowSignal("StepCompleted")]
    Task SignalStepCompletedAsync(int stepIndex, bool success, string? notes);

    [WorkflowSignal("EscalateRequested")]
    Task SignalEscalateAsync(string requestedBy, string reason);

    [WorkflowSignal("FaultCleared")]
    Task SignalFaultClearedAsync(string operatorId);

    [WorkflowQuery("GetCurrentState")]
    FaultResolutionState GetCurrentState();
}

public record FaultResolutionInput(
    int MachineId,
    string AlarmCode,
    string? MachineType,
    string? PartTrackingNumber,
    DateTime FaultOccurredAt
);

public record FaultResolutionResult(
    string AlarmCode,
    int MachineId,
    string Outcome,
    int EscalationLevel,
    int StepsCompleted,
    bool MaintenanceTicketCreated,
    Guid? MaintenanceWorkOrderId,
    TimeSpan ResolutionTime
);

public record FaultResolutionState(
    string AlarmCode,
    int EscalationLevel,
    int CurrentStepIndex,
    int TotalSteps,
    bool WaitingForOperator,
    bool WaitingForSupervisor,
    bool WaitingForEngineer,
    DateTime FaultStartedAt,
    IReadOnlyList<string> CompletedSteps
);
