using System;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    /// <summary>
    /// Residual-based anomaly detector for one sensor channel.
    /// <para>
    /// The residual is measurement minus a physics expectation (e.g. measured temperature minus the
    /// healthy shadow-model temperature for the same duty), so normal load changes are not flagged.
    /// The noise level is learned during commissioning; the baseline then adapts slowly
    /// (τ ≈ 40 s) so ordinary slow wear is left to the prognostics channel, while abrupt or
    /// accelerating deviations are caught by an EWMA control statistic and a two-sided CUSUM.
    /// Learning is frozen while the channel is anomalous (never learn from abnormal data).
    /// </para>
    /// </summary>
    public interface IAnomalyChannel
    {
        string Channel { get; }
        bool IsAnomalous { get; }
        double Score { get; }
        string Direction { get; }
        double EwmaZ { get; }
        double CusumHigh { get; }
        double CusumLow { get; }
        void Recommission();
    }

    public sealed class ResidualAnomalyDetector : IAnomalyChannel
    {
        private const double CusumK = 1.0;
        private const double CusumH = 10.0;
        private const double EwmaLimit = 4.5;

        private readonly int _commissioningSamples;
        private readonly double _minimumSigma;
        private readonly double _baselineLambda;
        private readonly Ewma _smoothed;
        private double _sum;
        private double _sumSquares;
        private int _learned;

        /// <param name="channel">Channel name for explanations.</param>
        /// <param name="commissioningSamples">Samples used to learn the noise fingerprint.</param>
        /// <param name="baselineLambda">Per-sample adaptation rate of the baseline.</param>
        /// <param name="minimumSigma">Noise floor to avoid over-sensitivity on very clean signals.</param>
        public ResidualAnomalyDetector(string channel, int commissioningSamples = 60, double baselineLambda = 0.0125, double minimumSigma = 1e-3)
        {
            Channel = channel;
            _commissioningSamples = Math.Max(10, commissioningSamples);
            _minimumSigma = minimumSigma;
            _baselineLambda = Math.Clamp(baselineLambda, 0, 1);
            _smoothed = new Ewma(0.15);
        }

        public string Channel { get; }
        public bool IsCommissioned => _learned >= _commissioningSamples;
        public double Baseline { get; private set; }
        public double NoiseSigma { get; private set; }
        public double EwmaZ { get; private set; }
        public double CusumHigh { get; private set; }
        public double CusumLow { get; private set; }

        /// <summary>0..1 anomaly severity.</summary>
        public double Score { get; private set; }

        public bool IsAnomalous => IsCommissioned && (Math.Abs(EwmaZ) > EwmaLimit || CusumHigh > CusumH || CusumLow > CusumH);

        public string Direction => CusumHigh + Math.Max(0, EwmaZ) >= CusumLow + Math.Max(0, -EwmaZ) ? "high" : "low";

        public void Add(double residual)
        {
            if (!IsCommissioned)
            {
                _learned++;
                _sum += residual;
                _sumSquares += residual * residual;
                if (IsCommissioned)
                {
                    Baseline = _sum / _learned;
                    double variance = Math.Max(0, (_sumSquares - _learned * Baseline * Baseline) / (_learned - 1));
                    NoiseSigma = Math.Max(_minimumSigma, Math.Sqrt(variance));
                    _smoothed.Reset();
                }

                return;
            }

            double z = (residual - Baseline) / NoiseSigma;
            double ewma = _smoothed.Update(z);
            double ewmaSigma = Math.Sqrt(_smoothed.Lambda / (2 - _smoothed.Lambda));
            EwmaZ = ewma / ewmaSigma;

            CusumHigh = Math.Max(0, CusumHigh + z - CusumK);
            CusumLow = Math.Max(0, CusumLow - z - CusumK);

            double cusumScore = Math.Max(CusumHigh, CusumLow) / (2 * CusumH);
            Score = Math.Clamp(Math.Max(Math.Abs(EwmaZ) / (1.5 * EwmaLimit), cusumScore), 0, 1);

            // Learn slowly while anomalous: a genuine fault keeps outrunning the baseline and stays flagged,
            // while a false alarm on slow, normal wear clears itself instead of latching forever.
            Baseline += (IsAnomalous ? _baselineLambda * 0.25 : _baselineLambda) * (residual - Baseline);
        }

        /// <summary>After maintenance the asset is a "new" asset: relearn its fingerprint.</summary>
        public void Recommission()
        {
            _learned = 0;
            _sum = 0;
            _sumSquares = 0;
            EwmaZ = 0;
            CusumHigh = 0;
            CusumLow = 0;
            Score = 0;
            _smoothed.Reset();
        }
    }

    /// <summary>
    /// Context-aware detector: separate fingerprints for running and idle duty, because e.g. bearing
    /// vibration is only representative while the spindle turns. Each context has its own baseline.
    /// </summary>
    public sealed class ContextualAnomalyDetector : IAnomalyChannel
    {
        private readonly ResidualAnomalyDetector _running;
        private readonly ResidualAnomalyDetector _idle;

        public ContextualAnomalyDetector(string channel, double minimumSigma, double baselineLambda = 0.025)
        {
            Channel = channel;
            _running = new ResidualAnomalyDetector(channel, 40, baselineLambda, minimumSigma);
            _idle = new ResidualAnomalyDetector(channel, 40, baselineLambda, minimumSigma);
        }

        public string Channel { get; }
        public bool IsAnomalous => _running.IsAnomalous || _idle.IsAnomalous;
        public double Score => Math.Max(_running.Score, _idle.Score);
        private ResidualAnomalyDetector Worst => _running.Score >= _idle.Score ? _running : _idle;
        public string Direction => Worst.Direction;
        public double EwmaZ => Worst.EwmaZ;
        public double CusumHigh => Worst.CusumHigh;
        public double CusumLow => Worst.CusumLow;

        public void Add(double residual, bool running) => (running ? _running : _idle).Add(residual);

        public void Recommission()
        {
            _running.Recommission();
            _idle.Recommission();
        }
    }
}
