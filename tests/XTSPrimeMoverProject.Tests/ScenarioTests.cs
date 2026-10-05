using System.Text.Json;
using XTSPrimeMoverProject.Services.Intelligence;
using Xunit.Abstractions;

namespace XTSPrimeMoverProject.Tests
{
    /// <summary>End-to-end what-if scenarios on the real engine.</summary>
    public class ScenarioTests
    {
        private readonly ITestOutputHelper _output;

        public ScenarioTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void Injected_Fault_Is_Detected_And_Autopilot_Prevents_Breakdown()
        {
            using var engine = LineSimulationTests.CreateEngine(seed: 11, autopilot: true);
            engine.AdvanceManually(60);
            engine.InjectFault(FaultScenario.LaserOpticsContamination);

            bool anomalySeen = false, pmScheduled = false;
            for (int i = 0; i < 240; i++)
            {
                engine.AdvanceManually(1);
                var m0 = engine.GetIntelligenceSnapshot().Machines[0];
                anomalySeen |= m0.AnomalyFlag;
                pmScheduled |= m0.MaintenanceCount > 0 || m0.MaintenanceState.Contains("planned");
            }

            var s = engine.GetIntelligenceSnapshot();
            foreach (var d in s.Decisions) _output.WriteLine($"t={d.SimTime:F0} [{d.Category}] {d.Action}: {d.Rationale}");
            _output.WriteLine($"M0 breakdowns {s.Machines[0].BreakdownCount}, PMs {s.Machines[0].MaintenanceCount}, good {s.Line.GoodParts} bad {s.Line.BadParts}");

            Assert.True(anomalySeen, "anomaly detector should flag the injected optics contamination");
            Assert.True(pmScheduled, "autopilot should schedule predictive maintenance");
            Assert.Equal(0, s.Machines[0].BreakdownCount);
            Assert.Contains(s.Decisions, d => d.Category == "Predictive maintenance");
        }

        [Fact]
        public void Without_Autopilot_The_Same_Fault_Ends_In_Breakdown_And_Scrap()
        {
            using var engine = LineSimulationTests.CreateEngine(seed: 11, autopilot: false);
            engine.AdvanceManually(60);
            engine.InjectFault(FaultScenario.LaserOpticsContamination);
            engine.AdvanceManually(240);

            var s = engine.GetIntelligenceSnapshot();
            _output.WriteLine($"M0 breakdowns {s.Machines[0].BreakdownCount}, good {s.Line.GoodParts} bad {s.Line.BadParts}, weld Cpk {s.Machines[0].KeySpc?.Cpk:F2}");
            Assert.True(s.Machines[0].BreakdownCount >= 1);
            Assert.True(s.Machines[0].KeySpc!.Cpk < 1.0, "weld depth capability should collapse while the optics degrade");
        }

        [Fact]
        public void Copilot_Answers_From_Live_Data_And_Builds_Grounded_Llm_Context()
        {
            using var engine = LineSimulationTests.CreateEngine(seed: 5);
            engine.AdvanceManually(180);

            string bottleneck = engine.AskCopilot("What is the bottleneck?");
            string machine = engine.AskCopilot("How is the laser welder doing?");
            string energy = engine.AskCopilot("energy per module?");
            _output.WriteLine(bottleneck);
            _output.WriteLine(machine);
            _output.WriteLine(energy);

            Assert.Contains("constraint", bottleneck, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Laser Welder", machine);
            Assert.Contains("Wh per good module", energy);

            using var doc = JsonDocument.Parse(engine.GetCopilotContextJson());
            Assert.Equal(4, doc.RootElement.GetProperty("machines").GetArrayLength());
            Assert.True(doc.RootElement.GetProperty("line").GetProperty("good").GetInt32() > 0);
        }

        [Fact]
        public void Operator_Maintenance_Request_Runs_Through_Lifecycle()
        {
            using var engine = LineSimulationTests.CreateEngine(seed: 3);
            engine.AdvanceManually(30);
            Assert.True(engine.RequestMaintenance(2));
            Assert.False(engine.RequestMaintenance(2)); // already queued

            bool sawInProgress = false;
            for (int i = 0; i < 90; i++)
            {
                engine.AdvanceManually(1);
                sawInProgress |= engine.Machines[2].Maintenance == XTSPrimeMoverProject.Models.MaintenanceMode.InProgress;
            }

            Assert.True(sawInProgress);
            Assert.Equal(XTSPrimeMoverProject.Models.MaintenanceMode.None, engine.Machines[2].Maintenance);
            Assert.Equal(1, engine.Machines[2].MaintenanceCount);
        }

        [Fact]
        public void Measurements_Are_Recorded_On_Every_Part()
        {
            using var engine = LineSimulationTests.CreateEngine(seed: 9);
            engine.AdvanceManually(200);
            var inFlight = engine.Movers.Where(m => m.CurrentPart != null && m.CurrentPart.MachinesCompleted > 0).Select(m => m.CurrentPart!).ToList();
            Assert.NotEmpty(inFlight);
            Assert.All(inFlight, p => Assert.True(p.Measurements.Count >= 4));
        }
    }
}
