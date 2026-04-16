using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    public class XTSSimulationEngine
    {
        private readonly DispatcherTimer _timer;
        private readonly Random _random;
        private readonly SimulationDataLogger _dataLogger;

        private readonly Dictionary<Guid, int> _partHistoryLogIndex;
        private readonly Dictionary<int, FbXtsMoverAxis> _moverAxes;
        private readonly Dictionary<int, FbMachineCycle> _machineCycles;
        private readonly Dictionary<int, bool> _machineAlarmStates;
        private readonly Dictionary<int, MoverState> _lastMoverStates;

        private readonly Dictionary<int, double> _machineWatchSeconds;
        private readonly Dictionary<int, string> _machineWatchSignature;
        private readonly Dictionary<int, double> _robotWatchSeconds;
        private readonly Dictionary<int, string> _robotWatchSignature;
        private readonly Dictionary<int, double> _moverWatchSeconds;
        private readonly Dictionary<int, string> _moverWatchSignature;

        private DateTime _lastUpdate;
        private readonly double[] _machineLoadAngles = { 45, 135, 225, 315 };
        private readonly double _exitAngle = 0;
        private int _nextPartCounter;
        private int _trackingCounter;
        private int _snapshotTickCounter;

        private const double MoverMinGapDegrees = 16.0;
        private const double MachineStallThresholdSeconds = 14.0;
        private const double RobotStallThresholdSeconds = 9.0;
        private const double MoverStallThresholdSeconds = 11.0;

        public List<Mover> Movers { get; private set; }
        public List<Machine> Machines { get; private set; }
        public List<Robot> Robots { get; private set; }

        public int TotalPartsProduced { get; private set; }
        public int GoodPartsCount { get; private set; }
        public int BadPartsCount { get; private set; }
        public int PrimeMoverEnteredCount { get; private set; }
        public int PrimeMoverExitedCount => GoodPartsCount + BadPartsCount;
        public bool IsRunning { get; private set; }
        public int TotalStationCount { get; private set; }
        public string DatabasePath => _dataLogger.DatabasePath;
        public bool EntryZoneBlink => _entryZoneBlinkRemaining > 0;
        public bool ExitZoneBlink => _exitZoneBlinkRemaining > 0;

        public event EventHandler? StateChanged;
        public event EventHandler<string>? LogGenerated;

        private readonly Dictionary<string, WatchdogStatusEntry> _watchdogStatus;

        private double _entryZoneBlinkRemaining;
        private double _exitZoneBlinkRemaining;

        public XTSSimulationEngine()
        {
            _random = new Random();
            _dataLogger = new SimulationDataLogger();

            _partHistoryLogIndex = new Dictionary<Guid, int>();
            _moverAxes = new Dictionary<int, FbXtsMoverAxis>();
            _machineCycles = new Dictionary<int, FbMachineCycle>();
            _machineAlarmStates = new Dictionary<int, bool>();
            _lastMoverStates = new Dictionary<int, MoverState>();

            _machineWatchSeconds = new Dictionary<int, double>();
            _machineWatchSignature = new Dictionary<int, string>();
            _robotWatchSeconds = new Dictionary<int, double>();
            _robotWatchSignature = new Dictionary<int, string>();
            _moverWatchSeconds = new Dictionary<int, double>();
            _moverWatchSignature = new Dictionary<int, string>();

            _watchdogStatus = new Dictionary<string, WatchdogStatusEntry>(StringComparer.OrdinalIgnoreCase);

            InitializeSystem();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += OnTimerTick;
        }

        public IReadOnlyList<PartHistoryEventRecord> GetPartHistory(string trackingNumber) => _dataLogger.GetPartHistory(trackingNumber);
        public PartSummaryRecord? GetPartSummary(string trackingNumber) => _dataLogger.GetPartSummary(trackingNumber);
        public IReadOnlyList<string> GetExportableTables() => _dataLogger.GetExportableTables();
        public string ExportTableToCsv(string tableName, string? exportDirectory = null) => _dataLogger.ExportTableToCsv(tableName, exportDirectory);
        public string GetDefaultExportDirectory() => System.IO.Path.Combine(AppContext.BaseDirectory, "Exports");

        public IReadOnlyList<WatchdogStatusEntry> GetWatchdogStatus()
        {
            return _watchdogStatus.Values
                .OrderByDescending(x => x.LastTriggeredAt)
                .ThenBy(x => x.Code)
                .ToList();
        }

        private void InitializeSystem()
        {
            Movers = new List<Mover>();
            for (int i = 0; i < 10; i++)
            {
                Movers.Add(new Mover(i));
            }

            Machines = new List<Machine>
            {
                new Machine(0, "Laser Welder", MachineType.LaserWelding, _machineLoadAngles[0]),
                new Machine(1, "Assembler", MachineType.PrecisionAssembly, _machineLoadAngles[1]),
                new Machine(2, "Inspector", MachineType.QualityInspection, _machineLoadAngles[2]),
                new Machine(3, "Tester", MachineType.FunctionalTesting, _machineLoadAngles[3])
            };

            Robots = new List<Robot>();
            for (int i = 0; i < 4; i++)
            {
                Robots.Add(new Robot(i, i));
            }

            _moverAxes.Clear();
            _machineCycles.Clear();
            _machineAlarmStates.Clear();
            _lastMoverStates.Clear();

            foreach (var mover in Movers)
            {
                _moverAxes[mover.MoverId] = new FbXtsMoverAxis();
                _lastMoverStates[mover.MoverId] = mover.State;
            }

            foreach (var machine in Machines)
            {
                _machineCycles[machine.MachineId] = new FbMachineCycle();
                _machineAlarmStates[machine.MachineId] = false;
            }

            TotalStationCount = Machines.Sum(m => m.Stations.Count);
            TotalPartsProduced = 0;
            GoodPartsCount = 0;
            BadPartsCount = 0;
            PrimeMoverEnteredCount = 0;
            _nextPartCounter = 0;
            _trackingCounter = 1;
            _snapshotTickCounter = 0;
            _partHistoryLogIndex.Clear();
            _watchdogStatus.Clear();
            _entryZoneBlinkRemaining = 0;
            _exitZoneBlinkRemaining = 0;

            InitializeWatchdogs();

            _dataLogger.SeedRecipes(Machines);
            _dataLogger.LogAlarm("Info", "Simulation", "System initialized", false);
            Log("Simulation initialized.");
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

            _moverWatchSeconds.Clear();
            _moverWatchSignature.Clear();
            foreach (var mover in Movers)
            {
                _moverWatchSeconds[mover.MoverId] = 0;
                _moverWatchSignature[mover.MoverId] = BuildMoverWatchSignature(mover);
            }
        }

        public void Start()
        {
            IsRunning = true;
            _lastUpdate = DateTime.Now;
            _timer.Start();
            _dataLogger.LogAlarm("Info", "Simulation", "System started", true);
            Log("System started.");
        }

        public void Stop()
        {
            IsRunning = false;
            _timer.Stop();
            _dataLogger.LogAlarm("Info", "Simulation", "System stopped", false);
            Log("System stopped.");
        }

        public void Reset()
        {
            Stop();
            InitializeSystem();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            try
            {
                DateTime now = DateTime.Now;
                double deltaTime = (now - _lastUpdate).TotalSeconds;
                _lastUpdate = now;

                Update(deltaTime);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _dataLogger.LogError("EngineTick", ex.Message, ex.ToString());
                Log($"FATAL: {ex.Message}");
                Stop();
            }
        }

        private void Update(double deltaTime)
        {
            UpdateMovers(deltaTime);
            UpdateRobots(deltaTime);
            UpdateMachines(deltaTime);
            ProcessRobotLogic();
            SyncPartHistoryLogs();
            ProcessPrimeMoverExits();
            LoadNewParts();
            RunWatchdogs(deltaTime);
            CapturePeriodicSnapshot();
            UpdateZoneBlinkers(deltaTime);
        }

        private void UpdateMovers(double deltaTime)
        {
            foreach (var mover in Movers)
            {
                int matchedStation = -1;
                for (int i = 0; i < _machineLoadAngles.Length; i++)
                {
                    if (mover.IsAtStation(_machineLoadAngles[i]))
                    {
                        matchedStation = i;
                        break;
                    }
                }

                bool holdAtStation = false;

                if (matchedStation == -1)
                {
                    if (mover.CurrentPart == null)
                    {
                        mover.State = MoverState.Moving;
                        mover.TargetStation = -1;
                    }
                    else if (mover.State != MoverState.Loaded)
                    {
                        mover.State = MoverState.Loaded;
                        mover.TargetStation = -1;
                    }
                }
                else if (mover.CurrentPart == null)
                {
                    bool robotWaitingToReturnPart = Robots.Any(r =>
                        r.AssignedMachineId == matchedStation &&
                        r.State == RobotState.PlacingOnMover &&
                        r.HeldPart != null);

                    if (robotWaitingToReturnPart)
                    {
                        mover.State = MoverState.AtUnloadStation;
                        mover.TargetStation = matchedStation;
                        holdAtStation = true;
                    }
                    else
                    {
                        mover.State = MoverState.Moving;
                        mover.TargetStation = -1;
                    }
                }
                else if (mover.CurrentPart.NextMachineIndex == matchedStation)
                {
                    mover.State = MoverState.AtLoadStation;
                    mover.TargetStation = matchedStation;
                    mover.CurrentPart.CurrentLocation = $"Mover-{mover.MoverId} at M{matchedStation}";
                    holdAtStation = true;
                }
                else
                {
                    mover.State = MoverState.Loaded;
                    mover.TargetStation = -1;
                }

                if (IsTooCloseToMoverAhead(mover, MoverMinGapDegrees))
                {
                    holdAtStation = true;
                    if (mover.State == MoverState.Moving)
                    {
                        mover.State = MoverState.Loaded;
                    }
                }

                _moverAxes[mover.MoverId].Cycle(mover, deltaTime, IsRunning, holdAtStation);

                if (_lastMoverStates.TryGetValue(mover.MoverId, out var prev) && prev != mover.State)
                {
                    _lastMoverStates[mover.MoverId] = mover.State;
                    Log($"Mover {mover.MoverId}: {prev} -> {mover.State}");
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
            }
        }

        private void ProcessRobotLogic()
        {
            for (int i = 0; i < Robots.Count; i++)
            {
                var robot = Robots[i];
                var machine = Machines[i];

                if (robot.State == RobotState.Idle)
                {
                    if (machine.HasCompletedPartReady())
                    {
                        var completedPart = machine.UnloadPart();
                        if (completedPart != null)
                        {
                            completedPart.NextMachineIndex = i + 1;
                            completedPart.CurrentLocation = $"Robot-{robot.RobotId} from {machine.Name}";
                            _dataLogger.LogMachineExit(machine, completedPart);
                            _dataLogger.LogPartEvent(completedPart, "MachineExit", machine.Name, $"Exited {machine.Name}");
                            Log($"{completedPart.TrackingNumber} exited {machine.Name}");

                            if (i == Machines.Count - 1)
                            {
                                completedPart.Status = completedPart.HasDefect ? PartStatus.Bad : PartStatus.Good;
                            }
                            else if (i == 1)
                            {
                                completedPart.Status = PartStatus.Assembled;
                            }
                            else if (i == 2)
                            {
                                completedPart.Status = PartStatus.Tested;
                            }
                            else
                            {
                                completedPart.Status = PartStatus.InProcess;
                            }

                            robot.StartPickFromMachine(completedPart);
                        }
                    }
                    else if (machine.CanAcceptPart())
                    {
                        var loadMover = Movers.FirstOrDefault(m =>
                            m.State == MoverState.AtLoadStation &&
                            m.TargetStation == i &&
                            m.CurrentPart != null &&
                            m.CurrentPart.NextMachineIndex == i);

                        if (loadMover?.CurrentPart != null)
                        {
                            var part = loadMover.CurrentPart;
                            loadMover.CurrentPart = null;
                            loadMover.State = MoverState.Moving;
                            part.CurrentLocation = $"Robot-{robot.RobotId} from Mover-{loadMover.MoverId}";
                            _dataLogger.LogPartEvent(part, "MoverUnload", $"M{i}", $"Robot picked from mover {loadMover.MoverId}");
                            Log($"{part.TrackingNumber} picked from mover {loadMover.MoverId} into {machine.Name}");
                            robot.StartPickFromMover(part);
                        }
                    }

                    continue;
                }

                if (!robot.IsStepComplete())
                {
                    continue;
                }

                if (robot.State == RobotState.PlacingInMachine && robot.HeldPart != null)
                {
                    if (machine.CanAcceptPart())
                    {
                        var part = robot.HeldPart;
                        part.Status = PartStatus.InProcess;
                        part.CurrentLocation = $"{machine.Name} - S0";
                        machine.LoadPart(part);
                        _dataLogger.LogMachineEntry(machine, part);
                        _dataLogger.LogPartEvent(part, "MachineEntry", machine.Name, $"Entered {machine.Name}");
                        Log($"{part.TrackingNumber} loaded into {machine.Name}");
                        robot.ReleaseHeldPart();
                    }
                    else
                    {
                        continue;
                    }
                }
                else if (robot.State == RobotState.PlacingOnMover && robot.HeldPart != null)
                {
                    var unloadMover = Movers.FirstOrDefault(m =>
                        m.State == MoverState.AtUnloadStation &&
                        m.TargetStation == i &&
                        m.CurrentPart == null);

                    if (unloadMover != null)
                    {
                        unloadMover.CurrentPart = robot.HeldPart;
                        unloadMover.State = MoverState.Loaded;
                        unloadMover.CurrentPart.CurrentLocation = $"Mover-{unloadMover.MoverId}";
                        _dataLogger.LogPartEvent(unloadMover.CurrentPart, "MoverLoad", $"M{i}", $"Robot placed on mover {unloadMover.MoverId}");
                        Log($"{unloadMover.CurrentPart.TrackingNumber} placed onto mover {unloadMover.MoverId} from M{i}");
                        robot.ReleaseHeldPart();
                    }
                    else
                    {
                        continue;
                    }
                }

                robot.AdvanceState();
            }
        }

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

            foreach (var robotPart in Robots.Where(r => r.HeldPart != null).Select(r => r.HeldPart!))
            {
                parts[robotPart.PartId] = robotPart;
            }

            foreach (var machinePart in Machines.SelectMany(m => m.Stations).Where(s => s.CurrentPart != null).Select(s => s.CurrentPart!))
            {
                parts[machinePart.PartId] = machinePart;
            }

            return parts.Values;
        }

        private void ProcessPrimeMoverExits()
        {
            foreach (var mover in Movers.Where(m => m.CurrentPart != null).ToList())
            {
                var part = mover.CurrentPart!;
                if ((part.Status == PartStatus.Good || part.Status == PartStatus.Bad) && mover.IsAtStation(_exitAngle))
                {
                    if (part.Status == PartStatus.Good)
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
                    Log($"{part.TrackingNumber} exited prime mover as {part.Status}");
                    TriggerExitZoneBlink();

                    mover.CurrentPart = null;
                    mover.State = MoverState.Moving;
                }
            }
        }

        private void LoadNewParts()
        {
            if (_nextPartCounter > 0)
            {
                _nextPartCounter--;
                return;
            }

            var emptyMover = Movers.FirstOrDefault(m =>
                m.CurrentPart == null &&
                m.State != MoverState.AtLoadStation &&
                m.State != MoverState.AtUnloadStation);

            if (emptyMover == null)
            {
                return;
            }

            var newPart = new Part
            {
                TrackingNumber = $"TRK-{_trackingCounter:00000}",
                EnteredPrimeMoverAt = DateTime.Now,
                CurrentLocation = $"Mover-{emptyMover.MoverId}"
            };

            _trackingCounter++;
            PrimeMoverEnteredCount++;

            newPart.AddProcessHistory("Loaded on XTS Mover");
            emptyMover.CurrentPart = newPart;
            emptyMover.State = MoverState.Loaded;

            _dataLogger.LogPartCreated(newPart);
            _dataLogger.LogPartEvent(newPart, "PrimeMoverEntry", "Entry", $"Loaded on mover {emptyMover.MoverId}");
            Log($"{newPart.TrackingNumber} entered prime mover on mover {emptyMover.MoverId}");
            TriggerEntryZoneBlink();

            _nextPartCounter = _random.Next(14, 35);
        }

        private void TriggerEntryZoneBlink()
        {
            _entryZoneBlinkRemaining = 0.8;
        }

        private void TriggerExitZoneBlink()
        {
            _exitZoneBlinkRemaining = 0.8;
        }

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

        private bool IsTooCloseToMoverAhead(Mover mover, double minGapDegrees)
        {
            var ahead = Movers
                .Where(m => m.MoverId != mover.MoverId)
                .Select(m => new { Mover = m, Delta = ForwardDelta(mover.Position, m.Position) })
                .Where(x => x.Delta > 0)
                .OrderBy(x => x.Delta)
                .FirstOrDefault();

            if (ahead == null)
            {
                return false;
            }

            bool aheadLikelyBlocking = ahead.Mover.State == MoverState.AtLoadStation
                                       || ahead.Mover.State == MoverState.AtUnloadStation
                                       || Math.Abs(ahead.Mover.Velocity) < 0.05;

            return aheadLikelyBlocking && ahead.Delta < minGapDegrees;
        }

        private static double ForwardDelta(double from, double to)
        {
            double delta = to - from;
            if (delta < 0)
            {
                delta += 360.0;
            }

            return delta;
        }

        private void RunWatchdogs(double deltaTime)
        {
            WatchMachines(deltaTime);
            WatchRobots(deltaTime);
            WatchMovers(deltaTime);
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

        private void WatchMovers(double deltaTime)
        {
            foreach (var mover in Movers)
            {
                string sig = BuildMoverWatchSignature(mover);
                if (_moverWatchSignature[mover.MoverId] == sig)
                {
                    _moverWatchSeconds[mover.MoverId] += deltaTime;
                }
                else
                {
                    _moverWatchSignature[mover.MoverId] = sig;
                    _moverWatchSeconds[mover.MoverId] = 0;
                }

                bool waitingState = mover.State == MoverState.AtLoadStation || mover.State == MoverState.AtUnloadStation;
                if (waitingState && _moverWatchSeconds[mover.MoverId] > MoverStallThresholdSeconds)
                {
                    RecoverMoverStall(mover);
                    _moverWatchSeconds[mover.MoverId] = 0;
                    _moverWatchSignature[mover.MoverId] = BuildMoverWatchSignature(mover);
                }
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

            if (robot.HeldPart != null)
            {
                var part = robot.HeldPart;
                int machineId = robot.AssignedMachineId;
                if (robot.State == RobotState.PickingFromMachine || robot.State == RobotState.MovingToMover || robot.State == RobotState.PlacingOnMover)
                {
                    part.NextMachineIndex = machineId + 1;
                }
                else
                {
                    part.NextMachineIndex = machineId;
                }

                part.CurrentLocation = $"Robot-{robot.RobotId} recovery";
                if (!TryRehomePartToTrack(part))
                {
                    ScrapPartByWatchdog(part, "WD-ROBOT-PART-UNPLACED");
                }

                robot.ReleaseHeldPart();
            }

            robot.State = RobotState.Idle;
            robot.ActionProgress = 0;
        }

        private void RecoverMoverStall(Mover mover)
        {
            string code = $"WD-MOVER-STALL-MV{mover.MoverId}";
            string msg = $"{code}: Mover stalled in {mover.State}. Releasing queue hold.";
            _dataLogger.LogAlarm("Warning", "Watchdog", msg, true);
            Log(msg);
            RegisterWatchdogEvent(code, $"Mover-{mover.MoverId}", msg);

            mover.TargetStation = -1;
            mover.State = mover.CurrentPart == null ? MoverState.Moving : MoverState.Loaded;
            mover.Position = (mover.Position + 2.0) % 360.0;
        }

        private bool TryRehomePartToTrack(Part part)
        {
            var mover = Movers.FirstOrDefault(m => m.CurrentPart == null && m.State != MoverState.AtLoadStation && m.State != MoverState.AtUnloadStation);
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

        private string BuildMachineWatchSignature(Machine machine)
        {
            var stationSig = string.Join("|", machine.Stations.Select(s => $"{s.StationId}:{s.Status}:{Math.Round(s.ElapsedTime, 1)}:{(s.CurrentPart?.TrackingNumber ?? "-")}"));
            return $"{machine.SequencerState}:{machine.CurrentStationIndex}:{stationSig}";
        }

        private static string BuildRobotWatchSignature(Robot robot)
        {
            return $"{robot.State}:{Math.Round(robot.ActionProgress, 1)}:{robot.HeldPart?.TrackingNumber ?? "-"}";
        }

        private static string BuildMoverWatchSignature(Mover mover)
        {
            return $"{mover.State}:{mover.TargetStation}:{Math.Round(mover.Position, 1)}:{Math.Round(mover.Velocity, 2)}:{mover.CurrentPart?.TrackingNumber ?? "-"}";
        }

        private void CapturePeriodicSnapshot()
        {
            _snapshotTickCounter++;
            if (_snapshotTickCounter % 40 == 0)
            {
                _dataLogger.LogSnapshot(PrimeMoverEnteredCount, GoodPartsCount, BadPartsCount);
            }
        }

        private void Log(string message)
        {
            LogGenerated?.Invoke(this, $"{DateTime.Now:HH:mm:ss.fff} | {message}");
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
}
