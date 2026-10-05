using XTSPrimeMoverProject.Models;
using XTSPrimeMoverProject.Services;
using Xunit.Abstractions;

namespace XTSPrimeMoverProject.Tests
{
    /// <summary>Runs the real engine headless and checks physical and logical invariants of the line.</summary>
    public class LineSimulationTests
    {
        private readonly ITestOutputHelper _output;

        public LineSimulationTests(ITestOutputHelper output) => _output = output;

        internal static XTSSimulationEngine CreateEngine(int seed, bool autopilot = false)
        {
            string db = Path.Combine(Path.GetTempPath(), $"xts-test-{Guid.NewGuid():N}.db");
            return new XTSSimulationEngine(InlineSimulationDispatcher.Instance, new SimulationOptions { Seed = seed, DatabasePath = db, AutopilotEnabled = autopilot });
        }

        [Fact]
        public void Line_Produces_Modules_Without_Collisions_Or_Deadlock()
        {
            using var engine = CreateEngine(seed: 7);
            double minGap = double.MaxValue;
            int lastProduced = 0;
            double lastProgressAt = 0;

            for (int i = 0; i < 6000; i++) // 600 s of simulation
            {
                engine.AdvanceManually(0.1);
                minGap = Math.Min(minGap, MinimumGap(engine.Movers));
                if (engine.TotalPartsProduced != lastProduced)
                {
                    lastProduced = engine.TotalPartsProduced;
                    lastProgressAt = engine.SimulationTimeSeconds;
                }

                // First module needs one full lead time (≈2 min); afterwards exits must keep coming.
                double allowed = lastProduced == 0 ? 200 : 120;
                Assert.True(engine.SimulationTimeSeconds - lastProgressAt < allowed, $"No module left the line for {allowed:F0} s at t={engine.SimulationTimeSeconds:F0}s (deadlock?)");
            }

            var snapshot = engine.GetIntelligenceSnapshot();
            _output.WriteLine($"Produced {engine.TotalPartsProduced} (good {engine.GoodPartsCount}, bad {engine.BadPartsCount}) in {engine.SimulationTimeSeconds:F0}s; min gap {minGap:F2}°");
            _output.WriteLine($"Throughput {snapshot.Line.ThroughputPerMinute:F2}/min, lead time {snapshot.Line.LeadTimeSeconds:F1}s, OEE {snapshot.Line.Oee:P1}, bottleneck {snapshot.Line.BottleneckName} ({snapshot.Line.BottleneckShare:P0}), {snapshot.Line.WhPerGoodPart:F0} Wh/part, {snapshot.Line.PowerKw:F2} kW");
            foreach (var m in snapshot.Machines)
            {
                _output.WriteLine($"  {m.Name}: OEE {m.Oee:P0} A{m.Availability:P0} P{m.Performance:P0} Q{m.Quality:P0} util {m.Utilization:P0} health {m.HealthEstimate:P0} (true {m.TrueHealth:P0}) rul {(m.RulSeconds.HasValue ? m.RulSeconds.Value.ToString("F0") : "-")} anomaly {m.AnomalyScore:P0} ({m.AnomalyChannel}{(m.AnomalyFlag ? "!" : "")}) temp {m.TemperatureC:F1} vib {m.VibrationRms:F2} maint {m.MaintenanceCount} bd {m.BreakdownCount}");
            }

            foreach (var w in engine.GetWatchdogStatus())
            {
                _output.WriteLine($"  watchdog {w.Code} x{w.TriggerCount}: {w.LastMessage}");
            }

            Assert.True(minGap >= XTSSimulationEngine.MoverPitchDegrees - 0.05, $"Movers came closer than the pitch: {minGap:F2}°");
            Assert.True(engine.TotalPartsProduced >= 20, $"Expected at least 20 modules in the first 10 minutes (incl. ramp-up), got {engine.TotalPartsProduced}");
        }

        internal static double MinimumGap(IReadOnlyList<Mover> movers)
        {
            var positions = movers.Select(m => m.Position).OrderBy(p => p).ToList();
            double min = double.MaxValue;
            for (int i = 0; i < positions.Count; i++)
            {
                double next = i == positions.Count - 1 ? positions[0] + 360 : positions[i + 1];
                min = Math.Min(min, next - positions[i]);
            }

            return min;
        }
    }
}
