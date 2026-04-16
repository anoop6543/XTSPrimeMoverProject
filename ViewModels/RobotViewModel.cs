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
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
