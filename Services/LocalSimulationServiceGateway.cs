using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    /// <summary>
    /// In-process gateway implementing both machine and data service contracts
    /// by delegating to the existing XTSSimulationEngine.
    /// All calls are wrapped with error handling to prevent unhandled exceptions
    /// from propagating to the ViewModel/UI layer.
    /// </summary>
    public sealed class LocalSimulationServiceGateway : IMachineGatewayService, IDataGatewayService
    {
        private readonly XTSSimulationEngine _engine;
        private readonly ErrorHandlingService _errorHandler = ErrorHandlingService.Instance;
        private GatewaySessionStatus _sessionStatus;

        public LocalSimulationServiceGateway(XTSSimulationEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _sessionStatus = CreateSessionStatus(
                GatewayConnectionState.Connected,
                "Connected",
                "Local in-process machine gateway active.");
            _engine.StateChanged += OnEngineStateChanged;
            _engine.LogGenerated += OnEngineLogGenerated;
        }

        // --- IMachineGatewayService ---

        public event EventHandler? StateChanged;
        public event EventHandler<string>? LogGenerated;
        public event EventHandler<GatewaySessionStatus>? SessionStatusChanged;

        public IReadOnlyList<Mover> Movers => _engine.Movers;
        public IReadOnlyList<Machine> Machines => _engine.Machines;
        public IReadOnlyList<Robot> Robots => _engine.Robots;

        public int TotalPartsProduced => _engine.TotalPartsProduced;
        public int GoodPartsCount => _engine.GoodPartsCount;
        public int BadPartsCount => _engine.BadPartsCount;
        public int PrimeMoverEnteredCount => _engine.PrimeMoverEnteredCount;
        public int PrimeMoverExitedCount => _engine.PrimeMoverExitedCount;
        public bool IsRunning => _engine.IsRunning;
        public bool EntryZoneBlink => _engine.EntryZoneBlink;
        public bool ExitZoneBlink => _engine.ExitZoneBlink;
        public GatewaySessionStatus SessionStatus => _sessionStatus;

        public void Start()
        {
            try
            {
                _engine.Start();
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "LocalGateway.Start", ex);
            }
        }

        public void Stop()
        {
            try
            {
                _engine.Stop();
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "LocalGateway.Stop", ex);
            }
        }

        public void Reset()
        {
            try
            {
                _engine.Reset();
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "LocalGateway.Reset", ex);
            }
        }

        public void SetSimulationSpeed(double speed)
        {
            try
            {
                _engine.SetSimulationSpeed(speed);
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "LocalGateway.SetSimulationSpeed", ex);
            }
        }

        public Task<IReadOnlyList<WatchdogStatusEntry>> GetWatchdogStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetWatchdogStatus(),
                "LocalGateway.GetWatchdogStatus",
                ErrorCategory.Gateway,
                fallback: Array.Empty<WatchdogStatusEntry>())!);
        }

        public Task<IReadOnlyList<ProductionSequenceStep>> GetOrchestrationStepsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetOrchestrationSteps(),
                "LocalGateway.GetOrchestrationSteps",
                ErrorCategory.Gateway,
                fallback: Array.Empty<ProductionSequenceStep>())!);
        }

        public Task<OrchestrationApplyResult> ApplyOrchestrationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                bool success = _engine.TryApplyOrchestration(stepDefinitions, out string message);
                return Task.FromResult(new OrchestrationApplyResult(success, message));
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "LocalGateway.TryApplyOrchestration", ex);
                return Task.FromResult(new OrchestrationApplyResult(false, $"Gateway error: {ex.Message}"));
            }
        }

        public Task<IReadOnlyList<string>> PreviewOrchestrationValidationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.PreviewOrchestrationValidation(stepDefinitions),
                "LocalGateway.PreviewOrchestrationValidation",
                ErrorCategory.Gateway,
                fallback: new List<string> { "Validation unavailable due to gateway error." })!);
        }

        public Task<IReadOnlyList<SafetyGateStatus>> GetOrchestrationSafetyGateStatusesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetOrchestrationSafetyGateStatuses(),
                "LocalGateway.GetOrchestrationSafetyGateStatuses",
                ErrorCategory.Gateway,
                fallback: Array.Empty<SafetyGateStatus>())!);
        }

        // --- IDataGatewayService ---

        public string DatabasePath => _engine.DatabasePath;

        public Task<IReadOnlyList<PartHistoryEventRecord>> GetPartHistoryAsync(string trackingNumber, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetPartHistory(trackingNumber),
                "LocalGateway.GetPartHistory",
                ErrorCategory.Gateway,
                fallback: Array.Empty<PartHistoryEventRecord>())!);
        }

        public Task<PartSummaryRecord?> GetPartSummaryAsync(string trackingNumber, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetPartSummary(trackingNumber),
                "LocalGateway.GetPartSummary",
                ErrorCategory.Gateway,
                fallback: null));
        }

        public Task<IReadOnlyList<string>> GetExportableTablesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetExportableTables(),
                "LocalGateway.GetExportableTables",
                ErrorCategory.Gateway,
                fallback: Array.Empty<string>())!);
        }

        public Task<IReadOnlyList<string>> GetAllTablesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetAllTables(),
                "LocalGateway.GetAllTables",
                ErrorCategory.Gateway,
                fallback: Array.Empty<string>())!);
        }

        public Task<IReadOnlyList<string>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetTableColumns(tableName),
                "LocalGateway.GetTableColumns",
                ErrorCategory.Gateway,
                fallback: Array.Empty<string>())!);
        }

        public Task<int> GetTableRowCountAsync(string tableName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetTableRowCount(tableName),
                "LocalGateway.GetTableRowCount",
                ErrorCategory.Gateway,
                fallback: 0));
        }

        public Task<IReadOnlyList<Dictionary<string, string>>> GetTableRowsAsync(string tableName, int maxRows = 500, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetTableRows(tableName, maxRows),
                "LocalGateway.GetTableRows",
                ErrorCategory.Gateway,
                fallback: Array.Empty<Dictionary<string, string>>())!);
        }

        public Task<string> ExportTableToCsvAsync(string tableName, string? exportDirectory = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.ExportTableToCsv(tableName, exportDirectory),
                "LocalGateway.ExportTableToCsv",
                ErrorCategory.Gateway,
                fallback: string.Empty)!);
        }

        public string GetDefaultExportDirectory() => _engine.GetDefaultExportDirectory();

        // --- Event forwarding ---

        private void OnEngineStateChanged(object? sender, EventArgs e)
        {
            try
            {
                PublishSessionStatus(
                    GatewayConnectionState.Connected,
                    "Connected",
                    _engine.IsRunning
                        ? "Local machine gateway connected and simulation running."
                        : "Local machine gateway connected and simulation stopped.");
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                PublishSessionStatus(GatewayConnectionState.Degraded, "Degraded", $"Local state forwarding recovered after error: {ex.Message}");
                _errorHandler.ReportException(ErrorCategory.Gateway, "LocalGateway.OnEngineStateChanged", ex, wasRecovered: true);
            }
        }

        private void OnEngineLogGenerated(object? sender, string message)
        {
            try
            {
                LogGenerated?.Invoke(this, message);
            }
            catch (Exception ex)
            {
                PublishSessionStatus(GatewayConnectionState.Degraded, "Degraded", $"Local log forwarding recovered after error: {ex.Message}");
                _errorHandler.ReportException(ErrorCategory.Gateway, "LocalGateway.OnEngineLogGenerated", ex, wasRecovered: true);
            }
        }

        private GatewaySessionStatus CreateSessionStatus(GatewayConnectionState state, string summary, string detail)
        {
            return new GatewaySessionStatus(state, summary, detail, DateTime.UtcNow, IsRemote: false);
        }

        private void PublishSessionStatus(GatewayConnectionState state, string summary, string detail)
        {
            var next = CreateSessionStatus(state, summary, detail);
            if (_sessionStatus == next)
            {
                return;
            }

            _sessionStatus = next;
            SessionStatusChanged?.Invoke(this, next);
        }
    }
}
