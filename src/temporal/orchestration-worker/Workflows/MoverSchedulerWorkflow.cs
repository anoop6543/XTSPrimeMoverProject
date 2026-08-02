using Temporalio.Workflows;
using XtsContracts.Workflows;
using OrchestrationWorker.Activities;

namespace OrchestrationWorker.Workflows;

/// <summary>
/// Long-running singleton managing the global mover pool.
/// Replaces the ad-hoc AssignMoverForPickup activity with a workflow-coordinated
/// allocation that is aware of WIP limits and machine priorities.
/// </summary>
[Workflow]
public class MoverSchedulerWorkflow : IMoverSchedulerWorkflow
{
    private readonly Queue<int> _availableMovers = new();
    private readonly List<MoverAssignment> _assignments = new();
    private readonly Queue<MoverRequest> _pendingRequests = new();
    private int _wipCount;
    private bool _shutdown;
    private int _maxWip;
    private int _totalMovers;

    [WorkflowRun]
    public async Task RunAsync(MoverSchedulerInput input)
    {
        _totalMovers = input.TotalMoverCount;
        _maxWip = input.MaxWipParts;

        // Seed all movers as available
        for (int i = 0; i < input.TotalMoverCount; i++)
            _availableMovers.Enqueue(i);

        Workflow.Logger.LogInformation(
            "MoverScheduler started: {Count} movers, MaxWIP={Wip}",
            input.TotalMoverCount, input.MaxWipParts);

        while (!_shutdown)
        {
            // Dispatch pending requests when movers are available and WIP < max
            while (_pendingRequests.Count > 0 && _availableMovers.Count > 0 && _wipCount < _maxWip)
            {
                var request = _pendingRequests.Dequeue();
                var moverId = _availableMovers.Dequeue();

                _assignments.Add(new MoverAssignment(moverId, request.MachineId,
                    request.PartTrackingNumber, Workflow.UtcNow));
                _wipCount++;

                await Workflow.ExecuteActivityAsync(
                    (OrchestrationActivities a) => a.NotifyMoverAssignedAsync(
                        moverId, request.MachineId, request.PartTrackingNumber,
                        request.CallbackWorkflowId),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

                Workflow.Logger.LogInformation(
                    "Mover {Mover} assigned to Machine {Machine}, Part {Part}",
                    moverId, request.MachineId, request.PartTrackingNumber);
            }

            await Workflow.DelayAsync(TimeSpan.FromMilliseconds(200));
        }

        Workflow.Logger.LogInformation("MoverScheduler shutdown.");
    }

    [WorkflowSignal("MoverAvailable")]
    public Task SignalMoverAvailableAsync(int moverId)
    {
        if (!_availableMovers.Contains(moverId))
            _availableMovers.Enqueue(moverId);
        return Task.CompletedTask;
    }

    [WorkflowSignal("MoverRequested")]
    public Task SignalMoverRequestedAsync(MoverRequest request)
    {
        _pendingRequests.Enqueue(request);
        return Task.CompletedTask;
    }

    [WorkflowSignal("MoverReleased")]
    public Task SignalMoverReleasedAsync(int moverId, string reason)
    {
        var assignment = _assignments.FirstOrDefault(a => a.MoverId == moverId);
        if (assignment != null)
        {
            _assignments.Remove(assignment);
            _wipCount = Math.Max(0, _wipCount - 1);
        }
        if (!_availableMovers.Contains(moverId))
            _availableMovers.Enqueue(moverId);

        Workflow.Logger.LogInformation("Mover {Mover} released: {Reason}", moverId, reason);
        return Task.CompletedTask;
    }

    [WorkflowSignal("Shutdown")]
    public Task SignalShutdownAsync()
    {
        _shutdown = true;
        return Task.CompletedTask;
    }

    [WorkflowQuery("GetPoolStatus")]
    public MoverPoolStatus GetPoolStatus() => new(
        _totalMovers,
        _availableMovers.Count,
        _assignments.Count,
        _wipCount, _maxWip,
        _assignments.AsReadOnly());
}
