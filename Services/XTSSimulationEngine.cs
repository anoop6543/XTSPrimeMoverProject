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

        private DateTime _lastUpdate;
        private readonly double[] _machineLoadAngles = { 45, 135, 225, 315 };
        private readonly double _exitAngle = 0;
        private int _nextPartCounter;
        private int _trackingCounter;
        private int _snapshotTickCounter;

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

        public IReadOnlyList<PartHistoryEventRecord> GetPartHistory(string trackingNumber)
        {
            return _dataLogger.GetPartHistory(trackingNumber);
        }

        public PartSummaryRecord? GetPartSummary(string trackingNumber)
        {
            return _dataLogger.GetPartSummary(trackingNumber);
        }

        public IReadOnlyList<string> GetExportableTables()
        {
            return _dataLogger.GetExportableTables();
        }

        public string ExportTableToCsv(string tableName, string? exportDirectory = null)
        {
            return _dataLogger.ExportTableToCsv(tableName, exportDirectory);
        }

        public string GetDefaultExportDirectory()
        {
            return System.IO.Path.Combine(AppContext.BaseDirectory, "Exports");
        }

        public event EventHandler? StateChanged;

        public XTSSimulationEngine()
        {
            _random = new Random();
            _dataLogger = new SimulationDataLogger();
            _partHistoryLogIndex = new Dictionary<Guid, int>();

            InitializeSystem();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += OnTimerTick;
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

            TotalStationCount = Machines.Sum(m => m.Stations.Count);
            TotalPartsProduced = 0;
            GoodPartsCount = 0;
            BadPartsCount = 0;
            PrimeMoverEnteredCount = 0;
            _nextPartCounter = 0;
            _trackingCounter = 1;
            _snapshotTickCounter = 0;
            _partHistoryLogIndex.Clear();

            _dataLogger.SeedRecipes(Machines);
            _dataLogger.LogAlarm("Info", "Simulation", "System initialized", false);
        }

        public void Start()
        {
            IsRunning = true;
            _lastUpdate = DateTime.Now;
            _timer.Start();
            _dataLogger.LogAlarm("Info", "Simulation", "System started", true);
        }

        public void Stop()
        {
            IsRunning = false;
            _timer.Stop();
            _dataLogger.LogAlarm("Info", "Simulation", "System stopped", false);
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
                Stop();
            }
        }

        private void Update(double deltaTime)
        {
            UpdateMovers(deltaTime);
            UpdateRobots(deltaTime);
            UpdateMachines(deltaTime);
            ProcessMachineLogic();
            ProcessRobotLogic();
            SyncPartHistoryLogs();
            ProcessPrimeMoverExits();
            LoadNewParts();
            CapturePeriodicSnapshot();
        }

        private void UpdateMovers(double deltaTime)
        {
            foreach (var mover in Movers)
            {
                if (mover.State == MoverState.Moving || mover.State == MoverState.Loaded)
                {
                    mover.UpdatePosition(deltaTime);
                }

                int matchedStation = -1;
                for (int i = 0; i < _machineLoadAngles.Length; i++)
                {
                    if (mover.IsAtStation(_machineLoadAngles[i]))
                    {
                        matchedStation = i;
                        break;
                    }
                }

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

                    continue;
                }

                if (mover.CurrentPart == null)
                {
                    mover.State = MoverState.AtUnloadStation;
                    mover.TargetStation = matchedStation;
                }
                else if (mover.CurrentPart.NextMachineIndex == matchedStation)
                {
                    mover.State = MoverState.AtLoadStation;
                    mover.TargetStation = matchedStation;
                    mover.CurrentPart.CurrentLocation = $"Mover-{mover.MoverId} at M{matchedStation}";
                }
                else
                {
                    mover.State = MoverState.Loaded;
                    mover.TargetStation = -1;
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
                machine.Update(deltaTime);
            }
        }

        private void ProcessMachineLogic()
        {
            foreach (var machine in Machines)
            {
                machine.TryMoveToNextStation();
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

            _nextPartCounter = _random.Next(14, 35);
        }

        private void CapturePeriodicSnapshot()
        {
            _snapshotTickCounter++;
            if (_snapshotTickCounter % 40 == 0)
            {
                _dataLogger.LogSnapshot(PrimeMoverEnteredCount, GoodPartsCount, BadPartsCount);
            }
        }
    }
}
