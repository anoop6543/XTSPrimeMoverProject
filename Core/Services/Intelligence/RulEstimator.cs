using System;
using System.Collections.Generic;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    public sealed record RulEstimate(
        double HealthIndex,
        double? RemainingSeconds,
        double Confidence,
        string Method,
        bool IsLearning);

    /// <summary>
    /// Prognostics with an exponential degradation model (P-F curve):
    /// degradation y(τ) follows ln(y + Φ) = a + b·τ over operating time τ.
    /// The model is fitted online by least squares on the smoothed health indicator from the
    /// primary sensor, then extrapolated to the failure threshold (y = 1).
    /// Remaining operating time is converted to calendar time using the observed duty cycle.
    /// </summary>
    public sealed class RulEstimator
    {
        private const int WindowSize = 300;
        private const int MinimumPoints = 20;
        private const double PointInterval = 1.0;
        private static readonly int[] FitWindows = { 60, 150, 300 };

        private readonly List<double> _tau = new();
        private readonly List<double> _logY = new();
        private readonly Ewma _smoothedDegradation = new(0.12);
        private readonly Ewma _dutyCycle = new(0.02);
        private double _lastTau = double.NaN;
        private double _lastTime = double.NaN;
        private double _lastPointTime = double.NegativeInfinity;

        public RulEstimate Current { get; private set; } = new(1.0, null, 0, "Learning", true);

        public double SmoothedDegradation => _smoothedDegradation.Count == 0 ? 0 : _smoothedDegradation.Value;

        /// <param name="simTime">Calendar (simulation) time in seconds.</param>
        /// <param name="operatingTime">Operating-equivalent time τ (stress-weighted hours).</param>
        /// <param name="observedDegradation">Normalised degradation indicator from the sensor (0..1).</param>
        public RulEstimate Update(double simTime, double operatingTime, double observedDegradation)
        {
            double y = _smoothedDegradation.Update(observedDegradation);

            if (!double.IsNaN(_lastTime) && simTime > _lastTime)
            {
                double rate = (operatingTime - _lastTau) / (simTime - _lastTime);
                _dutyCycle.Update(Math.Clamp(rate, 0, 2));
            }

            _lastTau = operatingTime;
            _lastTime = simTime;

            double health = Math.Clamp(1.0 - y, 0, 1);
            if (simTime - _lastPointTime >= PointInterval)
            {
                _lastPointTime = simTime;
                _tau.Add(operatingTime);
                _logY.Add(Math.Log(Math.Max(1e-4, y + Degradation.LinearisingOffset)));
                if (_tau.Count > WindowSize)
                {
                    _tau.RemoveAt(0);
                    _logY.RemoveAt(0);
                }
            }

            if (_tau.Count < MinimumPoints || _tau[^1] - _tau[0] < 5.0)
            {
                Current = new RulEstimate(health, null, 0, "Learning degradation fingerprint", true);
                return Current;
            }

            double duty = Math.Max(0.05, _dutyCycle.Count == 0 ? 0.6 : _dutyCycle.Value);
            double phi = Degradation.LinearisingOffset;

            // Multi-horizon fits: short windows react to a sudden acceleration (fault onset), long windows
            // are precise for slow wear. Use the most conservative credible estimate.
            double? bestRemaining = null;
            double bestConfidence = 0;
            double fallbackRemaining = double.NaN;
            double fallbackConfidence = 0;
            foreach (int window in FitWindows)
            {
                if (_tau.Count < Math.Min(window, MinimumPoints))
                {
                    continue;
                }

                int n = Math.Min(window, _tau.Count);
                var x = _tau.GetRange(_tau.Count - n, n);
                var yLog = _logY.GetRange(_logY.Count - n, n);
                if (x[^1] - x[0] < 5.0)
                {
                    continue;
                }

                var fit = Regression.Fit(x, yLog);
                if (fit.Slope <= 1e-6)
                {
                    continue;
                }

                double tauFail = (Math.Log(1.0 + phi) - fit.Intercept) / fit.Slope;
                double remaining = Math.Max(0, tauFail - operatingTime) / duty;
                double spanFactor = Math.Clamp((x[^1] - x[0]) / 40.0, 0.2, 1.0);
                double confidence = Math.Clamp(fit.RSquared * spanFactor, 0, 0.99);

                if (fit.RSquared >= 0.6 && (bestRemaining == null || remaining < bestRemaining))
                {
                    bestRemaining = remaining;
                    bestConfidence = confidence;
                }

                fallbackRemaining = remaining;
                fallbackConfidence = confidence;
            }

            if (bestRemaining.HasValue)
            {
                Current = new RulEstimate(health, bestRemaining, bestConfidence, "Exponential P-F model (multi-horizon LSQ)", false);
            }
            else if (!double.IsNaN(fallbackRemaining))
            {
                Current = new RulEstimate(health, fallbackRemaining, Math.Min(fallbackConfidence, 0.3), "Exponential P-F model (weak trend)", false);
            }
            else
            {
                Current = new RulEstimate(health, null, 0.3, "Stable – no degradation trend", false);
            }

            return Current;
        }

        public void Reset()
        {
            _tau.Clear();
            _logY.Clear();
            _smoothedDegradation.Reset();
            _lastTau = double.NaN;
            _lastTime = double.NaN;
            _lastPointTime = double.NegativeInfinity;
            Current = new RulEstimate(1.0, null, 0, "Learning degradation fingerprint", true);
        }
    }
}
