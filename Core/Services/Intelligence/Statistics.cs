using System;
using System.Collections.Generic;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    /// <summary>Standard normal distribution helpers.</summary>
    public static class NormalDistribution
    {
        /// <summary>Standard normal CDF (Zelen &amp; Severo / A&amp;S 26.2.17, |error| &lt; 7.5e-8).</summary>
        public static double Cdf(double z)
        {
            if (double.IsNaN(z))
            {
                return double.NaN;
            }

            double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(z));
            double poly = t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
            double tail = Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI) * poly;
            return z >= 0 ? 1.0 - tail : tail;
        }

        /// <summary>Inverse standard normal CDF (Acklam's rational approximation, relative error &lt; 1.2e-9).</summary>
        public static double InverseCdf(double p)
        {
            if (p <= 0) return double.NegativeInfinity;
            if (p >= 1) return double.PositiveInfinity;

            double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
            double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
            double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
            double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };

            const double pLow = 0.02425;
            const double pHigh = 1 - pLow;

            if (p < pLow)
            {
                double q = Math.Sqrt(-2 * Math.Log(p));
                return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                       ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }

            if (p <= pHigh)
            {
                double q = p - 0.5;
                double r = q * q;
                return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                       (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
            }

            {
                double q = Math.Sqrt(-2 * Math.Log(1 - p));
                return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                        ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
            }
        }

        /// <summary>Gaussian sample (Box-Muller).</summary>
        public static double Sample(Random rng, double mean = 0, double stdDev = 1)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + stdDev * z;
        }
    }

    /// <summary>Exponentially weighted moving average with EW variance.</summary>
    public sealed class Ewma
    {
        public Ewma(double lambda)
        {
            Lambda = Math.Clamp(lambda, 1e-4, 1.0);
        }

        public double Lambda { get; }
        public double Value { get; private set; }
        public double Variance { get; private set; }
        public long Count { get; private set; }
        public double StdDev => Math.Sqrt(Math.Max(0, Variance));

        public double Update(double x)
        {
            if (Count == 0)
            {
                Value = x;
                Variance = 0;
            }
            else
            {
                double diff = x - Value;
                double incr = Lambda * diff;
                Value += incr;
                Variance = (1 - Lambda) * (Variance + diff * incr);
            }

            Count++;
            return Value;
        }

        public void Reset()
        {
            Value = 0;
            Variance = 0;
            Count = 0;
        }
    }

    /// <summary>Fixed-capacity ring buffer for time series.</summary>
    public sealed class RingBuffer
    {
        private readonly double[] _items;
        private int _start;

        public RingBuffer(int capacity)
        {
            _items = new double[Math.Max(1, capacity)];
        }

        public int Capacity => _items.Length;
        public int Count { get; private set; }

        public double this[int index] => _items[(_start + index) % _items.Length];

        public double Last => Count == 0 ? double.NaN : this[Count - 1];

        public void Add(double value)
        {
            if (Count < _items.Length)
            {
                _items[(_start + Count) % _items.Length] = value;
                Count++;
            }
            else
            {
                _items[_start] = value;
                _start = (_start + 1) % _items.Length;
            }
        }

        public double[] ToArray()
        {
            var result = new double[Count];
            for (int i = 0; i < Count; i++)
            {
                result[i] = this[i];
            }

            return result;
        }

        public void Clear()
        {
            Count = 0;
            _start = 0;
        }
    }

    public readonly record struct LinearFit(double Slope, double Intercept, double RSquared, int Count)
    {
        public double Evaluate(double x) => Intercept + Slope * x;
    }

    public static class Regression
    {
        /// <summary>Ordinary least squares y = a + b x.</summary>
        public static LinearFit Fit(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            int n = Math.Min(x.Count, y.Count);
            if (n < 2)
            {
                return new LinearFit(0, n == 1 ? y[0] : 0, 0, n);
            }

            double mx = 0, my = 0;
            for (int i = 0; i < n; i++)
            {
                mx += x[i];
                my += y[i];
            }

            mx /= n;
            my /= n;

            double sxx = 0, sxy = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - mx;
                double dy = y[i] - my;
                sxx += dx * dx;
                sxy += dx * dy;
                syy += dy * dy;
            }

            if (sxx <= 1e-12)
            {
                return new LinearFit(0, my, 0, n);
            }

            double slope = sxy / sxx;
            double intercept = my - slope * mx;
            double r2 = syy <= 1e-12 ? 1.0 : (sxy * sxy) / (sxx * syy);
            return new LinearFit(slope, intercept, r2, n);
        }
    }

    /// <summary>Holt's linear (double) exponential smoothing for level + trend forecasting.</summary>
    public sealed class HoltForecaster
    {
        private readonly double _alpha;
        private readonly double _beta;
        private bool _initialized;

        public HoltForecaster(double alpha = 0.35, double beta = 0.1)
        {
            _alpha = Math.Clamp(alpha, 0.01, 1.0);
            _beta = Math.Clamp(beta, 0.0, 1.0);
        }

        public double Level { get; private set; }
        public double Trend { get; private set; }
        public int Count { get; private set; }

        public void Update(double y)
        {
            if (!_initialized)
            {
                Level = y;
                Trend = 0;
                _initialized = true;
                Count = 1;
                return;
            }

            double previousLevel = Level;
            Level = _alpha * y + (1 - _alpha) * (Level + Trend);
            Trend = _beta * (Level - previousLevel) + (1 - _beta) * Trend;
            Count++;
        }

        public double Forecast(double stepsAhead) => Level + Trend * stepsAhead;
    }
}
