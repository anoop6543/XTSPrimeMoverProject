using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    public sealed record OrchestrationApplyResult(bool Success, string Message);

    public enum GatewayConnectionState
    {
        Connected,
        Degraded,
        Reconnecting,
        Offline
    }

    public sealed record GatewaySessionStatus(
        GatewayConnectionState State,
        string Summary,
        string Detail,
        DateTime LastUpdatedUtc,
        bool IsRemote);

    public interface IMachineGatewayService
    {
        event EventHandler? StateChanged;
        event EventHandler<string>? LogGenerated;
        event EventHandler<GatewaySessionStatus>? SessionStatusChanged;

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
        GatewaySessionStatus SessionStatus { get; }

        void Start();
        void Stop();
        void Reset();
        void SetSimulationSpeed(double speed);

        Task<IReadOnlyList<WatchdogStatusEntry>> GetWatchdogStatusAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ProductionSequenceStep>> GetOrchestrationStepsAsync(CancellationToken cancellationToken = default);
        Task<OrchestrationApplyResult> ApplyOrchestrationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> PreviewOrchestrationValidationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SafetyGateStatus>> GetOrchestrationSafetyGateStatusesAsync(CancellationToken cancellationToken = default);
    }

    public interface IDataGatewayService
    {
        string DatabasePath { get; }

        Task<IReadOnlyList<PartHistoryEventRecord>> GetPartHistoryAsync(string trackingNumber, CancellationToken cancellationToken = default);
        Task<PartSummaryRecord?> GetPartSummaryAsync(string trackingNumber, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<string>> GetExportableTablesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> GetAllTablesAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<string>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken = default);
        Task<int> GetTableRowCountAsync(string tableName, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Dictionary<string, string>>> GetTableRowsAsync(string tableName, int maxRows = 500, CancellationToken cancellationToken = default);
        Task<string> ExportTableToCsvAsync(string tableName, string? exportDirectory = null, CancellationToken cancellationToken = default);
        string GetDefaultExportDirectory();
    }
}
