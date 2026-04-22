using System;
using System.Collections.Generic;
using System.Threading;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services.RemoteTwinCatMock
{
    /// <summary>
    /// Mock remote TwinCAT machine gateway.
    /// Wraps any IMachineGatewayService and injects a small command latency
    /// to emulate a remote control boundary.
    /// </summary>
    public sealed class RemoteTwinCatMachineGatewayMock : IMachineGatewayService
    {
        private readonly IMachineGatewayService _inner;
        private readonly int _commandLatencyMs;

        public RemoteTwinCatMachineGatewayMock(IMachineGatewayService inner, int commandLatencyMs = 40)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _commandLatencyMs = Math.Max(0, commandLatencyMs);

            _inner.StateChanged += OnInnerStateChanged;
            _inner.LogGenerated += OnInnerLogGenerated;
        }

        public event EventHandler? StateChanged;
        public event EventHandler<string>? LogGenerated;

        public IReadOnlyList<Mover> Movers => _inner.Movers;
        public IReadOnlyList<Machine> Machines => _inner.Machines;
        public IReadOnlyList<Robot> Robots => _inner.Robots;

        public int TotalPartsProduced => _inner.TotalPartsProduced;
        public int GoodPartsCount => _inner.GoodPartsCount;
        public int BadPartsCount => _inner.BadPartsCount;
        public int PrimeMoverEnteredCount => _inner.PrimeMoverEnteredCount;
        public int PrimeMoverExitedCount => _inner.PrimeMoverExitedCount;
        public bool IsRunning => _inner.IsRunning;
        public bool EntryZoneBlink => _inner.EntryZoneBlink;
        public bool ExitZoneBlink => _inner.ExitZoneBlink;

        public void Start()
        {
            SimulateNetworkCommandLatency();
            _inner.Start();
        }

        public void Stop()
        {
            SimulateNetworkCommandLatency();
            _inner.Stop();
        }

        public void Reset()
        {
            SimulateNetworkCommandLatency();
            _inner.Reset();
        }

        public void SetSimulationSpeed(double speed)
        {
            SimulateNetworkCommandLatency();
            _inner.SetSimulationSpeed(speed);
        }

        public IReadOnlyList<WatchdogStatusEntry> GetWatchdogStatus() => _inner.GetWatchdogStatus();

        public IReadOnlyList<ProductionSequenceStep> GetOrchestrationSteps() => _inner.GetOrchestrationSteps();

        public bool TryApplyOrchestration(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, out string message)
        {
            SimulateNetworkCommandLatency();
            return _inner.TryApplyOrchestration(stepDefinitions, out message);
        }

        public IReadOnlyList<string> PreviewOrchestrationValidation(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)
            => _inner.PreviewOrchestrationValidation(stepDefinitions);

        public IReadOnlyList<SafetyGateStatus> GetOrchestrationSafetyGateStatuses()
            => _inner.GetOrchestrationSafetyGateStatuses();

        private void SimulateNetworkCommandLatency()
        {
            if (_commandLatencyMs > 0)
            {
                Thread.Sleep(_commandLatencyMs);
            }
        }

        private void OnInnerStateChanged(object? sender, EventArgs e) => StateChanged?.Invoke(this, EventArgs.Empty);

        private void OnInnerLogGenerated(object? sender, string message)
            => LogGenerated?.Invoke(this, $"[RemoteTwinCATMock] {message}");
    }
}
