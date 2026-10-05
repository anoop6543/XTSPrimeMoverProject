using System;
using System.Collections.Generic;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    /// <summary>
    /// One critical-to-quality characteristic measured at a station.
    /// Healthy process spread is derived from the qualified process capability (Cpk),
    /// so the healthy reject rate is physically consistent with the spec window.
    /// </summary>
    public sealed class QualityCharacteristicSpec
    {
        public string Name { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public double Nominal { get; init; }
        public double? LowerSpecLimit { get; init; }
        public double? UpperSpecLimit { get; init; }

        /// <summary>Capability demonstrated at process qualification (run@rate).</summary>
        public double HealthyCpk { get; init; } = 1.33;

        /// <summary>Mean shift at full degradation, in multiples of the healthy sigma (signed).</summary>
        public double MeanShiftAtFailureSigma { get; init; }

        /// <summary>Relative growth of sigma at full degradation (0.5 = sigma x 1.5).</summary>
        public double SigmaGrowthAtFailure { get; init; }

        /// <summary>Shown on the machine's SPC chart.</summary>
        public bool IsKeyCharacteristic { get; init; }

        public double HealthySigma
        {
            get
            {
                double distance = double.MaxValue;
                if (LowerSpecLimit.HasValue) distance = Math.Min(distance, Nominal - LowerSpecLimit.Value);
                if (UpperSpecLimit.HasValue) distance = Math.Min(distance, UpperSpecLimit.Value - Nominal);
                if (distance == double.MaxValue || distance <= 0) return Math.Abs(Nominal) * 0.01 + 1e-6;
                return distance / (3.0 * HealthyCpk);
            }
        }

        public bool IsInSpec(double value)
        {
            if (LowerSpecLimit.HasValue && value < LowerSpecLimit.Value) return false;
            if (UpperSpecLimit.HasValue && value > UpperSpecLimit.Value) return false;
            return true;
        }

        /// <summary>Analytical probability of an out-of-spec result for a given mean/sigma.</summary>
        public double DefectProbability(double mean, double sigma)
        {
            double p = 0;
            if (LowerSpecLimit.HasValue) p += NormalDistribution.Cdf((LowerSpecLimit.Value - mean) / sigma);
            if (UpperSpecLimit.HasValue) p += 1.0 - NormalDistribution.Cdf((UpperSpecLimit.Value - mean) / sigma);
            return Math.Clamp(p, 0, 1);
        }
    }

    /// <summary>Failure mode of a machine cell and its sensor signature.</summary>
    public sealed class FailureModeSpec
    {
        public string Name { get; init; } = string.Empty;
        public string PrimarySensorName { get; init; } = string.Empty;
        public string PrimarySensorUnit { get; init; } = string.Empty;
        public double PrimaryHealthy { get; init; }
        public double PrimaryAtFailure { get; init; }
        public double PrimaryNoise { get; init; }

        /// <summary>Running-seconds to functional failure at nominal stress.</summary>
        public double NominalLifeSeconds { get; init; }

        /// <summary>Extra vibration RMS (mm/s) at full degradation.</summary>
        public double VibrationAtFailure { get; init; }

        /// <summary>Extra steady-state temperature (degC) at full degradation.</summary>
        public double TemperatureRiseAtFailure { get; init; }

        /// <summary>Cycle time multiplier growth at full degradation (0.4 = 40% slower).</summary>
        public double CycleSlowdownAtFailure { get; init; }

        /// <summary>Running steady-state temperature (degC) when healthy.</summary>
        public double RunningTemperature { get; init; } = 42;

        public string RecommendedAction { get; init; } = string.Empty;
    }

    /// <summary>
    /// Process and failure-mode knowledge base for the 12S prismatic EV battery module line.
    /// Values are representative of production practice (weld depth, fastening torque,
    /// module height, DC-IR, insulation resistance, ...).
    /// </summary>
    public static class EvModuleQualityCatalog
    {
        private static readonly Dictionary<MachineType, QualityCharacteristicSpec[]> Characteristics = new()
        {
            [MachineType.LaserWelding] = new[]
            {
                new QualityCharacteristicSpec { Name = "Stack compression force", Unit = " kN", Nominal = 12.0, LowerSpecLimit = 11.0, UpperSpecLimit = 13.0, HealthyCpk = 1.33, MeanShiftAtFailureSigma = 0.3, SigmaGrowthAtFailure = 0.2 },
                new QualityCharacteristicSpec { Name = "Weld penetration depth", Unit = " mm", Nominal = 1.20, LowerSpecLimit = 1.00, UpperSpecLimit = 1.40, HealthyCpk = 1.10, MeanShiftAtFailureSigma = -3.6, SigmaGrowthAtFailure = 0.5, IsKeyCharacteristic = true },
                new QualityCharacteristicSpec { Name = "Weld zone temperature", Unit = " °C", Nominal = 45, UpperSpecLimit = 60, HealthyCpk = 1.33, MeanShiftAtFailureSigma = 1.5, SigmaGrowthAtFailure = 0.2 },
                new QualityCharacteristicSpec { Name = "Seam porosity", Unit = " %", Nominal = 0.8, UpperSpecLimit = 2.0, HealthyCpk = 1.25, MeanShiftAtFailureSigma = 2.4, SigmaGrowthAtFailure = 0.6 },
            },
            [MachineType.PrecisionAssembly] = new[]
            {
                new QualityCharacteristicSpec { Name = "Pick offset", Unit = " mm", Nominal = 0.0, LowerSpecLimit = -0.15, UpperSpecLimit = 0.15, HealthyCpk = 1.33, MeanShiftAtFailureSigma = 0.3, SigmaGrowthAtFailure = 0.4 },
                new QualityCharacteristicSpec { Name = "Placement offset", Unit = " µm", Nominal = 0.0, LowerSpecLimit = -40, UpperSpecLimit = 40, HealthyCpk = 1.25, MeanShiftAtFailureSigma = 0.5, SigmaGrowthAtFailure = 0.8 },
                new QualityCharacteristicSpec { Name = "Final fastening torque", Unit = " N·m", Nominal = 2.50, LowerSpecLimit = 2.25, UpperSpecLimit = 2.75, HealthyCpk = 1.10, MeanShiftAtFailureSigma = -1.6, SigmaGrowthAtFailure = 1.6, IsKeyCharacteristic = true },
                new QualityCharacteristicSpec { Name = "Rundown angle", Unit = " °", Nominal = 720, LowerSpecLimit = 680, UpperSpecLimit = 760, HealthyCpk = 1.20, MeanShiftAtFailureSigma = 1.0, SigmaGrowthAtFailure = 1.0 },
                new QualityCharacteristicSpec { Name = "Connector seat score", Unit = " %", Nominal = 97, LowerSpecLimit = 90, HealthyCpk = 1.30, MeanShiftAtFailureSigma = -0.5, SigmaGrowthAtFailure = 0.3 },
            },
            [MachineType.QualityInspection] = new[]
            {
                new QualityCharacteristicSpec { Name = "Surface defect area", Unit = " mm²", Nominal = 0.10, UpperSpecLimit = 0.50, HealthyCpk = 1.30, MeanShiftAtFailureSigma = 1.0, SigmaGrowthAtFailure = 1.2 },
                new QualityCharacteristicSpec { Name = "Module height", Unit = " mm", Nominal = 108.00, LowerSpecLimit = 107.70, UpperSpecLimit = 108.30, HealthyCpk = 1.15, MeanShiftAtFailureSigma = 1.6, SigmaGrowthAtFailure = 1.5, IsKeyCharacteristic = true },
                new QualityCharacteristicSpec { Name = "Busbar roughness Ra", Unit = " µm", Nominal = 0.80, UpperSpecLimit = 1.60, HealthyCpk = 1.30, MeanShiftAtFailureSigma = 1.0, SigmaGrowthAtFailure = 1.0 },
                new QualityCharacteristicSpec { Name = "Module mass", Unit = " kg", Nominal = 11.80, LowerSpecLimit = 11.65, UpperSpecLimit = 11.95, HealthyCpk = 1.33, MeanShiftAtFailureSigma = 0.0, SigmaGrowthAtFailure = 0.2 },
            },
            [MachineType.FunctionalTesting] = new[]
            {
                new QualityCharacteristicSpec { Name = "Insulation resistance", Unit = " MΩ", Nominal = 550, LowerSpecLimit = 100, HealthyCpk = 1.25, MeanShiftAtFailureSigma = -0.5, SigmaGrowthAtFailure = 0.3 },
                new QualityCharacteristicSpec { Name = "Module DC-IR", Unit = " mΩ", Nominal = 3.20, LowerSpecLimit = 2.40, UpperSpecLimit = 4.00, HealthyCpk = 1.10, MeanShiftAtFailureSigma = 3.4, SigmaGrowthAtFailure = 0.6, IsKeyCharacteristic = true },
                new QualityCharacteristicSpec { Name = "Cell voltage spread", Unit = " mV", Nominal = 8, UpperSpecLimit = 20, HealthyCpk = 1.20, MeanShiftAtFailureSigma = 1.0, SigmaGrowthAtFailure = 0.5 },
                new QualityCharacteristicSpec { Name = "Module OCV", Unit = " V", Nominal = 44.40, LowerSpecLimit = 44.00, UpperSpecLimit = 44.80, HealthyCpk = 1.30, MeanShiftAtFailureSigma = -0.8, SigmaGrowthAtFailure = 0.3 },
                new QualityCharacteristicSpec { Name = "DMC print grade", Unit = "", Nominal = 3.6, LowerSpecLimit = 2.5, HealthyCpk = 1.30, MeanShiftAtFailureSigma = -0.3, SigmaGrowthAtFailure = 0.2 },
            },
        };

        private static readonly Dictionary<MachineType, FailureModeSpec> FailureModes = new()
        {
            [MachineType.LaserWelding] = new FailureModeSpec
            {
                Name = "Laser optics contamination",
                PrimarySensorName = "Laser output power",
                PrimarySensorUnit = "W",
                PrimaryHealthy = 2000,
                PrimaryAtFailure = 1560,
                PrimaryNoise = 9,
                NominalLifeSeconds = 1150,
                VibrationAtFailure = 0.6,
                TemperatureRiseAtFailure = 14,
                CycleSlowdownAtFailure = 0.25,
                RunningTemperature = 52,
                RecommendedAction = "Replace protective window and clean focusing optics; re-run weld power calibration."
            },
            [MachineType.PrecisionAssembly] = new FailureModeSpec
            {
                Name = "Fastening spindle bearing wear",
                PrimarySensorName = "Spindle motor current",
                PrimarySensorUnit = "A",
                PrimaryHealthy = 3.2,
                PrimaryAtFailure = 5.1,
                PrimaryNoise = 0.05,
                NominalLifeSeconds = 1500,
                VibrationAtFailure = 5.2,
                TemperatureRiseAtFailure = 9,
                CycleSlowdownAtFailure = 0.45,
                RunningTemperature = 41,
                RecommendedAction = "Swap nutrunner spindle cartridge, re-grease bearings and verify torque transducer."
            },
            [MachineType.QualityInspection] = new FailureModeSpec
            {
                Name = "Vision illumination drift",
                PrimarySensorName = "Ring light intensity",
                PrimarySensorUnit = "%",
                PrimaryHealthy = 100,
                PrimaryAtFailure = 61,
                PrimaryNoise = 0.8,
                NominalLifeSeconds = 1800,
                VibrationAtFailure = 0.4,
                TemperatureRiseAtFailure = 6,
                CycleSlowdownAtFailure = 0.5,
                RunningTemperature = 36,
                RecommendedAction = "Replace LED ring light, clean lens and re-run camera calibration target."
            },
            [MachineType.FunctionalTesting] = new FailureModeSpec
            {
                Name = "Test fixture pogo-pin wear",
                PrimarySensorName = "Contact resistance",
                PrimarySensorUnit = "mΩ",
                PrimaryHealthy = 4.0,
                PrimaryAtFailure = 18.0,
                PrimaryNoise = 0.25,
                NominalLifeSeconds = 1300,
                VibrationAtFailure = 0.5,
                TemperatureRiseAtFailure = 8,
                CycleSlowdownAtFailure = 0.35,
                RunningTemperature = 44,
                RecommendedAction = "Replace pogo-pin block, clean contact plates and run golden-sample verification."
            },
        };

        public static QualityCharacteristicSpec? GetCharacteristic(MachineType type, int stationIndex)
        {
            return Characteristics.TryGetValue(type, out var list) && stationIndex >= 0 && stationIndex < list.Length
                ? list[stationIndex]
                : null;
        }

        public static IReadOnlyList<QualityCharacteristicSpec> GetCharacteristics(MachineType type)
        {
            return Characteristics.TryGetValue(type, out var list) ? list : Array.Empty<QualityCharacteristicSpec>();
        }

        public static FailureModeSpec GetFailureMode(MachineType type) => FailureModes[type];
    }
}
