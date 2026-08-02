using Temporalio.Workflows;
using XtsContracts.Workflows;
using MasterWorker.Activities;

namespace MasterWorker.Workflows;

/// <summary>
/// Short-lived child workflow for each robot pick-and-place cycle.
/// Replaces the robot state machine in XTSSimulationEngine.
/// </summary>
[Workflow]
public class RobotTransferWorkflow : IRobotTransferWorkflow
{
    [WorkflowRun]
    public async Task<RobotTransferResult> RunAsync(RobotTransferInput input)
    {
        var started = Workflow.UtcNow;
        Workflow.Logger.LogInformation("RobotTransfer: Robot={Robot} {Direction} Part={Part}",
            input.RobotId, input.Direction, input.PartTrackingNumber);

        try
        {
            if (input.Direction == TransferDirection.MoverToMachine)
            {
                // Step 1: Pick from mover
                await Workflow.ExecuteActivityAsync(
                    (PrimeMoverActivities a) => a.RobotPickFromMoverAsync(input.RobotId, input.MoverId, input.PartTrackingNumber),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

                // Step 2: Move to machine
                await Workflow.ExecuteActivityAsync(
                    (PrimeMoverActivities a) => a.RobotMoveToMachineAsync(input.RobotId, input.MachineId),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

                // Step 3: Place in machine
                await Workflow.ExecuteActivityAsync(
                    (PrimeMoverActivities a) => a.RobotPlaceInMachineAsync(input.RobotId, input.MachineId, input.PartTrackingNumber),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
            }
            else
            {
                // Step 1: Pick from machine
                await Workflow.ExecuteActivityAsync(
                    (PrimeMoverActivities a) => a.RobotPickFromMachineAsync(input.RobotId, input.MachineId, input.PartTrackingNumber),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

                // Step 2: Move to mover
                await Workflow.ExecuteActivityAsync(
                    (PrimeMoverActivities a) => a.RobotMoveToMoverAsync(input.RobotId, input.MoverId),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

                // Step 3: Place on mover
                await Workflow.ExecuteActivityAsync(
                    (PrimeMoverActivities a) => a.RobotPlaceOnMoverAsync(input.RobotId, input.MoverId, input.PartTrackingNumber),
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
            }

            return new RobotTransferResult(true, null, Workflow.UtcNow - started);
        }
        catch (Exception ex)
        {
            Workflow.Logger.LogError("RobotTransfer failed: Robot={Robot}, Error={Error}", input.RobotId, ex.Message);
            return new RobotTransferResult(false, ex.Message, Workflow.UtcNow - started);
        }
    }
}
