using System;
using System.Collections.Generic;
using System.Linq;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    /// <summary>
    /// OEE time model for one machine (ISO 22400 style):
    /// planned time excludes planned maintenance, availability loses unplanned downtime,
    /// performance loses starving/blocking and slow cycles, quality loses out-of-spec parts.
    /// </summary>
    public sealed class OeeTracker
    {
        private readonly Dictionary<MachineActivity, double> _seconds = new();

        public OeeTracker(double idealCycleTimeSeconds)
        {
            IdealCycleTimeSeconds = idealCycleTimeSeconds;
            foreach (MachineActivity a in Enum.GetValues<MachineActivity>())
            {
                _seconds[a] = 0;
            }
        }

        public double IdealCycleTimeSeconds { get; }
        public int PartsCompleted { get; private set; }
        public int PartsRejected { get; private set; }

        public double Seconds(MachineActivity activity) => _seconds[activity];

        public double PlannedSeconds => _seconds[MachineActivity.Running] + _seconds[MachineActivity.Starved]
                                        + _seconds[MachineActivity.Blocked] + _seconds[MachineActivity.Down];

        public double OperatingSeconds => PlannedSeconds - _seconds[MachineActivity.Down];

        public double Availability => PlannedSeconds <= 1e-6 ? 1.0 : OperatingSeconds / PlannedSeconds;

        public double Performance => OperatingSeconds <= 1e-6 ? 1.0 : Math.Min(1.0, IdealCycleTimeSeconds * PartsCompleted / OperatingSeconds);

        public double Quality => PartsCompleted == 0 ? 1.0 : (PartsCompleted - PartsRejected) / (double)PartsCompleted;

        public double Oee => Availability * Performance * Quality;

        public double Utilization => PlannedSeconds <= 1e-6 ? 0 : _seconds[MachineActivity.Running] / PlannedSeconds;

        public void Accumulate(MachineActivity activity, double dt)
        {
            if (activity != MachineActivity.Stopped)
            {
                _seconds[activity] += dt;
            }
        }

        public void RecordPart(bool rejected)
        {
            PartsCompleted++;
            if (rejected)
            {
                PartsRejected++;
            }
        }

        /// <summary>The dominant loss, for explanations.</summary>
        public string DominantLoss()
        {
            double a = 1 - Availability, p = 1 - Performance, q = 1 - Quality;
            if (a >= p && a >= q) return "availability (unplanned downtime)";
            if (p >= q)
            {
                return _seconds[MachineActivity.Starved] >= _seconds[MachineActivity.Blocked]
                    ? "performance (starved – waiting for parts)"
                    : "performance (blocked – waiting for unload)";
            }

            return "quality (out-of-spec parts)";
        }
    }

    /// <summary>
    /// Active-period bottleneck detection (Roser, Nakano &amp; Tanaka). A machine is "active" while
    /// working or being repaired/maintained, "inactive" while starved or blocked. At any instant the
    /// machine with the longest uninterrupted active period is the momentary bottleneck; the share of
    /// time each machine is the momentary bottleneck identifies the average (and shifting) bottleneck.
    /// </summary>
    public sealed class BottleneckDetector
    {
        private readonly double[] _activeDuration;
        private readonly double[] _bottleneckSeconds;
        private readonly double[] _activeSum;
        private readonly int[] _activePeriods;
        private double _totalSeconds;

        public BottleneckDetector(int machineCount)
        {
            _activeDuration = new double[machineCount];
            _bottleneckSeconds = new double[machineCount];
            _activeSum = new double[machineCount];
            _activePeriods = new int[machineCount];
        }

        public int MomentaryBottleneck { get; private set; } = -1;

        /// <summary>True when the two longest active periods overlap closely (the bottleneck is shifting).</summary>
        public bool IsShifting { get; private set; }

        public static bool IsActive(MachineActivity a) =>
            a is MachineActivity.Running or MachineActivity.Down or MachineActivity.PlannedMaintenance;

        public void Step(IReadOnlyList<MachineActivity> activities, double dt)
        {
            if (activities.All(a => a == MachineActivity.Stopped))
            {
                return;
            }

            for (int i = 0; i < _activeDuration.Length && i < activities.Count; i++)
            {
                if (IsActive(activities[i]))
                {
                    if (_activeDuration[i] == 0)
                    {
                        _activePeriods[i]++;
                    }

                    _activeDuration[i] += dt;
                    _activeSum[i] += dt;
                }
                else
                {
                    _activeDuration[i] = 0;
                }
            }

            int best = -1;
            double bestValue = 0, second = 0;
            for (int i = 0; i < _activeDuration.Length; i++)
            {
                if (_activeDuration[i] > bestValue)
                {
                    second = bestValue;
                    bestValue = _activeDuration[i];
                    best = i;
                }
                else if (_activeDuration[i] > second)
                {
                    second = _activeDuration[i];
                }
            }

            MomentaryBottleneck = best;
            IsShifting = best >= 0 && second > 0.85 * bestValue;
            _totalSeconds += dt;
            if (best < 0)
            {
                return;
            }

            // Overlapping (tied) active periods: the bottleneck is shared/shifting, split the credit.
            int tied = 0;
            for (int i = 0; i < _activeDuration.Length; i++)
            {
                if (Math.Abs(_activeDuration[i] - bestValue) < 1e-9) tied++;
            }

            for (int i = 0; i < _activeDuration.Length; i++)
            {
                if (Math.Abs(_activeDuration[i] - bestValue) < 1e-9)
                {
                    _bottleneckSeconds[i] += dt / tied;
                }
            }
        }

        public double Share(int machineIndex) => _totalSeconds <= 0 ? 0 : _bottleneckSeconds[machineIndex] / _totalSeconds;

        public double MeanActivePeriod(int machineIndex) => _activePeriods[machineIndex] == 0 ? 0 : _activeSum[machineIndex] / _activePeriods[machineIndex];

        public int AverageBottleneck
        {
            get
            {
                if (_totalSeconds <= 0) return -1;
                int best = 0;
                for (int i = 1; i < _bottleneckSeconds.Length; i++)
                {
                    if (_bottleneckSeconds[i] > _bottleneckSeconds[best]) best = i;
                }

                return best;
            }
        }
    }

    /// <summary>
    /// Electrical energy model of the line. Movers: inertial + friction + eddy-drag force,
    /// copper losses (k·F²) and holding power; XTS motor modules: electronics base load;
    /// machines and robots: duty-dependent power with wear-related inefficiency.
    /// </summary>
    public sealed class EnergyMonitor
    {
        public const double GridCarbonIntensityKgPerKwh = 0.38;
        public const int MotorModuleCount = 24;
        private const double ModuleElectronicsWatts = 9.0;
        private const double MoverHoldWatts = 3.0;
        private const double DriveEfficiency = 0.88;
        private const double CopperLossWattsPerNewton2 = 0.02;
        private const double RollingFriction = 0.015;
        private const double EddyDragNewtonsPerMps = 2.0;
        private const double CarrierMassKg = 1.6;

        public double TotalKwh { get; private set; }
        public double CurrentKw { get; private set; }
        public double TrackKw { get; private set; }
        public double MachinesKw { get; private set; }
        public double RobotsKw { get; private set; }

        public double Co2Kg => TotalKwh * GridCarbonIntensityKgPerKwh;

        public static double MoverPowerWatts(double velocityMps, double accelerationMps2, double payloadKg)
        {
            double mass = CarrierMassKg + payloadKg;
            double friction = RollingFriction * mass * 9.81 * Math.Sign(velocityMps);
            double force = mass * accelerationMps2 + friction + EddyDragNewtonsPerMps * velocityMps;
            double mechanical = Math.Max(0, force * velocityMps);
            return MoverHoldWatts + mechanical / DriveEfficiency + CopperLossWattsPerNewton2 * force * force;
        }

        public void Step(double dt, double moversWatts, double machinesWatts, double robotsWatts)
        {
            TrackKw = (moversWatts + MotorModuleCount * ModuleElectronicsWatts) / 1000.0;
            MachinesKw = machinesWatts / 1000.0;
            RobotsKw = robotsWatts / 1000.0;
            CurrentKw = TrackKw + MachinesKw + RobotsKw;
            TotalKwh += CurrentKw * dt / 3600.0;
        }
    }
}
