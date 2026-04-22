using System;
using System.Collections.Generic;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    public interface IMachineGatewayService
    {
        event EventHandler? StateChanged;
        event EventHandler<string>? LogGenerated;

        IReadOnlyList<Mover> Movers { get; }
        IReadOnlyList<Machine> Machines { get; }
        IReadOnlyList<Robot> Robots { get; }

        int TotalPartsProduced { get; }
        int GoodPartsCount { get; }
        int BadPartsCount { get; }
        int PrimeMoverEnteredCount { get; }
        int PrimeMoverExitedCount { get; }
        bool IsRunning { get; }
        bool EntryZoneBlink { get; }
        bool ExitZoneBlink { get; }

        void Start();
        void Stop();
        void Reset();
        void SetSimulationSpeed(double speed);

        IReadOnlyList<WatchdogStatusEntry> GetWatchdogStatus();
        IReadOnlyList<ProductionSequenceStep> GetOrchestrationSteps();
        bool TryApplyOrchestration(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, out string message);
        IReadOnlyList<string> PreviewOrchestrationValidation(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions);
        IReadOnlyList<SafetyGateStatus> GetOrchestrationSafetyGateStatuses();
    }

    public interface IDataGatewayService
    {
        string DatabasePath { get; }

        IReadOnlyList<PartHistoryEventRecord> GetPartHistory(string trackingNumber);
        PartSummaryRecord? GetPartSummary(string trackingNumber);

        IReadOnlyList<string> GetExportableTables();
        IReadOnlyList<string> GetAllTables();
        IReadOnlyList<string> GetTableColumns(string tableName);
        int GetTableRowCount(string tableName);
        IReadOnlyList<Dictionary<string, string>> GetTableRows(string tableName, int maxRows = 500);
        string ExportTableToCsv(string tableName, string? exportDirectory = null);
        string GetDefaultExportDirectory();
    }
}
