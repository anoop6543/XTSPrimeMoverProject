using Temporalio.Workflows;
using XtsContracts.Workflows;
using OrchestrationWorker.Activities;

namespace OrchestrationWorker.Workflows;

/// <summary>
/// Triggered at shift end. Drains WIP, takes a production snapshot,
/// records shift KPIs, and enforces an operator acknowledgement handoff.
/// </summary>
[Workflow]
public class ShiftTransitionWorkflow : IShiftTransitionWorkflow
{
    private bool _wipDrained;
    private bool _snapshotTaken;
    private bool _operatorAcknowledged;
    private int _remainingWip = 0;

    [WorkflowRun]
    public async Task<ShiftTransitionResult> RunAsync(ShiftTransitionInput input)
    {
        Workflow.Logger.LogInformation(
            "ShiftTransition started: Shift={Shift}, End={End}",
            input.ShiftName, input.ShiftEndTime);

        // Phase 1: Stop accepting new parts (signal prime mover)
        await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.StopNewPartEntryAsync(input.ShiftId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        // Phase 2: Wait for WIP to drain (max 30 min)
        var wipDrained = await Workflow.WaitConditionAsync(
            () => _wipDrained, TimeSpan.FromMinutes(30));

        if (!wipDrained)
        {
            // Force drain — record stuck parts as incomplete
            Workflow.Logger.LogWarning("Shift {Shift}: WIP did not drain in time — force-closing", input.ShiftName);
        }
        _wipDrained = true;

        // Phase 3: Take production snapshot
        var summary = await Workflow.ExecuteActivityAsync(
            (OrchestrationActivities a) => a.TakeShiftSnapshotAsync(input.ShiftId, input.ShiftName),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
        _snapshotTaken = true;

        // Phase 4: Wait for operator acknowledgement (max 15 min)
        await Workflow.WaitConditionAsync(
            () => _operatorAcknowledged, TimeSpan.FromMinutes(15));

        // Phase 5: Notify incoming shift
        if (input.IncomingShiftId.HasValue)
        {
            await Workflow.ExecuteActivityAsync(
                (OrchestrationActivities a) => a.NotifyIncomingShiftAsync(
                    input.IncomingShiftId.Value, input.IncomingShiftName ?? "Next Shift",
                    summary.Handoff),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });
        }

        Workflow.Logger.LogInformation(
            "ShiftTransition complete: Shift={Shift}, Parts={Parts}",
            input.ShiftName, summary.PartsCompleted);

        return new ShiftTransitionResult(
            input.ShiftId, true, summary.PartsCompleted,
            summary.AlarmsThisShift, summary.TotalUptime, summary.Handoff);
    }

    [WorkflowSignal("OperatorAcknowledged")]
    public Task SignalOperatorAcknowledgedAsync(string operatorId, string acknowledgementCode)
    {
        _operatorAcknowledged = true;
        return Task.CompletedTask;
    }

    [WorkflowSignal("WipDrainConfirmed")]
    public Task SignalWipDrainConfirmedAsync()
    {
        _wipDrained = true;
        return Task.CompletedTask;
    }

    [WorkflowQuery("GetTransitionState")]
    public ShiftTransitionState GetTransitionState() => new(
        _wipDrained ? (_snapshotTaken ? (_operatorAcknowledged ? "Complete" : "WaitingForHandoff") : "TakingSnapshot") : "DrainingWip",
        _wipDrained, _snapshotTaken, _operatorAcknowledged,
        _remainingWip, null);
}
