using System;
using System.Collections.Generic;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    /// <summary>
    /// In-process gateway implementing both machine and data service contracts
    /// by delegating to the existing XTSSimulationEngine.
    /// </summary>
    public sealed class LocalSimulationServiceGateway : IMachineGatewayService, IDataGatewayService
    {
        private readonly XTSSimulationEngine _engine;

        public LocalSimulationServiceGateway(XTSSimulationEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _engine.StateChanged += OnEngineStateChanged;
            _engine.LogGenerated += OnEngineLogGenerated;
        }

        // --- IMachineGatewayService ---

        public event EventHandler? StateChanged;
        public event EventHandler<string>? LogGenerated;

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

        public void Start() => _engine.Start();
        public void Stop() => _engine.Stop();
        public void Reset() => _engine.Reset();
        public void SetSimulationSpeed(double speed) => _engine.SetSimulationSpeed(speed);

        public IReadOnlyList<WatchdogStatusEntry> GetWatchdogStatus() => _engine.GetWatchdogStatus();
        public IReadOnlyList<ProductionSequenceStep> GetOrchestrationSteps() => _engine.GetOrchestrationSteps();
        public bool TryApplyOrchestration(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, out string message)
            => _engine.TryApplyOrchestration(stepDefinitions, out message);
        public IReadOnlyList<string> PreviewOrchestrationValidation(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions)
            => _engine.PreviewOrchestrationValidation(stepDefinitions);
        public IReadOnlyList<SafetyGateStatus> GetOrchestrationSafetyGateStatuses()
            => _engine.GetOrchestrationSafetyGateStatuses();

        // --- IDataGatewayService ---

        public string DatabasePath => _engine.DatabasePath;

        public IReadOnlyList<PartHistoryEventRecord> GetPartHistory(string trackingNumber) => _engine.GetPartHistory(trackingNumber);
        public PartSummaryRecord? GetPartSummary(string trackingNumber) => _engine.GetPartSummary(trackingNumber);

        public IReadOnlyList<string> GetExportableTables() => _engine.GetExportableTables();
        public IReadOnlyList<string> GetAllTables() => _engine.GetAllTables();
        public IReadOnlyList<string> GetTableColumns(string tableName) => _engine.GetTableColumns(tableName);
        public int GetTableRowCount(string tableName) => _engine.GetTableRowCount(tableName);
        public IReadOnlyList<Dictionary<string, string>> GetTableRows(string tableName, int maxRows = 500) => _engine.GetTableRows(tableName, maxRows);
        public string ExportTableToCsv(string tableName, string? exportDirectory = null) => _engine.ExportTableToCsv(tableName, exportDirectory);
        public string GetDefaultExportDirectory() => _engine.GetDefaultExportDirectory();

        // --- Event forwarding ---

        private void OnEngineStateChanged(object? sender, EventArgs e) => StateChanged?.Invoke(this, EventArgs.Empty);
        private void OnEngineLogGenerated(object? sender, string message) => LogGenerated?.Invoke(this, message);
    }
}
