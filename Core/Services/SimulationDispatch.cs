using System;

namespace XTSPrimeMoverProject.Services
{
    /// <summary>
    /// Marshals engine notifications to the consumer's thread. The WPF host supplies a Dispatcher-backed
    /// implementation; headless hosts (tests, video recorder, services) use <see cref="InlineSimulationDispatcher"/>.
    /// </summary>
    public interface ISimulationDispatcher
    {
        bool IsShuttingDown { get; }
        bool CheckAccess();
        void Invoke(Action action);
        void BeginInvoke(Action action);
    }

    /// <summary>Runs callbacks synchronously on the calling thread.</summary>
    public sealed class InlineSimulationDispatcher : ISimulationDispatcher
    {
        public static readonly InlineSimulationDispatcher Instance = new();

        public bool IsShuttingDown => false;
        public bool CheckAccess() => true;
        public void Invoke(Action action) => action();
        public void BeginInvoke(Action action) => action();
    }

    public sealed class SimulationOptions
    {
        /// <summary>Seed for reproducible runs (video recording, tests). Null = time-based.</summary>
        public int? Seed { get; init; }

        /// <summary>SQLite file path. Null = XTSFactorySim.db next to the executable.</summary>
        public string? DatabasePath { get; init; }

        /// <summary>Start with the AI autopilot enabled.</summary>
        public bool AutopilotEnabled { get; init; }
    }
}
