using System.ComponentModel;
using System.Runtime.CompilerServices;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.ViewModels
{
    public class StationViewModel : INotifyPropertyChanged
    {
        private readonly Station _station;
        private readonly Machine _machine;

        public int StationIndex => _station.StationId;
        public int StationCount => _machine.Stations.Count;
        public string Name => _station.Name;
        public string Type => _station.Type.ToString();
        public string Status => _station.Status.ToString();
        public bool HasPart => _station.CurrentPart != null;
        public bool IsCurrentIndex => _machine.CurrentStationIndex == _station.StationId;
        public double Progress => _station.ProcessTime > 0 ? (_station.ElapsedTime / _station.ProcessTime) * 100.0 : 0.0;
        public string PartId => _station.CurrentPart == null ? "-" : _station.CurrentPart.TrackingNumber;

        public double ElapsedSeconds => _station.ElapsedTime;
        public double ProcessTimeSeconds => _station.ProcessTime;
        public double TimeoutThresholdSeconds => _station.ProcessTime * 2.0;
        public string EtPtText => $"ET/PT: {ElapsedSeconds:F2}/{ProcessTimeSeconds:F2}s";
        public string TimeoutText => $"Timeout@{TimeoutThresholdSeconds:F2}s";
        public bool IsTimeoutExceeded => _station.Status == StationStatus.Processing && ElapsedSeconds > TimeoutThresholdSeconds;

        public StationViewModel(Station station, Machine machine)
        {
            _station = station;
            _machine = machine;
        }

        public void Update()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(HasPart));
            OnPropertyChanged(nameof(IsCurrentIndex));
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(PartId));
            OnPropertyChanged(nameof(ElapsedSeconds));
            OnPropertyChanged(nameof(ProcessTimeSeconds));
            OnPropertyChanged(nameof(TimeoutThresholdSeconds));
            OnPropertyChanged(nameof(EtPtText));
            OnPropertyChanged(nameof(TimeoutText));
            OnPropertyChanged(nameof(IsTimeoutExceeded));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
