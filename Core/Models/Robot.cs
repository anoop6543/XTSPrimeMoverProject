using System;

namespace XTSPrimeMoverProject.Models
{
    public enum RobotState
    {
        Idle,
        WaitingForMover,
        PickingFromMover,
        MovingToMachine,
        PlacingInMachine,
        WaitingForMachine,
        PickingFromMachine,
        MovingToMover,
        PlacingOnMover
    }

    public class Robot
    {
        public int RobotId { get; set; }
        public string Name { get; set; }
        public RobotState State { get; set; }
        /// <summary>Gripper A.</summary>
        public Part? HeldPart { get; set; }

        /// <summary>Gripper B of the dual gripper: holds the finished part during a raw/finished swap at the mover.</summary>
        public Part? SecondaryHeldPart { get; set; }

        /// <summary>True while the robot waits at the dock with a finished part, ready for the next mover.</summary>
        public bool IsStagedAtDock { get; set; }
        public int AssignedMachineId { get; set; }
        public double ActionProgress { get; set; }
        public double ActionTime { get; set; }

        /// <summary>Nominal time per transfer step; ActionTime = BaseActionTime x gripper health factor.</summary>
        public double BaseActionTime { get; set; }

        public Robot(int id, int machineId)
        {
            RobotId = id;
            Name = $"Robot-{id}";
            AssignedMachineId = machineId;
            State = RobotState.Idle;
            HeldPart = null;
            ActionProgress = 0;
            BaseActionTime = 0.8;
            ActionTime = BaseActionTime;
        }

        public void Update(double deltaTime)
        {
            if (State != RobotState.Idle)
            {
                // Progress saturates at the step time: a robot waiting on an interlock shows a frozen
                // signature, which is what the robot watchdog looks for.
                ActionProgress = Math.Min(ActionTime, ActionProgress + deltaTime);
            }
        }

        public bool IsStepComplete()
        {
            return State != RobotState.Idle && ActionProgress >= ActionTime;
        }

        public void AdvanceState()
        {
            ActionProgress = 0;

            switch (State)
            {
                case RobotState.PickingFromMover:
                    State = RobotState.MovingToMachine;
                    break;
                case RobotState.MovingToMachine:
                    State = RobotState.PlacingInMachine;
                    break;
                case RobotState.PlacingInMachine:
                    State = RobotState.Idle;
                    break;
                case RobotState.PickingFromMachine:
                    State = RobotState.MovingToMover;
                    break;
                case RobotState.MovingToMover:
                    State = RobotState.PlacingOnMover;
                    break;
                case RobotState.PlacingOnMover:
                    State = RobotState.Idle;
                    break;
            }
        }

        public void StartPickFromMover(Part part)
        {
            HeldPart = part;
            State = RobotState.PickingFromMover;
            ActionProgress = 0;
        }

        public void StartPickFromMachine(Part part)
        {
            HeldPart = part;
            State = RobotState.PickingFromMachine;
            ActionProgress = 0;
        }

        /// <summary>Explicit transition used by the cell controller.</summary>
        public void TransitionTo(RobotState state)
        {
            State = state;
            ActionProgress = 0;
        }

        public void ReleaseHeldPart()
        {
            HeldPart = null;
        }
    }
}
