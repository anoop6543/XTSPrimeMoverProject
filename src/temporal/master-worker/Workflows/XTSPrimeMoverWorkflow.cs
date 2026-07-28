using Temporalio.Workflows;
using XtsContracts.Dtos;
using XtsContracts.Workflows;
using MasterWorker.Activities;

namespace MasterWorker.Workflows;

[Workflow]
public class XTSPrimeMoverWorkflow : IXTSPrimeMoverWorkflow
{
    // Mutable workflow state — safe because Temporal guarantees single-threaded execution
    private readonly List<MoverDto> _movers = new();
    private readonly List<MachineDto> _machines = new();
    private readonly List<RobotDto> _robots = new();
    private bool _running;
    private double _speedFactor = 1.0;
    private int _totalParts;
    private int _goodParts;
    private int _badParts;
    private int _entered;
    private bool _shouldStop;

    // Signals queue (CAS-safe via Temporal's single-threaded model)
    private readonly Queue<PartReadyFromMachineSignal> _pendingMachineReadySignals = new();
    private readonly Dictionary<int, string> _machineFaults = new();

    [WorkflowRun]
    public async Task RunAsync(PrimeMoverInput input)
    {
        _speedFactor = input.SimulationSpeedFactor;

        // Initialize movers
        for (int i = 0; i < input.MoverCount; i++)
        {
            _movers.Add(new MoverDto(i, i * (360.0 / input.MoverCount), 30.0, "Idle", null, -1));
        }

        // Initialize machine stubs
        foreach (var machineId in input.MachineIds)
        {
            _machines.Add(new MachineDto(machineId, $"Machine-{machineId}", "Unknown", "Init", true, false, string.Empty, 0, 0, new List<StationDto>(), 0, false, 0));
        }

        Workflow.Logger.LogInformation("XTSPrimeMoverWorkflow started. Movers={Count}, Machines={Machines}",
            input.MoverCount, string.Join(",", input.MachineIds));

        // Main production loop — continues until shutdown signal
        while (!_shouldStop)
        {
            if (_running)
            {
                // Process any machine-ready signals
                while (_pendingMachineReadySignals.TryDequeue(out var readySignal))
                {
                    await Workflow.ExecuteActivityAsync(
                        (PrimeMoverActivities a) => a.AssignMoverForPickupAsync(readySignal.MachineId, readySignal.PartTrackingNumber),
                        new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
                }

                // Tick the track engine via activity (side-effecting simulation step)
                var tick = await Workflow.ExecuteActivityAsync(
                    (PrimeMoverActivities a) => a.TickTrackEngineAsync(_speedFactor),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(5) });

                if (tick.NewPartEntered)
                {
                    _entered++;
                    // Spawn a PartLifecycleWorkflow for this part
                    await Workflow.StartChildWorkflowAsync(
                        (PartLifecycleWorkflow w) => w.RunAsync(new PartLifecycleInput(
                            tick.NewPartId!.Value,
                            tick.NewPartTrackingNumber!,
                            tick.MachineRouteIds ?? new List<int>())),
                        new ChildWorkflowOptions
                        {
                            Id = $"part-{tick.NewPartTrackingNumber}",
                            TaskQueue = "xts-prime-mover-queue"
                        });
                }

                if (tick.PartExited)
                {
                    _totalParts++;
                    if (tick.ExitedPartGood) _goodParts++;
                    else _badParts++;
                }

                // Update mover states from tick result
                if (tick.UpdatedMovers != null)
                {
                    _movers.Clear();
                    _movers.AddRange(tick.UpdatedMovers);
                }
            }

            // Wait ~100ms scaled by speed before next tick
            await Workflow.DelayAsync(TimeSpan.FromMilliseconds(100));
        }

        Workflow.Logger.LogInformation("XTSPrimeMoverWorkflow stopped. TotalParts={Total}", _totalParts);
    }

    [WorkflowSignal("StartProduction")]
    public Task SignalStartProductionAsync()
    {
        _running = true;
        return Task.CompletedTask;
    }

    [WorkflowSignal("StopProduction")]
    public Task SignalStopProductionAsync()
    {
        _running = false;
        return Task.CompletedTask;
    }

    [WorkflowSignal("SetSpeed")]
    public Task SignalSetSpeedAsync(double speedFactor)
    {
        _speedFactor = speedFactor;
        return Task.CompletedTask;
    }

    [WorkflowSignal("MachinePartReady")]
    public Task SignalMachinePartReadyAsync(PartReadyFromMachineSignal signal)
    {
        _pendingMachineReadySignals.Enqueue(signal);
        return Task.CompletedTask;
    }

    [WorkflowSignal("MachineFaulted")]
    public Task SignalMachineFaultedAsync(int machineId, string faultMessage)
    {
        _machineFaults[machineId] = faultMessage;
        Workflow.Logger.LogWarning("Machine {MachineId} faulted: {Fault}", machineId, faultMessage);
        return Task.CompletedTask;
    }

    [WorkflowSignal("Shutdown")]
    public Task SignalShutdownAsync()
    {
        _shouldStop = true;
        _running = false;
        return Task.CompletedTask;
    }

    [WorkflowQuery("GetSystemStatus")]
    public SystemStatusDto GetSystemStatus() =>
        new SystemStatusDto(
            _running, _totalParts, _goodParts, _badParts, _entered,
            _goodParts + _badParts,
            _movers.AsReadOnly(), _machines.AsReadOnly(), _robots.AsReadOnly(),
            DateTime.UtcNow);

    [WorkflowQuery("GetMoverPositions")]
    public IReadOnlyList<MoverDto> GetMoverPositions() => _movers.AsReadOnly();
}
