using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services
{
    /// <summary>
    /// REST + SignalR gateway that connects the WPF desktop client to the Prime Mover API
    /// running in Kubernetes / Docker Compose. Replaces LocalSimulationServiceGateway when
    /// the engine runs remotely (XTS_GATEWAY_MODE=remote in environment or app config).
    ///
    /// All ViewModels and XAML bindings work identically regardless of which gateway is active —
    /// only this class needs to change for remote mode.
    /// </summary>
    public sealed class RemoteRestGateway : IMachineGatewayService, IDataGatewayService, IDisposable
    {
        private readonly HttpClient _http;
        private readonly HubConnection _hub;
        private readonly ErrorHandlingService _errorHandler = ErrorHandlingService.Instance;
        private readonly string _baseUrl;

        // Cached state — updated from SignalR push at ~10 Hz
        private readonly object _stateLock = new();
        private List<Mover> _movers = new();
        private List<Machine> _machines = new();
        private List<Robot> _robots = new();
        private bool _isRunning;
        private int _totalParts;
        private int _goodParts;
        private int _badParts;
        private int _entered;

        // ── IMachineGatewayService ───────────────────────────────────────────

        public event EventHandler? StateChanged;
        public event EventHandler<string>? LogGenerated;

        public IReadOnlyList<Mover> Movers { get { lock (_stateLock) return _movers; } }
        public IReadOnlyList<Machine> Machines { get { lock (_stateLock) return _machines; } }
        public IReadOnlyList<Robot> Robots { get { lock (_stateLock) return _robots; } }
        public bool IsRunning { get { lock (_stateLock) return _isRunning; } }
        public bool EntryZoneBlink => false;   // driven by server-side engine
        public bool ExitZoneBlink => false;
        public int TotalPartsProduced { get { lock (_stateLock) return _totalParts; } }
        public int GoodPartsCount { get { lock (_stateLock) return _goodParts; } }
        public int BadPartsCount { get { lock (_stateLock) return _badParts; } }
        public int PrimeMoverEnteredCount { get { lock (_stateLock) return _entered; } }
        public int PrimeMoverExitedCount { get { lock (_stateLock) return _goodParts + _badParts; } }

        public RemoteRestGateway(string baseUrl = "http://localhost:8082")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _http = new HttpClient { BaseAddress = new Uri(_baseUrl + "/") };

            _hub = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}/hubs/system")
                .WithAutomaticReconnect()
                .Build();

            _hub.On<SystemStatusPayload>("SystemStatusUpdate", OnStatusUpdate);
            _hub.On<string>("LogMessage", msg => LogGenerated?.Invoke(this, $"[Remote] {msg}"));

            _hub.Reconnecting += _ =>
            {
                LogGenerated?.Invoke(this, "[RemoteGateway] SignalR reconnecting…");
                return Task.CompletedTask;
            };
            _hub.Reconnected += _ =>
            {
                LogGenerated?.Invoke(this, "[RemoteGateway] SignalR reconnected.");
                return Task.CompletedTask;
            };

            _ = ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            try
            {
                await _hub.StartAsync();
                LogGenerated?.Invoke(this, $"[RemoteGateway] Connected to {_baseUrl}/hubs/system");
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteGateway.Connect", ex, wasRecovered: true);
                LogGenerated?.Invoke(this, $"[RemoteGateway] Could not connect to SignalR hub: {ex.Message}");
            }
        }

        private void OnStatusUpdate(SystemStatusPayload payload)
        {
            lock (_stateLock)
            {
                _isRunning = payload.IsRunning;
                _totalParts = payload.TotalPartsProduced;
                _goodParts = payload.GoodPartsCount;
                _badParts = payload.BadPartsCount;
                _entered = payload.PrimeMoverEnteredCount;
                _movers = MapMovers(payload.Movers ?? new List<RemoteMoverDto>());
                _machines = MapMachines(payload.Machines ?? new List<RemoteMachineDto>());
                _robots = new List<Robot>();
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── Commands ─────────────────────────────────────────────────────────

        public void Start() =>
            FireAndForget(() => _http.PostAsync("api/system/start", null), "Start");

        public void Stop() =>
            FireAndForget(() => _http.PostAsync("api/system/stop", null), "Stop");

        public void Reset() =>
            FireAndForget(() => _http.PostAsync("api/system/reset", null), "Reset");

        public void SetSimulationSpeed(double speed) =>
            FireAndForget(() => _http.PostAsync($"api/system/set-speed?factor={speed:F2}", null), "SetSpeed");

        // ── Watchdog / Orchestration (not available in remote mode) ──────────

        public IReadOnlyList<WatchdogStatusEntry> GetWatchdogStatus() =>
            Array.Empty<WatchdogStatusEntry>();

        public IReadOnlyList<ProductionSequenceStep> GetOrchestrationSteps() =>
            Array.Empty<ProductionSequenceStep>();

        public bool TryApplyOrchestration(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, out string message)
        {
            message = "Orchestration changes require direct API call in remote mode. Use POST /api/recipes.";
            return false;
        }

        public IReadOnlyList<string> PreviewOrchestrationValidation(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions) =>
            new[] { "Validation not supported in remote gateway mode — use the Prime Mover web HMI." };

        public IReadOnlyList<SafetyGateStatus> GetOrchestrationSafetyGateStatuses() =>
            Array.Empty<SafetyGateStatus>();

        // ── IDataGatewayService ───────────────────────────────────────────────

        public string DatabasePath => $"(Remote PostgreSQL via {_baseUrl})";

        public IReadOnlyList<PartHistoryEventRecord> GetPartHistory(string trackingNumber)
        {
            try
            {
                var resp = _http.GetAsync($"api/parts/{Uri.EscapeDataString(trackingNumber)}").GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return Array.Empty<PartHistoryEventRecord>();
                // Response comes from Temporal workflow query; surface as simple list for now
                return Array.Empty<PartHistoryEventRecord>();
            }
            catch (Exception ex)
            {
                _errorHandler.ReportException(ErrorCategory.Gateway, "RemoteGateway.GetPartHistory", ex, wasRecovered: true);
                return Array.Empty<PartHistoryEventRecord>();
            }
        }

        public PartSummaryRecord? GetPartSummary(string trackingNumber) => null;

        public IReadOnlyList<string> GetExportableTables() =>
            new[] { "(Use Prime Mover web HMI for CSV exports in remote mode)" };

        public IReadOnlyList<string> GetAllTables() => Array.Empty<string>();
        public IReadOnlyList<string> GetTableColumns(string tableName) => Array.Empty<string>();
        public int GetTableRowCount(string tableName) => 0;

        public IReadOnlyList<Dictionary<string, string>> GetTableRows(string tableName, int maxRows = 500) =>
            Array.Empty<Dictionary<string, string>>();

        public string ExportTableToCsv(string tableName, string? exportDirectory = null) => string.Empty;
        public string GetDefaultExportDirectory() => string.Empty;

        // ── Mapping helpers ───────────────────────────────────────────────────

        private static List<Mover> MapMovers(List<RemoteMoverDto> dtos) =>
            dtos.ConvertAll(d =>
            {
                var m = new Mover(d.MoverId);
                m.Position = d.Position;
                m.Velocity = d.Velocity;
                m.State = Enum.TryParse<MoverState>(d.State, true, out var s) ? s : MoverState.Idle;
                return m;
            });

        private static List<Machine> MapMachines(List<RemoteMachineDto> dtos) =>
            dtos.ConvertAll(d =>
            {
                var type = Enum.TryParse<MachineType>(d.Type, true, out var t) ? t : MachineType.LaserWelding;
                var m = new Machine(d.MachineId, d.Name ?? $"Machine-{d.MachineId}", type, 0);
                m.IsOperational = d.IsOperational;
                m.FaultActive = d.FaultActive;
                m.FaultMessage = d.FaultMessage ?? string.Empty;
                m.PartsEnteredCount = d.PartsEnteredCount;
                m.PartsExitedCount = d.PartsExitedCount;
                m.CurrentStationIndex = d.CurrentStationIndex;
                m.IsIndexing = d.IsIndexing;
                m.RotaryAngle = d.RotaryAngle;
                if (Enum.TryParse<PlcSequencerState>(d.SequencerState, true, out var seq))
                    m.SequencerState = seq;
                return m;
            });

        private void FireAndForget(Func<Task<HttpResponseMessage>> action, string opName)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var response = await action();
                    if (!response.IsSuccessStatusCode)
                        LogGenerated?.Invoke(this, $"[RemoteGateway] {opName} returned {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    _errorHandler.ReportException(ErrorCategory.Gateway, $"RemoteGateway.{opName}", ex, wasRecovered: true);
                }
            });
        }

        public void Dispose()
        {
            try { _hub.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); } catch { }
            _http.Dispose();
        }

        // ── JSON payload shapes ───────────────────────────────────────────────

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
            public string? Name { get; set; }
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
