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
        public string CurrentPartId => _machine.CurrentStationIndex >= 0 && _machine.CurrentStationIndex < _machine.Stations.Count && _machine.Stations[_machine.CurrentStationIndex].CurrentPart != null
            ? _machine.Stations[_machine.CurrentStationIndex].CurrentPart.TrackingNumber
            : "-";
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

        public ObservableCollection<StationViewModel> Stations { get; }

        public MachineViewModel(Machine machine)
        {
            _machine = machine;
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

            foreach (var station in Stations)
            {
                station.Update();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
