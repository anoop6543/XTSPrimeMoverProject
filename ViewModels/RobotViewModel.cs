using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
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

        public bool IsTransferPathVisible => _robot.State != RobotState.Idle;

        public string TransferPathColor => _robot.State switch
        {
            RobotState.MovingToMachine or RobotState.PickingFromMover or RobotState.PlacingInMachine => "#00D4FF",
            RobotState.MovingToMover or RobotState.PickingFromMachine or RobotState.PlacingOnMover => "#FFD700",
            RobotState.WaitingForMover or RobotState.WaitingForMachine => "#8A8A8A",
            _ => "#5A5A5E"
        };

        public double TransferFromX
        {
            get
            {
                ResolveDirectionalTransferPoints(out double fromX, out _, out _, out _);
                return fromX;
            }
        }

        public double TransferFromY
        {
            get
            {
                ResolveDirectionalTransferPoints(out _, out double fromY, out _, out _);
                return fromY;
            }
        }

        public double TransferToX
        {
            get
            {
                ResolveDirectionalTransferPoints(out _, out _, out double toX, out _);
                return toX;
            }
        }

        public double TransferToY
        {
            get
            {
                ResolveDirectionalTransferPoints(out _, out _, out _, out double toY);
                return toY;
            }
        }

        public PointCollection TransferArrowPoints
        {
            get
            {
                double fromX = TransferFromX;
                double fromY = TransferFromY;
                double toX = TransferToX;
                double toY = TransferToY;

                double dx = toX - fromX;
                double dy = toY - fromY;
                double len = Math.Sqrt((dx * dx) + (dy * dy));
                if (len < 0.0001)
                {
                    return new PointCollection();
                }

                double ux = dx / len;
                double uy = dy / len;

                const double arrowLength = 12.0;
                const double arrowWidth = 6.0;

                Point tip = new(toX, toY);
                Point baseCenter = new(toX - (ux * arrowLength), toY - (uy * arrowLength));

                // Perpendicular vector
                double px = -uy;
                double py = ux;

                Point left = new(baseCenter.X + (px * arrowWidth), baseCenter.Y + (py * arrowWidth));
                Point right = new(baseCenter.X - (px * arrowWidth), baseCenter.Y - (py * arrowWidth));

                return new PointCollection { tip, left, right };
            }
        }

        public double CanvasX
        {
            get
            {
                ResolveTransferPoints(out double moverX, out _, out double machineX, out _);
                return ResolveRobotPosition(moverX, machineX) - 14;
            }
        }

        public double CanvasY
        {
            get
            {
                ResolveTransferPoints(out _, out double moverY, out _, out double machineY);
                return ResolveRobotPosition(moverY, machineY) - 14;
            }
        }

        public string CanvasTransferLabel => TransferLane;
        public bool ShowTransferLabel => IsTransferActive;

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
            OnPropertyChanged(nameof(CanvasX));
            OnPropertyChanged(nameof(CanvasY));
            OnPropertyChanged(nameof(CanvasTransferLabel));
            OnPropertyChanged(nameof(ShowTransferLabel));
            OnPropertyChanged(nameof(IsTransferPathVisible));
            OnPropertyChanged(nameof(TransferPathColor));
            OnPropertyChanged(nameof(TransferFromX));
            OnPropertyChanged(nameof(TransferFromY));
            OnPropertyChanged(nameof(TransferToX));
            OnPropertyChanged(nameof(TransferToY));
            OnPropertyChanged(nameof(TransferArrowPoints));
        }

        private double ResolveRobotPosition(double moverPoint, double machinePoint)
        {
            double t = Math.Clamp(Progress / 100.0, 0.0, 1.0);

            return _robot.State switch
            {
                RobotState.MovingToMachine => Lerp(moverPoint, machinePoint, t),
                RobotState.MovingToMover => Lerp(machinePoint, moverPoint, t),
                RobotState.PickingFromMover or RobotState.PlacingOnMover or RobotState.WaitingForMover => moverPoint,
                RobotState.PickingFromMachine or RobotState.PlacingInMachine or RobotState.WaitingForMachine => machinePoint,
                _ => machinePoint
            };
        }

        private void ResolveTransferPoints(out double moverX, out double moverY, out double machineX, out double machineY)
        {
            switch (_robot.AssignedMachineId)
            {
                case 0:
                    moverX = 730; moverY = 205;
                    machineX = 890; machineY = 120;
                    break;
                case 1:
                    moverX = 730; moverY = 395;
                    machineX = 860; machineY = 450;
                    break;
                case 2:
                    moverX = 250; moverY = 395;
                    machineX = 120; machineY = 450;
                    break;
                case 3:
                default:
                    moverX = 250; moverY = 205;
                    machineX = 90; machineY = 120;
                    break;
            }
        }

        private void ResolveDirectionalTransferPoints(out double fromX, out double fromY, out double toX, out double toY)
        {
            ResolveTransferPoints(out double moverX, out double moverY, out double machineX, out double machineY);

            bool machineToMover = _robot.State is RobotState.PickingFromMachine
                or RobotState.MovingToMover
                or RobotState.PlacingOnMover
                or RobotState.WaitingForMachine;

            if (machineToMover)
            {
                fromX = machineX;
                fromY = machineY;
                toX = moverX;
                toY = moverY;
                return;
            }

            fromX = moverX;
            fromY = moverY;
            toX = machineX;
            toY = machineY;
        }

        private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
