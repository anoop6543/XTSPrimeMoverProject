using Temporalio.Workflows;
using XtsContracts.Dtos;

namespace XtsContracts.Workflows;

/// <summary>
/// Single long-running master workflow owning the XTS oval track.
/// Manages mover queue, routing, and coordinates with machine workflows.
/// </summary>
[Workflow]
public interface IXTSPrimeMoverWorkflow
{
    [WorkflowRun]
    Task RunAsync(PrimeMoverInput input);

    [WorkflowSignal("StartProduction")]
    Task SignalStartProductionAsync();

    [WorkflowSignal("StopProduction")]
    Task SignalStopProductionAsync();

    [WorkflowSignal("SetSpeed")]
    Task SignalSetSpeedAsync(double speedFactor);

    [WorkflowSignal("MachinePartReady")]
    Task SignalMachinePartReadyAsync(PartReadyFromMachineSignal signal);

    [WorkflowSignal("MachineFaulted")]
    Task SignalMachineFaultedAsync(int machineId, string faultMessage);

    [WorkflowSignal("Reset")]
    Task SignalResetAsync();

    [WorkflowQuery("GetSystemStatus")]
    SystemStatusDto GetSystemStatus();

    [WorkflowQuery("GetMoverPositions")]
    IReadOnlyList<MoverDto> GetMoverPositions();
}

public record PrimeMoverInput(
    int MoverCount,
    IReadOnlyList<int> MachineIds,
    double SimulationSpeedFactor
);

public record PartReadyFromMachineSignal(
    int MachineId,
    string PartTrackingNumber,
    bool HasDefect,
    string PartStatus
);
