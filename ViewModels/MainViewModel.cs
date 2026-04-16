using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using XTSPrimeMoverProject.Models;
using XTSPrimeMoverProject.Services;

namespace XTSPrimeMoverProject.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly XTSSimulationEngine _engine;
        private string _statusText;
        private bool _isRunning;
        private string _partHistoryTrackingNumber;
        private string _partHistoryStatus;
        private string _partHistorySummary;
        private string _selectedExportTable;
        private string _csvExportStatus;
        private double _simulationSpeed;

        public ObservableCollection<MoverViewModel> Movers { get; }
        public ObservableCollection<MachineViewModel> Machines { get; }
        public ObservableCollection<RobotViewModel> Robots { get; }
        public ObservableCollection<PartHistoryEventRecord> PartHistoryEvents { get; }
        public ObservableCollection<string> ExportTables { get; }
        public ObservableCollection<string> ExecutionLogs { get; }
        public ObservableCollection<WatchdogStatusItemViewModel> WatchdogStatuses { get; }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand InspectPartHistoryCommand { get; }
        public ICommand ExportCsvCommand { get; }

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

        public string PartHistoryTrackingNumber
        {
            get => _partHistoryTrackingNumber;
            set { _partHistoryTrackingNumber = value; OnPropertyChanged(); }
        }

        public string PartHistoryStatus
        {
            get => _partHistoryStatus;
            set { _partHistoryStatus = value; OnPropertyChanged(); }
        }

        public string PartHistorySummary
        {
            get => _partHistorySummary;
            set { _partHistorySummary = value; OnPropertyChanged(); }
        }

        public string SelectedExportTable
        {
            get => _selectedExportTable;
            set
            {
                _selectedExportTable = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string CsvExportStatus
        {
            get => _csvExportStatus;
            set { _csvExportStatus = value; OnPropertyChanged(); }
        }

        public int TotalPartsProduced => _engine.TotalPartsProduced;
        public int GoodPartsCount => _engine.GoodPartsCount;
        public int BadPartsCount => _engine.BadPartsCount;
        public double YieldPercentage => TotalPartsProduced > 0 ? (GoodPartsCount * 100.0 / TotalPartsProduced) : 0;
        public int PrimeMoverEnteredCount => _engine.PrimeMoverEnteredCount;
        public int PrimeMoverExitedCount => _engine.PrimeMoverExitedCount;
        public string DatabasePath => _engine.DatabasePath;
        public bool EntryZoneBlink => _engine.EntryZoneBlink;
        public bool ExitZoneBlink => _engine.ExitZoneBlink;

        public double SimulationSpeed
        {
            get => _simulationSpeed;
            set
            {
                double clamped = Math.Clamp(value, 0.1, 5.0);
                if (Math.Abs(_simulationSpeed - clamped) < 0.0001)
                {
                    return;
                }

                _simulationSpeed = clamped;
                _engine.SetSimulationSpeed(_simulationSpeed);
                OnPropertyChanged();
                OnPropertyChanged(nameof(SimulationSpeedText));
            }
        }

        public string SimulationSpeedText => $"Speed: {SimulationSpeed:F1}x";

        public MainViewModel()
        {
            _engine = new XTSSimulationEngine();
            _engine.StateChanged += OnEngineStateChanged;
            _engine.LogGenerated += OnEngineLogGenerated;

            _statusText = "SYSTEM STOPPED";
            _partHistoryTrackingNumber = string.Empty;
            _partHistoryStatus = "Enter a tracking number and click Inspect.";
            _partHistorySummary = "No part selected.";
            _selectedExportTable = string.Empty;
            _csvExportStatus = "CSV export idle.";
            _simulationSpeed = 1.0;

            Movers = new ObservableCollection<MoverViewModel>();
            Machines = new ObservableCollection<MachineViewModel>();
            Robots = new ObservableCollection<RobotViewModel>();
            PartHistoryEvents = new ObservableCollection<PartHistoryEventRecord>();
            ExportTables = new ObservableCollection<string>();
            ExecutionLogs = new ObservableCollection<string>();
            WatchdogStatuses = new ObservableCollection<WatchdogStatusItemViewModel>();

            StartCommand = new RelayCommand(Start, () => !IsRunning);
            StopCommand = new RelayCommand(Stop, () => IsRunning);
            ResetCommand = new RelayCommand(Reset);
            InspectPartHistoryCommand = new RelayCommand(InspectPartHistory);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => !string.IsNullOrWhiteSpace(SelectedExportTable));

            _engine.SetSimulationSpeed(_simulationSpeed);

            InitializeViewModels();
            LoadExportTables();
            RefreshWatchdogStatuses();
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

        private void LoadExportTables()
        {
            ExportTables.Clear();
            foreach (var table in _engine.GetExportableTables())
            {
                ExportTables.Add(table);
            }

            if (ExportTables.Count > 0)
            {
                SelectedExportTable = ExportTables[0];
            }
        }

        private void InspectPartHistory()
        {
            string tracking = PartHistoryTrackingNumber?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tracking))
            {
                PartHistoryStatus = "Tracking number is required.";
                PartHistorySummary = "No part selected.";
                PartHistoryEvents.Clear();
                return;
            }

            try
            {
                PartHistoryEvents.Clear();
                var events = _engine.GetPartHistory(tracking);
                foreach (var item in events)
                {
                    PartHistoryEvents.Add(item);
                }

                var summary = _engine.GetPartSummary(tracking);
                if (summary == null)
                {
                    PartHistoryStatus = $"No records found for {tracking}.";
                    PartHistorySummary = "Part not found in database.";
                    return;
                }

                PartHistorySummary = $"Part: {summary.TrackingNumber} | Final: {summary.FinalStatus} | Stations: {summary.CompletedStations}/{summary.TotalStations} | Entered: {summary.EnteredPrimeMoverAt} | Exited: {summary.ExitedPrimeMoverAt}";
                PartHistoryStatus = $"Loaded {PartHistoryEvents.Count} events.";
            }
            catch (Exception ex)
            {
                PartHistoryStatus = $"Error reading history: {ex.Message}";
            }
        }

        private void ExportCsv()
        {
            if (string.IsNullOrWhiteSpace(SelectedExportTable))
            {
                CsvExportStatus = "Select a table to export.";
                return;
            }

            try
            {
                string filePath = _engine.ExportTableToCsv(SelectedExportTable, _engine.GetDefaultExportDirectory());
                CsvExportStatus = $"Exported {SelectedExportTable} -> {filePath}";
            }
            catch (Exception ex)
            {
                CsvExportStatus = $"Export failed: {ex.Message}";
            }
        }

        private void OnEngineLogGenerated(object? sender, string message)
        {
            ExecutionLogs.Add(message);
            while (ExecutionLogs.Count > 400)
            {
                ExecutionLogs.RemoveAt(0);
            }
        }

        private void RefreshWatchdogStatuses()
        {
            var latest = _engine.GetWatchdogStatus();

            var snapshot = latest
                .Select(x => new WatchdogStatusItemViewModel
                {
                    Code = x.Code,
                    TriggerCount = x.TriggerCount,
                    LastTriggeredAt = x.LastTriggeredAt,
                    LastRecoveredObject = x.LastRecoveredObject,
                    LastMessage = x.LastMessage
                })
                .OrderByDescending(x => x.LastTriggeredAt)
                .ThenBy(x => x.Code)
                .ToList();

            WatchdogStatuses.Clear();
            foreach (var row in snapshot)
            {
                WatchdogStatuses.Add(row);
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

            RefreshWatchdogStatuses();

            UpdateStatus();
            OnPropertyChanged(nameof(TotalPartsProduced));
            OnPropertyChanged(nameof(GoodPartsCount));
            OnPropertyChanged(nameof(BadPartsCount));
            OnPropertyChanged(nameof(YieldPercentage));
            OnPropertyChanged(nameof(PrimeMoverEnteredCount));
            OnPropertyChanged(nameof(PrimeMoverExitedCount));
            OnPropertyChanged(nameof(DatabasePath));
            OnPropertyChanged(nameof(EntryZoneBlink));
            OnPropertyChanged(nameof(ExitZoneBlink));
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
            CsvExportStatus = "CSV export idle.";
            PartHistoryStatus = "Enter a tracking number and click Inspect.";
            PartHistorySummary = "No part selected.";
            PartHistoryEvents.Clear();
            ExecutionLogs.Clear();
            RefreshWatchdogStatuses();
            _engine.SetSimulationSpeed(_simulationSpeed);
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

    public class WatchdogStatusItemViewModel
    {
        public string Code { get; set; } = string.Empty;
        public int TriggerCount { get; set; }
        public DateTime LastTriggeredAt { get; set; }
        public string LastTriggeredAtText => LastTriggeredAt == default ? "-" : LastTriggeredAt.ToString("HH:mm:ss");
        public string LastRecoveredObject { get; set; } = "-";
        public string LastMessage { get; set; } = string.Empty;
    }
}
