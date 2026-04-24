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
    /// All remote-boundary calls are wrapped with error handling to simulate
    /// real network failure modes and provide graceful degradation.
    /// </summary>
    public sealed class RemoteTwinCatMachineGatewayMock : IMachineGatewayService
    {
        private readonly IMachineGatewayService _inner;
        private readonly ErrorHandlingService _errorHandler = ErrorHandlingService.Instance;
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
            try
            {
                SimulateNetworkCommandLatency();
                _inner.Start();
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.Start", ex);
            }
        }

        public void Stop()
        {
            try
            {
                SimulateNetworkCommandLatency();
                _inner.Stop();
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.Stop", ex);
            }
        }

        public void Reset()
        {
            try
            {
                SimulateNetworkCommandLatency();
                _inner.Reset();
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.Reset", ex);
            }
        }

        public void SetSimulationSpeed(double speed)
        {
            try
            {
                SimulateNetworkCommandLatency();
                _inner.SetSimulationSpeed(speed);
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.SetSimulationSpeed", ex);
            }
        }

        public IReadOnlyList<WatchdogStatusEntry> GetWatchdogStatus()
        {
            return _errorHandler.ExecuteWithRetry(
                () => _inner.GetWatchdogStatus(),
                "RemoteMock.GetWatchdogStatus",
                ErrorCategory.Gateway,
                fallback: Array.Empty<WatchdogStatusEntry>())!;
        }

        public IReadOnlyList<ProductionSequenceStep> GetOrchestrationSteps()
        {
            return _errorHandler.ExecuteWithRetry(
                () => _inner.GetOrchestrationSteps(),
                "RemoteMock.GetOrchestrationSteps",
                ErrorCategory.Gateway,
                fallback: Array.Empty<ProductionSequenceStep>())!;
        }

        public bool TryApplyOrchestration(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, out string message)
        {
            try
            {
                SimulateNetworkCommandLatency();
                return _inner.TryApplyOrchestration(stepDefinitions, out message);
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.TryApplyOrchestration", ex);
                message = $"Remote gateway error: {ex.Message}";
                return false;
            }
        }

        public IReadOnlyList<string> PreviewOrchestrationValidation(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)
        {
            return _errorHandler.ExecuteWithRetry(
                () => _inner.PreviewOrchestrationValidation(stepDefinitions),
                "RemoteMock.PreviewOrchestrationValidation",
                ErrorCategory.Gateway,
                fallback: new List<string> { "Validation unavailable due to remote gateway error." })!;
        }

        public IReadOnlyList<SafetyGateStatus> GetOrchestrationSafetyGateStatuses()
        {
            return _errorHandler.ExecuteWithRetry(
                () => _inner.GetOrchestrationSafetyGateStatuses(),
                "RemoteMock.GetOrchestrationSafetyGateStatuses",
                ErrorCategory.Gateway,
                fallback: Array.Empty<SafetyGateStatus>())!;
        }

        private void SimulateNetworkCommandLatency()
        {
            if (_commandLatencyMs > 0)
            {
                Thread.Sleep(_commandLatencyMs);
            }
        }

        private void OnInnerStateChanged(object? sender, EventArgs e)
        {
            try
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.OnInnerStateChanged", ex, wasRecovered: true);
            }
        }

        private void OnInnerLogGenerated(object? sender, string message)
        {
            try
            {
                LogGenerated?.Invoke(this, $"[RemoteTwinCATMock] {message}");
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.OnInnerLogGenerated", ex, wasRecovered: true);
            }
        }
    }
}
