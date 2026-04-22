using System.ComponentModel;
using System.Runtime.CompilerServices;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.ViewModels
{
    public class RobotViewModel : INotifyPropertyChanged
    {
        private readonly Robot _robot;

        public int RobotId => _robot.RobotId;
        public string Name => _robot.Name;
        public string State => _robot.State.ToString();
        public bool HasPart => _robot.HeldPart != null;
        public int AssignedMachine => _robot.AssignedMachineId;
        public double Progress => (_robot.ActionProgress / _robot.ActionTime) * 100;
        public string HeldTrackingNumber => _robot.HeldPart?.TrackingNumber ?? "-";

        public bool IsTransferActive => _robot.State != RobotState.Idle && _robot.State != RobotState.WaitingForMover && _robot.State != RobotState.WaitingForMachine;

        public string TransferLane => _robot.State switch
        {
            RobotState.PickingFromMover or RobotState.MovingToMachine or RobotState.PlacingInMachine => "Prime Mover → Machine",
            RobotState.PickingFromMachine or RobotState.MovingToMover or RobotState.PlacingOnMover => "Machine → Prime Mover",
            RobotState.WaitingForMover => "Waiting at Prime Mover",
            RobotState.WaitingForMachine => "Waiting at Machine",
            _ => "Idle"
        };

        public string TransferStage => _robot.State switch
        {
            RobotState.PickingFromMover => "Pick from mover",
            RobotState.MovingToMachine => "Travel to machine",
            RobotState.PlacingInMachine => "Place into machine",
            RobotState.PickingFromMachine => "Pick from machine",
            RobotState.MovingToMover => "Travel to mover",
            RobotState.PlacingOnMover => "Place on mover",
            RobotState.WaitingForMover => "Awaiting loaded mover",
            RobotState.WaitingForMachine => "Awaiting machine completion",
            _ => "Standby"
        };

        public string TransferProgressText => IsTransferActive
            ? $"Transfer progress: {Progress:F0}%"
            : "Transfer progress: idle";

        public string TransferBadgeText => IsTransferActive ? "ACTIVE TRANSFER" : "IDLE";
        public string TransferBadgeColor => IsTransferActive ? "#00C875" : "#5A5A5E";

        public RobotViewModel(Robot robot)
        {
            _robot = robot;
        }

        public void Update()
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(HasPart));
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(HeldTrackingNumber));
            OnPropertyChanged(nameof(IsTransferActive));
            OnPropertyChanged(nameof(TransferLane));
            OnPropertyChanged(nameof(TransferStage));
            OnPropertyChanged(nameof(TransferProgressText));
            OnPropertyChanged(nameof(TransferBadgeText));
            OnPropertyChanged(nameof(TransferBadgeColor));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
