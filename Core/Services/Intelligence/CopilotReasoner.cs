using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    /// <summary>
    /// Offline, explainable reasoning engine. Turns analytics into ranked insights (with evidence,
    /// recommendation and confidence) and answers operator questions from the live snapshot.
    /// Also produces the grounded JSON context sent to the optional LLM copilot.
    /// </summary>
    public sealed class CopilotReasoner
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public IReadOnlyList<CopilotInsight> Generate(IntelligenceSnapshot s, IReadOnlyList<RobotDigitalTwin> robots)
        {
            var list = new List<CopilotInsight>();
            var line = s.Line;

            // --- Constraint (Theory of Constraints) -------------------------------------------
            if (line.BottleneckMachineId >= 0 && s.SimTimeSeconds > 30)
            {
                var b = s.Machines.First(m => m.MachineId == line.BottleneckMachineId);
                list.Add(new CopilotInsight
                {
                    Id = "toc",
                    Severity = line.BottleneckShifting ? InsightSeverity.Advisory : InsightSeverity.Info,
                    Category = "Constraint",
                    Title = line.BottleneckShifting
                        ? $"Bottleneck is shifting – {b.Name} leads ({line.BottleneckShare:P0})"
                        : $"{b.Name} is the line constraint ({line.BottleneckShare:P0} of the time)",
                    Evidence = $"Active-period method: mean uninterrupted active period {b.MeanActivePeriodSeconds:F1} s, utilisation {b.Utilization:P0}, " +
                               $"cycle factor ×{b.CycleTimeFactor:F2}. Line output {line.ThroughputPerMinute:F2}/min.",
                    Recommendation = $"Protect {b.Name}: never let it starve (keep a loaded mover queued at its dock) and move any off-line work away from it. " +
                                     "Every minute lost here is a minute lost for the whole line.",
                    Confidence = Math.Clamp(line.BottleneckShare, 0.3, 0.97),
                    MachineId = b.MachineId
                });
            }

            foreach (var m in s.Machines)
            {
                // --- Breakdown / maintenance state ------------------------------------------------
                if (m.MaintenanceState.Contains("breakdown", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new CopilotInsight
                    {
                        Id = $"bd-{m.MachineId}",
                        Severity = InsightSeverity.Critical,
                        Category = "Breakdown",
                        Title = $"{m.Name} is down: {m.FailureMode}",
                        Evidence = $"Unplanned repair in progress ({m.MaintenanceRemainingSeconds:F0} s left). Breakdowns so far: {m.BreakdownCount}.",
                        Recommendation = "Enable the autopilot so predictive maintenance is scheduled before functional failure next time. " + m.RecommendedAction,
                        Confidence = 0.99,
                        MachineId = m.MachineId
                    });
                    continue;
                }

                // --- Prognostics ------------------------------------------------------------------
                if (!m.RulLearning && m.RulSeconds.HasValue && m.RulSeconds.Value < 360 && m.RulConfidence >= 0.35)
                {
                    double dev = (m.PrimaryValue - m.PrimaryHealthy) / m.PrimaryHealthy;
                    list.Add(new CopilotInsight
                    {
                        Id = $"rul-{m.MachineId}",
                        Severity = m.RulSeconds.Value < 120 ? InsightSeverity.Critical : InsightSeverity.Warning,
                        Category = "Predictive maintenance",
                        Title = $"{m.Name}: {m.FailureMode} predicted in {LineIntelligenceHub.FormatDuration(m.RulSeconds.Value)}",
                        Evidence = $"{m.PrimaryName} {m.PrimaryValue.ToString("0.##", Inv)} {m.PrimaryUnit} ({dev:+0.0%;-0.0%} vs healthy), health index {m.HealthEstimate:P0}, " +
                                   $"{m.RulMethod}, confidence {m.RulConfidence:P0}.",
                        Recommendation = (m.MaintenanceState == "None" ? "Schedule a planned stop. " : "Maintenance already queued. ") + m.RecommendedAction,
                        Confidence = m.RulConfidence,
                        MachineId = m.MachineId
                    });
                }
                else if (m.HealthEstimate < 0.6)
                {
                    list.Add(new CopilotInsight
                    {
                        Id = $"hi-{m.MachineId}",
                        Severity = InsightSeverity.Advisory,
                        Category = "Asset health",
                        Title = $"{m.Name} health index {m.HealthEstimate:P0}",
                        Evidence = $"{m.PrimaryName} at {m.PrimaryValue.ToString("0.##", Inv)} {m.PrimaryUnit} (healthy {m.PrimaryHealthy.ToString("0.##", Inv)}). Cycle time ×{m.CycleTimeFactor:F2}.",
                        Recommendation = "Plan maintenance in the next starvation window. " + m.RecommendedAction,
                        Confidence = 0.7,
                        MachineId = m.MachineId
                    });
                }

                // --- Anomaly -----------------------------------------------------------------------
                if (m.AnomalyFlag)
                {
                    list.Add(new CopilotInsight
                    {
                        Id = $"anom-{m.MachineId}",
                        Severity = InsightSeverity.Warning,
                        Category = "Anomaly",
                        Title = $"Abnormal {m.AnomalyChannel} behaviour on {m.Name}",
                        Evidence = $"Residual vs healthy digital-twin model exceeds EWMA/CUSUM limits (score {m.AnomalyScore:P0}). " +
                                   $"Temp {m.TemperatureC:F1} °C, vibration {m.VibrationRms:F2} mm/s (ISO zone {m.VibrationZone}).",
                        Recommendation = $"Signature is consistent with '{m.FailureMode}'. Inspect at the next opportunity; watch SPC on {m.KeySpc?.Characteristic ?? "key characteristics"}.",
                        Confidence = Math.Clamp(0.5 + m.AnomalyScore / 2, 0.5, 0.95),
                        MachineId = m.MachineId
                    });
                }

                // --- SPC ---------------------------------------------------------------------------
                if (m.KeySpc is { IsOutOfControl: true } spc)
                {
                    string cpk = spc.Cpk.HasValue ? spc.Cpk.Value.ToString("0.00", Inv) : "n/a";
                    list.Add(new CopilotInsight
                    {
                        Id = $"spc-{m.MachineId}",
                        Severity = spc.Cpk is < 1.0 ? InsightSeverity.Warning : InsightSeverity.Advisory,
                        Category = "Quality (SPC)",
                        Title = $"{spc.Characteristic} out of statistical control on {m.Name}",
                        Evidence = $"{spc.LastViolation}. Rolling Cpk {cpk} (target ≥ 1.33), {spc.OutOfSpecCount} out-of-spec of {spc.SampleCount}.",
                        Recommendation = m.AnomalyFlag || m.HealthEstimate < 0.75
                            ? $"Root cause very likely '{m.FailureMode}' (sensor evidence agrees). {m.RecommendedAction}"
                            : "No sensor evidence of asset degradation – check material lot and process parameters.",
                        Confidence = m.AnomalyFlag ? 0.85 : 0.6,
                        MachineId = m.MachineId
                    });
                }
            }

            // --- Robots ----------------------------------------------------------------------------
            foreach (var r in s.Robots.Where(r => r.Health < 0.6))
            {
                list.Add(new CopilotInsight
                {
                    Id = $"robot-{r.RobotId}",
                    Severity = r.Health < 0.4 ? InsightSeverity.Warning : InsightSeverity.Advisory,
                    Category = "Robot",
                    Title = $"{r.Name} vacuum gripper weakening ({r.VacuumKpa:F0} kPa)",
                    Evidence = $"Transfers are {r.ActionTimeFactor - 1:P0} slower than nominal; healthy vacuum is {RobotDigitalTwin.HealthyVacuumKpa:F0} kPa.",
                    Recommendation = "Replace suction cups and check the vacuum generator/hoses at the next cell maintenance.",
                    Confidence = 0.8,
                    MachineId = r.MachineId
                });
            }

            // --- Quality / yield -------------------------------------------------------------------
            int total = line.GoodParts + line.BadParts;
            if (total >= 10 && line.Yield < 0.97)
            {
                var worst = s.Machines.OrderBy(m => m.Quality).First();
                list.Add(new CopilotInsight
                {
                    Id = "yield",
                    Severity = line.Yield < 0.9 ? InsightSeverity.Warning : InsightSeverity.Advisory,
                    Category = "Quality",
                    Title = $"First-pass yield {line.Yield:P1} – {worst.Name} is the top reject source",
                    Evidence = $"{line.BadParts} rejects of {total}. {worst.Name} quality rate {worst.Quality:P1}.",
                    Recommendation = $"Contain suspect modules from {worst.Name}; correlate rejects with {worst.KeySpc?.Characteristic ?? "its measurements"}.",
                    Confidence = 0.8,
                    MachineId = worst.MachineId
                });
            }

            // --- Flow / WIP --------------------------------------------------------------------------
            if (total >= 6 && line.LittlesLawWip > 0 && line.Wip > line.LittlesLawWip + 1.5 && !s.AutopilotEnabled)
            {
                list.Add(new CopilotInsight
                {
                    Id = "wip",
                    Severity = InsightSeverity.Advisory,
                    Category = "Flow",
                    Title = $"Excess WIP: {line.Wip} parts in flow, Little's law needs ≈{line.LittlesLawWip:F1}",
                    Evidence = $"Throughput {line.ThroughputPerMinute:F2}/min × lead time {line.LeadTimeSeconds:F0} s. Extra WIP only adds queueing time.",
                    Recommendation = "Enable the autopilot: it sizes the release cap from the critical WIP W₀ = r_b × T₀.",
                    Confidence = 0.75
                });
            }

            // --- Energy ------------------------------------------------------------------------------
            if (line.GoodParts >= 5)
            {
                var idle = s.Machines.Where(m => m.Activity is MachineActivity.Starved or MachineActivity.Blocked).ToList();
                list.Add(new CopilotInsight
                {
                    Id = "energy",
                    Severity = InsightSeverity.Info,
                    Category = "Energy",
                    Title = $"{line.WhPerGoodPart:F0} Wh per good module · {line.PowerKw:F2} kW now",
                    Evidence = $"Track {line.TrackKw:F2} kW, machines {line.MachinesKw:F2} kW, robots {line.RobotsKw:F2} kW; {line.Co2Kg * 1000:F0} g CO₂ so far. " +
                               $"{idle.Count} machine(s) idling right now.",
                    Recommendation = idle.Count > 0
                        ? $"Put idling cells ({string.Join(", ", idle.Select(m => m.Name))}) into standby during starvation (laser chiller and test racks dominate idle load)."
                        : "Energy profile nominal.",
                    Confidence = 0.7
                });
            }

            return list
                .OrderByDescending(i => i.Severity)
                .ThenByDescending(i => i.Confidence)
                .Take(10)
                .ToList();
        }

        /// <summary>Answers an operator question from the live snapshot (no network needed).</summary>
        public string Answer(string question, IntelligenceSnapshot s)
        {
            string q = (question ?? string.Empty).Trim().ToLowerInvariant();
            if (s.Machines.Count == 0)
            {
                return "The line has not produced data yet. Press START and ask again in a few seconds.";
            }

            var machine = FindMachine(q, s);
            if (machine != null && !ContainsAny(q, "bottleneck", "constraint"))
            {
                return DescribeMachine(machine, s);
            }

            if (ContainsAny(q, "bottleneck", "constraint", "slow", "throughput", "output", "capacity"))
            {
                var l = s.Line;
                var b = s.Machines.FirstOrDefault(m => m.MachineId == l.BottleneckMachineId);
                var sb = new StringBuilder();
                sb.AppendLine(b == null
                    ? "Not enough data yet to identify the constraint."
                    : $"The constraint is {b.Name}: it is the momentary bottleneck {l.BottleneckShare:P0} of the time (active-period method){(l.BottleneckShifting ? ", and the bottleneck is currently shifting" : string.Empty)}.");
                sb.AppendLine($"Throughput {l.ThroughputPerMinute:F2} modules/min, forecast {l.ForecastPerHour:F0}/h, lead time {l.LeadTimeSeconds:F0} s.");
                foreach (var m in s.Machines.OrderByDescending(m => m.BottleneckShare))
                {
                    sb.AppendLine($"• {m.Name}: bottleneck share {m.BottleneckShare:P0}, utilisation {m.Utilization:P0}, mean active period {m.MeanActivePeriodSeconds:F1} s, cycle ×{m.CycleTimeFactor:F2}");
                }

                if (b != null && b.CycleTimeFactor > 1.05)
                {
                    sb.AppendLine($"{b.Name} runs {b.CycleTimeFactor - 1:P0} slower than nominal because of {b.FailureMode.ToLowerInvariant()} – maintenance would also recover capacity.");
                }

                return sb.ToString().TrimEnd();
            }

            if (ContainsAny(q, "maint", "health", "rul", "fail", "predict", "wear", "break"))
            {
                var sb = new StringBuilder("Asset health (sensor-based estimate, worst first):\n");
                foreach (var m in s.Machines.OrderBy(m => m.HealthEstimate))
                {
                    string rul = m.RulLearning ? "learning" : m.RulSeconds.HasValue ? $"RUL {LineIntelligenceHub.FormatDuration(m.RulSeconds.Value)} ({m.RulConfidence:P0})" : "no degradation trend";
                    sb.AppendLine($"• {m.Name}: health {m.HealthEstimate:P0}, {rul}, {m.FailureMode}{(m.MaintenanceState != "None" ? $" – {m.MaintenanceState}" : string.Empty)}");
                }

                var worst = s.Machines.OrderBy(m => m.HealthEstimate).First();
                sb.Append($"Recommendation for {worst.Name}: {worst.RecommendedAction}");
                return sb.ToString();
            }

            if (ContainsAny(q, "quality", "defect", "reject", "yield", "cpk", "spc", "scrap"))
            {
                var l = s.Line;
                var sb = new StringBuilder($"Yield {l.Yield:P1} ({l.GoodParts} good / {l.BadParts} rejected).\n");
                foreach (var m in s.Machines)
                {
                    var spc = m.KeySpc;
                    string cpk = spc?.Cpk.HasValue == true ? spc.Cpk.Value.ToString("0.00", Inv) : "n/a";
                    sb.AppendLine($"• {m.Name}: quality {m.Quality:P1}; {spc?.Characteristic ?? "-"} Cpk {cpk}{(spc?.IsOutOfControl == true ? $" – OUT OF CONTROL ({spc.LastViolation})" : string.Empty)}");
                }

                return sb.ToString().TrimEnd();
            }

            if (ContainsAny(q, "energy", "power", "kwh", "co2", "carbon", "electric"))
            {
                var l = s.Line;
                return $"Line power {l.PowerKw:F2} kW (XTS track {l.TrackKw:F2}, machines {l.MachinesKw:F2}, robots {l.RobotsKw:F2}).\n" +
                       $"Energy {l.EnergyKwh:F3} kWh, {l.WhPerGoodPart:F0} Wh per good module, {l.Co2Kg * 1000:F0} g CO₂ at {EnergyMonitor.GridCarbonIntensityKgPerKwh} kg/kWh.\n" +
                       "Machines dominate the load; the XTS track itself draws well under 1 kW, so throughput is the biggest lever on Wh per module.";
            }

            if (ContainsAny(q, "oee", "efficien", "availability", "performance"))
            {
                var l = s.Line;
                var sb = new StringBuilder($"Line OEE {l.Oee:P1} = A {l.Availability:P1} × P {l.Performance:P1} × Q {l.Quality:P1}.\n");
                foreach (var m in s.Machines)
                {
                    sb.AppendLine($"• {m.Name}: OEE {m.Oee:P1} (A {m.Availability:P0} P {m.Performance:P0} Q {m.Quality:P0}) – main loss: {m.DominantLoss}");
                }

                return sb.ToString().TrimEnd();
            }

            if (ContainsAny(q, "wip", "lead", "flow", "little", "release"))
            {
                var l = s.Line;
                return $"WIP {l.Wip} (cap {l.WipCap}); Little's law estimate {l.LittlesLawWip:F1} = {l.ThroughputPerMinute / 60:F3}/s × {l.LeadTimeSeconds:F0} s lead time. " +
                       (s.AutopilotEnabled ? "The autopilot sizes the release cap from the critical WIP W₀ = r_b × T₀." : "Enable the autopilot to size release from the critical WIP W₀ = r_b × T₀.");
            }

            if (ContainsAny(q, "autopilot", "decision", "why did", "action"))
            {
                if (s.Decisions.Count == 0)
                {
                    return s.AutopilotEnabled ? "The autopilot is on but has not needed to act yet." : "The autopilot is off. Turn it on to let the AI control release (critical WIP) and predictive maintenance.";
                }

                var sb = new StringBuilder("Most recent autopilot decisions:\n");
                foreach (var d in s.Decisions.Reverse().Take(5))
                {
                    sb.AppendLine($"• t={d.SimTime:F0}s [{d.Category}] {d.Action} – {d.Rationale}");
                }

                return sb.ToString().TrimEnd();
            }

            var top = s.Insights.Take(3).ToList();
            var summary = new StringBuilder($"Line status: OEE {s.Line.Oee:P0}, {s.Line.ThroughputPerMinute:F2} modules/min, yield {s.Line.Yield:P1}, {s.Line.PowerKw:F1} kW.\n");
            if (top.Count == 0)
            {
                summary.Append("No findings yet. Ask about: bottleneck, health/maintenance, quality/SPC, energy, OEE, WIP or a machine (M0–M3).");
            }
            else
            {
                summary.AppendLine("Top findings:");
                foreach (var i in top)
                {
                    summary.AppendLine($"• [{i.Severity}] {i.Title} – {i.Recommendation}");
                }
            }

            return summary.ToString().TrimEnd();
        }

        /// <summary>Compact, grounded JSON view of the line for the LLM copilot.</summary>
        public static string BuildLlmContextJson(IntelligenceSnapshot s)
        {
            static double R(double v, int d = 3) => Math.Round(v, d);
            static double[] Tail(double[] values, int n = 12) => values.Skip(Math.Max(0, values.Length - n)).Select(v => Math.Round(v, 3)).ToArray();

            var payload = new
            {
                product = "12S prismatic EV battery module",
                simTimeSeconds = R(s.SimTimeSeconds, 1),
                autopilotEnabled = s.AutopilotEnabled,
                line = new
                {
                    oee = R(s.Line.Oee), availability = R(s.Line.Availability), performance = R(s.Line.Performance), quality = R(s.Line.Quality),
                    throughputPerMinute = R(s.Line.ThroughputPerMinute), forecastPerHour = R(s.Line.ForecastPerHour, 1),
                    leadTimeSeconds = R(s.Line.LeadTimeSeconds, 1), wip = s.Line.Wip, wipCap = s.Line.WipCap, littlesLawWip = R(s.Line.LittlesLawWip, 2),
                    powerKw = R(s.Line.PowerKw), energyKwh = R(s.Line.EnergyKwh, 4), whPerGoodPart = R(s.Line.WhPerGoodPart, 1), co2Kg = R(s.Line.Co2Kg, 4),
                    yield = R(s.Line.Yield), good = s.Line.GoodParts, rejected = s.Line.BadParts,
                    bottleneck = s.Line.BottleneckName, bottleneckShare = R(s.Line.BottleneckShare), bottleneckShifting = s.Line.BottleneckShifting
                },
                machines = s.Machines.Select(m => new
                {
                    id = m.MachineId, name = m.Name, failureMode = m.FailureMode, activity = m.Activity.ToString(),
                    healthEstimate = R(m.HealthEstimate), rulSeconds = m.RulSeconds.HasValue ? R(m.RulSeconds.Value, 0) : (double?)null,
                    rulConfidence = R(m.RulConfidence), anomalyScore = R(m.AnomalyScore), anomaly = m.AnomalyFlag, anomalyChannel = m.AnomalyChannel,
                    temperatureC = R(m.TemperatureC, 1), vibrationMmS = R(m.VibrationRms, 2), vibrationZoneIso10816 = m.VibrationZone,
                    primarySensor = new { name = m.PrimaryName, unit = m.PrimaryUnit, value = R(m.PrimaryValue), healthy = m.PrimaryHealthy, atFailure = m.PrimaryAtFailure, recent = Tail(m.PrimaryHistory) },
                    oee = R(m.Oee), availability = R(m.Availability), performance = R(m.Performance), quality = R(m.Quality), utilization = R(m.Utilization),
                    dominantLoss = m.DominantLoss, bottleneckShare = R(m.BottleneckShare), cycleTimeFactor = R(m.CycleTimeFactor),
                    maintenance = m.MaintenanceState, maintenanceCount = m.MaintenanceCount, breakdowns = m.BreakdownCount,
                    keyCharacteristic = m.KeySpc == null ? null : new
                    {
                        name = m.KeySpc.Characteristic, unit = m.KeySpc.Unit.Trim(), lsl = m.KeySpc.LowerSpecLimit, usl = m.KeySpc.UpperSpecLimit,
                        nominal = m.KeySpc.CenterLine, cpk = m.KeySpc.Cpk.HasValue ? R(m.KeySpc.Cpk.Value, 2) : (double?)null,
                        outOfControl = m.KeySpc.IsOutOfControl, lastRuleViolation = m.KeySpc.LastViolation, recent = Tail(m.KeySpc.Values)
                    },
                    recommendedAction = m.RecommendedAction
                }),
                robots = s.Robots.Select(r => new { r.Name, machineId = r.MachineId, vacuumKpa = R(r.VacuumKpa, 1), health = R(r.Health), actionTimeFactor = R(r.ActionTimeFactor, 2) }),
                insights = s.Insights.Select(i => new { severity = i.Severity.ToString(), i.Category, i.Title, i.Evidence, i.Recommendation, confidence = R(i.Confidence, 2) }),
                recentAutopilotDecisions = s.Decisions.Reverse().Take(6).Select(d => new { simTime = R(d.SimTime, 0), d.Category, d.Action, d.Rationale }),
                activeFaultInjections = s.ActiveFaults
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
        }

        private static MachineIntelligenceState? FindMachine(string q, IntelligenceSnapshot s)
        {
            string[][] aliases =
            {
                new[] { "m0", "laser", "weld", "busbar", "stack" },
                new[] { "m1", "assembler", "screw", "torque", "cmu", "spindle", "fasten" },
                new[] { "m2", "inspector", "vision", "camera", "height", "inspection" },
                new[] { "m3", "tester", "test", "eol", "dc-ir", "hipot", "fixture" }
            };

            for (int i = 0; i < aliases.Length; i++)
            {
                if (aliases[i].Any(a => q.Contains(a, StringComparison.Ordinal)))
                {
                    return s.Machines.FirstOrDefault(m => m.MachineId == i);
                }
            }

            return s.Machines.FirstOrDefault(m => q.Contains(m.Name.ToLowerInvariant(), StringComparison.Ordinal));
        }

        private static string DescribeMachine(MachineIntelligenceState m, IntelligenceSnapshot s)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{m.Name} ({m.MachineType}) is {m.Activity}{(m.MaintenanceState != "None" ? $", maintenance {m.MaintenanceState}" : string.Empty)}.");
            string rul = m.RulLearning ? "still learning its degradation fingerprint" : m.RulSeconds.HasValue ? $"remaining useful life ≈ {LineIntelligenceHub.FormatDuration(m.RulSeconds.Value)} (confidence {m.RulConfidence:P0})" : "no degradation trend detected";
            sb.AppendLine($"Health index {m.HealthEstimate:P0}; {rul}. Watched failure mode: {m.FailureMode}.");
            sb.AppendLine($"Sensors: {m.PrimaryName} {m.PrimaryValue.ToString("0.##", Inv)} {m.PrimaryUnit} (healthy {m.PrimaryHealthy.ToString("0.##", Inv)}), temperature {m.TemperatureC:F1} °C, vibration {m.VibrationRms:F2} mm/s (ISO zone {m.VibrationZone}), anomaly score {m.AnomalyScore:P0}{(m.AnomalyFlag ? $" – ANOMALY on {m.AnomalyChannel}" : string.Empty)}.");
            sb.AppendLine($"OEE {m.Oee:P1} (A {m.Availability:P0} · P {m.Performance:P0} · Q {m.Quality:P0}); main loss: {m.DominantLoss}. Bottleneck share {m.BottleneckShare:P0}.");
            if (m.KeySpc != null)
            {
                string cpk = m.KeySpc.Cpk.HasValue ? m.KeySpc.Cpk.Value.ToString("0.00", Inv) : "n/a";
                sb.AppendLine($"SPC {m.KeySpc.Characteristic}: Cpk {cpk}{(m.KeySpc.IsOutOfControl ? $", out of control – {m.KeySpc.LastViolation}" : ", in control")}.");
            }

            var insight = s.Insights.FirstOrDefault(i => i.MachineId == m.MachineId && i.Severity >= InsightSeverity.Advisory);
            sb.Append(insight != null ? $"Recommendation: {insight.Recommendation}" : $"Maintenance procedure on file: {m.RecommendedAction}");
            return sb.ToString();
        }

        private static bool ContainsAny(string text, params string[] terms) => terms.Any(t => text.Contains(t, StringComparison.Ordinal));
    }
}
