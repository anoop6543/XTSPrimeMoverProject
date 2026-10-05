using System;
using System.Collections.Generic;

namespace XTSPrimeMoverProject.Services.Intelligence
{
    /// <summary>
    /// Closed-loop line autopilot. Every action is logged with its rationale so an operator can
    /// audit what the AI did and why (explainable automation):
    /// <list type="bullet">
    /// <item>Release control (CONWIP sized by Little's law) to cut queueing without starving the constraint.</item>
    /// <item>Predictive / opportunistic maintenance scheduling from RUL, health index, SPC and anomalies.</item>
    /// </list>
    /// </summary>
    public sealed class AutopilotController
    {
        public const int DefaultMaxWip = 9;
        public const int MinWip = 3;
        private const int DecisionCapacity = 80;
        private const double MinSecondsBetweenWipChanges = 20.0;

        private readonly List<AutopilotDecision> _decisions = new();
        private double _lastWipChange = double.NegativeInfinity;
        private bool _enabled;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (!value)
                {
                    WipCap = DefaultMaxWip;
                }
            }
        }

        public int WipCap { get; private set; } = DefaultMaxWip;
        public IReadOnlyList<AutopilotDecision> Decisions => _decisions;

        public void Record(double simTime, string category, string action, string rationale)
        {
            _decisions.Add(new AutopilotDecision
            {
                SimTime = simTime,
                Category = category,
                Action = action,
                Rationale = rationale
            });

            if (_decisions.Count > DecisionCapacity)
            {
                _decisions.RemoveAt(0);
            }
        }

        /// <summary>
        /// CONWIP release control sized by the critical WIP of Hopp &amp; Spearman (Factory Physics):
        /// W₀ = r_b × T₀ (bottleneck rate × raw process time). Below W₀ the constraint starves; far above it
        /// a single no-overtaking XTS loop congests. The cap is W₀ plus a 35% buffer.
        /// Returns true when the cap changed.
        /// </summary>
        public bool UpdateReleaseControl(double simTime, double bottleneckRatePerSecond, double rawProcessTimeSeconds, string bottleneckName)
        {
            if (!Enabled)
            {
                WipCap = DefaultMaxWip;
                return false;
            }

            if (bottleneckRatePerSecond <= 0 || rawProcessTimeSeconds <= 0 || simTime - _lastWipChange < MinSecondsBetweenWipChanges)
            {
                return false;
            }

            double criticalWip = bottleneckRatePerSecond * rawProcessTimeSeconds;
            int target = Math.Clamp((int)Math.Ceiling(criticalWip * 1.35), MinWip, DefaultMaxWip);
            if (target == WipCap)
            {
                return false;
            }

            Record(
                simTime,
                "Release control",
                $"WIP cap {WipCap} → {target}",
                $"Critical WIP W₀ = r_b × T₀ = {bottleneckRatePerSecond * 60:F2}/min ({bottleneckName}) × {rawProcessTimeSeconds:F0} s = {criticalWip:F1}; " +
                "+35% buffer keeps the constraint fed without congesting the no-overtaking loop.");
            WipCap = target;
            _lastWipChange = simTime;
            return true;
        }

        public void Reset()
        {
            _decisions.Clear();
            WipCap = DefaultMaxWip;
            _lastWipChange = double.NegativeInfinity;
        }
    }
}
