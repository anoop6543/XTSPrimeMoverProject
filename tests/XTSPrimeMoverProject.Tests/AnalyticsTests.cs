using XTSPrimeMoverProject.Services.Intelligence;

namespace XTSPrimeMoverProject.Tests
{
    public class AnalyticsTests
    {
        private static QualityCharacteristicSpec WeldDepth => new()
        {
            Name = "Weld penetration depth", Unit = " mm", Nominal = 1.20, LowerSpecLimit = 1.00, UpperSpecLimit = 1.40, HealthyCpk = 1.10
        };

        [Fact]
        public void HealthySigma_Is_Consistent_With_Cpk()
        {
            var spec = WeldDepth;
            Assert.Equal(0.2 / 3.3, spec.HealthySigma, 9);
            // Centred two-sided process: P(out) = 2·Φ(-3·Cpk)
            double expected = 2 * NormalDistribution.Cdf(-3 * 1.10);
            Assert.Equal(expected, spec.DefectProbability(spec.Nominal, spec.HealthySigma), 9);
        }

        [Fact]
        public void Every_Station_Has_A_Characteristic_And_One_Key_Per_Machine()
        {
            foreach (var type in Enum.GetValues<XTSPrimeMoverProject.Models.MachineType>())
            {
                var machine = new XTSPrimeMoverProject.Models.Machine(0, "m", type, 0);
                for (int i = 0; i < machine.Stations.Count; i++)
                {
                    Assert.NotNull(EvModuleQualityCatalog.GetCharacteristic(type, i));
                }

                Assert.Single(EvModuleQualityCatalog.GetCharacteristics(type), c => c.IsKeyCharacteristic);
            }
        }

        [Fact]
        public void Spc_Detects_Sustained_Mean_Shift_Quickly()
        {
            var spc = new SpcMonitor(WeldDepth);
            var rng = new Random(1);
            for (int i = 0; i < 30; i++)
            {
                spc.Add(NormalDistribution.Sample(rng, 1.20, WeldDepth.HealthySigma));
            }

            int detectedAfter = -1;
            for (int i = 0; i < 30 && detectedAfter < 0; i++)
            {
                if (spc.Add(NormalDistribution.Sample(rng, 1.20 - 1.5 * WeldDepth.HealthySigma, WeldDepth.HealthySigma)) != null)
                {
                    detectedAfter = i + 1;
                }
            }

            Assert.InRange(detectedAfter, 1, 12);
        }

        [Fact]
        public void Spc_Rule1_Fires_On_Outlier_And_Cpk_Tracks_Capability()
        {
            var spc = new SpcMonitor(WeldDepth);
            var rng = new Random(2);
            for (int i = 0; i < 30; i++)
            {
                spc.Add(NormalDistribution.Sample(rng, 1.20, WeldDepth.HealthySigma));
            }

            Assert.InRange(spc.Cpk!.Value, 0.75, 1.6);
            var violation = spc.Add(1.20 + 3.5 * WeldDepth.HealthySigma);
            Assert.Equal(SpcRule.BeyondThreeSigma, violation!.Rule);
        }

        [Fact]
        public void AnomalyDetector_Is_Quiet_On_Noise_And_Slow_Drift_But_Catches_Fast_Change()
        {
            var rng = new Random(3);
            var detector = new ResidualAnomalyDetector("test");
            int flagged = 0;
            for (int i = 0; i < 4000; i++)
            {
                detector.Add(NormalDistribution.Sample(rng) + 0.002 * i * 0.25); // slow wear: 0.5σ per 1000 samples
                if (detector.IsAnomalous) flagged++;
            }

            Assert.True(flagged < 40, $"false alarm samples: {flagged}");

            double level = 0.002 * 4000 * 0.25;
            int detectedAfter = -1;
            for (int i = 0; i < 200 && detectedAfter < 0; i++)
            {
                level += 0.25; // fault: 0.25σ per sample
                detector.Add(NormalDistribution.Sample(rng) + level);
                if (detector.IsAnomalous) detectedAfter = i + 1;
            }

            Assert.InRange(detectedAfter, 1, 30);
        }

        [Fact]
        public void Rul_Converges_On_Exponential_Degradation()
        {
            // Ground truth: damage grows linearly in operating time, effect follows the P-F curve.
            var rul = new RulEstimator();
            var rng = new Random(4);
            double damageRate = 1.0 / 600.0; // fails at τ = 600 s
            double damage0 = 0.2;
            RulEstimate estimate = rul.Current;
            double t = 0;
            for (; t < 300; t += 0.5)
            {
                double damage = damage0 + damageRate * t;
                double effect = (Math.Exp(3 * damage) - 1) / (Math.Exp(3) - 1);
                estimate = rul.Update(t, t, effect + NormalDistribution.Sample(rng, 0, 0.01));
            }

            double trueRemaining = (1 - (damage0 + damageRate * t)) / damageRate;
            Assert.False(estimate.IsLearning);
            Assert.NotNull(estimate.RemainingSeconds);
            Assert.InRange(estimate.RemainingSeconds!.Value, trueRemaining * 0.7, trueRemaining * 1.3);
            Assert.True(estimate.Confidence > 0.5);
        }

        [Fact]
        public void Oee_Follows_Iso22400_Time_Model()
        {
            var oee = new OeeTracker(idealCycleTimeSeconds: 10);
            oee.Accumulate(MachineActivity.Running, 80);
            oee.Accumulate(MachineActivity.Starved, 10);
            oee.Accumulate(MachineActivity.Down, 10);
            oee.Accumulate(MachineActivity.PlannedMaintenance, 50); // excluded from planned time
            for (int i = 0; i < 7; i++) oee.RecordPart(rejected: i == 0);

            Assert.Equal(0.9, oee.Availability, 9);          // 90 / 100
            Assert.Equal(70.0 / 90.0, oee.Performance, 9);   // 7 x 10 / 90
            Assert.Equal(6.0 / 7.0, oee.Quality, 9);
            Assert.Equal(0.9 * 70.0 / 90.0 * 6.0 / 7.0, oee.Oee, 9);
        }

        [Fact]
        public void Bottleneck_Detector_Finds_Machine_With_Longest_Active_Periods()
        {
            var detector = new BottleneckDetector(3);
            for (int t = 0; t < 3000; t++)
            {
                // machine 0: active 4 of 10 s, machine 1: active 9 of 10 s, machine 2: active 5 of 10 s
                var acts = new[]
                {
                    t % 10 < 4 ? MachineActivity.Running : MachineActivity.Starved,
                    t % 10 < 9 ? MachineActivity.Running : MachineActivity.Blocked,
                    (t + 3) % 10 < 5 ? MachineActivity.Running : MachineActivity.Starved
                };
                detector.Step(acts, 1.0);
            }

            // Machine 1 holds the longest active periods; ties at synchronous starts are shared.
            Assert.Equal(1, detector.AverageBottleneck);
            Assert.Equal(0.6, detector.Share(1), 2);
            Assert.Equal(0.3, detector.Share(2), 2);
        }

        [Fact]
        public void Energy_Model_Is_Physically_Plausible()
        {
            double cruise = EnergyMonitor.MoverPowerWatts(0.43, 0, 11.8);
            double accelerating = EnergyMonitor.MoverPowerWatts(0.43, 2.7, 11.8);
            double standstill = EnergyMonitor.MoverPowerWatts(0, 0, 11.8);
            Assert.InRange(standstill, 2, 5);
            Assert.InRange(cruise, 3, 15);
            Assert.True(accelerating > cruise * 2);
        }

        [Fact]
        public void Release_Control_Uses_Critical_Wip()
        {
            var ap = new AutopilotController { Enabled = true };
            // r_b = 1/16.6 s, T0 = 98 s → W0 ≈ 5.9 → cap = ceil(1.35·W0) = 8 (the empirical optimum of the line)
            bool changed = ap.UpdateReleaseControl(simTime: 100, bottleneckRatePerSecond: 1 / 16.6, rawProcessTimeSeconds: 98, bottleneckName: "Tester");
            Assert.True(changed);
            Assert.Equal(8, ap.WipCap);
            Assert.Contains(ap.Decisions, d => d.Category == "Release control" && d.Rationale.Contains("W₀"));
        }
    }
}
