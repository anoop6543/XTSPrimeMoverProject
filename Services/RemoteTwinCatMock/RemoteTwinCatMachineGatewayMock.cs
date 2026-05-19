using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services.RemoteTwinCatMock
{
    /// <summary>
    /// Mock remote TwinCAT machine gateway.
    /// Wraps any IMachineGatewayService and injects a small command latency
    /// to emulate a remote control boundary.
    /// Commands are dispatched to the thread pool so the UI thread is never blocked
    /// by simulated network latency. Remote-boundary calls are wrapped with error
    /// handling to provide graceful degradation on failure.
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
        public event EventHandler<GatewayConnectionState>? ConnectionStateChanged
        {
            add => _inner.ConnectionStateChanged += value;
            remove => _inner.ConnectionStateChanged -= value;
        }

        public GatewayConnectionState ConnectionState => _inner.ConnectionState;

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
            DispatchWithLatency(() => _inner.Start());
        }

        public void Stop()
        {
            DispatchWithLatency(() => _inner.Stop());
        }

        public void Reset()
        {
            DispatchWithLatency(() => _inner.Reset());
        }

        public void SetSimulationSpeed(double speed)
        {
            DispatchWithLatency(() => _inner.SetSimulationSpeed(speed));
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

        public async Task<(bool Success, string Message)> TryApplyOrchestrationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)
        {
            try
            {
                if (_commandLatencyMs > 0)
                {
                    await Task.Delay(_commandLatencyMs).ConfigureAwait(false);
                }
                return await _inner.TryApplyOrchestrationAsync(stepDefinitions).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.TryApplyOrchestration", ex);
                return (false, $"Remote gateway error: {ex.Message}");
            }
        }

        public async Task<IReadOnlyList<string>> PreviewOrchestrationValidationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)
        {
            if (_commandLatencyMs > 0)
            {
                await Task.Delay(_commandLatencyMs).ConfigureAwait(false);
            }
            return await _errorHandler.ExecuteWithRetryAsync(
                async () => await _inner.PreviewOrchestrationValidationAsync(stepDefinitions),
                "RemoteMock.PreviewOrchestrationValidation",
                ErrorCategory.Gateway,
                fallback: new List<string> { "Validation unavailable due to remote gateway error." }).ConfigureAwait(false) ?? new List<string>();
        }

        public async Task<IReadOnlyList<SafetyGateStatus>> GetOrchestrationSafetyGateStatusesAsync()
        {
            if (_commandLatencyMs > 0)
            {
                await Task.Delay(_commandLatencyMs).ConfigureAwait(false);
            }
            return await _errorHandler.ExecuteWithRetryAsync(
                async () => await _inner.GetOrchestrationSafetyGateStatusesAsync(),
                "RemoteMock.GetOrchestrationSafetyGateStatuses",
                ErrorCategory.Gateway,
                fallback: (IReadOnlyList<SafetyGateStatus>)Array.Empty<SafetyGateStatus>()).ConfigureAwait(false) ?? Array.Empty<SafetyGateStatus>();
        }

        private void DispatchWithLatency(Action command)
        {
            if (_commandLatencyMs <= 0)
            {
                command();
                return;
            }

            Task.Run(async () =>
            {
                await Task.Delay(_commandLatencyMs).ConfigureAwait(false);
                command();
            });
        }

        private void SimulateNetworkLatencySync()
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
