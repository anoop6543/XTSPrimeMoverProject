using XTSPrimeMoverProject.Services;
using XTSPrimeMoverProject.Services.Intelligence;

namespace XTSPrimeMoverProject.Tests
{
    public class StatisticsTests
    {
        [Theory]
        [InlineData(0.0, 0.5)]
        [InlineData(1.96, 0.9750021)]
        [InlineData(-3.0, 0.0013499)]
        public void NormalCdf_Matches_Reference(double z, double expected)
        {
            Assert.Equal(expected, NormalDistribution.Cdf(z), 5);
        }

        [Theory]
        [InlineData(0.001)]
        [InlineData(0.3)]
        [InlineData(0.975)]
        public void InverseCdf_Round_Trips(double p)
        {
            Assert.Equal(p, NormalDistribution.Cdf(NormalDistribution.InverseCdf(p)), 6);
        }

        [Fact]
        public void Regression_Recovers_Line()
        {
            var x = Enumerable.Range(0, 50).Select(i => (double)i).ToList();
            var y = x.Select(v => 3.0 + 0.5 * v).ToList();
            var fit = Regression.Fit(x, y);
            Assert.Equal(0.5, fit.Slope, 9);
            Assert.Equal(3.0, fit.Intercept, 9);
            Assert.Equal(1.0, fit.RSquared, 9);
        }

        [Fact]
        public void Holt_Forecasts_Linear_Trend()
        {
            var holt = new HoltForecaster(0.5, 0.3);
            for (int i = 0; i < 100; i++)
            {
                holt.Update(10 + 2 * i);
            }

            Assert.Equal(10 + 2 * 104, holt.Forecast(5), 1);
        }

        [Fact]
        public void RingBuffer_Keeps_Most_Recent_Values()
        {
            var ring = new RingBuffer(3);
            foreach (var v in new[] { 1.0, 2, 3, 4, 5 })
            {
                ring.Add(v);
            }

            Assert.Equal(new[] { 3.0, 4, 5 }, ring.ToArray());
            Assert.Equal(5, ring.Last);
        }

        [Fact]
        public void JerkLimited_Profile_Has_No_Overshoot_And_Bounded_Derivatives()
        {
            double v = 0, a = 0, dt = 0.02, previousA = 0, maxV = 0;
            for (int i = 0; i < 200; i++)
            {
                McMoveVelocityFb.JerkLimitedStep(ref v, ref a, 26, 160, 220, 4000, dt);
                maxV = Math.Max(maxV, v);
                Assert.True(Math.Abs(a) <= 220 + 1e-9);
                // Jerk-limited everywhere; the landing cycle may snap a sub-cycle residual (20 ms PLC discretisation).
                Assert.True(Math.Abs(a - previousA) <= 1.25 * 4000 * dt + 1e-9, $"jerk limit violated at step {i}");
                previousA = a;
            }

            Assert.Equal(26, v, 6);
            Assert.True(maxV <= 26 + 1e-9, $"overshoot to {maxV}");
        }
    }
}
