using Temporalio.Workflows;

namespace XtsContracts.Workflows;

[Workflow]
public interface IShiftTransitionWorkflow
{
    [WorkflowRun]
    Task<ShiftTransitionResult> RunAsync(ShiftTransitionInput input);

    [WorkflowSignal("OperatorAcknowledged")]
    Task SignalOperatorAcknowledgedAsync(string operatorId, string acknowledgementCode);

    [WorkflowSignal("WipDrainConfirmed")]
    Task SignalWipDrainConfirmedAsync();

    [WorkflowQuery("GetTransitionState")]
    ShiftTransitionState GetTransitionState();
}

public record ShiftTransitionInput(
    Guid ShiftId,
    string ShiftName,
    DateTime ShiftEndTime,
    Guid? IncomingShiftId,
    string? IncomingShiftName
);

public record ShiftTransitionResult(
    Guid ShiftId,
    bool Success,
    int PartsCompletedThisShift,
    int AlarmsThisShift,
    TimeSpan TotalUptime,
    string? HandoffNotes
);

public record ShiftTransitionState(
    string Phase,
    bool WipDrained,
    bool SnapshotTaken,
    bool OperatorAcknowledged,
    int RemainingWipParts,
    DateTime? EstimatedCompletionTime
);

[Workflow]
public interface IProductionOrderWorkflow
{
    [WorkflowRun]
    Task<ProductionOrderResult> RunAsync(ProductionOrderInput input);

    [WorkflowSignal("PartCompleted")]
    Task SignalPartCompletedAsync(string partTrackingNumber, bool good, string? defectCategory);

    [WorkflowSignal("Pause")]
    Task SignalPauseAsync(string reason, string requestedBy);

    [WorkflowSignal("Resume")]
    Task SignalResumeAsync(string authorizedBy);

    [WorkflowSignal("Cancel")]
    Task SignalCancelAsync(string reason, string authorizedBy);

    [WorkflowQuery("GetProgress")]
    ProductionOrderProgress GetProgress();
}

public record ProductionOrderInput(
    Guid WorkOrderId,
    string OrderNumber,
    string ProductId,
    Guid RecipeId,
    int QuantityOrdered,
    int Priority,
    DateTime DueDate
);

public record ProductionOrderResult(
    Guid WorkOrderId,
    string OrderNumber,
    string FinalStatus,
    int QuantityCompleted,
    int QuantityGood,
    int QuantityBad,
    TimeSpan TotalRunTime
);

public record ProductionOrderProgress(
    string Status,
    int QuantityOrdered,
    int QuantityCompleted,
    int QuantityGood,
    int QuantityBad,
    double CompletionPercent,
    bool IsPaused,
    string? PauseReason,
    DateTime? EstimatedCompletion
);
