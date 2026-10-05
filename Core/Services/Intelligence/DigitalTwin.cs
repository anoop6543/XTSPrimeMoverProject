using System;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    /// <summary>Machine time-state classification used by OEE and bottleneck analytics.</summary>
    public enum MachineActivity
    {
        Stopped,
        Running,
        Starved,
        Blocked,
        Down,
        PlannedMaintenance
    }

    /// <summary>What-if fault scenarios an operator can inject to exercise the AI.</summary>
    public enum FaultScenario
    {
        LaserOpticsContamination,
        SpindleBearingDefect,
        VisionLightingDrift,
        FixtureContactWear,
        RobotGripperLeak
    }

    internal static class Degradation
    {
        private const double Convexity = 3.0;
        private static readonly double Denominator = Math.Exp(Convexity) - 1.0;

        /// <summary>
        /// P-F curve: observable effect of accumulated damage. Effects stay small for most of the life
        /// and accelerate toward functional failure (damage = 1).
        /// </summary>
        public static double Effect(double damage)
        {
            double d = Math.Clamp(damage, 0, 1.05);
            return (Math.Exp(Convexity * d) - 1.0) / Denominator;
        }

        /// <summary>Offset that linearises the P-F curve: ln(effect + Phi) is linear in damage.</summary>
        public static double LinearisingOffset => 1.0 / Denominator;
    }

    /// <summary>
    /// Physics-flavoured digital twin of one machine cell. Holds the hidden "ground truth"
    /// damage state and produces noisy sensor readings, exactly like a real asset would.
    /// The AI only ever sees the sensors and the measurements, never <see cref="Damage"/>.
    /// </summary>
    public sealed class MachineDigitalTwin
    {
        public const double AmbientTemperatureC = 23.0;
        private const double ThermalTimeConstantSeconds = 75.0;

        public MachineDigitalTwin(Machine machine, double initialDamage)
        {
            Machine = machine;
            Mode = EvModuleQualityCatalog.GetFailureMode(machine.Type);
            Damage = Math.Clamp(initialDamage, 0, 0.9);
            TemperatureC = AmbientTemperatureC + 4;
            ShadowTemperatureC = TemperatureC;
            PrimaryValue = Mode.PrimaryHealthy;
        }

        public Machine Machine { get; }
        public FailureModeSpec Mode { get; }

        /// <summary>Hidden ground-truth damage (0 = new, 1 = functional failure).</summary>
        public double Damage { get; private set; }

        /// <summary>Damage rate multiplier; &gt; 1 while a fault scenario is injected.</summary>
        public double DamageAcceleration { get; set; } = 1.0;

        public string? InjectedFault { get; set; }

        public double Effect => Degradation.Effect(Damage);
        public double TrueHealth => 1.0 - Effect;
        public double CycleTimeFactor => 1.0 + Mode.CycleSlowdownAtFailure * Effect;
        public bool FailureReached => Damage >= 1.0;

        public double TemperatureC { get; private set; }
        public double ShadowTemperatureC { get; private set; }
        public double VibrationRms { get; private set; }
        public double PrimaryValue { get; private set; }
        public double ExpectedVibrationRms { get; private set; }

        /// <summary>Operating-equivalent time: the prognostic time base (like operating hours).</summary>
        public double StressTimeSeconds { get; private set; }
        public double RunningSeconds { get; private set; }

        public MachineActivity LastActivity { get; private set; } = MachineActivity.Stopped;

        public void Step(double dt, MachineActivity activity)
        {
            LastActivity = activity;
            double stress = activity switch
            {
                MachineActivity.Running => 1.0 + 0.5 * Math.Max(0, (TemperatureC - Mode.RunningTemperature) / 20.0),
                MachineActivity.Starved or MachineActivity.Blocked => 0.03,
                _ => 0.0
            };

            if (activity == MachineActivity.Running)
            {
                RunningSeconds += dt;
            }

            StressTimeSeconds += stress * dt;
            Damage = Math.Min(1.05, Damage + DamageAcceleration * stress * dt / Mode.NominalLifeSeconds);

            double alpha = 1.0 - Math.Exp(-dt / ThermalTimeConstantSeconds);
            TemperatureC += (SteadyStateTemperature(activity, Effect) - TemperatureC) * alpha;
            ShadowTemperatureC += (SteadyStateTemperature(activity, 0) - ShadowTemperatureC) * alpha;
        }

        public void SampleSensors(Random rng)
        {
            bool running = LastActivity == MachineActivity.Running;
            double baseVibration = running ? (Machine.Type == MachineType.PrecisionAssembly ? 1.6 : 1.1) : 0.35;
            double wearVibration = Mode.VibrationAtFailure * Effect * (running ? 1.0 : 0.3);
            ExpectedVibrationRms = baseVibration;
            VibrationRms = Math.Max(0.05, baseVibration + wearVibration + NormalDistribution.Sample(rng, 0, 0.08));

            double healthy = Mode.PrimaryHealthy;
            double span = Mode.PrimaryAtFailure - Mode.PrimaryHealthy;
            PrimaryValue = healthy + span * Effect + NormalDistribution.Sample(rng, 0, Mode.PrimaryNoise);

            // Temperature probe noise is applied on read-out; keep the state noise free.
            MeasuredTemperatureC = TemperatureC + NormalDistribution.Sample(rng, 0, 0.25);
        }

        public double MeasuredTemperatureC { get; private set; }

        /// <summary>Normalised degradation indicator derived from the primary sensor (0 healthy .. 1 failure).</summary>
        public double ObservedDegradation => (PrimaryValue - Mode.PrimaryHealthy) / (Mode.PrimaryAtFailure - Mode.PrimaryHealthy);

        public void Restore(Random rng)
        {
            Damage = 0.01 + rng.NextDouble() * 0.04;
            DamageAcceleration = 1.0;
            InjectedFault = null;
        }

        private double SteadyStateTemperature(MachineActivity activity, double effect)
        {
            double running = Mode.RunningTemperature + Mode.TemperatureRiseAtFailure * effect;
            return activity switch
            {
                MachineActivity.Running => running,
                MachineActivity.Starved or MachineActivity.Blocked => AmbientTemperatureC + (running - AmbientTemperatureC) * 0.35,
                _ => AmbientTemperatureC + 2.0
            };
        }
    }

    /// <summary>Vacuum gripper health of a cell robot.</summary>
    public sealed class RobotDigitalTwin
    {
        public const double HealthyVacuumKpa = -78.0;
        public const double FailureVacuumKpa = -50.0;
        private const double NominalLifeSeconds = 7000.0;

        public RobotDigitalTwin(Robot robot, double initialDamage)
        {
            Robot = robot;
            Damage = Math.Clamp(initialDamage, 0, 0.6);
        }

        public Robot Robot { get; }
        public double Damage { get; private set; }
        public double DamageAcceleration { get; set; } = 1.0;
        public string? InjectedFault { get; set; }
        public double Effect => Degradation.Effect(Damage);
        public double TrueHealth => 1.0 - Effect;
        public double VacuumKpa { get; private set; } = HealthyVacuumKpa;

        /// <summary>Weaker vacuum forces slower, re-gripping moves.</summary>
        public double ActionTimeFactor => 1.0 + 0.9 * Effect;

        public void Step(double dt, bool active, Random rng)
        {
            if (active)
            {
                Damage = Math.Min(1.0, Damage + DamageAcceleration * dt / NominalLifeSeconds);
            }

            VacuumKpa = HealthyVacuumKpa + (FailureVacuumKpa - HealthyVacuumKpa) * Effect + NormalDistribution.Sample(rng, 0, 0.4);
        }

        public void Restore(Random rng)
        {
            Damage = 0.01 + rng.NextDouble() * 0.03;
            DamageAcceleration = 1.0;
            InjectedFault = null;
        }
    }
}
