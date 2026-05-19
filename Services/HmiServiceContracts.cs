using System;
using System.Collections.Generic;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    public enum GatewayConnectionState
    {
        Connected,
        Degraded,
        Reconnecting,
        Offline
    }

    public interface IMachineGatewayService
    {
        event EventHandler? StateChanged;
        event EventHandler<string>? LogGenerated;
        event EventHandler<GatewayConnectionState>? ConnectionStateChanged;

        GatewayConnectionState ConnectionState { get; }

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
        Task<(bool Success, string Message)> TryApplyOrchestrationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions);
        Task<IReadOnlyList<string>> PreviewOrchestrationValidationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions);
        Task<IReadOnlyList<SafetyGateStatus>> GetOrchestrationSafetyGateStatusesAsync();
    }

    public interface IDataGatewayService
    {
        string DatabasePath { get; }

        Task<IReadOnlyList<PartHistoryEventRecord>> GetPartHistoryAsync(string trackingNumber);
        Task<PartSummaryRecord?> GetPartSummaryAsync(string trackingNumber);

        Task<IReadOnlyList<string>> GetExportableTablesAsync();
        Task<IReadOnlyList<string>> GetAllTablesAsync();
        Task<IReadOnlyList<string>> GetTableColumnsAsync(string tableName);
        Task<int> GetTableRowCountAsync(string tableName);
        Task<IReadOnlyList<Dictionary<string, string>>> GetTableRowsAsync(string tableName, int maxRows = 500);
        Task<string> ExportTableToCsvAsync(string tableName, string? exportDirectory = null);
        Task<string> GetDefaultExportDirectoryAsync();
    }
}
