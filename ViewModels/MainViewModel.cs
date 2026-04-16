using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using XTSPrimeMoverProject.Models;
using XTSPrimeMoverProject.Services;

namespace XTSPrimeMoverProject.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private XTSSimulationEngine _engine;
        private string _statusText;
        private bool _isRunning;

        public ObservableCollection<MoverViewModel> Movers { get; set; }
        public ObservableCollection<MachineViewModel> Machines { get; set; }
        public ObservableCollection<RobotViewModel> Robots { get; set; }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ResetCommand { get; }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); }
        }

        public int TotalPartsProduced => _engine.TotalPartsProduced;
        public int GoodPartsCount => _engine.GoodPartsCount;
        public int BadPartsCount => _engine.BadPartsCount;
        public double YieldPercentage => TotalPartsProduced > 0 ? (GoodPartsCount * 100.0 / TotalPartsProduced) : 0;

        public MainViewModel()
        {
            _engine = new XTSSimulationEngine();
            _engine.StateChanged += OnEngineStateChanged;

            Movers = new ObservableCollection<MoverViewModel>();
            Machines = new ObservableCollection<MachineViewModel>();
            Robots = new ObservableCollection<RobotViewModel>();

            StartCommand = new RelayCommand(Start, () => !IsRunning);
            StopCommand = new RelayCommand(Stop, () => IsRunning);
            ResetCommand = new RelayCommand(Reset);

            InitializeViewModels();
            UpdateStatus();
        }

        private void InitializeViewModels()
        {
            Movers.Clear();
            foreach (var mover in _engine.Movers)
            {
                Movers.Add(new MoverViewModel(mover));
            }

            Machines.Clear();
            foreach (var machine in _engine.Machines)
            {
                Machines.Add(new MachineViewModel(machine));
            }

            Robots.Clear();
            foreach (var robot in _engine.Robots)
            {
                Robots.Add(new RobotViewModel(robot));
            }
        }

        private void OnEngineStateChanged(object sender, EventArgs e)
        {
            foreach (var mvm in Movers)
            {
                mvm.Update();
            }

            foreach (var mvm in Machines)
            {
                mvm.Update();
            }

            foreach (var rvm in Robots)
            {
                rvm.Update();
            }

            UpdateStatus();
            OnPropertyChanged(nameof(TotalPartsProduced));
            OnPropertyChanged(nameof(GoodPartsCount));
            OnPropertyChanged(nameof(BadPartsCount));
            OnPropertyChanged(nameof(YieldPercentage));
        }

        private void Start()
        {
            _engine.Start();
            IsRunning = true;
            UpdateStatus();
        }

        private void Stop()
        {
            _engine.Stop();
            IsRunning = false;
            UpdateStatus();
        }

        private void Reset()
        {
            _engine.Reset();
            InitializeViewModels();
            IsRunning = false;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            StatusText = IsRunning ? "SYSTEM RUNNING" : "SYSTEM STOPPED";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
