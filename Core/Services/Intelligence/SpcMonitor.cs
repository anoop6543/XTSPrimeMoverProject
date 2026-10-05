using System;
using System.Collections.Generic;
using System.Linq;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    public enum SpcRule
    {
        None,
        /// <summary>WE1: one point beyond 3 sigma.</summary>
        BeyondThreeSigma,
        /// <summary>WE2: 2 of 3 consecutive points beyond 2 sigma on the same side.</summary>
        TwoOfThreeBeyondTwoSigma,
        /// <summary>WE3: 4 of 5 consecutive points beyond 1 sigma on the same side.</summary>
        FourOfFiveBeyondOneSigma,
        /// <summary>WE4: 8 consecutive points on the same side of the centre line.</summary>
        EightOnOneSide,
        /// <summary>Nelson 3: 6 consecutive points steadily increasing or decreasing.</summary>
        SixPointTrend
    }

    public sealed record SpcViolation(SpcRule Rule, int SampleIndex, double Value, string Description);

    /// <summary>
    /// Individuals control chart with Western Electric / Nelson run rules and rolling Cpk.
    /// Control limits come from the qualification study (centre = nominal, sigma = healthy sigma),
    /// the usual "standard given" setup once a process is released for production.
    /// </summary>
    public sealed class SpcMonitor
    {
        private const int HistoryCapacity = 60;
        private const int CpkWindow = 30;

        private readonly QualityCharacteristicSpec _spec;
        private readonly List<double> _values = new();
        private readonly List<int> _violationIndices = new();
        private long _sampleCounter;

        public SpcMonitor(QualityCharacteristicSpec spec)
        {
            _spec = spec;
            CenterLine = spec.Nominal;
            Sigma = spec.HealthySigma;
        }

        public QualityCharacteristicSpec Spec => _spec;
        public double CenterLine { get; }
        public double Sigma { get; }
        public double UpperControlLimit => CenterLine + 3 * Sigma;
        public double LowerControlLimit => CenterLine - 3 * Sigma;
        public long SampleCount => _sampleCounter;
        public int OutOfSpecCount { get; private set; }
        public SpcViolation? LastViolation { get; private set; }
        public long LastViolationSample { get; private set; } = -1;

        public IReadOnlyList<double> Values => _values;

        /// <summary>Indices (into <see cref="Values"/>) of samples that triggered a rule.</summary>
        public IReadOnlyList<int> ViolationIndices => _violationIndices;

        public SpcViolation? Add(double value)
        {
            _sampleCounter++;
            if (!_spec.IsInSpec(value))
            {
                OutOfSpecCount++;
            }

            _values.Add(value);
            if (_values.Count > HistoryCapacity)
            {
                _values.RemoveAt(0);
                for (int i = _violationIndices.Count - 1; i >= 0; i--)
                {
                    _violationIndices[i]--;
                    if (_violationIndices[i] < 0)
                    {
                        _violationIndices.RemoveAt(i);
                    }
                }
            }

            var violation = Evaluate();
            if (violation != null)
            {
                _violationIndices.Add(_values.Count - 1);
                LastViolation = violation;
                LastViolationSample = _sampleCounter;
            }

            return violation;
        }

        /// <summary>Samples since the last rule violation (large number = in control for a while).</summary>
        public long SamplesSinceViolation => LastViolationSample < 0 ? _sampleCounter : _sampleCounter - LastViolationSample;

        public bool IsOutOfControl => LastViolation != null && SamplesSinceViolation < 8;

        public double? Cpk
        {
            get
            {
                int n = Math.Min(CpkWindow, _values.Count);
                if (n < 8)
                {
                    return null;
                }

                var window = _values.Skip(_values.Count - n).ToList();
                double mean = window.Average();
                double sd = Math.Sqrt(window.Sum(v => (v - mean) * (v - mean)) / (n - 1));
                if (sd <= 1e-12)
                {
                    return null;
                }

                double cpk = double.MaxValue;
                if (_spec.UpperSpecLimit.HasValue) cpk = Math.Min(cpk, (_spec.UpperSpecLimit.Value - mean) / (3 * sd));
                if (_spec.LowerSpecLimit.HasValue) cpk = Math.Min(cpk, (mean - _spec.LowerSpecLimit.Value) / (3 * sd));
                return cpk == double.MaxValue ? null : cpk;
            }
        }

        public void ResetRuns()
        {
            LastViolation = null;
            LastViolationSample = -1;
        }

        private SpcViolation? Evaluate()
        {
            int n = _values.Count;
            double last = _values[n - 1];
            double z(int i) => (_values[i] - CenterLine) / Sigma;
            int index = n - 1;

            if (Math.Abs(z(index)) > 3)
            {
                return new SpcViolation(SpcRule.BeyondThreeSigma, index, last, "Point beyond 3σ control limit");
            }

            if (n >= 3 && CountSameSide(n - 3, 3, 2, out _) >= 2)
            {
                return new SpcViolation(SpcRule.TwoOfThreeBeyondTwoSigma, index, last, "2 of 3 points beyond 2σ (shift)");
            }

            if (n >= 5 && CountSameSide(n - 5, 5, 1, out _) >= 4)
            {
                return new SpcViolation(SpcRule.FourOfFiveBeyondOneSigma, index, last, "4 of 5 points beyond 1σ (small sustained shift)");
            }

            if (n >= 8)
            {
                bool allAbove = true, allBelow = true;
                for (int i = n - 8; i < n; i++)
                {
                    allAbove &= z(i) > 0;
                    allBelow &= z(i) < 0;
                }

                if (allAbove || allBelow)
                {
                    return new SpcViolation(SpcRule.EightOnOneSide, index, last, $"8 points {(allAbove ? "above" : "below")} centre line (mean shift)");
                }
            }

            if (n >= 6)
            {
                bool up = true, down = true;
                for (int i = n - 5; i < n; i++)
                {
                    up &= _values[i] > _values[i - 1];
                    down &= _values[i] < _values[i - 1];
                }

                if (up || down)
                {
                    return new SpcViolation(SpcRule.SixPointTrend, index, last, $"6 points steadily {(up ? "increasing" : "decreasing")} (drift)");
                }
            }

            return null;
        }

        private int CountSameSide(int start, int length, double sigmaMultiple, out int side)
        {
            int above = 0, below = 0;
            for (int i = start; i < start + length; i++)
            {
                double zi = (_values[i] - CenterLine) / Sigma;
                if (zi > sigmaMultiple) above++;
                if (zi < -sigmaMultiple) below++;
            }

            side = above >= below ? 1 : -1;
            return Math.Max(above, below);
        }
    }
}
