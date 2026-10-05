using System;
using System.Collections.Generic;
using System.Linq;
using XTSPrimeMoverProject.Models;
using XTSPrimeMoverProject.Services;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    /// <summary>Per-tick observation of the line handed to the intelligence hub by the engine.</summary>
    public sealed class LineObservation
    {
        public bool IsRunning { get; init; }
        public int Wip { get; init; }
        public IReadOnlyList<Mover> Movers { get; init; } = Array.Empty<Mover>();
    }

    /// <summary>
    /// The AI layer of the line. Runs on the simulation thread inside the engine lock:
    /// digital twins, measurement-driven quality + SPC, residual anomaly detection, RUL prognostics,
    /// OEE, active-period bottleneck detection, energy, throughput forecasting, maintenance lifecycle,
    /// closed-loop autopilot and the explainable copilot. Publishes immutable snapshots for the HMI.
    /// </summary>
    public sealed class LineIntelligenceHub : IStationProcessModel
    {
        public const double PlannedMaintenanceSeconds = 9.0;
        public const double BreakdownRepairSeconds = 35.0;

        private const double SensorSampleInterval = 0.5;
        private const double HistoryInterval = 1.0;
        private const double AnalyticsInterval = 1.0;
        private const double ThroughputWindowSeconds = 120.0;
        private const double ThroughputHistoryInterval = 5.0;
        private const int FlowTimeWindow = 20;

        private readonly Random _rng;
        private readonly List<MachineCell> _cells = new();
        private readonly Dictionary<int, MachineCell> _cellsById = new();
        private readonly Dictionary<Station, StationContext> _stations = new(ReferenceEqualityComparer.Instance);
        private readonly List<RobotDigitalTwin> _robotTwins = new();
        private readonly BottleneckDetector _bottleneck;
        private readonly EnergyMonitor _energy = new();
        private readonly HoltForecaster _throughputForecast = new(0.4, 0.15);
        private readonly Queue<double> _exitTimes = new();
        private readonly Queue<double> _flowTimes = new();
        private readonly RingBuffer _throughputHistory = new(120);
        private readonly RingBuffer _powerHistory = new(180);
        private readonly RingBuffer _oeeHistory = new(120);
        private readonly List<IntelligenceEvent> _events = new();
        private readonly CopilotReasoner _copilot = new();
        private readonly double _idealLineCycleSeconds;

        private double _sensorTimer;
        private double _historyTimer;
        private double _analyticsTimer;
        private double _throughputTimer;
        private double _runningSeconds;
        private int _good;
        private int _bad;
        private int _exits;
        private int _wip;
        private double _lastDeferredLog = double.NegativeInfinity;
        private long _version;
        private IReadOnlyList<CopilotInsight> _insights = Array.Empty<CopilotInsight>();
        private volatile IntelligenceSnapshot _snapshot = IntelligenceSnapshot.Empty;

        public LineIntelligenceHub(IReadOnlyList<Machine> machines, IReadOnlyList<Robot> robots, int seed)
        {
            _rng = new Random(seed);
            Machines = machines;
            Robots = robots;

            int index = 0;
            foreach (var machine in machines)
            {
                // Assets are commissioned with some service history: random initial wear.
                var twin = new MachineDigitalTwin(machine, 0.05 + _rng.NextDouble() * 0.4);
                var cell = new MachineCell(machine, twin, index++);
                _cells.Add(cell);
                _cellsById[machine.MachineId] = cell;

                for (int s = 0; s < machine.Stations.Count; s++)
                {
                    var station = machine.Stations[s];
                    var spec = EvModuleQualityCatalog.GetCharacteristic(machine.Type, s);
                    if (spec == null)
                    {
                        continue;
                    }

                    var spc = new SpcMonitor(spec);
                    cell.Spc.Add(spc);
                    if (spec.IsKeyCharacteristic)
                    {
                        cell.KeySpc = spc;
                    }

                    _stations[station] = new StationContext(cell, spec, spc, s == machine.Stations.Count - 1);
                }
            }

            foreach (var robot in robots)
            {
                _robotTwins.Add(new RobotDigitalTwin(robot, _rng.NextDouble() * 0.25));
            }

            _bottleneck = new BottleneckDetector(_cells.Count);
            _idealLineCycleSeconds = machines.Count == 0 ? 1 : machines.Max(m => m.IdealCycleTimeSeconds) + 3 * 0.8;
            Autopilot = new AutopilotController();
        }

        public IReadOnlyList<Machine> Machines { get; }
        public IReadOnlyList<Robot> Robots { get; }
        public AutopilotController Autopilot { get; }
        public CopilotReasoner Copilot => _copilot;
        public double SimTime { get; private set; }

        /// <summary>Latest published snapshot; immutable and safe to read from any thread.</summary>
        public IntelligenceSnapshot Snapshot => _snapshot;

        public MachineDigitalTwin GetTwin(int machineId) => _cellsById[machineId].Twin;
        public RobotDigitalTwin GetRobotTwin(int robotId) => _robotTwins.First(r => r.Robot.RobotId == robotId);

        // ------------------------------------------------------------------ station process model

        public double GetCycleTimeFactor(Station station)
        {
            return _stations.TryGetValue(station, out var ctx) ? ctx.Cell.Twin.CycleTimeFactor : 1.0;
        }

        public StationMeasurement? Measure(Station station, Part part)
        {
            if (!_stations.TryGetValue(station, out var ctx))
            {
                return null;
            }

            var spec = ctx.Spec;
            double effect = ctx.Cell.Twin.Effect;
            double sigma = spec.HealthySigma;
            double mean = spec.Nominal + spec.MeanShiftAtFailureSigma * sigma * effect;
            double sd = sigma * (1.0 + spec.SigmaGrowthAtFailure * effect);
            double value = NormalDistribution.Sample(_rng, mean, sd);
            if (!spec.LowerSpecLimit.HasValue && spec.Nominal >= 0)
            {
                value = Math.Max(0, value);
            }

            bool inSpec = spec.IsInSpec(value);
            var violation = ctx.Spc.Add(value);
            if (violation != null && ctx.Spc.SamplesSinceViolation == 0 && ctx.LastViolationLogged + 10 <= ctx.Spc.SampleCount)
            {
                ctx.LastViolationLogged = ctx.Spc.SampleCount;
                Raise("Warning", $"SPC {ctx.Cell.Machine.Name}", $"{spec.Name}: {violation.Description} (value {value:0.###}{spec.Unit}).", alarm: spec.IsKeyCharacteristic);
            }

            if (!inSpec)
            {
                ctx.Cell.NokParts.Add(part.PartId);
            }

            if (ctx.IsLastStation)
            {
                ctx.Cell.Oee.RecordPart(ctx.Cell.NokParts.Remove(part.PartId));
            }

            return new StationMeasurement
            {
                Characteristic = spec.Name,
                Unit = spec.Unit,
                Value = value,
                Nominal = spec.Nominal,
                LowerSpecLimit = spec.LowerSpecLimit,
                UpperSpecLimit = spec.UpperSpecLimit,
                InSpec = inSpec,
                StationName = station.Name,
                MachineId = ctx.Cell.Machine.MachineId
            };
        }

        // ------------------------------------------------------------------ events from the engine

        public void RecordPartEntry(Part part)
        {
            part.EnteredSimTime = SimTime;
        }

        public void RecordPartExit(Part part, bool good)
        {
            _exits++;
            if (good) _good++; else _bad++;
            _exitTimes.Enqueue(SimTime);
            if (part.EnteredSimTime > 0 || SimTime > 0)
            {
                _flowTimes.Enqueue(Math.Max(0, SimTime - part.EnteredSimTime));
                while (_flowTimes.Count > FlowTimeWindow)
                {
                    _flowTimes.Dequeue();
                }
            }
        }

        public IReadOnlyList<IntelligenceEvent> DrainEvents()
        {
            if (_events.Count == 0)
            {
                return Array.Empty<IntelligenceEvent>();
            }

            var copy = _events.ToList();
            _events.Clear();
            return copy;
        }

        // ------------------------------------------------------------------ operator commands

        public bool RequestMaintenance(int machineId, MaintenanceKind kind, string reason, string requestedBy)
        {
            if (!_cellsById.TryGetValue(machineId, out var cell))
            {
                return false;
            }

            var machine = cell.Machine;
            if (machine.Maintenance != MaintenanceMode.None)
            {
                return false;
            }

            machine.Maintenance = MaintenanceMode.Pending;
            machine.MaintenanceKind = kind;
            machine.MaintenanceDurationSeconds = kind == MaintenanceKind.Breakdown ? BreakdownRepairSeconds : PlannedMaintenanceSeconds;
            machine.MaintenanceRemainingSeconds = machine.MaintenanceDurationSeconds;
            machine.MaintenanceReason = reason;
            Raise(kind == MaintenanceKind.Breakdown ? "Critical" : "Info",
                $"Maintenance {machine.Name}",
                $"{(kind == MaintenanceKind.Breakdown ? "Breakdown repair" : "Planned maintenance")} requested by {requestedBy}: {reason}. Draining cell.",
                alarm: kind == MaintenanceKind.Breakdown);
            return true;
        }

        public string InjectFault(FaultScenario scenario)
        {
            string label;
            switch (scenario)
            {
                case FaultScenario.LaserOpticsContamination:
                    label = Inject(MachineType.LaserWelding, 22, "Laser optics contamination (spatter on protective window)");
                    break;
                case FaultScenario.SpindleBearingDefect:
                    label = Inject(MachineType.PrecisionAssembly, 18, "Spindle bearing outer-race defect");
                    break;
                case FaultScenario.VisionLightingDrift:
                    label = Inject(MachineType.QualityInspection, 16, "Ring-light LED degradation");
                    break;
                case FaultScenario.FixtureContactWear:
                    label = Inject(MachineType.FunctionalTesting, 20, "Pogo-pin contact wear / contamination");
                    break;
                case FaultScenario.RobotGripperLeak:
                    var robotTwin = _robotTwins.Count > 1 ? _robotTwins[1] : _robotTwins.FirstOrDefault();
                    if (robotTwin == null)
                    {
                        return "No robot available.";
                    }

                    robotTwin.DamageAcceleration = 60;
                    robotTwin.InjectedFault = "Vacuum gripper leak";
                    label = $"Vacuum gripper leak injected on {robotTwin.Robot.Name}";
                    break;
                default:
                    return "Unknown scenario.";
            }

            Raise("Warning", "What-if", $"Fault scenario injected: {label}. The AI must find it from sensors alone.", alarm: false);
            return label;
        }

        public void ClearFaults()
        {
            foreach (var cell in _cells)
            {
                cell.Twin.DamageAcceleration = 1.0;
                cell.Twin.InjectedFault = null;
            }

            foreach (var r in _robotTwins)
            {
                r.DamageAcceleration = 1.0;
                r.InjectedFault = null;
            }

            Raise("Info", "What-if", "Fault injections cleared (accumulated wear remains until maintenance).", alarm: false);
        }

        /// <summary>Expected seconds until the machine can accept a new part (for eco-glide planning).</summary>
        public double EstimateMachineFreeInSeconds(int machineId)
        {
            if (!_cellsById.TryGetValue(machineId, out var cell))
            {
                return 0;
            }

            var m = cell.Machine;
            if (m.Maintenance != MaintenanceMode.None || m.FaultActive)
            {
                return double.PositiveInfinity;
            }

            double remaining = 0;
            bool found = false;
            for (int i = 0; i < m.Stations.Count; i++)
            {
                var s = m.Stations[i];
                if (s.CurrentPart != null)
                {
                    found = true;
                    remaining += Math.Max(0, s.EffectiveProcessTime - s.ElapsedTime);
                }
                else if (found)
                {
                    remaining += s.ProcessTime * cell.Twin.CycleTimeFactor;
                }
            }

            return remaining;
        }

        // ------------------------------------------------------------------ main loop

        public void Step(double dt, LineObservation observation)
        {
            if (dt <= 0 || !observation.IsRunning)
            {
                return;
            }

            SimTime += dt;
            _runningSeconds += dt;
            _wip = observation.Wip;

            var activities = new MachineActivity[_cells.Count];
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                var activity = Classify(cell);
                cell.Activity = activity;
                activities[i] = activity;
                cell.Twin.Step(dt, activity);
                cell.Oee.Accumulate(activity, dt);
            }

            _bottleneck.Step(activities, dt);

            foreach (var robotTwin in _robotTwins)
            {
                robotTwin.Step(dt, robotTwin.Robot.State != RobotState.Idle, _rng);
                robotTwin.Robot.ActionTime = robotTwin.Robot.BaseActionTime * robotTwin.ActionTimeFactor;
            }

            StepEnergy(dt, observation.Movers);
            StepMaintenance(dt);
            DetectBreakdowns();

            _sensorTimer += dt;
            if (_sensorTimer >= SensorSampleInterval)
            {
                _sensorTimer -= SensorSampleInterval;
                SampleSensors();
            }

            _historyTimer += dt;
            if (_historyTimer >= HistoryInterval)
            {
                _historyTimer -= HistoryInterval;
                foreach (var cell in _cells)
                {
                    cell.TemperatureHistory.Add(cell.Twin.MeasuredTemperatureC);
                    cell.VibrationHistory.Add(cell.Twin.VibrationRms);
                    cell.PrimaryHistory.Add(cell.Twin.PrimaryValue);
                    cell.HealthHistory.Add(cell.Rul.Current.HealthIndex * 100);
                }

                _powerHistory.Add(_energy.CurrentKw);
            }

            _throughputTimer += dt;
            if (_throughputTimer >= ThroughputHistoryInterval)
            {
                _throughputTimer -= ThroughputHistoryInterval;
                double rate = ThroughputPerMinute();
                _throughputHistory.Add(rate);
                _throughputForecast.Update(rate);
                _oeeHistory.Add(LineOee().oee * 100);
            }

            _analyticsTimer += dt;
            if (_analyticsTimer >= AnalyticsInterval)
            {
                _analyticsTimer -= AnalyticsInterval;
                RunAutopilot();
                var draft = BuildSnapshot(_insights);
                _insights = _copilot.Generate(draft, _robotTwins);
            }
        }

        public IntelligenceSnapshot PublishSnapshot()
        {
            var snapshot = BuildSnapshot(_insights);
            _snapshot = snapshot;
            return snapshot;
        }

        /// <summary>Diagnostics string of the anomaly/prognostics internals (tests, debugging).</summary>
        internal string DescribeDetectors(int machineId)
        {
            var c = _cellsById[machineId];
            static string D(IAnomalyChannel d) =>
                $"{d.Channel}: ewmaZ {d.EwmaZ:F2} cusum+ {d.CusumHigh:F1} cusum- {d.CusumLow:F1}{(d.IsAnomalous ? " ANOM" : string.Empty)}";
            return $"t={SimTime:F0} D={c.Twin.Damage:F3} eff={c.Twin.Effect:F3} prim={c.Twin.PrimaryValue:0.###} T={c.Twin.MeasuredTemperatureC:F1}/{c.Twin.ShadowTemperatureC:F1} act={c.Activity} | " +
                   $"{D(c.TemperatureDetector)} | {D(c.VibrationDetector)} | {D(c.PrimaryDetector)} | RUL {c.Rul.Current.RemainingSeconds:F0} conf {c.Rul.Current.Confidence:F2}";
        }

        internal LineIntelligenceHub DebugSelf => this;

        // ------------------------------------------------------------------ internals

        private MachineActivity Classify(MachineCell cell)
        {
            var m = cell.Machine;
            if (m.Maintenance == MaintenanceMode.InProgress)
            {
                return m.MaintenanceKind == MaintenanceKind.Breakdown ? MachineActivity.Down : MachineActivity.PlannedMaintenance;
            }

            if (m.FaultActive || m.SequencerState == PlcSequencerState.Fault)
            {
                return MachineActivity.Down;
            }

            if (m.IsIndexing || m.Stations.Any(s => s.Status == StationStatus.Processing))
            {
                return MachineActivity.Running;
            }

            var robot = Robots.FirstOrDefault(r => r.AssignedMachineId == m.MachineId);
            if (robot != null && robot.State is RobotState.PickingFromMover or RobotState.MovingToMachine or RobotState.PlacingInMachine)
            {
                return MachineActivity.Running;
            }

            if (m.Stations.Any(s => s.CurrentPart != null))
            {
                return MachineActivity.Blocked;
            }

            return MachineActivity.Starved;
        }

        private void StepEnergy(double dt, IReadOnlyList<Mover> movers)
        {
            double moverWatts = 0;
            foreach (var mover in movers)
            {
                double payload = mover.CurrentPart == null ? 0 : 10.9 + 0.3 * Math.Min(3, mover.CurrentPart.MachinesCompleted);
                moverWatts += EnergyMonitor.MoverPowerWatts(
                    XtsTrackGeometry.ToMeters(mover.Velocity),
                    XtsTrackGeometry.ToMeters(mover.Acceleration),
                    payload);
            }

            double machineWatts = 0;
            foreach (var cell in _cells)
            {
                machineWatts += MachinePowerWatts(cell);
            }

            double robotWatts = 0;
            foreach (var robot in Robots)
            {
                robotWatts += robot.State switch
                {
                    RobotState.Idle => 110,
                    RobotState.MovingToMachine or RobotState.MovingToMover => 650,
                    _ => 380
                };
            }

            _energy.Step(dt, moverWatts, machineWatts, robotWatts);
        }

        private static double MachinePowerWatts(MachineCell cell)
        {
            var m = cell.Machine;
            double inefficiency = 1.0 + 0.15 * cell.Twin.Effect;
            switch (cell.Activity)
            {
                case MachineActivity.Running:
                    double running = m.Type switch
                    {
                        MachineType.LaserWelding => m.Stations.Count > 1 && m.Stations[1].Status == StationStatus.Processing ? 4200 : 900,
                        MachineType.PrecisionAssembly => 950,
                        MachineType.QualityInspection => 650,
                        MachineType.FunctionalTesting => 1500,
                        _ => 800
                    };
                    return running * inefficiency;
                case MachineActivity.Starved:
                case MachineActivity.Blocked:
                    return (m.Type == MachineType.LaserWelding ? 800 : 380) * inefficiency;
                case MachineActivity.Down:
                case MachineActivity.PlannedMaintenance:
                    return 150;
                default:
                    return 0;
            }
        }

        private void StepMaintenance(double dt)
        {
            bool technicianBusy = _cells.Any(c => c.Machine.Maintenance == MaintenanceMode.InProgress);
            bool breakdownWaiting = _cells.Any(c => c.Machine.Maintenance == MaintenanceMode.Pending && c.Machine.MaintenanceKind == MaintenanceKind.Breakdown);

            foreach (var cell in _cells)
            {
                var m = cell.Machine;
                if (m.Maintenance == MaintenanceMode.Pending)
                {
                    if (technicianBusy || (breakdownWaiting && m.MaintenanceKind != MaintenanceKind.Breakdown))
                    {
                        continue;
                    }

                    var robot = Robots.FirstOrDefault(r => r.AssignedMachineId == m.MachineId);
                    bool robotClear = robot == null || (robot.State == RobotState.Idle && robot.HeldPart == null && robot.SecondaryHeldPart == null);
                    if (m.IsEmpty && m.OutfeedNest == null && robotClear)
                    {
                        m.Maintenance = MaintenanceMode.InProgress;
                        m.MaintenanceRemainingSeconds = m.MaintenanceDurationSeconds;
                        technicianBusy = true;
                        Raise("Info", $"Maintenance {m.Name}",
                            $"Cell locked out (LOTO). Technician started {(m.MaintenanceKind == MaintenanceKind.Breakdown ? "breakdown repair" : "planned maintenance")}: {cell.Twin.Mode.RecommendedAction}",
                            alarm: false);
                    }
                }
                else if (m.Maintenance == MaintenanceMode.InProgress)
                {
                    m.MaintenanceRemainingSeconds = Math.Max(0, m.MaintenanceRemainingSeconds - dt);
                    if (m.MaintenanceRemainingSeconds <= 0)
                    {
                        CompleteMaintenance(cell);
                    }
                }
            }
        }

        private void CompleteMaintenance(MachineCell cell)
        {
            var m = cell.Machine;
            bool breakdown = m.MaintenanceKind == MaintenanceKind.Breakdown;
            if (!breakdown)
            {
                m.MaintenanceCount++;
            }

            m.Maintenance = MaintenanceMode.None;
            m.MaintenanceRemainingSeconds = 0;
            m.MaintenanceReason = string.Empty;

            cell.Twin.Restore(_rng);
            foreach (var robotTwin in _robotTwins.Where(r => r.Robot.AssignedMachineId == m.MachineId))
            {
                robotTwin.Restore(_rng);
            }

            cell.TemperatureDetector.Recommission();
            cell.VibrationDetector.Recommission();
            cell.PrimaryDetector.Recommission();
            cell.Rul.Reset();
            foreach (var spc in cell.Spc)
            {
                spc.ResetRuns();
            }

            cell.LastMaintenanceEnd = SimTime;
            cell.AnomalyLatched = false;
            Raise("Info", $"Maintenance {m.Name}",
                $"{(breakdown ? "Repair" : "Planned maintenance")} complete – cell back in production, health restored, AI baselines recommissioned.",
                alarm: false);
        }

        private void DetectBreakdowns()
        {
            foreach (var cell in _cells)
            {
                var m = cell.Machine;
                if (!cell.Twin.FailureReached || m.Maintenance == MaintenanceMode.InProgress ||
                    (m.Maintenance == MaintenanceMode.Pending && m.MaintenanceKind == MaintenanceKind.Breakdown))
                {
                    continue;
                }

                m.Maintenance = MaintenanceMode.Pending;
                m.MaintenanceKind = MaintenanceKind.Breakdown;
                m.MaintenanceDurationSeconds = BreakdownRepairSeconds;
                m.MaintenanceRemainingSeconds = BreakdownRepairSeconds;
                m.MaintenanceReason = $"Functional failure: {cell.Twin.Mode.Name}";
                m.BreakdownCount++;

                int damaged = 0;
                foreach (var station in m.Stations)
                {
                    if (station.CurrentPart != null)
                    {
                        station.CurrentPart.HasDefect = true;
                        station.CurrentPart.AddProcessHistory($"Damaged during breakdown of {m.Name}");
                        damaged++;
                    }
                }

                Raise("Critical", $"Breakdown {m.Name}",
                    $"UNPLANNED BREAKDOWN: {cell.Twin.Mode.Name}. {damaged} part(s) in cell will be rejected; repair ≈ {BreakdownRepairSeconds:F0} s.",
                    alarm: true);
            }
        }

        private void SampleSensors()
        {
            foreach (var cell in _cells)
            {
                var twin = cell.Twin;
                twin.SampleSensors(_rng);
                cell.TemperatureDetector.Add(twin.MeasuredTemperatureC - twin.ShadowTemperatureC);
                cell.VibrationDetector.Add(twin.VibrationRms - twin.ExpectedVibrationRms, cell.Activity == MachineActivity.Running);
                cell.PrimaryDetector.Add(twin.PrimaryValue - twin.Mode.PrimaryHealthy);
                cell.Rul.Update(SimTime, twin.StressTimeSeconds, twin.ObservedDegradation);

                bool anomalous = cell.AnyAnomaly;
                if (anomalous && !cell.AnomalyLatched)
                {
                    var worst = cell.WorstDetector;
                    Raise("Warning", $"Anomaly {cell.Machine.Name}",
                        $"Anomalous {worst.Channel} residual ({worst.Direction}, EWMA z={worst.EwmaZ:F1}, CUSUM={Math.Max(worst.CusumHigh, worst.CusumLow):F1}). Signature matches '{twin.Mode.Name}'.",
                        alarm: true);
                }

                cell.AnomalyLatched = anomalous;
            }
        }

        private void RunAutopilot()
        {
            var (bottleneckRate, rawProcessTime, bottleneckName) = EstimateCriticalWipInputs();
            Autopilot.UpdateReleaseControl(SimTime, bottleneckRate, rawProcessTime, bottleneckName);

            if (!Autopilot.Enabled)
            {
                return;
            }

            bool technicianBusy = _cells.Any(c => c.Machine.Maintenance != MaintenanceMode.None);
            foreach (var cell in _cells.OrderBy(c => c.Rul.Current.RemainingSeconds ?? double.MaxValue))
            {
                var m = cell.Machine;
                if (m.Maintenance != MaintenanceMode.None || SimTime - cell.LastMaintenanceEnd < 45)
                {
                    continue;
                }

                var rul = cell.Rul.Current;
                var robotTwin = _robotTwins.FirstOrDefault(r => r.Robot.AssignedMachineId == m.MachineId);
                string? reason = null;
                if (!rul.IsLearning && rul.RemainingSeconds.HasValue && rul.RemainingSeconds.Value < 150 && rul.Confidence >= 0.45)
                {
                    reason = $"predicted '{cell.Twin.Mode.Name}' failure in {FormatDuration(rul.RemainingSeconds.Value)} (confidence {rul.Confidence:P0})";
                }
                else if (rul.HealthIndex < 0.42)
                {
                    reason = $"health index {rul.HealthIndex:P0} is below the 42% PM threshold";
                }
                else if (cell.KeySpc?.IsOutOfControl == true && cell.AnyAnomaly && rul.HealthIndex < 0.8)
                {
                    reason = $"SPC rule violation on '{cell.KeySpc.Spec.Name}' correlated with a {cell.WorstDetector.Channel} anomaly";
                }
                else if (robotTwin != null && robotTwin.TrueHealthEstimate() < 0.45)
                {
                    reason = $"{robotTwin.Robot.Name} gripper vacuum degraded to {robotTwin.VacuumKpa:F0} kPa (transfers {robotTwin.ActionTimeFactor - 1:P0} slower)";
                }

                if (reason == null)
                {
                    continue;
                }

                if (technicianBusy)
                {
                    if (SimTime - _lastDeferredLog > 30)
                    {
                        _lastDeferredLog = SimTime;
                        Autopilot.Record(SimTime, "Predictive maintenance", $"Deferred PM on {m.Name}", $"Technician busy; {reason}. Queued for next window.");
                    }

                    continue;
                }

                if (RequestMaintenance(m.MachineId, MaintenanceKind.Planned, reason, "Autopilot"))
                {
                    Autopilot.Record(SimTime, "Predictive maintenance", $"Scheduled PM on {m.Name}",
                        $"{char.ToUpper(reason[0])}{reason[1..]}. A {PlannedMaintenanceSeconds:F0} s planned stop now avoids a ≈{BreakdownRepairSeconds:F0} s breakdown plus scrap.");
                    technicianBusy = true;
                }
            }
        }

        /// <summary>
        /// Model-based inputs for the critical WIP: bottleneck rate r_b from the slowest cell (ideal cycle ×
        /// health slowdown + robot load time) and raw process time T₀ (process + transfers + loop travel).
        /// </summary>
        private (double bottleneckRate, double rawProcessTime, string bottleneckName) EstimateCriticalWipInputs()
        {
            const double LoadTransferSeconds = 3 * 0.8;          // pick raw, move, place into machine
            const double OutboundTransferSeconds = 2 * 0.8 + 4.0; // pick from nest, move, typical wait for an empty mover
            const double RouteDegrees = 515.0;                   // entry → M0 → M1 → M2 → M3 → exit
            const double DockDwellSeconds = 1.2 + 1.0;           // entry load + exit unload

            double slowest = 0;
            string name = "-";
            double process = 0;
            foreach (var cell in _cells)
            {
                var robot = _robotTwins.FirstOrDefault(r => r.Robot.AssignedMachineId == cell.Machine.MachineId);
                double load = LoadTransferSeconds * (robot?.ActionTimeFactor ?? 1.0);
                double cycle = cell.Machine.IdealCycleTimeSeconds * cell.Twin.CycleTimeFactor;
                process += cycle + load + OutboundTransferSeconds;
                if (cycle + load > slowest)
                {
                    slowest = cycle + load;
                    name = cell.Machine.Name;
                }
            }

            double travel = RouteDegrees / FbXtsMoverAxis.LoadedCruiseVelocity;
            return (slowest > 0 ? 1.0 / slowest : 0, process + travel + DockDwellSeconds, name);
        }

        private double ThroughputPerMinute()
        {
            while (_exitTimes.Count > 0 && SimTime - _exitTimes.Peek() > ThroughputWindowSeconds)
            {
                _exitTimes.Dequeue();
            }

            double window = Math.Min(ThroughputWindowSeconds, Math.Max(1, _runningSeconds));
            return _exitTimes.Count * 60.0 / window;
        }

        private (double oee, double a, double p, double q) LineOee()
        {
            if (_runningSeconds < 1)
            {
                return (0, 1, 0, 1);
            }

            int bottleneck = _bottleneck.AverageBottleneck;
            double availability = bottleneck >= 0 ? _cells[bottleneck].Oee.Availability : 1.0;
            int total = _good + _bad;
            double quality = total == 0 ? 1.0 : _good / (double)total;
            double performance = Math.Min(1.0, total * _idealLineCycleSeconds / Math.Max(1e-6, _runningSeconds * availability));
            return (availability * performance * quality, availability, performance, quality);
        }

        private IntelligenceSnapshot BuildSnapshot(IReadOnlyList<CopilotInsight> insights)
        {
            int bottleneck = _bottleneck.AverageBottleneck;
            var machineStates = _cells.Select(cell => BuildMachineState(cell, bottleneck)).ToList();
            var robotStates = _robotTwins.Select(r => new RobotIntelligenceState
            {
                RobotId = r.Robot.RobotId,
                Name = r.Robot.Name,
                MachineId = r.Robot.AssignedMachineId,
                VacuumKpa = r.VacuumKpa,
                Health = r.TrueHealthEstimate(),
                ActionTimeFactor = r.ActionTimeFactor,
                InjectedFault = r.InjectedFault
            }).ToList();

            var (oee, a, p, q) = LineOee();
            double throughput = ThroughputPerMinute();
            double leadTime = _flowTimes.Count == 0 ? 0 : _flowTimes.Average();
            int total = _good + _bad;

            var line = new LineKpiState
            {
                Oee = oee,
                Availability = a,
                Performance = p,
                Quality = q,
                ThroughputPerMinute = throughput,
                ForecastPerHour = Math.Max(0, _throughputForecast.Count == 0 ? throughput : _throughputForecast.Forecast(6)) * 60,
                LeadTimeSeconds = leadTime,
                Wip = _wip,
                WipCap = Autopilot.WipCap,
                LittlesLawWip = throughput / 60.0 * leadTime,
                PowerKw = _energy.CurrentKw,
                TrackKw = _energy.TrackKw,
                MachinesKw = _energy.MachinesKw,
                RobotsKw = _energy.RobotsKw,
                EnergyKwh = _energy.TotalKwh,
                WhPerGoodPart = _good == 0 ? 0 : _energy.TotalKwh * 1000 / _good,
                Co2Kg = _energy.Co2Kg,
                Yield = total == 0 ? 1.0 : _good / (double)total,
                GoodParts = _good,
                BadParts = _bad,
                BottleneckMachineId = bottleneck >= 0 ? _cells[bottleneck].Machine.MachineId : -1,
                BottleneckName = bottleneck >= 0 ? _cells[bottleneck].Machine.Name : "-",
                BottleneckShare = bottleneck >= 0 ? _bottleneck.Share(bottleneck) : 0,
                BottleneckShifting = _bottleneck.IsShifting
            };

            var faults = _cells.Where(c => c.Twin.InjectedFault != null).Select(c => $"{c.Machine.Name}: {c.Twin.InjectedFault}")
                .Concat(_robotTwins.Where(r => r.InjectedFault != null).Select(r => $"{r.Robot.Name}: {r.InjectedFault}"))
                .ToList();

            return new IntelligenceSnapshot
            {
                Version = ++_version,
                SimTimeSeconds = SimTime,
                AutopilotEnabled = Autopilot.Enabled,
                Line = line,
                Machines = machineStates,
                Robots = robotStates,
                Insights = insights,
                Decisions = Autopilot.Decisions.ToList(),
                ThroughputHistory = _throughputHistory.ToArray(),
                PowerHistory = _powerHistory.ToArray(),
                OeeHistory = _oeeHistory.ToArray(),
                ActiveFaults = faults
            };
        }

        private MachineIntelligenceState BuildMachineState(MachineCell cell, int bottleneck)
        {
            var m = cell.Machine;
            var twin = cell.Twin;
            var rul = cell.Rul.Current;
            var worst = cell.WorstDetector;
            double vibration = twin.VibrationRms;

            SpcChartState? spc = null;
            if (cell.KeySpc != null)
            {
                var k = cell.KeySpc;
                spc = new SpcChartState
                {
                    Characteristic = k.Spec.Name,
                    Unit = k.Spec.Unit,
                    Values = k.Values.ToArray(),
                    ViolationIndices = k.ViolationIndices.ToArray(),
                    CenterLine = k.CenterLine,
                    UpperControlLimit = k.UpperControlLimit,
                    LowerControlLimit = k.LowerControlLimit,
                    LowerSpecLimit = k.Spec.LowerSpecLimit,
                    UpperSpecLimit = k.Spec.UpperSpecLimit,
                    Cpk = k.Cpk,
                    LastViolation = k.LastViolation?.Description ?? string.Empty,
                    IsOutOfControl = k.IsOutOfControl,
                    OutOfSpecCount = k.OutOfSpecCount,
                    SampleCount = k.SampleCount
                };
            }

            return new MachineIntelligenceState
            {
                MachineId = m.MachineId,
                Name = m.Name,
                MachineType = m.Type.ToString(),
                FailureMode = twin.Mode.Name,
                Activity = cell.Activity,
                HealthEstimate = rul.HealthIndex,
                TrueHealth = twin.TrueHealth,
                RulSeconds = rul.RemainingSeconds,
                RulConfidence = rul.Confidence,
                RulLearning = rul.IsLearning,
                RulMethod = rul.Method,
                AnomalyScore = cell.MaxAnomalyScore,
                AnomalyFlag = cell.AnyAnomaly,
                AnomalyChannel = worst.Channel,
                TemperatureC = twin.MeasuredTemperatureC,
                VibrationRms = vibration,
                VibrationZone = vibration <= 1.4 ? "A" : vibration <= 2.8 ? "B" : vibration <= 4.5 ? "C" : "D",
                PrimaryValue = twin.PrimaryValue,
                PrimaryName = twin.Mode.PrimarySensorName,
                PrimaryUnit = twin.Mode.PrimarySensorUnit,
                PrimaryHealthy = twin.Mode.PrimaryHealthy,
                PrimaryAtFailure = twin.Mode.PrimaryAtFailure,
                TemperatureHistory = cell.TemperatureHistory.ToArray(),
                VibrationHistory = cell.VibrationHistory.ToArray(),
                PrimaryHistory = cell.PrimaryHistory.ToArray(),
                HealthHistory = cell.HealthHistory.ToArray(),
                Availability = cell.Oee.Availability,
                Performance = cell.Oee.Performance,
                Quality = cell.Oee.Quality,
                Oee = cell.Oee.Oee,
                Utilization = cell.Oee.Utilization,
                DominantLoss = cell.Oee.DominantLoss(),
                BottleneckShare = _bottleneck.Share(cell.Index),
                IsBottleneck = cell.Index == bottleneck,
                MeanActivePeriodSeconds = _bottleneck.MeanActivePeriod(cell.Index),
                CycleTimeFactor = twin.CycleTimeFactor,
                MaintenanceState = m.Maintenance == MaintenanceMode.None
                    ? "None"
                    : $"{m.Maintenance}{(m.MaintenanceKind == MaintenanceKind.Breakdown ? " (breakdown)" : " (planned)")}",
                MaintenanceRemainingSeconds = m.MaintenanceRemainingSeconds,
                MaintenanceCount = m.MaintenanceCount,
                BreakdownCount = m.BreakdownCount,
                InjectedFault = twin.InjectedFault,
                RecommendedAction = twin.Mode.RecommendedAction,
                KeySpc = spc
            };
        }

        private void Raise(string severity, string source, string message, bool alarm)
        {
            _events.Add(new IntelligenceEvent { Severity = severity, Source = source, Message = message, RaiseAlarm = alarm });
        }

        private string Inject(MachineType type, double acceleration, string description)
        {
            var cell = _cells.FirstOrDefault(c => c.Machine.Type == type);
            if (cell == null)
            {
                return "Machine not found.";
            }

            cell.Twin.DamageAcceleration = acceleration;
            cell.Twin.InjectedFault = description;
            return $"{description} injected on {cell.Machine.Name}";
        }

        public static string FormatDuration(double seconds)
        {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds)) return "∞";
            var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:00}m" : ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}m {ts.Seconds:00}s" : $"{ts.Seconds}s";
        }

        private sealed class StationContext
        {
            public StationContext(MachineCell cell, QualityCharacteristicSpec spec, SpcMonitor spc, bool isLastStation)
            {
                Cell = cell;
                Spec = spec;
                Spc = spc;
                IsLastStation = isLastStation;
            }

            public MachineCell Cell { get; }
            public QualityCharacteristicSpec Spec { get; }
            public SpcMonitor Spc { get; }
            public bool IsLastStation { get; }
            public long LastViolationLogged { get; set; } = -100;
        }

        private sealed class MachineCell
        {
            public MachineCell(Machine machine, MachineDigitalTwin twin, int index)
            {
                Machine = machine;
                Twin = twin;
                Index = index;
                Oee = new OeeTracker(machine.IdealCycleTimeSeconds);
                TemperatureDetector = new ResidualAnomalyDetector("temperature", minimumSigma: 0.6);
                VibrationDetector = new ContextualAnomalyDetector("vibration", minimumSigma: 0.08);
                PrimaryDetector = new ResidualAnomalyDetector(twin.Mode.PrimarySensorName.ToLowerInvariant(), minimumSigma: twin.Mode.PrimaryNoise * 0.5);
            }

            public Machine Machine { get; }
            public MachineDigitalTwin Twin { get; }
            public int Index { get; }
            public OeeTracker Oee { get; }
            public RulEstimator Rul { get; } = new();
            public ResidualAnomalyDetector TemperatureDetector { get; }
            public ContextualAnomalyDetector VibrationDetector { get; }
            public ResidualAnomalyDetector PrimaryDetector { get; }
            public List<SpcMonitor> Spc { get; } = new();
            public SpcMonitor? KeySpc { get; set; }
            public HashSet<Guid> NokParts { get; } = new();
            public MachineActivity Activity { get; set; } = MachineActivity.Stopped;
            public double LastMaintenanceEnd { get; set; } = double.NegativeInfinity;
            public bool AnomalyLatched { get; set; }
            public RingBuffer TemperatureHistory { get; } = new(120);
            public RingBuffer VibrationHistory { get; } = new(120);
            public RingBuffer PrimaryHistory { get; } = new(120);
            public RingBuffer HealthHistory { get; } = new(120);

            public bool AnyAnomaly => TemperatureDetector.IsAnomalous || VibrationDetector.IsAnomalous || PrimaryDetector.IsAnomalous;

            public double MaxAnomalyScore => Math.Max(TemperatureDetector.Score, Math.Max(VibrationDetector.Score, PrimaryDetector.Score));

            public IAnomalyChannel WorstDetector
            {
                get
                {
                    IAnomalyChannel worst = TemperatureDetector;
                    if (VibrationDetector.Score > worst.Score) worst = VibrationDetector;
                    if (PrimaryDetector.Score > worst.Score) worst = PrimaryDetector;
                    return worst;
                }
            }
        }
    }

    internal static class RobotTwinExtensions
    {
        /// <summary>Health as observable from the vacuum sensor (what a real system can know).</summary>
        public static double TrueHealthEstimate(this RobotDigitalTwin twin)
        {
            double span = RobotDigitalTwin.FailureVacuumKpa - RobotDigitalTwin.HealthyVacuumKpa;
            return Math.Clamp(1.0 - (twin.VacuumKpa - RobotDigitalTwin.HealthyVacuumKpa) / span, 0, 1);
        }
    }
}
