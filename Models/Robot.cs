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
        public Part HeldPart { get; set; }
        public int AssignedMachineId { get; set; }
        public double ActionProgress { get; set; }
        public double ActionTime { get; set; }

        public Robot(int id, int machineId)
        {
            RobotId = id;
            Name = $"Robot-{id}";
            AssignedMachineId = machineId;
            State = RobotState.Idle;
            HeldPart = null;
            ActionProgress = 0;
            ActionTime = 1.5;
        }

        public void Update(double deltaTime)
        {
            if (State != RobotState.Idle && State != RobotState.WaitingForMover && State != RobotState.WaitingForMachine)
            {
                ActionProgress += deltaTime;
                if (ActionProgress >= ActionTime)
                {
                    CompleteCurrentAction();
                }
            }
        }

        private void CompleteCurrentAction()
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
                    HeldPart = null;
                    State = RobotState.Idle;
                    break;
                case RobotState.PickingFromMachine:
                    State = RobotState.MovingToMover;
                    break;
                case RobotState.MovingToMover:
                    State = RobotState.PlacingOnMover;
                    break;
                case RobotState.PlacingOnMover:
                    HeldPart = null;
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
    }
}
