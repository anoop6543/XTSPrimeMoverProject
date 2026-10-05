using System;
using System.Collections.Generic;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    public enum InsightSeverity
    {
        Info,
        Advisory,
        Warning,
        Critical
    }

    /// <summary>Explainable finding produced by the copilot reasoner.</summary>
    public sealed class CopilotInsight
    {
        public string Id { get; init; } = string.Empty;
        public InsightSeverity Severity { get; init; }
        public string Category { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Evidence { get; init; } = string.Empty;
        public string Recommendation { get; init; } = string.Empty;
        public double Confidence { get; init; }
        public int? MachineId { get; init; }
    }

    /// <summary>Closed-loop action taken by the autopilot, with its rationale (decision log).</summary>
    public sealed class AutopilotDecision
    {
        public double SimTime { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public string Category { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Rationale { get; init; } = string.Empty;
    }

    public sealed class IntelligenceEvent
    {
        public string Severity { get; init; } = "Info";
        public string Source { get; init; } = "AI";
        public string Message { get; init; } = string.Empty;
        public bool RaiseAlarm { get; init; }
    }

    public sealed class SpcChartState
    {
        public string Characteristic { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public double[] Values { get; init; } = Array.Empty<double>();
        public int[] ViolationIndices { get; init; } = Array.Empty<int>();
        public double CenterLine { get; init; }
        public double UpperControlLimit { get; init; }
        public double LowerControlLimit { get; init; }
        public double? LowerSpecLimit { get; init; }
        public double? UpperSpecLimit { get; init; }
        public double? Cpk { get; init; }
        public string LastViolation { get; init; } = string.Empty;
        public bool IsOutOfControl { get; init; }
        public int OutOfSpecCount { get; init; }
        public long SampleCount { get; init; }
    }

    public sealed class MachineIntelligenceState
    {
        public int MachineId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string MachineType { get; init; } = string.Empty;
        public string FailureMode { get; init; } = string.Empty;
        public MachineActivity Activity { get; init; }

        public double HealthEstimate { get; init; }
        public double TrueHealth { get; init; }
        public double? RulSeconds { get; init; }
        public double RulConfidence { get; init; }
        public bool RulLearning { get; init; }
        public string RulMethod { get; init; } = string.Empty;

        public double AnomalyScore { get; init; }
        public bool AnomalyFlag { get; init; }
        public string AnomalyChannel { get; init; } = "-";

        public double TemperatureC { get; init; }
        public double VibrationRms { get; init; }
        public string VibrationZone { get; init; } = "A";
        public double PrimaryValue { get; init; }
        public string PrimaryName { get; init; } = string.Empty;
        public string PrimaryUnit { get; init; } = string.Empty;
        public double PrimaryHealthy { get; init; }
        public double PrimaryAtFailure { get; init; }

        public double[] TemperatureHistory { get; init; } = Array.Empty<double>();
        public double[] VibrationHistory { get; init; } = Array.Empty<double>();
        public double[] PrimaryHistory { get; init; } = Array.Empty<double>();
        public double[] HealthHistory { get; init; } = Array.Empty<double>();

        public double Availability { get; init; }
        public double Performance { get; init; }
        public double Quality { get; init; }
        public double Oee { get; init; }
        public double Utilization { get; init; }
        public string DominantLoss { get; init; } = string.Empty;

        public double BottleneckShare { get; init; }
        public bool IsBottleneck { get; init; }
        public double MeanActivePeriodSeconds { get; init; }
        public double CycleTimeFactor { get; init; }

        public string MaintenanceState { get; init; } = "None";
        public double MaintenanceRemainingSeconds { get; init; }
        public int MaintenanceCount { get; init; }
        public int BreakdownCount { get; init; }
        public string? InjectedFault { get; init; }
        public string RecommendedAction { get; init; } = string.Empty;

        public SpcChartState? KeySpc { get; init; }
    }

    public sealed class RobotIntelligenceState
    {
        public int RobotId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int MachineId { get; init; }
        public double VacuumKpa { get; init; }
        public double Health { get; init; }
        public double ActionTimeFactor { get; init; }
        public string? InjectedFault { get; init; }
    }

    public sealed class LineKpiState
    {
        public double Oee { get; init; }
        public double Availability { get; init; }
        public double Performance { get; init; }
        public double Quality { get; init; }
        public double ThroughputPerMinute { get; init; }
        public double ForecastPerHour { get; init; }
        public double LeadTimeSeconds { get; init; }
        public int Wip { get; init; }
        public int WipCap { get; init; }
        public double LittlesLawWip { get; init; }
        public double PowerKw { get; init; }
        public double TrackKw { get; init; }
        public double MachinesKw { get; init; }
        public double RobotsKw { get; init; }
        public double EnergyKwh { get; init; }
        public double WhPerGoodPart { get; init; }
        public double Co2Kg { get; init; }
        public double Yield { get; init; }
        public int GoodParts { get; init; }
        public int BadParts { get; init; }
        public int BottleneckMachineId { get; init; } = -1;
        public string BottleneckName { get; init; } = "-";
        public double BottleneckShare { get; init; }
        public bool BottleneckShifting { get; init; }
    }

    public sealed class IntelligenceSnapshot
    {
        public static readonly IntelligenceSnapshot Empty = new();

        public long Version { get; init; }
        public double SimTimeSeconds { get; init; }
        public bool AutopilotEnabled { get; init; }
        public LineKpiState Line { get; init; } = new();
        public IReadOnlyList<MachineIntelligenceState> Machines { get; init; } = Array.Empty<MachineIntelligenceState>();
        public IReadOnlyList<RobotIntelligenceState> Robots { get; init; } = Array.Empty<RobotIntelligenceState>();
        public IReadOnlyList<CopilotInsight> Insights { get; init; } = Array.Empty<CopilotInsight>();
        public IReadOnlyList<AutopilotDecision> Decisions { get; init; } = Array.Empty<AutopilotDecision>();
        public double[] ThroughputHistory { get; init; } = Array.Empty<double>();
        public double[] PowerHistory { get; init; } = Array.Empty<double>();
        public double[] OeeHistory { get; init; } = Array.Empty<double>();
        public IReadOnlyList<string> ActiveFaults { get; init; } = Array.Empty<string>();
    }
}
