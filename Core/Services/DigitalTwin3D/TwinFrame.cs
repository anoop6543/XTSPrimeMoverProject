using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using XTSPrimeMoverProject.Models;
using XTSPrimeMoverProject.Services.Intelligence;

namespace XTSPrimeMoverProject.Services.DigitalTwin3D
{
    /// <summary>
    /// One frame of line state for the 3D digital twin (Three.js). The same contract is used live
    /// (WPF → WebView2 PostWebMessage) and offline (headless recorder → video), so recordings show
    /// exactly what the HMI shows.
    /// </summary>
    public sealed class TwinFrame
    {
        public double T { get; init; }
        public bool Running { get; init; }
        public TwinLayout Layout { get; init; } = new();
        public List<TwinMover> Movers { get; init; } = new();
        public List<TwinMachine> Machines { get; init; } = new();
        public List<TwinRobot> Robots { get; init; } = new();
        public TwinKpi Kpi { get; init; } = new();
        public List<TwinAlert> Alerts { get; init; } = new();
        public List<string> Faults { get; init; } = new();
        public List<string> Decisions { get; init; } = new();
    }

    public sealed class TwinLayout
    {
        public double TrackLengthM { get; init; } = XtsTrackGeometry.TrackLengthMeters;
        public double[] MachineAngles { get; init; } = XTSSimulationEngine.MachineLoadAngles;
        public double EntryAngle { get; init; } = XTSSimulationEngine.EntryLoadAngle;
        public double ExitAngle { get; init; } = XTSSimulationEngine.ExitAngle;
    }

    public sealed class TwinPart
    {
        public string Trk { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public int Stage { get; init; }
        public bool Defect { get; init; }
    }

    public sealed class TwinMover
    {
        public int Id { get; init; }
        public double Pos { get; init; }
        public double Vel { get; init; }
        public double Acc { get; init; }
        public string State { get; init; } = string.Empty;
        public int Dock { get; init; } = -1;
        public TwinPart? Part { get; init; }
    }

    public sealed class TwinStation
    {
        public string N { get; init; } = string.Empty;
        public string St { get; init; } = string.Empty;
        public double P { get; init; }
        public bool Part { get; init; }
    }

    public sealed class TwinMachine
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Seq { get; init; } = string.Empty;
        public int Idx { get; init; }
        public List<TwinStation> Stations { get; init; } = new();
        public TwinPart? Nest { get; init; }
        public TwinPart? Part { get; init; }
        public string Maint { get; init; } = "None";
        public string MaintKind { get; init; } = string.Empty;
        public double MaintLeft { get; init; }
        public bool Fault { get; init; }
        public bool Indexing { get; init; }
        public double Rotary { get; init; }
        public string Activity { get; init; } = string.Empty;
        public double Health { get; init; } = 1;
        public double? Rul { get; init; }
        public double Anomaly { get; init; }
        public bool AnomalyFlag { get; init; }
        public double Temp { get; init; }
        public double Vib { get; init; }
        public double Oee { get; init; }
        public bool Bottleneck { get; init; }
        public bool SpcAlarm { get; init; }
        public string? Fault2 { get; init; }
    }

    public sealed class TwinRobot
    {
        public int Id { get; init; }
        public int M { get; init; }
        public string St { get; init; } = string.Empty;
        public double P { get; init; }
        public TwinPart? Part { get; init; }
        public TwinPart? Part2 { get; init; }
        public bool Staged { get; init; }
        public double Health { get; init; } = 1;
    }

    public sealed class TwinKpi
    {
        public double Oee { get; init; }
        public double Tput { get; init; }
        public double Forecast { get; init; }
        public double Yield { get; init; } = 1;
        public int Wip { get; init; }
        public int WipCap { get; init; }
        public double Kw { get; init; }
        public double WhPart { get; init; }
        public int Good { get; init; }
        public int Bad { get; init; }
        public string Bottleneck { get; init; } = "-";
        public bool Autopilot { get; init; }
        public double LeadTime { get; init; }
    }

    public sealed class TwinAlert
    {
        public string Sev { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public int? M { get; init; }
    }

    public static class TwinFrameBuilder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static string ToJson(TwinFrame frame) => JsonSerializer.Serialize(frame, JsonOptions);

        public static string ToJson(IEnumerable<TwinFrame> frames) => JsonSerializer.Serialize(frames, JsonOptions);

        public static TwinFrame Build(IMachineGatewayService gateway)
        {
            var snapshot = gateway.GetIntelligenceSnapshot();
            var insightByMachine = snapshot.Machines.ToDictionary(m => m.MachineId);

            var movers = gateway.Movers.Select(m => new TwinMover
            {
                Id = m.MoverId,
                Pos = Math.Round(m.Position, 3),
                Vel = Math.Round(m.Velocity, 2),
                Acc = Math.Round(m.Acceleration, 1),
                State = m.State.ToString(),
                Dock = m.State switch
                {
                    MoverState.AtLoadStation or MoverState.AtUnloadStation => m.TargetStation,
                    MoverState.AtEntryStation => -2,
                    MoverState.AtExitStation => -3,
                    _ => -1
                },
                Part = ToPart(m.CurrentPart)
            }).ToList();

            var machines = gateway.Machines.Select(m =>
            {
                insightByMachine.TryGetValue(m.MachineId, out var ai);
                var current = m.Stations.FirstOrDefault(s => s.CurrentPart != null)?.CurrentPart;
                return new TwinMachine
                {
                    Id = m.MachineId,
                    Name = m.Name,
                    Type = m.Type.ToString(),
                    Seq = m.SequencerState.ToString(),
                    Idx = m.CurrentStationIndex,
                    Stations = m.Stations.Select(s => new TwinStation
                    {
                        N = s.Name,
                        St = s.Status.ToString(),
                        P = s.EffectiveProcessTime > 0 ? Math.Round(Math.Clamp(s.ElapsedTime / s.EffectiveProcessTime, 0, 1), 3) : 0,
                        Part = s.CurrentPart != null
                    }).ToList(),
                    Nest = ToPart(m.OutfeedNest),
                    Part = ToPart(current),
                    Maint = m.Maintenance.ToString(),
                    MaintKind = m.Maintenance == MaintenanceMode.None ? string.Empty : m.MaintenanceKind.ToString(),
                    MaintLeft = Math.Round(m.MaintenanceRemainingSeconds, 1),
                    Fault = m.FaultActive,
                    Indexing = m.IsIndexing,
                    Rotary = Math.Round(m.RotaryAngle, 1),
                    Activity = ai?.Activity.ToString() ?? string.Empty,
                    Health = Math.Round(ai?.HealthEstimate ?? 1, 3),
                    Rul = ai?.RulSeconds is double rul ? Math.Round(rul, 0) : null,
                    Anomaly = Math.Round(ai?.AnomalyScore ?? 0, 3),
                    AnomalyFlag = ai?.AnomalyFlag ?? false,
                    Temp = Math.Round(ai?.TemperatureC ?? 0, 1),
                    Vib = Math.Round(ai?.VibrationRms ?? 0, 2),
                    Oee = Math.Round(ai?.Oee ?? 0, 3),
                    Bottleneck = ai?.IsBottleneck ?? false,
                    SpcAlarm = ai?.KeySpc?.IsOutOfControl ?? false,
                    Fault2 = ai?.InjectedFault
                };
            }).ToList();

            var robotHealth = snapshot.Robots.ToDictionary(r => r.RobotId, r => r.Health);
            var robots = gateway.Robots.Select(r => new TwinRobot
            {
                Id = r.RobotId,
                M = r.AssignedMachineId,
                St = r.State.ToString(),
                P = r.ActionTime > 0 ? Math.Round(Math.Clamp(r.ActionProgress / r.ActionTime, 0, 1), 3) : 0,
                Part = ToPart(r.HeldPart),
                Part2 = ToPart(r.SecondaryHeldPart),
                Staged = r.IsStagedAtDock,
                Health = Math.Round(robotHealth.TryGetValue(r.RobotId, out var h) ? h : 1, 3)
            }).ToList();

            var line = snapshot.Line;
            return new TwinFrame
            {
                T = Math.Round(gateway.SimulationTimeSeconds, 3),
                Running = gateway.IsRunning,
                Movers = movers,
                Machines = machines,
                Robots = robots,
                Kpi = new TwinKpi
                {
                    Oee = Math.Round(line.Oee, 3),
                    Tput = Math.Round(line.ThroughputPerMinute, 2),
                    Forecast = Math.Round(line.ForecastPerHour, 0),
                    Yield = Math.Round(line.Yield, 4),
                    Wip = line.Wip,
                    WipCap = line.WipCap,
                    Kw = Math.Round(line.PowerKw, 2),
                    WhPart = Math.Round(line.WhPerGoodPart, 1),
                    Good = gateway.GoodPartsCount,
                    Bad = gateway.BadPartsCount,
                    Bottleneck = line.BottleneckName,
                    Autopilot = snapshot.AutopilotEnabled,
                    LeadTime = Math.Round(line.LeadTimeSeconds, 1)
                },
                Alerts = snapshot.Insights
                    .Where(i => i.Severity >= InsightSeverity.Advisory)
                    .Take(3)
                    .Select(i => new TwinAlert { Sev = i.Severity.ToString(), Title = i.Title, M = i.MachineId })
                    .ToList(),
                Faults = snapshot.ActiveFaults.ToList(),
                Decisions = snapshot.Decisions.Reverse().Take(3).Select(d => $"{d.Action} – {d.Rationale}").ToList()
            };
        }

        private static TwinPart? ToPart(Part? part) => part == null
            ? null
            : new TwinPart
            {
                Trk = part.TrackingNumber,
                Status = part.Status.ToString(),
                Stage = part.MachinesCompleted,
                Defect = part.HasDefect
            };
    }
}
