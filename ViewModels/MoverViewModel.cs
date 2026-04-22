using System.ComponentModel;
using System.Runtime.CompilerServices;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.ViewModels
{
    public class MoverViewModel : INotifyPropertyChanged
    {
        private readonly Mover _mover;

        public int MoverId => _mover.MoverId;
        public double Position => _mover.Position;
        public string PositionDeg => $"{_mover.Position:F1}°";
        public string State => _mover.State.ToString();
        public bool HasPart => _mover.CurrentPart != null;
        public string PartStatus => _mover.CurrentPart?.Status.ToString() ?? "Empty";
        public string TrackingNumber => _mover.CurrentPart?.TrackingNumber ?? "-";
        public string ShortTrackingNumber => _mover.CurrentPart?.TrackingNumber?.Replace("TRK-", "") ?? "";
        public string NextMachine => _mover.CurrentPart == null
            ? "-"
            : (_mover.CurrentPart.NextMachineIndex >= 4 ? "Exit" : $"M{_mover.CurrentPart.NextMachineIndex}");
        public string CurrentLocation => _mover.CurrentPart?.CurrentLocation ?? "Track";
        public double CompletionPercent => _mover.CurrentPart?.GetCompletionPercent(18) ?? 0;
        public string TargetStation => _mover.TargetStation >= 0 ? $"M{_mover.TargetStation}" : "-";

        public string FlowMeaning => PartStatus switch
        {
            "BaseLayer" => "Loaded / waiting to enter first machine",
            "InProcess" => "Between machine operations",
            "Assembled" => "Completed assembly stage",
            "Tested" => "Completed inspection/testing stage",
            "Good" => "Final OK, moving to exit",
            "Bad" => "Rejected, moving to bad exit",
            _ => "Empty carrier"
        };

        public string WaitReason => _mover.State switch
        {
            MoverState.AtLoadStation => $"Waiting at machine interface ({TargetStation})",
            MoverState.AtUnloadStation => $"Waiting for machine output ({TargetStation})",
            MoverState.Loaded when HasPart => "Queued behind upstream mover",
            MoverState.Loaded => "Speed-limited queue spacing",
            MoverState.Moving => "In transit",
            _ => "Idle"
        };

        public MoverViewModel(Mover mover)
        {
            _mover = mover;
        }

        public void Update()
        {
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(PositionDeg));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(HasPart));
            OnPropertyChanged(nameof(PartStatus));
            OnPropertyChanged(nameof(TrackingNumber));
            OnPropertyChanged(nameof(ShortTrackingNumber));
            OnPropertyChanged(nameof(NextMachine));
            OnPropertyChanged(nameof(CurrentLocation));
            OnPropertyChanged(nameof(CompletionPercent));
            OnPropertyChanged(nameof(TargetStation));
            OnPropertyChanged(nameof(FlowMeaning));
            OnPropertyChanged(nameof(WaitReason));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
