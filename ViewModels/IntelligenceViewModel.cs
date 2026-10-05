using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using XTSPrimeMoverProject.Services;
using XTSPrimeMoverProject.Services.Intelligence;

namespace XTSPrimeMoverProject.ViewModels
{
    /// <summary>
    /// Projection of the AI intelligence snapshot for the "AI Command Center": line KPIs, machine health
    /// cards with sensor trends and SPC, copilot insights, autopilot decision log, what-if fault injection
    /// and the copilot chat (offline reasoner, optionally Claude).
    /// </summary>
    public sealed class IntelligenceViewModel : INotifyPropertyChanged
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);

        private readonly IMachineGatewayService _machine;
        private readonly ErrorHandlingService _errorHandler = ErrorHandlingService.Instance;
        private readonly Stopwatch _refreshClock = Stopwatch.StartNew();
        private ClaudeCopilotClient? _claude;
        private long _lastVersion = -1;
        private string _insightSignature = string.Empty;
        private int _decisionCount;
        private IntelligenceSnapshot _snapshot = IntelligenceSnapshot.Empty;
        private string _copilotQuestion = "What is limiting output right now?";
        private string _copilotAnswer = "Ask about the bottleneck, asset health, quality/SPC, energy, OEE, WIP or a machine (M0–M3).";
        private string _copilotSource = "Offline copilot";
        private bool _isCopilotBusy;
        private bool _useClaude;
        private FaultScenarioOption _selectedFault;
        private string _whatIfStatus = "No fault injected.";
        private bool? _pendingAutopilot;
        private DateTime _pendingAutopilotSince;

        public IntelligenceViewModel(IMachineGatewayService machine)
        {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            FaultScenarios = new[]
            {
                new FaultScenarioOption(FaultScenario.LaserOpticsContamination, "M0 · Laser optics contamination"),
                new FaultScenarioOption(FaultScenario.SpindleBearingDefect, "M1 · Spindle bearing defect"),
                new FaultScenarioOption(FaultScenario.VisionLightingDrift, "M2 · Vision ring-light drift"),
                new FaultScenarioOption(FaultScenario.FixtureContactWear, "M3 · Test fixture pogo-pin wear"),
                new FaultScenarioOption(FaultScenario.RobotGripperLeak, "R1 · Robot vacuum gripper leak")
            };
            _selectedFault = FaultScenarios[0];
            _useClaude = ClaudeCopilotClient.HasEnvironmentCredentials;

            InjectFaultCommand = new RelayCommand(InjectFault);
            ClearFaultsCommand = new RelayCommand(ClearFaults);
            AskCopilotCommand = new RelayCommand(async () => await AskCopilotAsync(), () => !IsCopilotBusy);
            AskSuggestedCommand = new RelayCommand<object?>(async q => { if (q is string s) { CopilotQuestion = s; await AskCopilotAsync(); } });
            RequestMaintenanceCommand = new RelayCommand<object?>(RequestMaintenance);

            Refresh(force: true);
        }

        public ObservableCollection<MachineHealthViewModel> Machines { get; } = new();
        public ObservableCollection<CopilotInsightViewModel> Insights { get; } = new();
        public ObservableCollection<AutopilotDecisionViewModel> Decisions { get; } = new();
        public IReadOnlyList<FaultScenarioOption> FaultScenarios { get; }

        public IReadOnlyList<string> SuggestedQuestions { get; } = new[]
        {
            "What is the bottleneck?",
            "Which machine needs maintenance first?",
            "Why did the autopilot act?",
            "How is quality and Cpk?",
            "Energy per module?",
            "Explain the OEE losses"
        };

        public ICommand InjectFaultCommand { get; }
        public ICommand ClearFaultsCommand { get; }
        public ICommand AskCopilotCommand { get; }
        public ICommand AskSuggestedCommand { get; }
        public ICommand RequestMaintenanceCommand { get; }

        // ------------------------------------------------------------------ line KPIs

        public LineKpiState Line => _snapshot.Line;
        public double LineOee => Line.Oee;
        public double LineAvailability => Line.Availability;
        public double LinePerformance => Line.Performance;
        public double LineQuality => Line.Quality;
        public string ThroughputText => $"{Line.ThroughputPerMinute:F2}/min";
        public string ForecastText => $"{Line.ForecastPerHour:F0}/h forecast";
        public string LeadTimeText => Line.LeadTimeSeconds > 0 ? $"{Line.LeadTimeSeconds:F0} s" : "–";
        public string WipText => $"{Line.Wip} / {Line.WipCap}";
        public string LittlesLawText => Line.LittlesLawWip > 0 ? $"Little's law {Line.LittlesLawWip:F1}" : "Little's law –";
        public string PowerText => $"{Line.PowerKw:F2} kW";
        public string PowerSplitText => $"Track {Line.TrackKw:F2} · Machines {Line.MachinesKw:F2} · Robots {Line.RobotsKw:F2}";
        public string EnergyText => Line.WhPerGoodPart > 0 ? $"{Line.WhPerGoodPart:F0} Wh/module" : "– Wh/module";
        public string Co2Text => $"{Line.EnergyKwh:F3} kWh · {Line.Co2Kg * 1000:F0} g CO₂";
        public string YieldText => $"{Line.Yield:P1}";
        public string GoodBadText => $"{Line.GoodParts} good · {Line.BadParts} rejected";
        public string BottleneckText => Line.BottleneckMachineId < 0 ? "Constraint: learning…" : $"Constraint: {Line.BottleneckName} ({Line.BottleneckShare:P0}){(Line.BottleneckShifting ? " · shifting" : string.Empty)}";
        public string SimTimeText => $"t = {_snapshot.SimTimeSeconds:F0} s";
        public double[] ThroughputHistory => _snapshot.ThroughputHistory;
        public double[] PowerHistory => _snapshot.PowerHistory;
        public double[] OeeHistory => _snapshot.OeeHistory;
        public string ActiveFaultsText => _snapshot.ActiveFaults.Count == 0 ? "No active fault injection." : "Active: " + string.Join(" · ", _snapshot.ActiveFaults);

        // ------------------------------------------------------------------ autopilot + what-if

        public bool AutopilotEnabled
        {
            get
            {
                // Commands to a remote runtime arrive with latency: show the requested state until the
                // runtime confirms it (or 3 s pass), so the toggle does not snap back.
                if (_pendingAutopilot is bool pending)
                {
                    if (pending == _snapshot.AutopilotEnabled || DateTime.UtcNow - _pendingAutopilotSince > TimeSpan.FromSeconds(3))
                    {
                        _pendingAutopilot = null;
                    }
                    else
                    {
                        return pending;
                    }
                }

                return _snapshot.AutopilotEnabled;
            }
            set
            {
                if (value == AutopilotEnabled)
                {
                    return;
                }

                _pendingAutopilot = value;
                _pendingAutopilotSince = DateTime.UtcNow;
                _machine.SetAutopilotEnabled(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutopilotStatusText));
            }
        }

        public string AutopilotStatusText => AutopilotEnabled
            ? "ENGAGED – release (critical WIP) and predictive maintenance are AI-controlled"
            : "Manual – the AI advises, the operator decides";

        public FaultScenarioOption SelectedFault
        {
            get => _selectedFault;
            set { _selectedFault = value; OnPropertyChanged(); }
        }

        public string WhatIfStatus
        {
            get => _whatIfStatus;
            private set { _whatIfStatus = value; OnPropertyChanged(); }
        }

        // ------------------------------------------------------------------ copilot chat

        public string CopilotQuestion
        {
            get => _copilotQuestion;
            set { _copilotQuestion = value; OnPropertyChanged(); }
        }

        public string CopilotAnswer
        {
            get => _copilotAnswer;
            private set { _copilotAnswer = value; OnPropertyChanged(); }
        }

        public string CopilotSource
        {
            get => _copilotSource;
            private set { _copilotSource = value; OnPropertyChanged(); }
        }

        public bool IsCopilotBusy
        {
            get => _isCopilotBusy;
            private set
            {
                _isCopilotBusy = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool UseClaude
        {
            get => _useClaude;
            set { _useClaude = value; OnPropertyChanged(); OnPropertyChanged(nameof(ClaudeStatusText)); }
        }

        public string ClaudeStatusText => UseClaude
            ? (ClaudeCopilotClient.HasEnvironmentCredentials
                ? $"Claude ({ClaudeCopilotClient.ModelId}) – grounded on live line data"
                : $"Claude ({ClaudeCopilotClient.ModelId}) – no ANTHROPIC_API_KEY found; will try an `ant auth login` profile")
            : "Offline reasoning engine (no network, no API cost)";

        // ------------------------------------------------------------------ refresh

        /// <summary>Called on the UI thread after each engine tick; throttled to 4 Hz.</summary>
        public void Refresh(bool force = false)
        {
            if (!force && _refreshClock.Elapsed < RefreshInterval)
            {
                return;
            }

            _refreshClock.Restart();
            var snapshot = _machine.GetIntelligenceSnapshot();
            if (!force && snapshot.Version == _lastVersion)
            {
                return;
            }

            _lastVersion = snapshot.Version;
            _snapshot = snapshot;

            SyncMachines(snapshot);
            SyncInsights(snapshot);
            SyncDecisions(snapshot);

            OnPropertyChanged(string.Empty);
        }

        public void Reinitialize()
        {
            Machines.Clear();
            Insights.Clear();
            Decisions.Clear();
            _insightSignature = string.Empty;
            _decisionCount = 0;
            WhatIfStatus = "No fault injected.";
            Refresh(force: true);
        }

        private void SyncMachines(IntelligenceSnapshot snapshot)
        {
            if (Machines.Count != snapshot.Machines.Count)
            {
                Machines.Clear();
                foreach (var m in snapshot.Machines)
                {
                    Machines.Add(new MachineHealthViewModel(m.MachineId));
                }
            }

            for (int i = 0; i < snapshot.Machines.Count; i++)
            {
                var robot = snapshot.Robots.FirstOrDefault(r => r.MachineId == snapshot.Machines[i].MachineId);
                Machines[i].Update(snapshot.Machines[i], robot);
            }
        }

        private void SyncInsights(IntelligenceSnapshot snapshot)
        {
            string signature = string.Join("|", snapshot.Insights.Select(i => $"{i.Id}:{i.Severity}:{i.Title}"));
            if (signature == _insightSignature)
            {
                // Same findings: refresh evidence text in place (numbers move every second).
                for (int i = 0; i < Insights.Count && i < snapshot.Insights.Count; i++)
                {
                    Insights[i].Update(snapshot.Insights[i]);
                }

                return;
            }

            _insightSignature = signature;
            Insights.Clear();
            foreach (var insight in snapshot.Insights)
            {
                Insights.Add(new CopilotInsightViewModel(insight));
            }
        }

        private void SyncDecisions(IntelligenceSnapshot snapshot)
        {
            if (snapshot.Decisions.Count < _decisionCount)
            {
                Decisions.Clear();
                _decisionCount = 0;
            }

            foreach (var decision in snapshot.Decisions.Skip(_decisionCount))
            {
                Decisions.Insert(0, new AutopilotDecisionViewModel(decision));
            }

            _decisionCount = snapshot.Decisions.Count;
            while (Decisions.Count > 60)
            {
                Decisions.RemoveAt(Decisions.Count - 1);
            }
        }

        // ------------------------------------------------------------------ commands

        private void InjectFault()
        {
            try
            {
                WhatIfStatus = _machine.InjectFault(SelectedFault.Scenario);
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.ViewModel, "IntelligenceVM.InjectFault", ex);
                WhatIfStatus = $"Injection failed: {ex.Message}";
            }
        }

        private void ClearFaults()
        {
            _machine.ClearFaults();
            WhatIfStatus = "Fault injections cleared (wear remains until maintenance).";
        }

        private void RequestMaintenance(object? parameter)
        {
            if (parameter is not MachineHealthViewModel machine)
            {
                return;
            }

            bool accepted = _machine.RequestMaintenance(machine.MachineId);
            machine.ActionStatus = accepted ? "Maintenance requested – cell drains, then the technician starts." : "Already in maintenance.";
        }

        private async Task AskCopilotAsync()
        {
            string question = (CopilotQuestion ?? string.Empty).Trim();
            if (question.Length == 0 || IsCopilotBusy)
            {
                return;
            }

            IsCopilotBusy = true;
            try
            {
                if (UseClaude)
                {
                    CopilotSource = $"Asking Claude ({ClaudeCopilotClient.ModelId})…";
                    _claude ??= new ClaudeCopilotClient();
                    string context = _machine.GetCopilotContextJson();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                    var answer = await _claude.AskAsync(question, context, cts.Token);
                    if (!answer.IsError)
                    {
                        CopilotAnswer = answer.Text;
                        CopilotSource = answer.Source;
                        return;
                    }

                    CopilotAnswer = $"{answer.Text}\n\nOffline copilot:\n{_machine.AskCopilot(question)}";
                    CopilotSource = "Offline copilot (Claude unavailable)";
                    return;
                }

                CopilotAnswer = await Task.Run(() => _machine.AskCopilot(question));
                CopilotSource = "Offline copilot";
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.ViewModel, "IntelligenceVM.AskCopilot", ex);
                CopilotAnswer = $"Copilot error: {ex.Message}";
            }
            finally
            {
                IsCopilotBusy = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed record FaultScenarioOption(FaultScenario Scenario, string Label)
    {
        public override string ToString() => Label;
    }

    public sealed class MachineHealthViewModel : INotifyPropertyChanged
    {
        private MachineIntelligenceState? _state;
        private RobotIntelligenceState? _robot;
        private string _actionStatus = string.Empty;

        public MachineHealthViewModel(int machineId) => MachineId = machineId;

        public int MachineId { get; }
        public string Name => _state?.Name ?? $"M{MachineId}";
        public string Title => $"M{MachineId} · {Name}";
        public string FailureMode => _state?.FailureMode ?? "-";
        public string Activity => _state?.Activity.ToString() ?? "-";
        public double Health => _state?.HealthEstimate ?? 1;
        public string HealthText => $"{Health:P0}";
        public string TrueHealthText => _state == null ? "-" : $"twin ground truth {_state.TrueHealth:P0}";
        public string RulText => _state == null || _state.RulLearning ? "learning fingerprint…"
            : _state.RulSeconds.HasValue ? $"{LineIntelligenceHub.FormatDuration(_state.RulSeconds.Value)} ({_state.RulConfidence:P0})" : "no degradation trend";
        public double AnomalyScore => _state?.AnomalyScore ?? 0;
        public bool AnomalyFlag => _state?.AnomalyFlag ?? false;
        public string AnomalyText => AnomalyFlag ? $"ANOMALY · {_state!.AnomalyChannel}" : $"normal ({AnomalyScore:P0})";
        public string AnomalyColor => AnomalyFlag ? "#FF4D6D" : AnomalyScore > 0.5 ? "#FBBF24" : "#36D399";
        public string TemperatureText => $"{_state?.TemperatureC ?? 0:F1} °C";
        public string VibrationText => $"{_state?.VibrationRms ?? 0:F2} mm/s · ISO {_state?.VibrationZone ?? "A"}";
        public string PrimaryText => _state == null ? "-" : $"{_state.PrimaryValue:0.##} {_state.PrimaryUnit}";
        public string PrimaryName => _state?.PrimaryName ?? "-";
        public double[] TemperatureHistory => _state?.TemperatureHistory ?? Array.Empty<double>();
        public double[] VibrationHistory => _state?.VibrationHistory ?? Array.Empty<double>();
        public double[] PrimaryHistory => _state?.PrimaryHistory ?? Array.Empty<double>();
        public double[] HealthHistory => _state?.HealthHistory ?? Array.Empty<double>();
        public double PrimaryHealthy => _state?.PrimaryHealthy ?? double.NaN;
        public double PrimaryAtFailure => _state?.PrimaryAtFailure ?? double.NaN;

        public double Oee => _state?.Oee ?? 0;
        public string OeeText => _state == null ? "-" : $"A {_state.Availability:P0} · P {_state.Performance:P0} · Q {_state.Quality:P0}";
        public string LossText => _state == null ? "-" : $"Main loss: {_state.DominantLoss}";
        public bool IsBottleneck => _state?.IsBottleneck ?? false;
        public string BottleneckText => _state == null ? "-" : $"{_state.BottleneckShare:P0} bottleneck share · ×{_state.CycleTimeFactor:F2} cycle";
        public string MaintenanceText => _state == null || _state.MaintenanceState == "None"
            ? $"PM {_state?.MaintenanceCount ?? 0} · breakdowns {_state?.BreakdownCount ?? 0}"
            : $"{_state.MaintenanceState} {(_state.MaintenanceState.StartsWith("InProgress") ? $"{_state.MaintenanceRemainingSeconds:F0} s" : string.Empty)}";
        public bool InMaintenance => _state != null && _state.MaintenanceState != "None";
        public string InjectedFault => _state?.InjectedFault ?? string.Empty;
        public bool HasInjectedFault => !string.IsNullOrEmpty(InjectedFault);

        public string RobotText => _robot == null ? "-" : $"{_robot.Name} vacuum {_robot.VacuumKpa:F0} kPa · transfers ×{_robot.ActionTimeFactor:F2}";

        public string SpcTitle => _state?.KeySpc == null ? "-" : $"SPC · {_state.KeySpc.Characteristic}";
        public string SpcText => _state?.KeySpc is { } spc
            ? $"Cpk {(spc.Cpk.HasValue ? spc.Cpk.Value.ToString("0.00") : "n/a")} · {spc.OutOfSpecCount}/{spc.SampleCount} out of spec{(spc.IsOutOfControl ? " · " + spc.LastViolation : string.Empty)}"
            : "-";
        public bool SpcAlarm => _state?.KeySpc?.IsOutOfControl ?? false;
        public double[] SpcValues => _state?.KeySpc?.Values ?? Array.Empty<double>();
        public int[] SpcMarkers => _state?.KeySpc?.ViolationIndices ?? Array.Empty<int>();
        public double SpcCenter => _state?.KeySpc?.CenterLine ?? double.NaN;
        public double SpcUcl => _state?.KeySpc?.UpperControlLimit ?? double.NaN;
        public double SpcLcl => _state?.KeySpc?.LowerControlLimit ?? double.NaN;
        public double SpcUsl => _state?.KeySpc?.UpperSpecLimit ?? double.NaN;
        public double SpcLsl => _state?.KeySpc?.LowerSpecLimit ?? double.NaN;
        public string RecommendedAction => _state?.RecommendedAction ?? string.Empty;

        public string ActionStatus
        {
            get => _actionStatus;
            set { _actionStatus = value; OnPropertyChanged(); }
        }

        public void Update(MachineIntelligenceState state, RobotIntelligenceState? robot)
        {
            _state = state;
            _robot = robot;
            if (state.MaintenanceState == "None" && ActionStatus.StartsWith("Maintenance requested", StringComparison.Ordinal) && state.MaintenanceCount > 0)
            {
                ActionStatus = string.Empty;
            }

            OnPropertyChanged(string.Empty);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class CopilotInsightViewModel : INotifyPropertyChanged
    {
        private CopilotInsight _insight;

        public CopilotInsightViewModel(CopilotInsight insight) => _insight = insight;

        public string Severity => _insight.Severity.ToString().ToUpperInvariant();
        public string SeverityColor => _insight.Severity switch
        {
            InsightSeverity.Critical => "#FF4D6D",
            InsightSeverity.Warning => "#FB923C",
            InsightSeverity.Advisory => "#FBBF24",
            _ => "#3FB6FF"
        };
        public string Category => _insight.Category;
        public string Title => _insight.Title;
        public string Evidence => _insight.Evidence;
        public string Recommendation => _insight.Recommendation;
        public string ConfidenceText => $"confidence {_insight.Confidence:P0}";

        public void Update(CopilotInsight insight)
        {
            if (insight.Evidence == _insight.Evidence && insight.Recommendation == _insight.Recommendation)
            {
                return;
            }

            _insight = insight;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class AutopilotDecisionViewModel
    {
        public AutopilotDecisionViewModel(AutopilotDecision decision)
        {
            TimeText = $"t={decision.SimTime:F0}s";
            Category = decision.Category;
            Action = decision.Action;
            Rationale = decision.Rationale;
        }

        public string TimeText { get; }
        public string Category { get; }
        public string Action { get; }
        public string Rationale { get; }
    }
}
