using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.ViewModels
{
    public class MachineViewModel : INotifyPropertyChanged
    {
        private readonly Machine _machine;
        private readonly ObservableCollection<RobotViewModel> _allRobots;

        public int MachineId => _machine.MachineId;
        public string Name => _machine.Name;
        public string Type => _machine.Type.ToString();
        public string Status => _machine.IsOperational ? "Operational" : "Offline";
        public string CurrentStation => _machine.CurrentStationIndex >= 0 && _machine.CurrentStationIndex < _machine.Stations.Count
            ? _machine.Stations[_machine.CurrentStationIndex].Name
            : "Idle";
        public int CurrentStationIndex => _machine.CurrentStationIndex;
        public int StationCount => _machine.Stations.Count;
        public double Progress => _machine.CurrentStationIndex >= 0 && _machine.CurrentStationIndex < _machine.Stations.Count
            ? (_machine.Stations[_machine.CurrentStationIndex].ElapsedTime / _machine.Stations[_machine.CurrentStationIndex].ProcessTime) * 100
            : 0;
        public string CurrentPartId
        {
            get
            {
                if (_machine.CurrentStationIndex < 0 || _machine.CurrentStationIndex >= _machine.Stations.Count)
                {
                    return "-";
                }

                var station = _machine.Stations[_machine.CurrentStationIndex];
                return station.CurrentPart?.TrackingNumber ?? "-";
            }
        }
        public int PartsEnteredCount => _machine.PartsEnteredCount;
        public int PartsExitedCount => _machine.PartsExitedCount;
        public string SequencerState => _machine.SequencerState.ToString();
        public bool IsIndexing => _machine.IsIndexing;
        public bool FaultActive => _machine.FaultActive;
        public string FaultMessage => string.IsNullOrWhiteSpace(_machine.FaultMessage) ? "-" : _machine.FaultMessage;
        public double RotaryAngle => _machine.RotaryAngle;

        public string RuntimeAction => FaultActive
            ? "Faulted"
            : IsIndexing
                ? "Indexing to next station"
                : CurrentPartId != "-"
                    ? "Processing current station"
                    : "Waiting for incoming part";

        public string CurrentStationEtPt => _machine.CurrentStationIndex >= 0 && _machine.CurrentStationIndex < _machine.Stations.Count
            ? $"ET/PT: {_machine.Stations[_machine.CurrentStationIndex].ElapsedTime:F2}/{_machine.Stations[_machine.CurrentStationIndex].ProcessTime:F2}s"
            : "ET/PT: -";

        // Visual helpers for enhanced machine tabs
        public string TypeColor => _machine.Type switch
        {
            MachineType.LaserWelding      => "#FF6B35",
            MachineType.PrecisionAssembly => "#00C875",
            MachineType.QualityInspection => "#9CDCFE",
            MachineType.FunctionalTesting => "#FFD700",
            _                             => "#CCCCCC"
        };

        public string TypeIcon => _machine.Type switch
        {
            MachineType.LaserWelding      => "⚡",
            MachineType.PrecisionAssembly => "🔧",
            MachineType.QualityInspection => "🔍",
            MachineType.FunctionalTesting => "🧪",
            _                             => "⚙"
        };

        public string TypeDescription => _machine.Type switch
        {
            MachineType.LaserWelding      => "High-precision laser welding with pre-heat, weld and post-inspection cycle",
            MachineType.PrecisionAssembly => "Multi-step torque-controlled pick-place-drive-verify assembly",
            MachineType.QualityInspection => "Vision + dimensional + weight measurement inspection cell",
            MachineType.FunctionalTesting => "Power-on through stress-test full-function qualification cell",
            _                             => "Processing cell"
        };

        public string StatusBadgeText => FaultActive ? "FAULT"
            : IsIndexing   ? "INDEXING"
            : CurrentPartId != "-" ? "RUNNING"
            : SequencerState == "Run" ? "READY"
            : SequencerState;

        public string StatusBadgeColor => FaultActive ? "#D83B01"
            : IsIndexing   ? "#FFD700"
            : CurrentPartId != "-" ? "#00C875"
            : "#5A5A5E";

        public string EfficiencyText
        {
            get
            {
                int entered = _machine.PartsEnteredCount;
                if (entered == 0) return "—";
                double pct = (double)_machine.PartsExitedCount / entered * 100.0;
                return $"{_machine.PartsExitedCount}/{entered}  ({pct:F0}% yield)";
            }
        }

        public string TotalStationsText => $"{_machine.Stations.Count} stations";

        public string ActiveStationDescription
        {
            get
            {
                int idx = _machine.CurrentStationIndex;
                if (idx < 0 || idx >= _machine.Stations.Count) return "No active station";
                var s = _machine.Stations[idx];
                return $"[{idx + 1}/{_machine.Stations.Count}]  {s.Name}  –  {s.Type}";
            }
        }

        public bool HasFault => _machine.FaultActive;

        public RobotViewModel? AssignedRobot => _allRobots.FirstOrDefault(r => r.AssignedMachine == MachineId);
        public string AssignedRobotName => AssignedRobot?.Name ?? $"Robot-M{MachineId} (unmapped)";
        public string AssignedRobotState => AssignedRobot?.State ?? "Unavailable";
        public string AssignedRobotTransferLane => AssignedRobot?.TransferLane ?? "-";
        public string AssignedRobotTransferStage => AssignedRobot?.TransferStage ?? "-";
        public string AssignedRobotHeldPart => AssignedRobot?.HeldTrackingNumber ?? "-";
        public double AssignedRobotProgress => AssignedRobot?.Progress ?? 0.0;
        public bool IsRobotTransferActive => AssignedRobot?.IsTransferActive ?? false;
        public string RobotTransferBadgeText => IsRobotTransferActive ? "ROBOT TRANSFER ACTIVE" : "ROBOT IDLE";
        public string RobotTransferBadgeColor => IsRobotTransferActive ? "#00C875" : "#5A5A5E";

        public ObservableCollection<StationViewModel> Stations { get; }

        public MachineViewModel(Machine machine, ObservableCollection<RobotViewModel> allRobots)
        {
            _machine = machine;
            _allRobots = allRobots;
            Stations = new ObservableCollection<StationViewModel>();

            foreach (var station in _machine.Stations)
            {
                Stations.Add(new StationViewModel(station, _machine));
            }
        }

        public void Update()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(CurrentStation));
            OnPropertyChanged(nameof(CurrentStationIndex));
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(CurrentPartId));
            OnPropertyChanged(nameof(PartsEnteredCount));
            OnPropertyChanged(nameof(PartsExitedCount));
            OnPropertyChanged(nameof(SequencerState));
            OnPropertyChanged(nameof(IsIndexing));
            OnPropertyChanged(nameof(FaultActive));
            OnPropertyChanged(nameof(FaultMessage));
            OnPropertyChanged(nameof(RotaryAngle));
            OnPropertyChanged(nameof(RuntimeAction));
            OnPropertyChanged(nameof(CurrentStationEtPt));
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(StatusBadgeColor));
            OnPropertyChanged(nameof(EfficiencyText));
            OnPropertyChanged(nameof(ActiveStationDescription));
            OnPropertyChanged(nameof(HasFault));
            OnPropertyChanged(nameof(AssignedRobot));
            OnPropertyChanged(nameof(AssignedRobotName));
            OnPropertyChanged(nameof(AssignedRobotState));
            OnPropertyChanged(nameof(AssignedRobotTransferLane));
            OnPropertyChanged(nameof(AssignedRobotTransferStage));
            OnPropertyChanged(nameof(AssignedRobotHeldPart));
            OnPropertyChanged(nameof(AssignedRobotProgress));
            OnPropertyChanged(nameof(IsRobotTransferActive));
            OnPropertyChanged(nameof(RobotTransferBadgeText));
            OnPropertyChanged(nameof(RobotTransferBadgeColor));

            foreach (var station in Stations)
            {
                station.Update();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
