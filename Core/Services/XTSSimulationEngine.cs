using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using XTSPrimeMoverProject.Models;
using XTSPrimeMoverProject.Services.Intelligence;

namespace XTSPrimeMoverProject.Services
{
    /// <summary>
    /// XTS line runtime: a fixed-cycle PLC-style simulation of the EV battery module line.
    /// <list type="bullet">
    /// <item>Movers never overtake: braking-curve gap control + jerk-limited S-curve motion + precise docking.</item>
    /// <item>Robot cells swap parts with docked movers through a machine outfeed nest (deadlock-free on a single loop).</item>
    /// <item>Timed entry (cell-stack load) and exit (module unload) docks.</item>
    /// <item>Watchdog supervision, SQLite traceability, and the AI intelligence layer (digital twins, PdM, SPC, autopilot).</item>
    /// </list>
    /// </summary>
    public class XTSSimulationEngine : IDisposable
    {
        /// <summary>PLC task cycle used for all physics and logic (independent of the UI tick and speed factor).</summary>
        public const double FixedStepSeconds = 0.02;

        public const double ExitAngle = 0;
        public const double EntryLoadAngle = 205;
        public static readonly double[] MachineLoadAngles = { 45, 135, 225, 315 };

        /// <summary>Minimum centre-to-centre distance between movers (≈ 0.20 m carrier + clearance).</summary>
        public const double MoverPitchDegrees = 12.0;

        private const double PlanningDeceleration = 150.0;
        private const double DockSnapDegrees = 0.4;
        private const double EntryLoadSeconds = 1.2;
        private const double ExitUnloadSeconds = 1.0;
        private const double MaxTickSimSeconds = 0.5;
        private const int MaxWipParts = AutopilotController.DefaultMaxWip;
        private const int MinEmptyMoversToKeepCirculating = 3;
        private const double MachineStallThresholdSeconds = 14.0;
        private const double RobotStallThresholdSeconds = 9.0;
        private const double MoverDockStallThresholdSeconds = 45.0;

        private readonly System.Threading.Timer _timer;
        private readonly ISimulationDispatcher _dispatcher;
        private readonly SimulationOptions _options;
        private readonly Random _random;
        private readonly SimulationDataLogger _dataLogger;
        private readonly ErrorHandlingService _errorHandler = ErrorHandlingService.Instance;
        private readonly object _simulationLock = new();
        private int _tickActive;
        private ProductionOrchestration _orchestration;
        private LineIntelligenceHub _intel = null!;

        private readonly Dictionary<Guid, int> _partHistoryLogIndex = new();
        private readonly Dictionary<int, FbXtsMoverAxis> _moverAxes = new();
        private readonly Dictionary<int, MoverRuntime> _moverRuntime = new();
        private readonly Dictionary<int, FbMachineCycle> _machineCycles = new();
        private readonly Dictionary<int, bool> _machineAlarmStates = new();
        private readonly Dictionary<int, MoverState> _lastMoverStates = new();
        private readonly Dictionary<int, double> _machineLoadAngleByMachineId = new();
        private readonly Dictionary<int, int> _pickupAssignments = new();
        private readonly List<StopPoint> _stopPoints = new();

        private readonly Dictionary<int, double> _machineWatchSeconds = new();
        private readonly Dictionary<int, string> _machineWatchSignature = new();
        private readonly Dictionary<int, double> _robotWatchSeconds = new();
        private readonly Dictionary<int, string> _robotWatchSignature = new();

        private readonly Dictionary<string, WatchdogStatusEntry> _watchdogStatus = new(StringComparer.OrdinalIgnoreCase);
        private OrchestrationProfile _lastKnownGoodOrchestration;

        private DateTime _lastUpdate;
        private int _trackingCounter;
        private double _releaseCooldown;
        private double _snapshotTimer;
        private int _entryAssignedMoverId = -1;
        private double _entryZoneBlinkRemaining;
        private double _exitZoneBlinkRemaining;
        private double _simulationSpeedFactor = 1.0;
        private double _lastReleaseHoldLog = double.NegativeInfinity;

        public XTSSimulationEngine(ISimulationDispatcher? dispatcher = null, SimulationOptions? options = null)
        {
            _options = options ?? new SimulationOptions();
            _random = _options.Seed.HasValue ? new Random(_options.Seed.Value) : new Random();
            _dataLogger = new SimulationDataLogger(_options.DatabasePath);
            _dispatcher = dispatcher ?? InlineSimulationDispatcher.Instance;

            _orchestration = new ProductionOrchestration(Array.Empty<Machine>());
            _lastKnownGoodOrchestration = _orchestration.ActiveProfile;

            InitializeSystem();

            _timer = new System.Threading.Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public List<Mover> Movers { get; private set; } = new();
        public List<Machine> Machines { get; private set; } = new();
        public List<Robot> Robots { get; private set; } = new();

        public int TotalPartsProduced { get; private set; }
        public int GoodPartsCount { get; private set; }
        public int BadPartsCount { get; private set; }
        public int PrimeMoverEnteredCount { get; private set; }
        public int PrimeMoverExitedCount => GoodPartsCount + BadPartsCount;
        public bool IsRunning { get; private set; }
        public int TotalStationCount { get; private set; }
        public double SimulationTimeSeconds { get; private set; }
        public string DatabasePath => _dataLogger.DatabasePath;
        public bool EntryZoneBlink => _entryZoneBlinkRemaining > 0;
        public bool ExitZoneBlink => _exitZoneBlinkRemaining > 0;
        public bool AutopilotEnabled => _intel.Autopilot.Enabled;

        public event EventHandler? StateChanged;
        public event EventHandler<string>? LogGenerated;

        // ------------------------------------------------------------------ data access

        public IReadOnlyList<PartHistoryEventRecord> GetPartHistory(string trackingNumber) => _dataLogger.GetPartHistory(trackingNumber);
        public PartSummaryRecord? GetPartSummary(string trackingNumber) => _dataLogger.GetPartSummary(trackingNumber);
        public IReadOnlyList<string> GetExportableTables() => _dataLogger.GetExportableTables();
        public IReadOnlyList<string> GetAllTables() => _dataLogger.GetAllTables();
        public IReadOnlyList<string> GetTableColumns(string tableName) => _dataLogger.GetTableColumns(tableName);
        public int GetTableRowCount(string tableName) => _dataLogger.GetTableRowCount(tableName);
        public IReadOnlyList<Dictionary<string, string>> GetTableRows(string tableName, int maxRows = 500) => _dataLogger.GetTableRows(tableName, maxRows);
        public string ExportTableToCsv(string tableName, string? exportDirectory = null) => _dataLogger.ExportTableToCsv(tableName, exportDirectory);
        public string GetDefaultExportDirectory() => System.IO.Path.Combine(AppContext.BaseDirectory, "Exports");

        public IReadOnlyList<WatchdogStatusEntry> GetWatchdogStatus()
        {
            lock (_simulationLock)
            {
                return _watchdogStatus.Values
                    .OrderByDescending(x => x.LastTriggeredAt)
                    .ThenBy(x => x.Code)
                    .Select(x => new WatchdogStatusEntry
                    {
                        Code = x.Code,
                        TriggerCount = x.TriggerCount,
                        LastTriggeredAt = x.LastTriggeredAt,
                        LastRecoveredObject = x.LastRecoveredObject,
                        LastMessage = x.LastMessage
                    })
                    .ToList();
            }
        }

        // ------------------------------------------------------------------ intelligence API

        /// <summary>Latest immutable AI snapshot (thread-safe, lock-free read).</summary>
        public IntelligenceSnapshot GetIntelligenceSnapshot() => _intel.Snapshot;

        public void SetAutopilotEnabled(bool enabled)
        {
            lock (_simulationLock)
            {
                if (_intel.Autopilot.Enabled == enabled)
                {
                    return;
                }

                _intel.Autopilot.Enabled = enabled;
                _intel.Autopilot.Record(SimulationTimeSeconds, "Mode", enabled ? "Autopilot engaged" : "Autopilot disengaged",
                    enabled ? "Operator handed release control and predictive maintenance scheduling to the AI." : "Operator took manual control.");
                _intel.PublishSnapshot();
            }

            Log($"AI autopilot {(enabled ? "ENGAGED" : "disengaged")}.");
        }

        public string InjectFault(FaultScenario scenario)
        {
            string result;
            lock (_simulationLock)
            {
                result = _intel.InjectFault(scenario);
                FlushIntelligenceEvents();
            }

            return result;
        }

        public void ClearFaults()
        {
            lock (_simulationLock)
            {
                _intel.ClearFaults();
                FlushIntelligenceEvents();
            }
        }

        public bool RequestMaintenance(int machineId, string requestedBy = "Operator")
        {
            bool accepted;
            lock (_simulationLock)
            {
                accepted = _intel.RequestMaintenance(machineId, MaintenanceKind.Planned, "Operator-requested maintenance", requestedBy);
                FlushIntelligenceEvents();
                _intel.PublishSnapshot();
            }

            return accepted;
        }

        /// <summary>Offline copilot answer computed from the latest snapshot.</summary>
        public string AskCopilot(string question) => _intel.Copilot.Answer(question, _intel.Snapshot);

        public string GetCopilotContextJson() => CopilotReasoner.BuildLlmContextJson(_intel.Snapshot);

        // ------------------------------------------------------------------ orchestration

        public IReadOnlyList<ProductionSequenceStep> GetOrchestrationSteps() => _orchestration.Steps;

        public bool TryApplyOrchestration(IReadOnlyList<int> orderedMachineIds, out string message)
        {
            var stepDefs = orderedMachineIds
                .Select((machineId, idx) => new OrchestrationStepDefinition
                {
                    MachineId = machineId,
                    OutputStatus = idx == orderedMachineIds.Count - 1 ? PartStatus.Good : PartStatus.InProcess
                })
                .ToList();

            return TryApplyOrchestration(stepDefs, out message);
        }

        public bool TryApplyOrchestration(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, out string message)
        {
            if (stepDefinitions == null || stepDefinitions.Count == 0)
            {
                message = "Orchestration apply rejected: sequence is empty.";
                return false;
            }

            lock (_simulationLock)
            {
                if (!CanSafelyApplyOrchestration(out string interlockReason))
                {
                    message = $"Orchestration apply rejected by interlocks: {interlockReason}";
                    _dataLogger.LogAlarm("Warning", "Orchestration", message, true);
                    Log(message);
                    return false;
                }

                var candidate = _orchestration.CreateProfile(
                    stepDefinitions,
                    name: "HMI-Edited",
                    version: _orchestration.ActiveProfile.Version + 1);

                var previousProfile = _orchestration.ActiveProfile.Clone(_orchestration.ActiveProfile.Name, _orchestration.ActiveProfile.Version);

                if (!_orchestration.TrySetProfile(candidate, out var errors))
                {
                    _orchestration.TrySetProfile(_lastKnownGoodOrchestration, out _);
                    message = "Orchestration validation failed: " + string.Join(" | ", errors);
                    _dataLogger.LogAlarm("Warning", "Orchestration", message, true);
                    Log(message);
                    return false;
                }

                _lastKnownGoodOrchestration = previousProfile;

                var orderedMachineIds = stepDefinitions.Select(s => s.MachineId).ToList();
                foreach (var robot in Robots)
                {
                    if (!orderedMachineIds.Contains(robot.AssignedMachineId))
                    {
                        robot.AssignedMachineId = orderedMachineIds.First();
                    }
                }

                message = $"Orchestration applied successfully: {_orchestration.DescribeFlow()}";
                _dataLogger.LogAlarm("Info", "Orchestration", message, false);
            }

            Log(message);
            return true;
        }

        public IReadOnlyList<string> PreviewOrchestrationValidation(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)
        {
            var candidate = _orchestration.CreateProfile(
                stepDefinitions,
                name: "HMI-Preview",
                version: _orchestration.ActiveProfile.Version + 1);

            var validation = _orchestration.ValidateProfile(candidate);
            return validation.Errors;
        }

        public IReadOnlyList<SafetyGateStatus> GetOrchestrationSafetyGateStatuses()
        {
            bool machineFault = Machines.Any(m => m.FaultActive || m.SequencerState == PlcSequencerState.Fault);
            bool robotsBusy = Robots.Any(r => r.State != RobotState.Idle || r.HeldPart != null);
            bool loadedDocked = Movers.Any(m => m.CurrentPart != null && (m.State == MoverState.AtLoadStation || m.State == MoverState.AtUnloadStation));
            return new List<SafetyGateStatus>
            {
                new() { GateName = "Simulation stopped", IsPassing = !IsRunning, Detail = IsRunning ? "Stop simulation before apply." : "OK" },
                new() { GateName = "No machine faults", IsPassing = !machineFault, Detail = machineFault ? "One or more machines faulted." : "OK" },
                new() { GateName = "Robots idle and empty", IsPassing = !robotsBusy, Detail = robotsBusy ? "At least one robot is active or holding a part." : "OK" },
                new() { GateName = "No loaded mover docked", IsPassing = !loadedDocked, Detail = loadedDocked ? "Loaded mover docked at station." : "OK" }
            };
        }

        private bool CanSafelyApplyOrchestration(out string reason)
        {
            var failedGate = GetOrchestrationSafetyGateStatuses().FirstOrDefault(g => !g.IsPassing);
            if (failedGate != null)
            {
                reason = failedGate.Detail;
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // ------------------------------------------------------------------ lifecycle

        private void InitializeSystem()
        {
            Movers = new List<Mover>();
            for (int i = 0; i < 10; i++)
            {
                Movers.Add(new Mover(i));
            }

            Machines = new List<Machine>
            {
                new Machine(0, "Laser Welder", MachineType.LaserWelding, MachineLoadAngles[0]),
                new Machine(1, "Assembler", MachineType.PrecisionAssembly, MachineLoadAngles[1]),
                new Machine(2, "Inspector", MachineType.QualityInspection, MachineLoadAngles[2]),
                new Machine(3, "Tester", MachineType.FunctionalTesting, MachineLoadAngles[3])
            };

            Robots = new List<Robot>();
            for (int i = 0; i < Machines.Count; i++)
            {
                Robots.Add(new Robot(i, Machines[i].MachineId));
            }

            _machineLoadAngleByMachineId.Clear();
            _stopPoints.Clear();
            for (int i = 0; i < Machines.Count && i < MachineLoadAngles.Length; i++)
            {
                _machineLoadAngleByMachineId[Machines[i].MachineId] = MachineLoadAngles[i];
                _stopPoints.Add(new StopPoint($"M{Machines[i].MachineId}", MachineLoadAngles[i], StopKind.Machine, Machines[i].MachineId));
            }

            _stopPoints.Add(new StopPoint("ENTRY", EntryLoadAngle, StopKind.Entry, -1));
            _stopPoints.Add(new StopPoint("EXIT", ExitAngle, StopKind.Exit, -1));

            _orchestration = new ProductionOrchestration(Machines);
            _lastKnownGoodOrchestration = _orchestration.ActiveProfile;

            _moverAxes.Clear();
            _moverRuntime.Clear();
            _machineCycles.Clear();
            _machineAlarmStates.Clear();
            _lastMoverStates.Clear();
            _pickupAssignments.Clear();

            foreach (var mover in Movers)
            {
                mover.State = MoverState.Moving;
                _moverAxes[mover.MoverId] = new FbXtsMoverAxis();
                _moverRuntime[mover.MoverId] = new MoverRuntime();
                _lastMoverStates[mover.MoverId] = mover.State;
            }

            foreach (var machine in Machines)
            {
                _machineCycles[machine.MachineId] = new FbMachineCycle();
                _machineAlarmStates[machine.MachineId] = false;
            }

            bool autopilot = _intel?.Autopilot.Enabled ?? _options.AutopilotEnabled;
            int hubSeed = _options.Seed ?? Environment.TickCount;
            _intel = new LineIntelligenceHub(Machines, Robots, hubSeed);
            _intel.Autopilot.Enabled = autopilot;
            foreach (var station in Machines.SelectMany(m => m.Stations))
            {
                station.ProcessModel = _intel;
            }

            TotalStationCount = Machines.Sum(m => m.Stations.Count);
            TotalPartsProduced = 0;
            GoodPartsCount = 0;
            BadPartsCount = 0;
            PrimeMoverEnteredCount = 0;
            SimulationTimeSeconds = 0;
            _trackingCounter = 1;
            _releaseCooldown = 0;
            _snapshotTimer = 0;
            _entryAssignedMoverId = -1;
            _partHistoryLogIndex.Clear();
            _watchdogStatus.Clear();
            _entryZoneBlinkRemaining = 0;
            _exitZoneBlinkRemaining = 0;

            InitializeWatchdogs();

            _dataLogger.SeedRecipes(Machines);
            _dataLogger.LogAlarm("Info", "Simulation", "System initialized", false);
            Log("Simulation initialized: 12S EV battery module line, 10 XTS movers, 4 robot cells.");
            Log($"Orchestrated sequence: {_orchestration.DescribeFlow()}");
            Log("Transport: no-overtake gap control, jerk-limited motion, swap-cell docking with outfeed nests.");
            _lastKnownGoodOrchestration = _orchestration.ActiveProfile.Clone(_orchestration.ActiveProfile.Name, _orchestration.ActiveProfile.Version);
            _intel.PublishSnapshot();
        }

        private void InitializeWatchdogs()
        {
            _machineWatchSeconds.Clear();
            _machineWatchSignature.Clear();
            foreach (var machine in Machines)
            {
                _machineWatchSeconds[machine.MachineId] = 0;
                _machineWatchSignature[machine.MachineId] = BuildMachineWatchSignature(machine);
            }

            _robotWatchSeconds.Clear();
            _robotWatchSignature.Clear();
            foreach (var robot in Robots)
            {
                _robotWatchSeconds[robot.RobotId] = 0;
                _robotWatchSignature[robot.RobotId] = BuildRobotWatchSignature(robot);
            }
        }

        public void Start()
        {
            lock (_simulationLock)
            {
                IsRunning = true;
                _lastUpdate = DateTime.Now;
                _timer.Change(0, 50);
            }

            _dataLogger.LogAlarm("Info", "Simulation", "System started", true);
            Log("System started.");
        }

        public void Stop()
        {
            lock (_simulationLock)
            {
                IsRunning = false;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                foreach (var mover in Movers)
                {
                    _moverAxes[mover.MoverId].Stop(mover);
                }
            }

            _dataLogger.LogAlarm("Info", "Simulation", "System stopped", false);
            Log("System stopped.");
        }

        public void Reset()
        {
            Stop();
            lock (_simulationLock)
            {
                InitializeSystem();
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetSimulationSpeed(double speedFactor)
        {
            Interlocked.Exchange(ref _simulationSpeedFactor, Math.Clamp(speedFactor, 0.1, 5.0));
        }

        /// <summary>
        /// Headless operation (tests, video recorder): runs the line for <paramref name="seconds"/> of
        /// simulation time without the wall-clock timer, then raises <see cref="StateChanged"/>.
        /// </summary>
        public void AdvanceManually(double seconds)
        {
            lock (_simulationLock)
            {
                IsRunning = true;
                RunFixedSteps(seconds);
                _intel.PublishSnapshot();
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnTimerTick(object? state)
        {
            if (Interlocked.CompareExchange(ref _tickActive, 1, 0) != 0)
            {
                return;
            }

            try
            {
                lock (_simulationLock)
                {
                    if (!IsRunning)
                    {
                        return;
                    }

                    DateTime now = DateTime.Now;
                    double deltaTime = Math.Min(MaxTickSimSeconds, (now - _lastUpdate).TotalSeconds * _simulationSpeedFactor);
                    _lastUpdate = now;

                    RunFixedSteps(deltaTime);
                    _intel.PublishSnapshot();
                }

                if (!_dispatcher.IsShuttingDown)
                {
                    _dispatcher.Invoke(() => StateChanged?.Invoke(this, EventArgs.Empty));
                }
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Engine, "EngineTick", ex);
                _dataLogger.LogError("EngineTick", ex.Message, ex.ToString());
                Log($"ENGINE ERROR: {ex.Message}");
                Stop();
            }
            finally
            {
                Interlocked.Exchange(ref _tickActive, 0);
            }
        }

        private double _stepCarry;

        /// <summary>Integrates <paramref name="deltaTime"/> in fixed PLC cycles (deterministic at any speed).</summary>
        private void RunFixedSteps(double deltaTime)
        {
            _stepCarry += deltaTime;
            while (_stepCarry >= FixedStepSeconds)
            {
                _stepCarry -= FixedStepSeconds;
                Update(FixedStepSeconds);
            }
        }

        private void Update(double dt)
        {
            SimulationTimeSeconds += dt;
            RunSubsystem(() => UpdateMachines(dt), "UpdateMachines");
            RunSubsystem(() => UpdateRobots(dt), "UpdateRobots");
            RunSubsystem(ProcessRobotCells, "ProcessRobotCells");
            RunSubsystem(() => ProcessEntryExitDocks(dt), "ProcessEntryExitDocks");
            RunSubsystem(() => UpdateMovers(dt), "UpdateMovers");
            RunSubsystem(SyncPartHistoryLogs, "SyncPartHistoryLogs");
            RunSubsystem(() => RunWatchdogs(dt), "RunWatchdogs");
            RunSubsystem(() => CapturePeriodicSnapshot(dt), "CapturePeriodicSnapshot");
            RunSubsystem(() => UpdateZoneBlinkers(dt), "UpdateZoneBlinkers");
            RunSubsystem(() => UpdateIntelligence(dt), "UpdateIntelligence");
        }

        private void RunSubsystem(Action subsystem, string name)
        {
            try
            {
                subsystem();
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Engine, $"Engine.{name}", ex, wasRecovered: true);
                _dataLogger.LogError($"Engine.{name}", ex.Message, ex.ToString());
                Log($"Subsystem '{name}' error (recovered): {ex.Message}");
            }
        }

        // ------------------------------------------------------------------ machines

        private void UpdateMachines(double deltaTime)
        {
            foreach (var machine in Machines)
            {
                var cycleFb = _machineCycles[machine.MachineId];
                cycleFb.Enable = IsRunning;
                cycleFb.InterlockPermit = machine.IsOperational;
                cycleFb.ResetAlarms = !IsRunning;
                cycleFb.Cycle(machine, deltaTime);

                bool wasAlarm = _machineAlarmStates[machine.MachineId];
                if (cycleFb.AlarmActive && !wasAlarm)
                {
                    _dataLogger.LogAlarm("Warning", $"Machine{machine.MachineId}", cycleFb.AlarmText, true);
                    Log($"Machine {machine.MachineId} alarm: {cycleFb.AlarmText}");
                }
                else if (!cycleFb.AlarmActive && wasAlarm)
                {
                    _dataLogger.LogAlarm("Info", $"Machine{machine.MachineId}", "Machine alarm reset", false);
                    Log($"Machine {machine.MachineId} alarm reset.");
                }

                _machineAlarmStates[machine.MachineId] = cycleFb.AlarmActive;

                // Internal outfeed shuttle: finished part leaves the last station into the nest.
                if (machine.OutfeedNest == null && machine.HasCompletedPartReady())
                {
                    var part = machine.UnloadPart();
                    if (part != null)
                    {
                        part.NextMachineIndex = _orchestration.GetNextMachineIndex(machine.MachineId);
                        part.MachinesCompleted++;
                        part.Status = _orchestration.ResolveOutboundStatus(machine.MachineId, part);
                        part.CurrentLocation = $"{machine.Name} outfeed nest";
                        machine.OutfeedNest = part;
                        _dataLogger.LogMachineExit(machine, part);
                        _dataLogger.LogPartEvent(part, "MachineExit", machine.Name, $"Exited {machine.Name} to outfeed nest");
                        Log($"{part.TrackingNumber} finished {machine.Name} → outfeed nest ({part.Status}).");
                    }
                }
            }
        }

        private void UpdateRobots(double deltaTime)
        {
            foreach (var robot in Robots)
            {
                robot.Update(deltaTime);
            }
        }

        private static bool IsMachineAvailable(Machine machine) =>
            machine.IsOperational
            && machine.Maintenance == MaintenanceMode.None
            && !machine.FaultActive
            && machine.SequencerState != PlcSequencerState.Fault;

        private Mover? DockedMoverAt(int machineId)
        {
            foreach (var mover in Movers)
            {
                var rt = _moverRuntime[mover.MoverId];
                if (rt.DockKind == StopKind.Machine && rt.DockMachineId == machineId)
                {
                    return mover;
                }
            }

            return null;
        }

        /// <summary>
        /// Dual-gripper robot cell controller. The robot pre-fetches a finished module from the outfeed nest
        /// and stages it at the dock. When a mover docks:
        /// <list type="bullet">
        /// <item>empty mover → finished module placed (≈0.8 s dwell);</item>
        /// <item>loaded mover for this cell → gripper A picks the raw module, gripper B places the finished one
        /// (≈1.6 s dwell), then the robot loads the machine while the mover is already gone.</item>
        /// </list>
        /// Short dwell matters on a single XTS loop because movers cannot overtake a docked carrier.
        /// </summary>
        private void ProcessRobotCells()
        {
            var machineById = Machines.ToDictionary(m => m.MachineId);
            foreach (var robot in Robots)
            {
                if (!machineById.TryGetValue(robot.AssignedMachineId, out var machine))
                {
                    continue;
                }

                int machineId = machine.MachineId;
                var docked = DockedMoverAt(machineId);

                if (robot.State == RobotState.Idle)
                {
                    robot.IsStagedAtDock = false;

                    // 1) Machine first: a waiting raw module goes in immediately (keeps the machine busy; the
                    //    mover leaves after a 0.8 s pick). Protects the constraint.
                    var dockedPart = docked?.CurrentPart;
                    if (docked != null && dockedPart != null && dockedPart.NextMachineIndex == machineId)
                    {
                        if (!IsMachineAvailable(machine))
                        {
                            Log($"{dockedPart.TrackingNumber}: {machine.Name} unavailable ({machine.Maintenance}) – mover {docked.MoverId} recirculates.");
                            ReleaseMover(docked);
                            continue;
                        }

                        if (machine.CanAcceptPart())
                        {
                            PickRawFromMover(robot, machine, docked);
                            continue;
                        }
                    }

                    // 2) Pre-fetch the finished module from the outfeed nest and stage it at the dock.
                    if (machine.OutfeedNest != null && machine.Maintenance != MaintenanceMode.InProgress)
                    {
                        StartPickFromNest(robot, machine);
                        continue;
                    }

                    // 3) Nothing to do for a docked mover that is empty or bound elsewhere.
                    if (docked != null && (dockedPart == null || dockedPart.NextMachineIndex != machineId))
                    {
                        ReleaseMover(docked);
                    }

                    continue;
                }

                if (robot.State == RobotState.PlacingOnMover)
                {
                    HandleDockPlacement(robot, machine, docked);
                    continue;
                }

                if (!robot.IsStepComplete())
                {
                    continue;
                }

                switch (robot.State)
                {
                    case RobotState.PickingFromMover:
                        if (robot.SecondaryHeldPart != null)
                        {
                            robot.TransitionTo(RobotState.PlacingOnMover);
                        }
                        else
                        {
                            if (docked != null)
                            {
                                ReleaseMover(docked);
                            }

                            robot.TransitionTo(RobotState.MovingToMachine);
                        }

                        break;

                    case RobotState.MovingToMachine:
                        robot.TransitionTo(RobotState.PlacingInMachine);
                        break;

                    case RobotState.PlacingInMachine:
                        if (robot.HeldPart == null)
                        {
                            robot.TransitionTo(RobotState.Idle);
                            break;
                        }

                        if (!machine.CanCompleteInboundTransfer())
                        {
                            break;
                        }

                        var part = robot.HeldPart;
                        part.Status = PartStatus.InProcess;
                        part.CurrentLocation = $"{machine.Name} - S0";
                        machine.LoadPart(part);
                        _dataLogger.LogMachineEntry(machine, part);
                        _dataLogger.LogPartEvent(part, "MachineEntry", machine.Name, $"Entered {machine.Name}");
                        robot.ReleaseHeldPart();
                        robot.TransitionTo(RobotState.Idle);
                        break;

                    case RobotState.PickingFromMachine:
                        robot.TransitionTo(RobotState.MovingToMover);
                        break;

                    case RobotState.MovingToMover:
                        robot.TransitionTo(RobotState.PlacingOnMover);
                        break;
                }
            }
        }

        private void PickRawFromMover(Robot robot, Machine machine, Mover docked)
        {
            var raw = docked.CurrentPart!;
            docked.CurrentPart = null;
            docked.State = MoverState.AtUnloadStation;
            raw.CurrentLocation = $"Robot-{robot.RobotId} from Mover-{docked.MoverId}";
            _dataLogger.LogPartEvent(raw, "MoverUnload", $"M{machine.MachineId}", $"Robot picked from mover {docked.MoverId}");
            Log($"{raw.TrackingNumber} picked from mover {docked.MoverId} for {machine.Name}.");

            if (robot.HeldPart != null)
            {
                // Swap: finished module moves to gripper B, gripper A takes the raw module.
                robot.SecondaryHeldPart = robot.HeldPart;
                robot.HeldPart = raw;
                robot.TransitionTo(RobotState.PickingFromMover);
            }
            else
            {
                robot.StartPickFromMover(raw);
            }
        }

        /// <summary>Robot holds a finished module at the dock: place it on an empty mover or swap with a loaded one.</summary>
        private void HandleDockPlacement(Robot robot, Machine machine, Mover? docked)
        {
            var outbound = robot.SecondaryHeldPart ?? robot.HeldPart;
            if (outbound == null)
            {
                robot.TransitionTo(robot.HeldPart != null ? RobotState.MovingToMachine : RobotState.Idle);
                return;
            }

            if (docked == null)
            {
                // Staged: wait at the dock for the next mover (not a stall).
                robot.IsStagedAtDock = true;
                robot.ActionProgress = 0;
                return;
            }

            robot.IsStagedAtDock = false;
            if (docked.CurrentPart != null)
            {
                bool swapCandidate = robot.SecondaryHeldPart == null
                                     && docked.CurrentPart.NextMachineIndex == machine.MachineId
                                     && IsMachineAvailable(machine)
                                     && machine.CanAcceptPart();
                if (swapCandidate)
                {
                    PickRawFromMover(robot, machine, docked);
                }
                else if (docked.CurrentPart.NextMachineIndex != machine.MachineId || !IsMachineAvailable(machine))
                {
                    ReleaseMover(docked);
                }

                return;
            }

            if (!robot.IsStepComplete())
            {
                return;
            }

            docked.CurrentPart = outbound;
            docked.State = MoverState.AtUnloadStation;
            outbound.CurrentLocation = $"Mover-{docked.MoverId}";
            _dataLogger.LogPartEvent(outbound, "MoverLoad", $"M{machine.MachineId}", $"Robot placed on mover {docked.MoverId}");
            Log($"{outbound.TrackingNumber} placed onto mover {docked.MoverId} at {machine.Name} → {DescribeNext(outbound)}.");
            ReleaseMover(docked);

            if (robot.SecondaryHeldPart != null)
            {
                robot.SecondaryHeldPart = null;
                robot.TransitionTo(RobotState.MovingToMachine);
            }
            else
            {
                robot.ReleaseHeldPart();
                robot.TransitionTo(RobotState.Idle);
            }
        }

        private void StartPickFromNest(Robot robot, Machine machine)
        {
            var nestPart = machine.OutfeedNest!;
            machine.OutfeedNest = null;
            nestPart.CurrentLocation = $"Robot-{robot.RobotId} from {machine.Name}";
            robot.StartPickFromMachine(nestPart);
        }

        private string DescribeNext(Part part) =>
            part.Status is PartStatus.Good or PartStatus.Bad
                ? $"exit ({part.Status})"
                : Machines.FirstOrDefault(m => m.MachineId == part.NextMachineIndex)?.Name ?? "exit";

        // ------------------------------------------------------------------ entry / exit docks

        private int ActivePartCount()
        {
            int count = Movers.Count(m => m.CurrentPart != null) + Robots.Count(r => r.HeldPart != null) + Robots.Count(r => r.SecondaryHeldPart != null);
            foreach (var machine in Machines)
            {
                count += machine.Stations.Count(s => s.CurrentPart != null);
                if (machine.OutfeedNest != null) count++;
            }

            return count;
        }

        private int CurrentWipCap => _intel.Autopilot.Enabled ? _intel.Autopilot.WipCap : MaxWipParts;

        private bool IsReleaseAllowed()
        {
            if (_releaseCooldown > 0)
            {
                return false;
            }

            if (ActivePartCount() >= CurrentWipCap)
            {
                return false;
            }

            if (Movers.Count(m => m.CurrentPart == null) <= MinEmptyMoversToKeepCirculating)
            {
                return false;
            }

            if (_intel.Autopilot.Enabled)
            {
                var first = _orchestration.Steps.Count > 0 ? Machines.FirstOrDefault(m => m.MachineId == _orchestration.Steps[0].MachineId) : null;
                if (first != null && !IsMachineAvailable(first))
                {
                    if (SimulationTimeSeconds - _lastReleaseHoldLog > 20)
                    {
                        _lastReleaseHoldLog = SimulationTimeSeconds;
                        _intel.Autopilot.Record(SimulationTimeSeconds, "Release control", "Release hold",
                            $"{first.Name} (first operation) is unavailable – releasing new cell stacks would only add circulating WIP.");
                    }

                    return false;
                }
            }

            return true;
        }

        private void ProcessEntryExitDocks(double dt)
        {
            if (_releaseCooldown > 0)
            {
                _releaseCooldown -= dt;
            }

            foreach (var mover in Movers)
            {
                var rt = _moverRuntime[mover.MoverId];
                if (rt.DockKind == StopKind.Entry)
                {
                    rt.DockTimer += dt;
                    if (rt.DockTimer >= EntryLoadSeconds)
                    {
                        if (mover.CurrentPart == null)
                        {
                            LoadNewPart(mover);
                        }

                        ReleaseMover(mover);
                    }
                }
                else if (rt.DockKind == StopKind.Exit)
                {
                    rt.DockTimer += dt;
                    if (rt.DockTimer >= ExitUnloadSeconds)
                    {
                        if (mover.CurrentPart != null)
                        {
                            DischargePart(mover);
                        }

                        ReleaseMover(mover);
                    }
                }
            }
        }

        private void LoadNewPart(Mover mover)
        {
            var newPart = new Part
            {
                TrackingNumber = $"TRK-{_trackingCounter:00000}",
                EnteredPrimeMoverAt = DateTime.Now,
                CurrentLocation = $"Mover-{mover.MoverId}",
                NextMachineIndex = _orchestration.Steps.Count > 0 ? _orchestration.Steps[0].MachineId : 0
            };

            _trackingCounter++;
            PrimeMoverEnteredCount++;
            _intel.RecordPartEntry(newPart);

            newPart.AddProcessHistory("12S cell stack loaded on XTS mover");
            mover.CurrentPart = newPart;

            _dataLogger.LogPartCreated(newPart);
            _dataLogger.LogPartEvent(newPart, "PrimeMoverEntry", "Entry", $"Loaded on mover {mover.MoverId}");
            Log($"{newPart.TrackingNumber} cell stack loaded on mover {mover.MoverId} (WIP cap {CurrentWipCap}).");
            TriggerEntryZoneBlink();

            _releaseCooldown = 0.5 + _random.NextDouble() * 0.7;
        }

        private void DischargePart(Mover mover)
        {
            var part = mover.CurrentPart!;
            bool good = part.Status == PartStatus.Good;
            if (good)
            {
                GoodPartsCount++;
            }
            else
            {
                BadPartsCount++;
                _dataLogger.LogAlarm("Warning", "Quality", $"Part {part.TrackingNumber} failed final quality", true);
            }

            TotalPartsProduced++;
            part.ExitedPrimeMoverAt = DateTime.Now;
            part.CurrentLocation = "PrimeMoverExit";
            _dataLogger.LogPartEvent(part, "PrimeMoverExit", "Exit", $"Part exited as {part.Status}");
            _dataLogger.LogPartResult(part, TotalStationCount);
            _intel.RecordPartExit(part, good);
            Log($"{part.TrackingNumber} unloaded at exit as {part.Status}.");
            TriggerExitZoneBlink();

            mover.CurrentPart = null;
        }

        private void ReleaseMover(Mover mover)
        {
            var rt = _moverRuntime[mover.MoverId];
            rt.IgnoreStopKey = rt.DockKey;
            rt.IgnoreStopAngle = mover.Position;
            rt.DockKind = StopKind.None;
            rt.DockKey = null;
            rt.DockMachineId = -1;
            rt.DockTimer = 0;
            rt.DockedSeconds = 0;
            mover.TargetStation = -1;
            mover.State = mover.CurrentPart == null ? MoverState.Moving : MoverState.Loaded;
        }

        // ------------------------------------------------------------------ mover motion planning

        private void UpdateMovers(double dt)
        {
            AssignEntryAndPickups();

            var ordered = Movers.OrderBy(m => m.MoverId).ToList();
            foreach (var mover in ordered)
            {
                var rt = _moverRuntime[mover.MoverId];
                var axis = _moverAxes[mover.MoverId];

                if (rt.DockKind != StopKind.None)
                {
                    rt.DockedSeconds += dt;
                    axis.Stop(mover);
                    mover.State = rt.DockKind switch
                    {
                        StopKind.Entry => MoverState.AtEntryStation,
                        StopKind.Exit => MoverState.AtExitStation,
                        _ => mover.CurrentPart != null && mover.CurrentPart.NextMachineIndex == rt.DockMachineId
                            ? MoverState.AtLoadStation
                            : MoverState.AtUnloadStation
                    };
                    mover.TargetStation = rt.DockKind == StopKind.Machine ? rt.DockMachineId : -1;
                    TrackStateChange(mover);
                    continue;
                }

                if (rt.IgnoreStopKey != null && ForwardDelta(rt.IgnoreStopAngle, mover.Position) > 3.0 && ForwardDelta(rt.IgnoreStopAngle, mover.Position) < 300)
                {
                    rt.IgnoreStopKey = null;
                }

                double baseCruise = mover.CurrentPart == null ? FbXtsMoverAxis.EmptyCruiseVelocity : FbXtsMoverAxis.LoadedCruiseVelocity;
                var (stop, stopDistance) = FindNextStop(mover, rt);

                double limit = baseCruise * mover.VelocityScale;

                if (stop != null)
                {
                    if (stopDistance <= DockSnapDegrees && mover.Velocity < 4.0)
                    {
                        Dock(mover, rt, stop);
                        axis.Stop(mover);
                        continue;
                    }

                    limit = Math.Min(limit, Math.Sqrt(2 * PlanningDeceleration * Math.Max(0, stopDistance - 0.05)));
                }

                var ahead = FindMoverAhead(mover, out double gap);
                bool gapLimited = false;
                if (ahead != null)
                {
                    double free = gap - MoverPitchDegrees;
                    double aheadBraking = ahead.Velocity * ahead.Velocity * PlanningDeceleration / FbXtsMoverAxis.MaxDeceleration;
                    double gapLimit = Math.Sqrt(Math.Max(0, aheadBraking + 2 * PlanningDeceleration * Math.Max(0, free)));
                    if (gapLimit < limit)
                    {
                        limit = gapLimit;
                        gapLimited = true;
                    }
                }

                double step = axis.Cycle(mover, dt, IsRunning, limit);

                if (stop != null && step >= stopDistance)
                {
                    step = stopDistance;
                    axis.Stop(mover);
                }

                if (ahead != null)
                {
                    double maxStep = Math.Max(0, gap - MoverPitchDegrees);
                    if (step > maxStep)
                    {
                        step = maxStep;
                        axis.LimitVelocity(mover, ahead.Velocity);
                    }
                }

                if (step > 0)
                {
                    mover.Advance(step);
                }

                if (mover.Velocity < 0.5 && (gapLimited || stop != null))
                {
                    mover.State = MoverState.Queued;
                }
                else
                {
                    mover.State = mover.CurrentPart == null ? MoverState.Moving : MoverState.Loaded;
                }

                mover.TargetStation = -1;
                TrackStateChange(mover);
            }

        }

        private void Dock(Mover mover, MoverRuntime rt, StopPoint stop)
        {
            double delta = ForwardDelta(mover.Position, stop.Angle);
            if (delta > 0 && delta < 1.0)
            {
                mover.Advance(delta);
            }

            rt.DockKind = stop.Kind;
            rt.DockKey = stop.Key;
            rt.DockMachineId = stop.MachineId;
            rt.DockTimer = 0;
            rt.DockedSeconds = 0;

            if (stop.Kind == StopKind.Entry && _entryAssignedMoverId == mover.MoverId)
            {
                _entryAssignedMoverId = -1;
            }

            if (stop.Kind == StopKind.Machine && _pickupAssignments.TryGetValue(stop.MachineId, out int assigned) && assigned == mover.MoverId)
            {
                _pickupAssignments.Remove(stop.MachineId);
            }

            if (mover.CurrentPart != null)
            {
                mover.CurrentPart.CurrentLocation = stop.Kind switch
                {
                    StopKind.Machine => $"Mover-{mover.MoverId} docked at M{stop.MachineId}",
                    StopKind.Exit => $"Mover-{mover.MoverId} at exit",
                    _ => mover.CurrentPart.CurrentLocation
                };
            }
        }

        private (StopPoint? stop, double distance) FindNextStop(Mover mover, MoverRuntime rt)
        {
            StopPoint? best = null;
            double bestDistance = double.MaxValue;
            double brakingDistance = mover.Velocity * mover.Velocity / (2 * FbXtsMoverAxis.MaxDeceleration);

            foreach (var stop in _stopPoints)
            {
                if (stop.Key == rt.IgnoreStopKey)
                {
                    continue;
                }

                double distance = ForwardDelta(mover.Position, stop.Angle);
                if (distance >= bestDistance)
                {
                    continue;
                }

                if (!ShouldStopAt(mover, stop))
                {
                    continue;
                }

                if (distance + DockSnapDegrees < brakingDistance)
                {
                    continue;
                }

                best = stop;
                bestDistance = distance;
            }

            return (best, best == null ? double.MaxValue : bestDistance);
        }

        private bool ShouldStopAt(Mover mover, StopPoint stop)
        {
            var part = mover.CurrentPart;
            switch (stop.Kind)
            {
                case StopKind.Entry:
                    return part == null && _entryAssignedMoverId == mover.MoverId;

                case StopKind.Exit:
                    return part != null && (part.Status == PartStatus.Good || part.Status == PartStatus.Bad);

                case StopKind.Machine:
                    if (part != null)
                    {
                        if (part.Status == PartStatus.Good || part.Status == PartStatus.Bad || part.NextMachineIndex != stop.MachineId)
                        {
                            return false;
                        }

                        var machine = Machines.First(m => m.MachineId == stop.MachineId);
                        return IsMachineAvailable(machine);
                    }

                    return _pickupAssignments.TryGetValue(stop.MachineId, out int assigned) && assigned == mover.MoverId;
            }

            return false;
        }

        /// <summary>Reserves the entry dock and machine pickups for the best-placed empty movers.</summary>
        private void AssignEntryAndPickups()
        {
            var reserved = new HashSet<int>();

            foreach (var machine in Machines)
            {
                if (_pickupAssignments.TryGetValue(machine.MachineId, out int current))
                {
                    var assignedMover = Movers.FirstOrDefault(m => m.MoverId == current);
                    bool stillValid = assignedMover != null && assignedMover.CurrentPart == null
                                      && _moverRuntime[current].DockKind == StopKind.None && HasPickupDemand(machine);
                    if (stillValid)
                    {
                        reserved.Add(current);
                        continue;
                    }

                    _pickupAssignments.Remove(machine.MachineId);
                }

                if (!HasPickupDemand(machine) || DockedMoverAt(machine.MachineId) != null)
                {
                    continue;
                }

                double dockAngle = _machineLoadAngleByMachineId[machine.MachineId];
                double nearestSwapDistance = Movers
                    .Where(m => m.CurrentPart != null && m.CurrentPart.NextMachineIndex == machine.MachineId && IsMachineAvailable(machine))
                    .Select(m => ForwardDelta(m.Position, dockAngle))
                    .DefaultIfEmpty(double.MaxValue)
                    .Min();

                var candidate = Movers
                    .Where(m => m.CurrentPart == null && _moverRuntime[m.MoverId].DockKind == StopKind.None
                                && m.MoverId != _entryAssignedMoverId && !reserved.Contains(m.MoverId))
                    .Select(m => new { Mover = m, Distance = ForwardDelta(m.Position, dockAngle) })
                    .Where(x => x.Distance + DockSnapDegrees >= x.Mover.Velocity * x.Mover.Velocity / (2 * FbXtsMoverAxis.MaxDeceleration))
                    .OrderBy(x => x.Distance)
                    .FirstOrDefault();

                if (candidate != null && candidate.Distance < nearestSwapDistance)
                {
                    _pickupAssignments[machine.MachineId] = candidate.Mover.MoverId;
                    reserved.Add(candidate.Mover.MoverId);
                }
            }

            bool entryOccupied = Movers.Any(m => _moverRuntime[m.MoverId].DockKind == StopKind.Entry);
            if (_entryAssignedMoverId >= 0)
            {
                var assigned = Movers.First(m => m.MoverId == _entryAssignedMoverId);
                if (assigned.CurrentPart != null || reserved.Contains(assigned.MoverId) || !IsReleaseAllowed())
                {
                    _entryAssignedMoverId = -1;
                }
            }

            if (_entryAssignedMoverId < 0 && !entryOccupied && IsReleaseAllowed())
            {
                var candidate = Movers
                    .Where(m => m.CurrentPart == null && _moverRuntime[m.MoverId].DockKind == StopKind.None && !reserved.Contains(m.MoverId))
                    .Select(m => new { Mover = m, Distance = ForwardDelta(m.Position, EntryLoadAngle) })
                    .Where(x => x.Distance + DockSnapDegrees >= x.Mover.Velocity * x.Mover.Velocity / (2 * FbXtsMoverAxis.MaxDeceleration))
                    .OrderBy(x => x.Distance)
                    .FirstOrDefault();

                if (candidate != null)
                {
                    _entryAssignedMoverId = candidate.Mover.MoverId;
                }
            }
        }

        private bool HasPickupDemand(Machine machine)
        {
            if (machine.Maintenance == MaintenanceMode.InProgress)
            {
                return false;
            }

            if (machine.OutfeedNest != null)
            {
                return true;
            }

            // A finished module is on its way to (or staged at) the dock in the robot's gripper.
            var robot = Robots.FirstOrDefault(r => r.AssignedMachineId == machine.MachineId);
            return robot != null && robot.HeldPart != null && robot.SecondaryHeldPart == null
                   && robot.State is RobotState.PickingFromMachine or RobotState.MovingToMover or RobotState.PlacingOnMover;
        }

        private Mover? FindMoverAhead(Mover mover, out double gap)
        {
            Mover? ahead = null;
            gap = double.MaxValue;
            foreach (var other in Movers)
            {
                if (other.MoverId == mover.MoverId)
                {
                    continue;
                }

                double delta = ForwardDelta(mover.Position, other.Position);
                if (delta <= 0)
                {
                    delta = 360.0;
                }

                if (delta < gap)
                {
                    gap = delta;
                    ahead = other;
                }
            }

            return ahead;
        }

        private void TrackStateChange(Mover mover)
        {
            if (_lastMoverStates.TryGetValue(mover.MoverId, out var prev) && prev != mover.State)
            {
                _lastMoverStates[mover.MoverId] = mover.State;
                bool docking = mover.State is MoverState.AtLoadStation or MoverState.AtUnloadStation or MoverState.AtEntryStation or MoverState.AtExitStation;
                bool undocking = prev is MoverState.AtLoadStation or MoverState.AtUnloadStation or MoverState.AtEntryStation or MoverState.AtExitStation;
                if (docking || (undocking && !docking && mover.State != MoverState.AtLoadStation))
                {
                    Log($"Mover {mover.MoverId}: {prev} -> {mover.State}{(mover.TargetStation >= 0 ? $" (M{mover.TargetStation})" : string.Empty)}");
                }
            }
        }

        private static double ForwardDelta(double from, double to)
        {
            double delta = to - from;
            while (delta < 0) delta += 360.0;
            while (delta >= 360.0) delta -= 360.0;
            return delta;
        }

        // ------------------------------------------------------------------ traceability

        private void SyncPartHistoryLogs()
        {
            foreach (var part in GetActiveParts())
            {
                if (!_partHistoryLogIndex.TryGetValue(part.PartId, out int loggedUntil))
                {
                    _partHistoryLogIndex[part.PartId] = 0;
                    loggedUntil = 0;
                }

                while (loggedUntil < part.ProcessStep)
                {
                    string history = part.ProcessHistory[loggedUntil] ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(history))
                    {
                        _dataLogger.LogPartEvent(part, "ProcessStep", part.CurrentLocation, history);
                    }

                    loggedUntil++;
                }

                _partHistoryLogIndex[part.PartId] = loggedUntil;
            }
        }

        private IEnumerable<Part> GetActiveParts()
        {
            var parts = new Dictionary<Guid, Part>();

            foreach (var moverPart in Movers.Where(m => m.CurrentPart != null).Select(m => m.CurrentPart!))
            {
                parts[moverPart.PartId] = moverPart;
            }

            foreach (var robot in Robots)
            {
                if (robot.HeldPart != null) parts[robot.HeldPart.PartId] = robot.HeldPart;
                if (robot.SecondaryHeldPart != null) parts[robot.SecondaryHeldPart.PartId] = robot.SecondaryHeldPart;
            }

            foreach (var machine in Machines)
            {
                foreach (var stationPart in machine.Stations.Where(s => s.CurrentPart != null).Select(s => s.CurrentPart!))
                {
                    parts[stationPart.PartId] = stationPart;
                }

                if (machine.OutfeedNest != null)
                {
                    parts[machine.OutfeedNest.PartId] = machine.OutfeedNest;
                }
            }

            return parts.Values;
        }

        // ------------------------------------------------------------------ watchdogs

        private void RunWatchdogs(double deltaTime)
        {
            WatchMachines(deltaTime);
            WatchRobots(deltaTime);
            WatchDockedMovers();
        }

        private void WatchMachines(double deltaTime)
        {
            foreach (var machine in Machines)
            {
                string sig = BuildMachineWatchSignature(machine);
                if (_machineWatchSignature[machine.MachineId] == sig)
                {
                    _machineWatchSeconds[machine.MachineId] += deltaTime;
                }
                else
                {
                    _machineWatchSignature[machine.MachineId] = sig;
                    _machineWatchSeconds[machine.MachineId] = 0;
                }

                bool hasActivePart = machine.Stations.Any(s => s.CurrentPart != null);
                if (machine.SequencerState == PlcSequencerState.Run && hasActivePart && _machineWatchSeconds[machine.MachineId] > MachineStallThresholdSeconds)
                {
                    RecoverMachineStall(machine);
                    _machineWatchSeconds[machine.MachineId] = 0;
                    _machineWatchSignature[machine.MachineId] = BuildMachineWatchSignature(machine);
                }
            }
        }

        private void WatchRobots(double deltaTime)
        {
            foreach (var robot in Robots)
            {
                string sig = BuildRobotWatchSignature(robot);
                if (robot.IsStagedAtDock)
                {
                    _robotWatchSignature[robot.RobotId] = sig;
                    _robotWatchSeconds[robot.RobotId] = 0;
                    continue;
                }

                if (_robotWatchSignature[robot.RobotId] == sig)
                {
                    _robotWatchSeconds[robot.RobotId] += deltaTime;
                }
                else
                {
                    _robotWatchSignature[robot.RobotId] = sig;
                    _robotWatchSeconds[robot.RobotId] = 0;
                }

                if (robot.State != RobotState.Idle && _robotWatchSeconds[robot.RobotId] > RobotStallThresholdSeconds)
                {
                    RecoverRobotStall(robot);
                    _robotWatchSeconds[robot.RobotId] = 0;
                    _robotWatchSignature[robot.RobotId] = BuildRobotWatchSignature(robot);
                }
            }
        }

        private void WatchDockedMovers()
        {
            foreach (var mover in Movers)
            {
                var rt = _moverRuntime[mover.MoverId];
                if (rt.DockKind != StopKind.Machine || rt.DockedSeconds < MoverDockStallThresholdSeconds)
                {
                    continue;
                }

                var robot = Robots.FirstOrDefault(r => r.AssignedMachineId == rt.DockMachineId);
                if (robot != null && robot.State != RobotState.Idle)
                {
                    continue;
                }

                string code = $"WD-MOVER-DOCK-MV{mover.MoverId}";
                string msg = $"{code}: Mover docked at M{rt.DockMachineId} for {rt.DockedSeconds:F0}s without transfer. Released to recirculate.";
                _dataLogger.LogAlarm("Warning", "Watchdog", msg, true);
                Log(msg);
                RegisterWatchdogEvent(code, $"Mover-{mover.MoverId}", msg);
                ReleaseMover(mover);
            }
        }

        private void RecoverMachineStall(Machine machine)
        {
            string code = $"WD-MACH-STALL-M{machine.MachineId}";
            string msg = $"{code}: No machine transition for {MachineStallThresholdSeconds:F0}s. Controlled reset/retry.";
            _dataLogger.LogAlarm("Warning", "Watchdog", msg, true);
            Log(msg);
            RegisterWatchdogEvent(code, $"Machine-{machine.MachineId}", msg);

            var orphanedParts = new List<Part>();
            foreach (var station in machine.Stations)
            {
                if (station.CurrentPart != null)
                {
                    var part = station.CompletePart();
                    if (part != null)
                    {
                        orphanedParts.Add(part);
                    }
                }
            }

            machine.SequencerState = PlcSequencerState.Reset;
            machine.FaultActive = true;
            machine.FaultMessage = msg;

            foreach (var part in orphanedParts)
            {
                part.NextMachineIndex = machine.MachineId;
                part.Status = PartStatus.InProcess;
                part.CurrentLocation = $"Recovery Queue M{machine.MachineId}";
                if (!TryRehomePartToTrack(part))
                {
                    ScrapPartByWatchdog(part, "WD-PART-ORPHAN");
                }
            }
        }

        private void RecoverRobotStall(Robot robot)
        {
            string code = $"WD-ROBOT-STALL-R{robot.RobotId}";
            string msg = $"{code}: Robot stuck in {robot.State}. Controlled reset.";
            _dataLogger.LogAlarm("Warning", "Watchdog", msg, true);
            Log(msg);
            RegisterWatchdogEvent(code, $"Robot-{robot.RobotId}", msg);

            if (robot.SecondaryHeldPart != null)
            {
                var finished = robot.SecondaryHeldPart;
                robot.SecondaryHeldPart = null;
                var nestMachine = Machines.FirstOrDefault(m => m.MachineId == robot.AssignedMachineId);
                if (nestMachine != null && nestMachine.OutfeedNest == null)
                {
                    nestMachine.OutfeedNest = finished;
                }
                else if (!TryRehomePartToTrack(finished))
                {
                    ScrapPartByWatchdog(finished, "WD-ROBOT-PART-UNPLACED");
                }
            }

            if (robot.HeldPart != null)
            {
                var part = robot.HeldPart;
                bool outbound = robot.State is RobotState.PickingFromMachine or RobotState.MovingToMover or RobotState.PlacingOnMover;
                if (!outbound)
                {
                    part.NextMachineIndex = robot.AssignedMachineId;
                }

                var machine = Machines.FirstOrDefault(m => m.MachineId == robot.AssignedMachineId);
                var docked = DockedMoverAt(robot.AssignedMachineId);
                part.CurrentLocation = $"Robot-{robot.RobotId} recovery";
                if (docked != null && docked.CurrentPart == null)
                {
                    docked.CurrentPart = part;
                    _dataLogger.LogPartEvent(part, "WatchdogRehome", "Dock", $"Placed back on docked mover {docked.MoverId}");
                }
                else if (outbound && machine != null && machine.OutfeedNest == null)
                {
                    machine.OutfeedNest = part;
                }
                else if (!TryRehomePartToTrack(part))
                {
                    ScrapPartByWatchdog(part, "WD-ROBOT-PART-UNPLACED");
                }

                robot.ReleaseHeldPart();
            }

            robot.IsStagedAtDock = false;
            robot.TransitionTo(RobotState.Idle);
        }

        private bool TryRehomePartToTrack(Part part)
        {
            var mover = Movers.FirstOrDefault(m => m.CurrentPart == null && _moverRuntime[m.MoverId].DockKind == StopKind.None);
            if (mover == null)
            {
                return false;
            }

            mover.CurrentPart = part;
            mover.State = MoverState.Loaded;
            part.CurrentLocation = $"Mover-{mover.MoverId} (watchdog rehome)";
            _dataLogger.LogPartEvent(part, "WatchdogRehome", "Track", $"Rehomed to mover {mover.MoverId}");
            RegisterWatchdogEvent("WD-REHOME", part.TrackingNumber, $"Rehomed to mover {mover.MoverId}");
            Log($"{part.TrackingNumber} rehomed to mover {mover.MoverId} by watchdog.");
            return true;
        }

        private void ScrapPartByWatchdog(Part part, string code)
        {
            part.Status = PartStatus.Bad;
            part.ExitedPrimeMoverAt = DateTime.Now;
            part.CurrentLocation = "WatchdogScrap";
            _dataLogger.LogPartEvent(part, "WatchdogScrap", code, $"Scrapped by watchdog: {code}");
            _dataLogger.LogPartResult(part, TotalStationCount);
            RegisterWatchdogEvent(code, part.TrackingNumber, $"Part scrapped by watchdog ({code})");
            BadPartsCount++;
            TotalPartsProduced++;
            _intel.RecordPartExit(part, good: false);
            Log($"{part.TrackingNumber} scrapped by watchdog ({code}).");
        }

        private void RegisterWatchdogEvent(string code, string recoveredObject, string message)
        {
            if (!_watchdogStatus.TryGetValue(code, out var item))
            {
                item = new WatchdogStatusEntry { Code = code };
                _watchdogStatus[code] = item;
            }

            item.TriggerCount++;
            item.LastTriggeredAt = DateTime.Now;
            item.LastRecoveredObject = recoveredObject;
            item.LastMessage = message;
        }

        private static string BuildMachineWatchSignature(Machine machine)
        {
            var stationSig = string.Join("|", machine.Stations.Select(s => $"{s.StationId}:{s.Status}:{Math.Round(s.ElapsedTime, 1)}:{(s.CurrentPart?.TrackingNumber ?? "-")}"));
            return $"{machine.SequencerState}:{machine.CurrentStationIndex}:{stationSig}";
        }

        private static string BuildRobotWatchSignature(Robot robot)
        {
            return $"{robot.State}:{Math.Round(robot.ActionProgress, 1)}:{robot.HeldPart?.TrackingNumber ?? "-"}:{robot.SecondaryHeldPart?.TrackingNumber ?? "-"}";
        }

        // ------------------------------------------------------------------ periodic / intelligence

        private void CapturePeriodicSnapshot(double dt)
        {
            _snapshotTimer += dt;
            if (_snapshotTimer >= 2.0)
            {
                _snapshotTimer -= 2.0;
                _dataLogger.LogSnapshot(PrimeMoverEnteredCount, GoodPartsCount, BadPartsCount);
            }
        }

        private void UpdateIntelligence(double dt)
        {
            _intel.Step(dt, new LineObservation
            {
                IsRunning = IsRunning,
                Wip = ActivePartCount(),
                Movers = Movers
            });

            FlushIntelligenceEvents();
        }

        private void FlushIntelligenceEvents()
        {
            foreach (var evt in _intel.DrainEvents())
            {
                if (evt.RaiseAlarm || evt.Severity is "Warning" or "Critical")
                {
                    _dataLogger.LogAlarm(evt.Severity, evt.Source, evt.Message, evt.RaiseAlarm);
                }

                Log($"AI [{evt.Severity}] {evt.Source}: {evt.Message}");
            }
        }

        private void TriggerEntryZoneBlink() => _entryZoneBlinkRemaining = 0.8;

        private void TriggerExitZoneBlink() => _exitZoneBlinkRemaining = 0.8;

        private void UpdateZoneBlinkers(double deltaTime)
        {
            if (_entryZoneBlinkRemaining > 0)
            {
                _entryZoneBlinkRemaining = Math.Max(0, _entryZoneBlinkRemaining - deltaTime);
            }

            if (_exitZoneBlinkRemaining > 0)
            {
                _exitZoneBlinkRemaining = Math.Max(0, _exitZoneBlinkRemaining - deltaTime);
            }
        }

        private void Log(string message)
        {
            var msg = $"{DateTime.Now:HH:mm:ss.fff} | {message}";
            if (_dispatcher.IsShuttingDown || _dispatcher.CheckAccess())
            {
                LogGenerated?.Invoke(this, msg);
            }
            else
            {
                _dispatcher.BeginInvoke(() => LogGenerated?.Invoke(this, msg));
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
            _dataLogger.Dispose();
        }

        private enum StopKind
        {
            None,
            Machine,
            Entry,
            Exit
        }

        private sealed record StopPoint(string Key, double Angle, StopKind Kind, int MachineId);

        private sealed class MoverRuntime
        {
            public StopKind DockKind { get; set; }
            public string? DockKey { get; set; }
            public int DockMachineId { get; set; } = -1;
            public double DockTimer { get; set; }
            public double DockedSeconds { get; set; }
            public string? IgnoreStopKey { get; set; }
            public double IgnoreStopAngle { get; set; }
        }
    }

    public class WatchdogStatusEntry
    {
        public string Code { get; set; } = string.Empty;
        public int TriggerCount { get; set; }
        public DateTime LastTriggeredAt { get; set; }
        public string LastRecoveredObject { get; set; } = "-";
        public string LastMessage { get; set; } = string.Empty;
    }

    public class SafetyGateStatus
    {
        public string GateName { get; set; } = string.Empty;
        public bool IsPassing { get; set; }
        public string Detail { get; set; } = string.Empty;
    }
}
