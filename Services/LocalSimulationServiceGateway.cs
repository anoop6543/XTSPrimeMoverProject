using System;
using System.Collections.Generic;
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

        public LocalSimulationServiceGateway(XTSSimulationEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _engine.StateChanged += OnEngineStateChanged;
            _engine.LogGenerated += OnEngineLogGenerated;
        }

        // --- IMachineGatewayService ---

        public event EventHandler? StateChanged;
        public event EventHandler<string>? LogGenerated;
        public event EventHandler<GatewayConnectionState>? ConnectionStateChanged { add { } remove { } }

        public GatewayConnectionState ConnectionState => GatewayConnectionState.Connected;

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

        public IReadOnlyList<WatchdogStatusEntry> GetWatchdogStatus()
        {
            return _errorHandler.ExecuteWithRetry(
                () => _engine.GetWatchdogStatus(),
                "LocalGateway.GetWatchdogStatus",
                ErrorCategory.Gateway,
                fallback: Array.Empty<WatchdogStatusEntry>())!;
        }

        public IReadOnlyList<ProductionSequenceStep> GetOrchestrationSteps()
        {
            return _errorHandler.ExecuteWithRetry(
                () => _engine.GetOrchestrationSteps(),
                "LocalGateway.GetOrchestrationSteps",
                ErrorCategory.Gateway,
                fallback: Array.Empty<ProductionSequenceStep>())!;
        }

        public Task<(bool Success, string Message)> TryApplyOrchestrationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)
        {
            try
            {
                bool success = _engine.TryApplyOrchestration(stepDefinitions, out string msg);
                return Task.FromResult((success, msg));
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "LocalGateway.TryApplyOrchestration", ex);
                return Task.FromResult((false, $"Gateway error: {ex.Message}"));
            }
        }

        public Task<IReadOnlyList<string>> PreviewOrchestrationValidationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.PreviewOrchestrationValidation(stepDefinitions),
                "LocalGateway.PreviewOrchestrationValidation",
                ErrorCategory.Gateway,
                fallback: new List<string> { "Validation unavailable due to gateway error." })!);
        }

        public Task<IReadOnlyList<SafetyGateStatus>> GetOrchestrationSafetyGateStatusesAsync()
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetOrchestrationSafetyGateStatuses(),
                "LocalGateway.GetOrchestrationSafetyGateStatuses",
                ErrorCategory.Gateway,
                fallback: Array.Empty<SafetyGateStatus>())!);
        }

        // --- IDataGatewayService ---

        public string DatabasePath => _engine.DatabasePath;

        public Task<IReadOnlyList<PartHistoryEventRecord>> GetPartHistoryAsync(string trackingNumber)
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetPartHistory(trackingNumber),
                "LocalGateway.GetPartHistory",
                ErrorCategory.Gateway,
                fallback: Array.Empty<PartHistoryEventRecord>())!);
        }

        public Task<PartSummaryRecord?> GetPartSummaryAsync(string trackingNumber)
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetPartSummary(trackingNumber),
                "LocalGateway.GetPartSummary",
                ErrorCategory.Gateway,
                fallback: null));
        }

        public Task<IReadOnlyList<string>> GetExportableTablesAsync()
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetExportableTables(),
                "LocalGateway.GetExportableTables",
                ErrorCategory.Gateway,
                fallback: Array.Empty<string>())!);
        }

        public Task<IReadOnlyList<string>> GetAllTablesAsync()
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetAllTables(),
                "LocalGateway.GetAllTables",
                ErrorCategory.Gateway,
                fallback: Array.Empty<string>())!);
        }

        public Task<IReadOnlyList<string>> GetTableColumnsAsync(string tableName)
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetTableColumns(tableName),
                "LocalGateway.GetTableColumns",
                ErrorCategory.Gateway,
                fallback: Array.Empty<string>())!);
        }

        public Task<int> GetTableRowCountAsync(string tableName)
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetTableRowCount(tableName),
                "LocalGateway.GetTableRowCount",
                ErrorCategory.Gateway,
                fallback: 0));
        }

        public Task<IReadOnlyList<Dictionary<string, string>>> GetTableRowsAsync(string tableName, int maxRows = 500)
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.GetTableRows(tableName, maxRows),
                "LocalGateway.GetTableRows",
                ErrorCategory.Gateway,
                fallback: Array.Empty<Dictionary<string, string>>())!);
        }

        public Task<string> ExportTableToCsvAsync(string tableName, string? exportDirectory = null)
        {
            return Task.FromResult(_errorHandler.ExecuteWithRetry(
                () => _engine.ExportTableToCsv(tableName, exportDirectory),
                "LocalGateway.ExportTableToCsv",
                ErrorCategory.Gateway,
                fallback: string.Empty)!);
        }

        public Task<string> GetDefaultExportDirectoryAsync() => Task.FromResult(_engine.GetDefaultExportDirectory());

        // --- Event forwarding ---

        private void OnEngineStateChanged(object? sender, EventArgs e)
        {
            try
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
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
                _errorHandler.ReportException(ErrorCategory.Gateway, "LocalGateway.OnEngineLogGenerated", ex, wasRecovered: true);
            }
        }
    }
}
