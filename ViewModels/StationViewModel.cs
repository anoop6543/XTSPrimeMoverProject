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

        // Visual helpers for enhanced machine tabs
        public string TypeIcon => _station.Type switch
        {
            StationType.Assembly    => "🔧",
            StationType.Welding     => "⚡",
            StationType.Inspection  => "🔍",
            StationType.Testing     => "🧪",
            StationType.Packaging   => "📦",
            _                       => "⚙"
        };

        public string TypeDescription => _station.Type switch
        {
            StationType.Assembly    => "Assembly Operation",
            StationType.Welding     => "Laser Welding",
            StationType.Inspection  => "Vision / Measurement",
            StationType.Testing     => "Functional Test",
            StationType.Packaging   => "Label & Pack",
            _                       => "Process Step"
        };

        public string TaskIcon => _station.Type switch
        {
            StationType.Assembly => "🛠",
            StationType.Welding => "🔥",
            StationType.Inspection => "📷",
            StationType.Testing => "📈",
            StationType.Packaging => "🏷",
            _ => "⚙"
        };

        public string TaskSummary => _station.Type switch
        {
            StationType.Assembly => "Pick-align-fasten components",
            StationType.Welding => "Fuse joint at controlled heat",
            StationType.Inspection => "Scan dimensions and surface",
            StationType.Testing => "Validate functional behavior",
            StationType.Packaging => "Label and prep outbound",
            _ => "Process operation"
        };

        public string CurrentTaskText => _station.Status switch
        {
            StationStatus.Processing => $"{TaskSummary} (active)",
            StationStatus.Complete => $"{TaskSummary} (completed)",
            StationStatus.Error => $"{TaskSummary} (faulted)",
            _ => $"{TaskSummary} (standby)"
        };

        public string StepIndicatorText => $"S{StationIndex + 1} / {StationCount}";

        public string EtPtCompactText => _station.Status == StationStatus.Processing
            ? $"ET {ElapsedSeconds:F1}s / PT {ProcessTimeSeconds:F1}s"
            : $"PT {ProcessTimeSeconds:F1}s";

        public string StationCardGlowColor => _station.Status switch
        {
            StationStatus.Processing => "#00C875",
            StationStatus.Complete => "#0078D4",
            StationStatus.Error => "#D83B01",
            _ => "#3F3F46"
        };

        public string StatusBadgeText => _station.Status switch
        {
            StationStatus.Processing => "RUNNING",
            StationStatus.Complete   => "DONE",
            StationStatus.Error      => "FAULT",
            _                        => "IDLE"
        };

        public string StatusBadgeColor => _station.Status switch
        {
            StationStatus.Processing => "#00C875",
            StationStatus.Complete   => "#0078D4",
            StationStatus.Error      => "#D83B01",
            _                        => "#5A5A5E"
        };

        public string ProgressBarColor => IsTimeoutExceeded ? "#D83B01"
            : _station.Status == StationStatus.Processing  ? "#00C875"
            : _station.Status == StationStatus.Complete    ? "#0078D4"
            : "#3F3F46";

        public string ProgressText => _station.Status == StationStatus.Processing
            ? $"{Progress:F0}%  ({ElapsedSeconds:F1}/{ProcessTimeSeconds:F1}s)"
            : _station.Status == StationStatus.Complete ? "✓ Complete"
            : _station.Status == StationStatus.Error    ? "⚠ Error"
            : "—";

        public bool IsActive => _station.Status == StationStatus.Processing;

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
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(StatusBadgeColor));
            OnPropertyChanged(nameof(ProgressBarColor));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(TaskIcon));
            OnPropertyChanged(nameof(TaskSummary));
            OnPropertyChanged(nameof(CurrentTaskText));
            OnPropertyChanged(nameof(StepIndicatorText));
            OnPropertyChanged(nameof(EtPtCompactText));
            OnPropertyChanged(nameof(StationCardGlowColor));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
