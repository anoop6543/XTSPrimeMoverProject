using Temporalio.Workflows;
using XtsContracts.Workflows;
using OrchestrationWorker.Activities;

namespace OrchestrationWorker.Workflows;

/// <summary>
/// Per-inspection-event workflow. Aggregates measurement results, applies recipe
/// quality thresholds, decides Pass / Hold / Scrap, creates NCR if failed.
/// Waits up to 60 s for a supervisor override before auto-deciding.
/// </summary>
[Workflow]
public class QualityGateWorkflow : IQualityGateWorkflow
{
    private readonly List<InspectionResult> _results = new();
    private string? _supervisorDecision;
    private string? _supervisorOperatorId;
    private string? _supervisorReason;
    private bool _decisionMade;

    [WorkflowRun]
    public async Task<QualityGateResult> RunAsync(QualityGateInput input)
    {
        Workflow.Logger.LogInformation(
            "QualityGate started for Part {Part}, WorkOrder {WO}",
            input.PartTrackingNumber, input.WorkOrderId);

        // Wait for all inspection results to arrive (or timeout after 120 s)
        await Workflow.WaitConditionAsync(
            () => _decisionMade || _results.Count > 0,
            TimeSpan.FromSeconds(120));

        // Evaluate measurements against recipe thresholds
        bool hasDefect = _results.Any(r => r.HasDefect);
        string? defectCategory = _results.FirstOrDefault(r => r.HasDefect)?.DefectCategory;

        // Check quantitative thresholds
        foreach (var result in _results)
        {
            foreach (var (key, value) in result.Measurements)
            {
                if (input.QualityThresholds.TryGetValue(key, out var threshold) && value > threshold)
                {
                    hasDefect = true;
                    defectCategory ??= $"{key} out of spec ({value:F3} > {threshold:F3})";
                }
            }
        }

        // If borderline — wait for supervisor override (up to 30 s)
        if (hasDefect && !_decisionMade)
        {
            Workflow.Logger.LogWarning("Part {Part} borderline — notifying supervisor", input.PartTrackingNumber);

            await Workflow.ExecuteActivityAsync(
                (OrchestrationActivities a) => a.NotifySupervisorQualityHoldAsync(
                    input.PartTrackingNumber, defectCategory ?? "Unknown", input.MachineId),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

            // Wait up to 30 s for supervisor override
            await Workflow.WaitConditionAsync(() => _supervisorDecision != null, TimeSpan.FromSeconds(30));
        }

        string finalDecision = _supervisorDecision ?? (hasDefect ? "Scrap" : "Pass");
        Guid? ncrId = null;

        if (finalDecision != "Pass")
        {
            ncrId = await Workflow.ExecuteActivityAsync(
                (OrchestrationActivities a) => a.CreateNonConformanceReportAsync(
                    input.PartTrackingNumber, input.MachineId, defectCategory ?? "Defect",
                    finalDecision, input.WorkOrderId),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
        }

        await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.RecordQualityDecisionAsync(
                input.PartTrackingNumber, finalDecision, _results, input.WorkOrderId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });

        Workflow.Logger.LogInformation(
            "QualityGate complete: Part={Part}, Decision={Decision}", input.PartTrackingNumber, finalDecision);

        return new QualityGateResult(input.PartTrackingNumber, finalDecision, ncrId.HasValue, ncrId,
            hasDefect ? defectCategory : null);
    }

    [WorkflowSignal("InspectionResultReceived")]
    public Task SignalInspectionResultAsync(InspectionResult result)
    {
        _results.Add(result);
        return Task.CompletedTask;
    }

    [WorkflowSignal("SupervisorOverride")]
    public Task SignalSupervisorOverrideAsync(string decision, string operatorId, string reason)
    {
        _supervisorDecision = decision;
        _supervisorOperatorId = operatorId;
        _supervisorReason = reason;
        _decisionMade = true;
        Workflow.Logger.LogInformation(
            "Supervisor override: Decision={Decision}, Operator={Op}", decision, operatorId);
        return Task.CompletedTask;
    }

    [WorkflowQuery("GetDecision")]
    public QualityGateDecision GetDecision() => new(
        _decisionMade ? "Decided" : "Pending",
        _supervisorDecision,
        _supervisorDecision == null && _results.Any(r => r.HasDefect),
        _results.AsReadOnly());
}
