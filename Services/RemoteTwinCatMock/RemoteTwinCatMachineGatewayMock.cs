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

		public async Task<IReadOnlyList<WatchdogStatusEntry>> GetWatchdogStatusAsync(CancellationToken cancellationToken = default)
		{
			await SimulateNetworkLatencyAsync(cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				return await _inner.GetWatchdogStatusAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.GetWatchdogStatusAsync", ex);
				return Array.Empty<WatchdogStatusEntry>();
			}
		}

		public async Task<IReadOnlyList<ProductionSequenceStep>> GetOrchestrationStepsAsync(CancellationToken cancellationToken = default)
		{
			await SimulateNetworkLatencyAsync(cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				return await _inner.GetOrchestrationStepsAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.GetOrchestrationStepsAsync", ex);
				return Array.Empty<ProductionSequenceStep>();
			}
		}

		public async Task<OrchestrationApplyResult> ApplyOrchestrationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, CancellationToken cancellationToken = default)
		{
			try
			{
				await SimulateNetworkLatencyAsync(cancellationToken).ConfigureAwait(false);
				cancellationToken.ThrowIfCancellationRequested();
				return await _inner.ApplyOrchestrationAsync(stepDefinitions, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				_errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.ApplyOrchestrationAsync", ex);
				return new OrchestrationApplyResult(false, $"Remote gateway error: {ex.Message}");
			}
		}

		public async Task<IReadOnlyList<string>> PreviewOrchestrationValidationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, CancellationToken cancellationToken = default)
		{
			await SimulateNetworkLatencyAsync(cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				return await _inner.PreviewOrchestrationValidationAsync(stepDefinitions, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.PreviewOrchestrationValidationAsync", ex);
				return new List<string> { "Validation unavailable due to remote gateway error." };
			}
		}

		public async Task<IReadOnlyList<SafetyGateStatus>> GetOrchestrationSafetyGateStatusesAsync(CancellationToken cancellationToken = default)
		{
			await SimulateNetworkLatencyAsync(cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				return await _inner.GetOrchestrationSafetyGateStatusesAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_errorHandler.ReportException(ErrorCategory.Gateway, "RemoteMock.GetOrchestrationSafetyGateStatusesAsync", ex);
				return Array.Empty<SafetyGateStatus>();
			}
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

		private Task SimulateNetworkLatencyAsync(CancellationToken cancellationToken)
		{
			return _commandLatencyMs > 0
				? Task.Delay(_commandLatencyMs, cancellationToken)
				: Task.CompletedTask;
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
