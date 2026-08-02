using Temporalio.Workflows;
using XtsContracts.Dtos;
using XtsContracts.Workflows;
using MasterWorker.Activities;

namespace MasterWorker.Workflows;

/// <summary>
/// One workflow instance per part. The Temporal event history IS the part's audit trail.
/// No separate database table needed for part events — query this workflow directly.
/// </summary>
[Workflow]
public class PartLifecycleWorkflow : IPartLifecycleWorkflow
{
    private PartDto _currentStatus;
    private readonly List<StationEventRecord> _stationHistory = new();
    private DateTime _startedAt;
    private bool _exited;

    // Awaitable signals (using TaskCompletionSource-equivalent pattern in Temporal .NET SDK)
    private int? _assignedMoverId;
    private int? _loadedMachineId;
    private int? _unloadedMachineId;
    private bool _unloadedHasDefect;
    private string? _unloadedPartStatus;

    public PartLifecycleWorkflow()
    {
        _currentStatus = new PartDto(Guid.Empty, string.Empty, "Empty", DateTime.MinValue, false, 0, 0, "Entry", Array.Empty<string>());
    }

    [WorkflowRun]
    public async Task<PartLifecycleResult> RunAsync(PartLifecycleInput input)
    {
        _startedAt = Workflow.UtcNow;
        _currentStatus = _currentStatus with
        {
            PartId = input.PartId,
            TrackingNumber = input.TrackingNumber,
            Status = "BaseLayer",
            CreatedAt = _startedAt,
            CurrentLocation = "Entry"
        };

        Workflow.Logger.LogInformation("Part {Tracking} lifecycle started. Route: [{Route}]",
            input.TrackingNumber, string.Join("->", input.MachineRouteIds));

        // Route through each machine
        foreach (var machineId in input.MachineRouteIds)
        {
            // Wait for mover assignment
            await Workflow.WaitConditionAsync(() => _assignedMoverId.HasValue);
            _currentStatus = _currentStatus with { CurrentLocation = $"Mover-{_assignedMoverId}" };

            // Wait for load into machine
            await Workflow.WaitConditionAsync(() => _loadedMachineId.HasValue);
            _currentStatus = _currentStatus with { CurrentLocation = $"Machine-{machineId}" };

            // Wait for station processing + unload
            await Workflow.WaitConditionAsync(() => _unloadedMachineId.HasValue);

            var history = _currentStatus.ProcessHistory.ToList();
            history.Add($"{Workflow.UtcNow:HH:mm:ss.fff} - Machine-{machineId} complete, defect={_unloadedHasDefect}");

            _currentStatus = _currentStatus with
            {
                HasDefect = _currentStatus.HasDefect || _unloadedHasDefect,
                Status = _unloadedPartStatus ?? _currentStatus.Status,
                NextMachineIndex = _currentStatus.NextMachineIndex + 1,
                ProcessHistory = history.AsReadOnly(),
                CurrentLocation = $"Mover-{_assignedMoverId}"
            };

            // Reset for next machine
            _assignedMoverId = null;
            _loadedMachineId = null;
            _unloadedMachineId = null;
        }

        // Wait for exit signal
        await Workflow.WaitConditionAsync(() => _exited);

        var cycleTime = Workflow.UtcNow - _startedAt;
        var good = !_currentStatus.HasDefect;

        Workflow.Logger.LogInformation("Part {Tracking} exited. Good={Good}, CycleTime={Time:F2}s",
            input.TrackingNumber, good, cycleTime.TotalSeconds);

        return new PartLifecycleResult(input.TrackingNumber, good, _currentStatus.CompletedStations, cycleTime);
    }

    [WorkflowSignal("MoverAssigned")]
    public Task SignalMoverAssignedAsync(int moverId)
    {
        _assignedMoverId = moverId;
        return Task.CompletedTask;
    }

    [WorkflowSignal("LoadedToMachine")]
    public Task SignalLoadedToMachineAsync(int machineId)
    {
        _loadedMachineId = machineId;
        return Task.CompletedTask;
    }

    [WorkflowSignal("StationComplete")]
    public Task SignalStationCompleteAsync(int stationId, string stationName, bool hadDefect)
    {
        _stationHistory.Add(new StationEventRecord(
            _loadedMachineId ?? 0, stationId, stationName,
            Workflow.UtcNow.AddSeconds(-1), Workflow.UtcNow, hadDefect));
        _currentStatus = _currentStatus with { CompletedStations = _currentStatus.CompletedStations + 1 };
        return Task.CompletedTask;
    }

    [WorkflowSignal("UnloadedFromMachine")]
    public Task SignalUnloadedFromMachineAsync(int machineId, bool hasDefect, string partStatus)
    {
        _unloadedMachineId = machineId;
        _unloadedHasDefect = hasDefect;
        _unloadedPartStatus = partStatus;
        return Task.CompletedTask;
    }

    [WorkflowSignal("Exited")]
    public Task SignalExitedAsync(bool good)
    {
        _exited = true;
        _currentStatus = _currentStatus with
        {
            Status = good ? "Good" : "Bad",
            CurrentLocation = "Exit"
        };
        return Task.CompletedTask;
    }

    [WorkflowQuery("GetCurrentStatus")]
    public PartDto GetCurrentStatus() => _currentStatus;

    [WorkflowQuery("GetStationHistory")]
    public IReadOnlyList<StationEventRecord> GetStationHistory() => _stationHistory.AsReadOnly();
}
