using Temporalio.Workflows;

namespace XtsContracts.Workflows;

[Workflow]
public interface IQualityGateWorkflow
{
    [WorkflowRun]
    Task<QualityGateResult> RunAsync(QualityGateInput input);

    [WorkflowSignal("InspectionResultReceived")]
    Task SignalInspectionResultAsync(InspectionResult result);

    [WorkflowSignal("SupervisorOverride")]
    Task SignalSupervisorOverrideAsync(string decision, string operatorId, string reason);

    [WorkflowQuery("GetDecision")]
    QualityGateDecision GetDecision();
}

public record QualityGateInput(
    string PartTrackingNumber,
    int MachineId,
    Guid? WorkOrderId,
    string RecipeId,
    IReadOnlyDictionary<string, double> QualityThresholds
);

public record InspectionResult(
    int StationId,
    string StationName,
    IReadOnlyDictionary<string, double> Measurements,
    bool HasDefect,
    string? DefectCategory
);

public record QualityGateResult(
    string PartTrackingNumber,
    string Decision,
    bool NcrCreated,
    Guid? NcrId,
    string? RejectionReason
);

public record QualityGateDecision(
    string Status,
    string? Decision,
    bool WaitingForSupervisor,
    IReadOnlyList<InspectionResult> Results
);
