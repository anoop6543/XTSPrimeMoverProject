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
        public string State => _mover.State.ToString();
        public bool HasPart => _mover.CurrentPart != null;
        public string PartStatus => _mover.CurrentPart?.Status.ToString() ?? "Empty";

        public MoverViewModel(Mover mover)
        {
            _mover = mover;
        }

        public void Update()
        {
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(HasPart));
            OnPropertyChanged(nameof(PartStatus));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
