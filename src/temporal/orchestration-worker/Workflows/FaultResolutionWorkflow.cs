using Temporalio.Workflows;
using XtsContracts.Workflows;
using OrchestrationWorker.Activities;

namespace OrchestrationWorker.Workflows;

/// <summary>
/// Spawned per fault event. Looks up resolution steps from the knowledge base,
/// guides the operator through them with acknowledgement gates, and auto-escalates
/// if unresolved within configurable timeouts.
/// Escalation: Operator (0–10 min) → Supervisor (10–25 min) → Engineer (25+ min)
/// </summary>
[Workflow]
public class FaultResolutionWorkflow : IFaultResolutionWorkflow
{
    private readonly List<string> _completedSteps = new();
    private int _currentStepIndex;
    private int _escalationLevel; // 0=Operator, 1=Supervisor, 2=Engineer
    private bool _faultCleared;
    private bool _stepAcknowledged;
    private bool _stepCompleted;
    private bool _stepSuccess;
    private string? _stepNotes;
    private bool _escalateRequested;
    private Guid? _maintenanceWorkOrderId;
    private IReadOnlyList<ResolutionStepInfo> _resolutionSteps = Array.Empty<ResolutionStepInfo>();

    [WorkflowRun]
    public async Task<FaultResolutionResult> RunAsync(FaultResolutionInput input)
    {
        var startedAt = Workflow.UtcNow;

        Workflow.Logger.LogWarning(
            "FaultResolution started: Machine={Machine}, Alarm={Alarm}",
            input.MachineId, input.AlarmCode);

        // Look up resolution steps from knowledge base
        _resolutionSteps = await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.LookupFaultResolutionStepsAsync(
                input.AlarmCode, input.MachineType ?? "Unknown"),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        // Push resolution guidance to operator terminal
        await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.PushGuidanceToTerminalAsync(
                input.MachineId, input.AlarmCode, _resolutionSteps),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        // Execute step-by-step resolution with timeouts per escalation level
        while (!_faultCleared && _currentStepIndex < _resolutionSteps.Count)
        {
            _stepAcknowledged = false;
            _stepCompleted = false;

            // Wait for step acknowledgement (operator saw the step)
            var ackTimeout = _escalationLevel switch
            {
                0 => TimeSpan.FromMinutes(10),
                1 => TimeSpan.FromMinutes(15),
                _ => TimeSpan.FromMinutes(20)
            };

            var acked = await Workflow.WaitConditionAsync(
                () => _stepAcknowledged || _escalateRequested || _faultCleared,
                ackTimeout);

            if (!acked || _escalateRequested)
            {
                await EscalateAsync(input.MachineId, input.AlarmCode);
                _escalateRequested = false;
                continue;
            }

            if (_faultCleared) break;

            // Wait for step completion report from operator
            var completionTimeout = TimeSpan.FromMinutes(20);
            var completed = await Workflow.WaitConditionAsync(
                () => _stepCompleted || _faultCleared, completionTimeout);

            if (!completed)
            {
                await EscalateAsync(input.MachineId, input.AlarmCode);
                continue;
            }

            if (_faultCleared) break;

            if (_stepSuccess)
            {
                _completedSteps.Add(_resolutionSteps[_currentStepIndex].Description);
                _currentStepIndex++;
            }
            else
            {
                // Step failed — escalate
                await EscalateAsync(input.MachineId, input.AlarmCode);
            }
        }

        // Determine outcome
        bool resolved = _faultCleared;
        bool needsMaintenance = _escalationLevel >= 2 || !resolved;

        if (needsMaintenance)
        {
            _maintenanceWorkOrderId = await Workflow.ExecuteActivityAsync(
                (OrchestrationActivities a) => a.CreateMaintenanceWorkOrderAsync(
                    input.MachineId, input.AlarmCode,
                    $"Alarm {input.AlarmCode} required engineer escalation",
                    "Corrective"),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
        }

        var resolutionTime = Workflow.UtcNow - startedAt;

        await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.RecordFaultResolutionOutcomeAsync(
                input.AlarmCode, input.MachineId, _completedSteps, resolved,
                (int)resolutionTime.TotalMinutes),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });

        Workflow.Logger.LogInformation(
            "FaultResolution complete: Alarm={Alarm}, Resolved={R}, Level={Level}",
            input.AlarmCode, resolved, _escalationLevel);

        return new FaultResolutionResult(
            input.AlarmCode, input.MachineId,
            resolved ? "Resolved" : "Escalated",
            _escalationLevel, _completedSteps.Count,
            _maintenanceWorkOrderId.HasValue, _maintenanceWorkOrderId,
            resolutionTime);
    }

    private async Task EscalateAsync(int machineId, string alarmCode)
    {
        _escalationLevel++;
        string[] personnel = { "Operator", "Supervisor", "Engineer" };
        var target = personnel[Math.Min(_escalationLevel, personnel.Length - 1)];

        Workflow.Logger.LogWarning(
            "Escalating fault: Alarm={Alarm}, Machine={Machine}, Level={Level} → {Target}",
            alarmCode, machineId, _escalationLevel, target);

        await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.EscalateFaultAsync(
                machineId, alarmCode, _escalationLevel, target),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });
    }

    [WorkflowSignal("StepAcknowledged")]
    public Task SignalStepAcknowledgedAsync(int stepIndex, string operatorId)
    {
        if (stepIndex == _currentStepIndex)
            _stepAcknowledged = true;
        return Task.CompletedTask;
    }

    [WorkflowSignal("StepCompleted")]
    public Task SignalStepCompletedAsync(int stepIndex, bool success, string? notes)
    {
        if (stepIndex == _currentStepIndex)
        {
            _stepCompleted = true;
            _stepSuccess = success;
            _stepNotes = notes;
        }
        return Task.CompletedTask;
    }

    [WorkflowSignal("EscalateRequested")]
    public Task SignalEscalateAsync(string requestedBy, string reason)
    {
        _escalateRequested = true;
        return Task.CompletedTask;
    }

    [WorkflowSignal("FaultCleared")]
    public Task SignalFaultClearedAsync(string operatorId)
    {
        _faultCleared = true;
        return Task.CompletedTask;
    }

    [WorkflowQuery("GetCurrentState")]
    public FaultResolutionState GetCurrentState() => new(
        _resolutionSteps.Count > 0 ? _resolutionSteps[Math.Min(_currentStepIndex, _resolutionSteps.Count - 1)].Code : "Unknown",
        _escalationLevel, _currentStepIndex, _resolutionSteps.Count,
        !_stepAcknowledged, _escalationLevel == 1, _escalationLevel >= 2,
        Workflow.UtcNow, _completedSteps.AsReadOnly());
}

public record ResolutionStepInfo(string Code, int StepIndex, string Description, string? ToolRequired, bool RequiresAck);
