using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using XTSPrimeMoverProject.Models;
using XTSPrimeMoverProject.Services;

namespace XTSPrimeMoverProject.Services
{
    /// <summary>
    /// REST + SignalR gateway that connects the WPF desktop client to the Prime Mover API
    /// running in Kubernetes. Replaces LocalSimulationServiceGateway for remote mode.
    /// Implements the same IMachineGatewayService + IDataGatewayService interfaces so all
    /// existing ViewModels and XAML bindings work without modification.
    /// </summary>
    public sealed class RemoteRestGateway : IMachineGatewayService, IDataGatewayService, IDisposable
    {
        private readonly HttpClient _http;
        private readonly HubConnection _hub;
        private readonly ErrorHandlingService _errorHandler = ErrorHandlingService.Instance;

        // Cached state updated from SignalR push
        private List<Mover> _movers = new();
        private List<Machine> _machines = new();
        private List<Robot> _robots = new();
        private bool _isRunning;
        private bool _entryBlink;
        private bool _exitBlink;
        private int _totalParts;
        private int _goodParts;
        private int _badParts;
        private int _entered;

        public event EventHandler? StateChanged;
        public event EventHandler<string>? LogGenerated;

        // --- IMachineGatewayService ---
        public IReadOnlyList<Mover> Movers => _movers;
        public IReadOnlyList<Machine> Machines => _machines;
        public IReadOnlyList<Robot> Robots => _robots;
        public bool IsRunning => _isRunning;
        public bool EntryZoneBlink => _entryBlink;
        public bool ExitZoneBlink => _exitBlink;
        public int TotalPartsProduced => _totalParts;
        public int GoodPartsCount => _goodParts;
        public int BadPartsCount => _badParts;
        public int PrimeMoverEnteredCount => _entered;
        public int PrimeMoverExitedCount => _goodParts + _badParts;

        public RemoteRestGateway(string baseUrl = "http://localhost:8082")
        {
            _http = new HttpClient { BaseAddress = new Uri(baseUrl) };

            _hub = new HubConnectionBuilder()
                .WithUrl($"{baseUrl.TrimEnd('/')}/hubs/system")
                .WithAutomaticReconnect()
                .Build();

            _hub.On<SystemStatusPayload>("SystemStatusUpdate", OnStatusUpdate);
            _hub.On<string>("LogMessage", msg => LogGenerated?.Invoke(this, msg));

            _ = ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            try
            {
                await _hub.StartAsync();
                LogGenerated?.Invoke(this, "[RemoteGateway] SignalR connected to Prime Mover API.");
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteGateway.Connect", ex);
            }
        }

        private void OnStatusUpdate(SystemStatusPayload payload)
        {
            _isRunning = payload.IsRunning;
            _totalParts = payload.TotalPartsProduced;
            _goodParts = payload.GoodPartsCount;
            _badParts = payload.BadPartsCount;
            _entered = payload.PrimeMoverEnteredCount;

            // Map remote DTOs to local WPF models (ViewModels bind to these directly)
            _movers = MapMovers(payload.Movers ?? new List<RemoteMoverDto>());
            _machines = MapMachines(payload.Machines ?? new List<RemoteMachineDto>());

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Start() => FireAndForget(() => _http.PostAsync("/api/system/start", null));
        public void Stop() => FireAndForget(() => _http.PostAsync("/api/system/stop", null));
        public void Reset() => FireAndForget(() => _http.PostAsync("/api/system/reset", null));

        public void SetSimulationSpeed(double speed) =>
            FireAndForget(() => _http.PostAsync($"/api/system/set-speed?factor={speed}", null));

        public IReadOnlyList<WatchdogStatusEntry> GetWatchdogStatus() => Array.Empty<WatchdogStatusEntry>();

        public IReadOnlyList<ProductionSequenceStep> GetOrchestrationSteps() => Array.Empty<ProductionSequenceStep>();

        public bool TryApplyOrchestration(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, out string message)
        {
            message = "Orchestration changes must be made via the Prime Mover API in remote mode.";
            return false;
        }

        public IReadOnlyList<string> PreviewOrchestrationValidation(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions) =>
            new[] { "Validation not supported in remote gateway mode." };

        public IReadOnlyList<SafetyGateStatus> GetOrchestrationSafetyGateStatuses() => Array.Empty<SafetyGateStatus>();

        // --- IDataGatewayService ---
        public string DatabasePath => "(Remote PostgreSQL via Prime Mover API)";

        public IReadOnlyList<PartHistoryEventRecord> GetPartHistory(string trackingNumber)
        {
            try
            {
                var resp = _http.GetAsync($"/api/parts/{trackingNumber}").GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return Array.Empty<PartHistoryEventRecord>();
                var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                // Deserialize remote response and map to WPF record type
                return Array.Empty<PartHistoryEventRecord>(); // simplified for now
            }
            catch { return Array.Empty<PartHistoryEventRecord>(); }
        }

        public PartSummaryRecord? GetPartSummary(string trackingNumber) => null;
        public IReadOnlyList<string> GetExportableTables() => new[] { "(Use web HMI for exports in remote mode)" };
        public IReadOnlyList<string> GetAllTables() => Array.Empty<string>();
        public IReadOnlyList<string> GetTableColumns(string tableName) => Array.Empty<string>();
        public int GetTableRowCount(string tableName) => 0;
        public IReadOnlyList<Dictionary<string, string>> GetTableRows(string tableName, int maxRows = 500) => Array.Empty<Dictionary<string, string>>();
        public string ExportTableToCsv(string tableName, string? exportDirectory = null) => string.Empty;
        public string GetDefaultExportDirectory() => string.Empty;

        private static List<Mover> MapMovers(List<RemoteMoverDto> dtos) =>
            dtos.Select(d =>
            {
                var m = new Mover(d.MoverId);
                m.Position = d.Position;
                m.Velocity = d.Velocity;
                m.State = Enum.TryParse<MoverState>(d.State, out var s) ? s : MoverState.Idle;
                return m;
            }).ToList();

        private static List<Machine> MapMachines(List<RemoteMachineDto> dtos) =>
            dtos.Select(d =>
            {
                var type = Enum.TryParse<MachineType>(d.Type, out var t) ? t : MachineType.LaserWelding;
                var m = new Machine(d.MachineId, d.Name, type, 0);
                m.IsOperational = d.IsOperational;
                m.FaultActive = d.FaultActive;
                m.FaultMessage = d.FaultMessage ?? string.Empty;
                m.PartsEnteredCount = d.PartsEnteredCount;
                m.PartsExitedCount = d.PartsExitedCount;
                m.CurrentStationIndex = d.CurrentStationIndex;
                m.IsIndexing = d.IsIndexing;
                m.RotaryAngle = d.RotaryAngle;
                if (Enum.TryParse<PlcSequencerState>(d.SequencerState, out var seq))
                    m.SequencerState = seq;
                return m;
            }).ToList();

        private void FireAndForget(Func<Task<HttpResponseMessage>> action)
        {
            _ = Task.Run(async () =>
            {
                try { await action(); }
                catch (Exception ex) { _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteGateway.Command", ex, wasRecovered: true); }
            });
        }

        public void Dispose()
        {
            _hub.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            _http.Dispose();
        }

        // Payload DTOs for deserialization
        private class SystemStatusPayload
        {
            public bool IsRunning { get; set; }
            public int TotalPartsProduced { get; set; }
            public int GoodPartsCount { get; set; }
            public int BadPartsCount { get; set; }
            public int PrimeMoverEnteredCount { get; set; }
            public List<RemoteMoverDto>? Movers { get; set; }
            public List<RemoteMachineDto>? Machines { get; set; }
        }

        private class RemoteMoverDto
        {
            public int MoverId { get; set; }
            public double Position { get; set; }
            public double Velocity { get; set; }
            public string State { get; set; } = "Idle";
        }

        private class RemoteMachineDto
        {
            public int MachineId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = "LaserWelding";
            public string SequencerState { get; set; } = "Init";
            public bool IsOperational { get; set; }
            public bool FaultActive { get; set; }
            public string? FaultMessage { get; set; }
            public int PartsEnteredCount { get; set; }
            public int PartsExitedCount { get; set; }
            public int CurrentStationIndex { get; set; }
            public bool IsIndexing { get; set; }
            public double RotaryAngle { get; set; }
        }
    }
}
