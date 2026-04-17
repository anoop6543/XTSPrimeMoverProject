using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
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
        private string _selectedDbTable;
        private string _dbTableStatus;
        private string _dbTableSearchText;
        private string _orchestrationStatus;
        private string _orchestrationValidationStatus;

        public ObservableCollection<MoverViewModel> Movers { get; }
        public ObservableCollection<MachineViewModel> Machines { get; }
        public ObservableCollection<RobotViewModel> Robots { get; }
        public ObservableCollection<PartHistoryEventRecord> PartHistoryEvents { get; }
        public ObservableCollection<string> ExportTables { get; }
        public ObservableCollection<string> ExecutionLogs { get; }
        public ObservableCollection<WatchdogStatusItemViewModel> WatchdogStatuses { get; }
        public ObservableCollection<string> DbTables { get; }
        public ObservableCollection<OrchestrationStepEditItem> OrchestrationSteps { get; }
        public ObservableCollection<SafetyGateStatusItemViewModel> SafetyGates { get; }
        private DataView _dbTableRowsView;

        public DataView DbTableRowsView
        {
            get => _dbTableRowsView;
            set { _dbTableRowsView = value; OnPropertyChanged(); }
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand InspectPartHistoryCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand RefreshDbTablesCommand { get; }
        public ICommand MoveOrchestrationStepUpCommand { get; }
        public ICommand MoveOrchestrationStepDownCommand { get; }
        public ICommand ApplyOrchestrationCommand { get; }
        public ICommand ReloadOrchestrationCommand { get; }
        public ICommand PreviewOrchestrationValidationCommand { get; }
        public ICommand RefreshSafetyGatesCommand { get; }

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

        public string SelectedDbTable
        {
            get => _selectedDbTable;
            set
            {
                if (_selectedDbTable == value)
                {
                    return;
                }

                _selectedDbTable = value;
                OnPropertyChanged();
                LoadSelectedDbTableRows();
            }
        }

        public string DbTableStatus
        {
            get => _dbTableStatus;
            set { _dbTableStatus = value; OnPropertyChanged(); }
        }

        public string OrchestrationStatus
        {
            get => _orchestrationStatus;
            set { _orchestrationStatus = value; OnPropertyChanged(); }
        }

        public string OrchestrationValidationStatus
        {
            get => _orchestrationValidationStatus;
            set { _orchestrationValidationStatus = value; OnPropertyChanged(); }
        }

        public string DbTableSearchText
        {
            get => _dbTableSearchText;
            set
            {
                string next = value ?? string.Empty;
                if (_dbTableSearchText == next)
                {
                    return;
                }

                _dbTableSearchText = next;
                OnPropertyChanged();
                ApplyDbTableFilter();
            }
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
            _selectedDbTable = string.Empty;
            _dbTableStatus = "Select a table to view rows.";
            _dbTableSearchText = string.Empty;
            _orchestrationStatus = "Edit sequence then apply (only when simulation is stopped).";
            _orchestrationValidationStatus = "Run preview validation to inspect rule violations before apply.";

            Movers = new ObservableCollection<MoverViewModel>();
            Machines = new ObservableCollection<MachineViewModel>();
            Robots = new ObservableCollection<RobotViewModel>();
            PartHistoryEvents = new ObservableCollection<PartHistoryEventRecord>();
            ExportTables = new ObservableCollection<string>();
            ExecutionLogs = new ObservableCollection<string>();
            WatchdogStatuses = new ObservableCollection<WatchdogStatusItemViewModel>();
            DbTables = new ObservableCollection<string>();
            OrchestrationSteps = new ObservableCollection<OrchestrationStepEditItem>();
            SafetyGates = new ObservableCollection<SafetyGateStatusItemViewModel>();
            DbTableRowsView = CreateEmptyDbTableView();

            StartCommand = new RelayCommand(Start, () => !IsRunning);
            StopCommand = new RelayCommand(Stop, () => IsRunning);
            ResetCommand = new RelayCommand(Reset);
            InspectPartHistoryCommand = new RelayCommand(InspectPartHistory);
            ExportCsvCommand = new RelayCommand(ExportCsv, () => !string.IsNullOrWhiteSpace(SelectedExportTable));
            RefreshDbTablesCommand = new RelayCommand(LoadDbTables);
            MoveOrchestrationStepUpCommand = new RelayCommand<object?>(MoveOrchestrationStepUp);
            MoveOrchestrationStepDownCommand = new RelayCommand<object?>(MoveOrchestrationStepDown);
            ApplyOrchestrationCommand = new RelayCommand(ApplyOrchestrationFromHmi);
            ReloadOrchestrationCommand = new RelayCommand(LoadOrchestrationSteps);
            PreviewOrchestrationValidationCommand = new RelayCommand(PreviewOrchestrationValidation);
            RefreshSafetyGatesCommand = new RelayCommand(RefreshSafetyGates);

            _engine.SetSimulationSpeed(_simulationSpeed);

            InitializeViewModels();
            LoadExportTables();
            LoadDbTables();
            LoadOrchestrationSteps();
            RefreshSafetyGates();
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

        private void LoadDbTables()
        {
            DbTables.Clear();
            foreach (var table in _engine.GetAllTables())
            {
                DbTables.Add(table);
            }

            if (DbTables.Count == 0)
            {
                SelectedDbTable = string.Empty;
                DbTableRowsView = CreateEmptyDbTableView();
                DbTableStatus = "No tables found in the runtime database.";
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedDbTable) || !DbTables.Contains(SelectedDbTable))
            {
                SelectedDbTable = DbTables[0];
            }
            else
            {
                LoadSelectedDbTableRows();
            }
        }

        private void LoadSelectedDbTableRows()
        {
            if (string.IsNullOrWhiteSpace(SelectedDbTable))
            {
                DbTableRowsView = CreateEmptyDbTableView();
                DbTableStatus = "Select a table to view rows.";
                return;
            }

            try
            {
                var columns = _engine.GetTableColumns(SelectedDbTable);
                int totalRows = _engine.GetTableRowCount(SelectedDbTable);
                var rows = _engine.GetTableRows(SelectedDbTable, 500);

                var table = new DataTable(SelectedDbTable);
                foreach (var col in columns)
                {
                    table.Columns.Add(col, typeof(string));
                }

                foreach (var row in rows)
                {
                    var dr = table.NewRow();
                    foreach (DataColumn col in table.Columns)
                    {
                        dr[col.ColumnName] = row.TryGetValue(col.ColumnName, out var value) ? value : string.Empty;
                    }

                    table.Rows.Add(dr);
                }

                DbTableRowsView = table.DefaultView;
                ApplyDbTableFilter();
                DbTableStatus = $"Loaded {table.Rows.Count} rows (of {totalRows}) from {SelectedDbTable}. Columns: {table.Columns.Count}.";
            }
            catch (Exception ex)
            {
                DbTableRowsView = CreateEmptyDbTableView();
                DbTableStatus = $"DB table validation/load failed: {ex.Message}";
            }
        }

        private static DataView CreateEmptyDbTableView()
        {
            var t = new DataTable("DbTableRows");
            t.Columns.Add("Info", typeof(string));
            return t.DefaultView;
        }

        private void ApplyDbTableFilter()
        {
            if (DbTableRowsView == null || DbTableRowsView.Table == null)
            {
                return;
            }

            string term = (DbTableSearchText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                DbTableRowsView.RowFilter = string.Empty;
                return;
            }

            string escaped = term.Replace("'", "''");
            var expressions = DbTableRowsView.Table.Columns
                .Cast<DataColumn>()
                .Select(c => $"Convert([{c.ColumnName}], 'System.String') LIKE '%{escaped}%'");

            DbTableRowsView.RowFilter = string.Join(" OR ", expressions);
        }

        private void LoadOrchestrationSteps()
        {
            OrchestrationSteps.Clear();
            foreach (var step in _engine.GetOrchestrationSteps().OrderBy(s => s.Order))
            {
                OrchestrationSteps.Add(new OrchestrationStepEditItem
                {
                    Order = step.Order,
                    MachineId = step.MachineId,
                    MachineName = step.MachineName,
                    OutputStatus = step.OutputStatus.ToString()
                });
            }

            OrchestrationStatus = OrchestrationSteps.Count == 0
                ? "No orchestration steps available."
                : $"Loaded {OrchestrationSteps.Count} steps. Edit order/output then apply when simulation is stopped.";
            RefreshSafetyGates();
        }

        private void MoveOrchestrationStepUp(object? parameter)
        {
            if (parameter is not OrchestrationStepEditItem item)
            {
                return;
            }

            int index = OrchestrationSteps.IndexOf(item);
            if (index <= 0)
            {
                return;
            }

            OrchestrationSteps.Move(index, index - 1);
            ReindexOrchestrationSteps();
        }

        private void MoveOrchestrationStepDown(object? parameter)
        {
            if (parameter is not OrchestrationStepEditItem item)
            {
                return;
            }

            int index = OrchestrationSteps.IndexOf(item);
            if (index < 0 || index >= OrchestrationSteps.Count - 1)
            {
                return;
            }

            OrchestrationSteps.Move(index, index + 1);
            ReindexOrchestrationSteps();
        }

        private void ReindexOrchestrationSteps()
        {
            for (int i = 0; i < OrchestrationSteps.Count; i++)
            {
                OrchestrationSteps[i].Order = i;
            }
        }

        private void ApplyOrchestrationFromHmi()
        {
            var stepDefs = BuildStepDefinitionsFromEditor();
            if (_engine.TryApplyOrchestration(stepDefs, out var message))
            {
                OrchestrationStatus = message;
                OrchestrationValidationStatus = "Apply successful.";
                LoadOrchestrationSteps();
            }
            else
            {
                OrchestrationStatus = message;
            }

            RefreshSafetyGates();
        }

        private void PreviewOrchestrationValidation()
        {
            var stepDefs = BuildStepDefinitionsFromEditor();
            var errors = _engine.PreviewOrchestrationValidation(stepDefs);
            if (errors.Count == 0)
            {
                OrchestrationValidationStatus = "Validation OK: no rule violations.";
            }
            else
            {
                OrchestrationValidationStatus = "Validation errors: " + string.Join(" | ", errors);
            }

            RefreshSafetyGates();
        }

        private List<OrchestrationStepDefinition> BuildStepDefinitionsFromEditor()
        {
            return OrchestrationSteps
                .OrderBy(s => s.Order)
                .Select(s => new OrchestrationStepDefinition
                {
                    MachineId = s.MachineId,
                    OutputStatus = Enum.TryParse<PartStatus>(s.OutputStatus, out var parsed) ? parsed : PartStatus.InProcess
                })
                .ToList();
        }

        private void RefreshSafetyGates()
        {
            SafetyGates.Clear();
            foreach (var gate in _engine.GetOrchestrationSafetyGateStatuses())
            {
                SafetyGates.Add(new SafetyGateStatusItemViewModel
                {
                    GateName = gate.GateName,
                    IsPassing = gate.IsPassing,
                    Detail = gate.Detail
                });
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
            LoadDbTables();
            LoadOrchestrationSteps();
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

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool>? _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (parameter is T typed)
            {
                return _canExecute?.Invoke(typed) ?? true;
            }

            if (parameter == null && default(T) == null)
            {
                return _canExecute?.Invoke((T)parameter!) ?? true;
            }

            return false;
        }

        public void Execute(object? parameter)
        {
            if (parameter is T typed)
            {
                _execute(typed);
                return;
            }

            if (parameter == null && default(T) == null)
            {
                _execute((T)parameter!);
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public class OrchestrationStepEditItem : INotifyPropertyChanged
    {
        private int _order;
        private string _outputStatus = string.Empty;

        public int Order
        {
            get => _order;
            set { _order = value; OnPropertyChanged(); OnPropertyChanged(nameof(OrderText)); }
        }

        public string OrderText => $"Step {Order + 1}";
        public int MachineId { get; set; }
        public string MachineName { get; set; } = string.Empty;

        public string OutputStatus
        {
            get => _outputStatus;
            set { _outputStatus = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SafetyGateStatusItemViewModel
    {
        public string GateName { get; set; } = string.Empty;
        public bool IsPassing { get; set; }
        public string Detail { get; set; } = string.Empty;
        public string GateColor => IsPassing ? "#4CAF50" : "#F44336";
        public string GateStateText => IsPassing ? "PASS" : "BLOCK";
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
