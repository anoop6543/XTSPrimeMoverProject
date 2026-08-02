using Temporalio.Workflows;
using XtsContracts.Workflows;
using OrchestrationWorker.Activities;

namespace OrchestrationWorker.Workflows;

/// <summary>
/// Long-running singleton. Manages E-stop propagation and lockout/tagout
/// across all machines. All safety state is in this single authoritative workflow.
/// </summary>
[Workflow]
public class SafetyOrchestrationWorkflow : ISafetyOrchestrationWorkflow
{
    private bool _eStopActive;
    private string? _eStopReason;
    private string? _eStopSource;
    private DateTime? _eStopTriggeredAt;
    private readonly HashSet<int> _lockedOutMachines = new();
    private readonly HashSet<int> _pendingResetMachines = new();
    private bool _shutdown;
    private string? _resetAuthorizedBy;
    private bool _resetRequested;

    [WorkflowRun]
    public async Task RunAsync(SafetyOrchestrationInput input)
    {
        Workflow.Logger.LogInformation(
            "SafetyOrchestration started for machines: [{Machines}]",
            string.Join(",", input.MachineIds));

        while (!_shutdown)
        {
            await Workflow.WaitConditionAsync(() => _resetRequested || _shutdown);

            if (_shutdown) break;

            if (_resetRequested && _eStopActive)
            {
                _resetRequested = false;
                Workflow.Logger.LogInformation(
                    "Safety reset authorized by {Auth}", _resetAuthorizedBy);

                // Verify all machines are locked out before allowing reset
                bool allLocked = _lockedOutMachines.Count >= input.MachineIds.Count;
                if (!allLocked)
                {
                    Workflow.Logger.LogWarning(
                        "Safety reset rejected — not all machines locked out");
                    continue;
                }

                // Clear E-stop
                _eStopActive = false;
                _eStopReason = null;
                _eStopSource = null;
                _eStopTriggeredAt = null;
                _lockedOutMachines.Clear();
                _pendingResetMachines.Clear();

                await Workflow.ExecuteActivityAsync(
                    (OrchestrationActivities a) => a.BroadcastSafetyAllClearAsync(_resetAuthorizedBy!),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

                Workflow.Logger.LogInformation("Safety all-clear. E-stop cleared.");
            }
        }
    }

    [WorkflowSignal("EmergencyStop")]
    public async Task SignalEmergencyStopAsync(string sourceId, string reason, string triggeredBy)
    {
        if (_eStopActive) return; // already in E-stop

        _eStopActive = true;
        _eStopReason = reason;
        _eStopSource = sourceId;
        _eStopTriggeredAt = Workflow.UtcNow;

        Workflow.Logger.LogCritical(
            "EMERGENCY STOP: Source={Source}, Reason={Reason}, By={By}",
            sourceId, reason, triggeredBy);

        // Fire-and-forget stop all machines (activity handles per-machine stops)
        await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.EmergencyStopAllMachinesAsync(sourceId, reason),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });

        await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.NotifySafetyOfficerAsync(sourceId, reason, triggeredBy),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });
    }

    [WorkflowSignal("LockoutTagoutConfirmed")]
    public Task SignalLockoutTagoutConfirmedAsync(int machineId, string technicianId, string lockTag)
    {
        _lockedOutMachines.Add(machineId);
        _pendingResetMachines.Remove(machineId);
        Workflow.Logger.LogInformation(
            "Lockout confirmed: Machine={M}, Tech={T}, Tag={Tag}",
            machineId, technicianId, lockTag);
        return Task.CompletedTask;
    }

    [WorkflowSignal("SafetyResetRequested")]
    public Task SignalSafetyResetAsync(string authorizedBy, string verificationCode)
    {
        _resetAuthorizedBy = authorizedBy;
        _resetRequested = true;
        return Task.CompletedTask;
    }

    [WorkflowSignal("Shutdown")]
    public Task SignalShutdownAsync()
    {
        _shutdown = true;
        return Task.CompletedTask;
    }

    [WorkflowQuery("GetSafetyState")]
    public SafetySystemState GetSafetyState() => new(
        _eStopActive, _eStopReason, _eStopSource, _eStopTriggeredAt,
        _lockedOutMachines.ToList().AsReadOnly(),
        _pendingResetMachines.ToList().AsReadOnly(),
        !_eStopActive);
}
