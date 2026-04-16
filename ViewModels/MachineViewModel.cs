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
        public int StationCount => _machine.Stations.Count;
        public double Progress => _machine.CurrentStationIndex >= 0 && _machine.CurrentStationIndex < _machine.Stations.Count
            ? (_machine.Stations[_machine.CurrentStationIndex].ElapsedTime / _machine.Stations[_machine.CurrentStationIndex].ProcessTime) * 100
            : 0;

        public MachineViewModel(Machine machine)
        {
            _machine = machine;
        }

        public void Update()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(CurrentStation));
            OnPropertyChanged(nameof(Progress));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
