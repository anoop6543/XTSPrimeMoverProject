using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XTSPrimeMoverProject.Models;

namespace XTSPrimeMoverProject.Services.RemoteTwinCat
{
	/// <summary>
	/// HTTP-based machine gateway that polls a remote runtime and exposes it through the machine gateway contract.
	/// </summary>
	public sealed class RemoteTwinCatHttpGateway : IMachineGatewayService, IDisposable
	{
		private static readonly TimeSpan MinimumTimeout = TimeSpan.FromSeconds(1);
		private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

		private readonly HttpClient _httpClient;
		private readonly JsonSerializerOptions _jsonOptions;
		private readonly ErrorHandlingService _errorHandler = ErrorHandlingService.Instance;
		private readonly object _stateLock = new();
		private readonly Timer _pollTimer;
		private int _pollActive;

		private List<Mover> _movers = new();
		private List<Machine> _machines = new();
		private List<Robot> _robots = new();
		private int _totalPartsProduced;
		private int _goodPartsCount;
		private int _badPartsCount;
		private int _primeMoverEnteredCount;
		private int _primeMoverExitedCount;
		private bool _isRunning;
		private bool _entryZoneBlink;
		private bool _exitZoneBlink;
		private GatewaySessionStatus _sessionStatus;

		public RemoteTwinCatHttpGateway(string baseAddress, int timeoutMs)
		{
			if (string.IsNullOrWhiteSpace(baseAddress))
			{
				throw new ArgumentException("Remote TwinCAT gateway base address is required.", nameof(baseAddress));
			}

			_jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				PropertyNameCaseInsensitive = true
			};
			_httpClient = new HttpClient
			{
				BaseAddress = CreateBaseUri(baseAddress),
				Timeout = TimeSpan.FromMilliseconds(Math.Max((int)MinimumTimeout.TotalMilliseconds, timeoutMs))
			};
			_sessionStatus = CreateSessionStatus(
				GatewayConnectionState.Reconnecting,
				"Reconnecting",
				$"Attempting connection to remote TwinCAT endpoint {_httpClient.BaseAddress}.");
			_pollTimer = new Timer(PollSnapshotTimerCallback, null, TimeSpan.Zero, DefaultPollInterval);
		}

		public event EventHandler? StateChanged;
		public event EventHandler<string>? LogGenerated;
		public event EventHandler<GatewaySessionStatus>? SessionStatusChanged;

		public IReadOnlyList<Mover> Movers
		{
			get
			{
				lock (_stateLock)
				{
					return _movers.AsReadOnly();
				}
			}
		}

		public IReadOnlyList<Machine> Machines
		{
			get
			{
				lock (_stateLock)
				{
					return _machines.AsReadOnly();
				}
			}
		}

		public IReadOnlyList<Robot> Robots
		{
			get
			{
				lock (_stateLock)
				{
					return _robots.AsReadOnly();
				}
			}
		}

		public int TotalPartsProduced
		{
			get { lock (_stateLock) { return _totalPartsProduced; } }
		}

		public int GoodPartsCount
		{
			get { lock (_stateLock) { return _goodPartsCount; } }
		}

		public int BadPartsCount
		{
			get { lock (_stateLock) { return _badPartsCount; } }
		}

		public int PrimeMoverEnteredCount
		{
			get { lock (_stateLock) { return _primeMoverEnteredCount; } }
		}

		public int PrimeMoverExitedCount
		{
			get { lock (_stateLock) { return _primeMoverExitedCount; } }
		}

		public bool IsRunning
		{
			get { lock (_stateLock) { return _isRunning; } }
		}

		public bool EntryZoneBlink
		{
			get { lock (_stateLock) { return _entryZoneBlink; } }
		}

		public bool ExitZoneBlink
		{
			get { lock (_stateLock) { return _exitZoneBlink; } }
		}

		public GatewaySessionStatus SessionStatus => _sessionStatus;

		/// <summary>
		/// Dispatches a remote start command.
		/// </summary>
		public void Start()
		{
			_ = SendCommandAsync("api/machine/start", payload: null, successLogMessage: "Remote start command sent.");
		}

		/// <summary>
		/// Dispatches a remote stop command.
		/// </summary>
		public void Stop()
		{
			_ = SendCommandAsync("api/machine/stop", payload: null, successLogMessage: "Remote stop command sent.");
		}

		/// <summary>
		/// Dispatches a remote reset command.
		/// </summary>
		public void Reset()
		{
			_ = SendCommandAsync("api/machine/reset", payload: null, successLogMessage: "Remote reset command sent.");
		}

		/// <summary>
		/// Dispatches a remote speed update command.
		/// </summary>
		public void SetSimulationSpeed(double speed)
		{
			_ = SendCommandAsync("api/machine/speed", new { speed }, $"Remote simulation speed set to {speed:F1}x.");
		}

		/// <summary>
		/// Fetches watchdog status from the remote runtime.
		/// </summary>
		public async Task<IReadOnlyList<WatchdogStatusEntry>> GetWatchdogStatusAsync(CancellationToken cancellationToken = default)
		{
			var result = await GetFromJsonAsync<List<WatchdogStatusEntry>>("api/machine/watchdogs", cancellationToken).ConfigureAwait(false);
			return result ?? new List<WatchdogStatusEntry>();
		}

		/// <summary>
		/// Fetches orchestration steps from the remote runtime.
		/// </summary>
		public async Task<IReadOnlyList<ProductionSequenceStep>> GetOrchestrationStepsAsync(CancellationToken cancellationToken = default)
		{
			var result = await GetFromJsonAsync<List<ProductionSequenceStep>>("api/machine/orchestration", cancellationToken).ConfigureAwait(false);
			return result ?? new List<ProductionSequenceStep>();
		}

		/// <summary>
		/// Sends orchestration updates to the remote runtime.
		/// </summary>
		public async Task<OrchestrationApplyResult> ApplyOrchestrationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, CancellationToken cancellationToken = default)
		{
			PublishSessionStatus(GatewayConnectionState.Reconnecting, "Reconnecting", "Submitting orchestration update to remote TwinCAT endpoint.");

			try
			{
				using var response = await _httpClient.PostAsJsonAsync("api/machine/orchestration", stepDefinitions, _jsonOptions, cancellationToken).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					string message = $"Remote orchestration apply failed with HTTP {(int)response.StatusCode}.";
					PublishSessionStatus(GatewayConnectionState.Offline, "Offline", message);
					return new OrchestrationApplyResult(false, message);
				}

				var result = await response.Content.ReadFromJsonAsync<OrchestrationApplyResult>(_jsonOptions, cancellationToken).ConfigureAwait(false)
					?? new OrchestrationApplyResult(true, "Remote orchestration apply accepted.");

				PublishSessionStatus(
					result.Success ? GatewayConnectionState.Connected : GatewayConnectionState.Degraded,
					result.Success ? "Connected" : "Degraded",
					result.Message);

				await PollSnapshotAsync(cancellationToken).ConfigureAwait(false);
				return result;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				string message = $"Remote orchestration apply failed: {ex.Message}";
				PublishSessionStatus(GatewayConnectionState.Offline, "Offline", message);
				_errorHandler.ReportException(ErrorCategory.Gateway, "RemoteHttpGateway.ApplyOrchestrationAsync", ex);
				return new OrchestrationApplyResult(false, message);
			}
		}

		/// <summary>
		/// Requests orchestration validation from the remote runtime.
		/// </summary>
		public async Task<IReadOnlyList<string>> PreviewOrchestrationValidationAsync(IReadOnlyList<OrchestrationStepDefinition> stepDefinitions, CancellationToken cancellationToken = default)
		{
			try
			{
				using var response = await _httpClient.PostAsJsonAsync("api/machine/orchestration/validate", stepDefinitions, _jsonOptions, cancellationToken).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode)
				{
					string message = $"Remote orchestration validation failed with HTTP {(int)response.StatusCode}.";
					PublishSessionStatus(GatewayConnectionState.Degraded, "Degraded", message);
					return new List<string> { message };
				}

				var result = await response.Content.ReadFromJsonAsync<List<string>>(_jsonOptions, cancellationToken).ConfigureAwait(false)
					?? new List<string>();
				PublishSessionStatus(result.Count == 0 ? GatewayConnectionState.Connected : GatewayConnectionState.Degraded, result.Count == 0 ? "Connected" : "Degraded", result.Count == 0 ? BuildConnectedDetail() : "Remote validation returned rule violations.");
				return result;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				string message = $"Remote orchestration validation failed: {ex.Message}";
				PublishSessionStatus(GatewayConnectionState.Offline, "Offline", message);
				_errorHandler.ReportException(ErrorCategory.Gateway, "RemoteHttpGateway.PreviewOrchestrationValidationAsync", ex);
				return new List<string> { message };
			}
		}

		/// <summary>
		/// Fetches orchestration safety gate status from the remote runtime.
		/// </summary>
		public async Task<IReadOnlyList<SafetyGateStatus>> GetOrchestrationSafetyGateStatusesAsync(CancellationToken cancellationToken = default)
		{
			var result = await GetFromJsonAsync<List<SafetyGateStatus>>("api/machine/orchestration/safety-gates", cancellationToken).ConfigureAwait(false);
			return result ?? new List<SafetyGateStatus>();
		}

		public void Dispose()
		{
			_pollTimer.Dispose();
			_httpClient.Dispose();
		}

		private async void PollSnapshotTimerCallback(object? state)
		{
			if (Interlocked.Exchange(ref _pollActive, 1) == 1)
			{
				return;
			}

			try
			{
				await PollSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
			}
			finally
			{
				Interlocked.Exchange(ref _pollActive, 0);
			}
		}

		private async Task PollSnapshotAsync(CancellationToken cancellationToken)
		{
			try
			{
				var snapshot = await GetFromJsonAsync<RemoteMachineSnapshotDto>("api/machine/snapshot", cancellationToken).ConfigureAwait(false);
				if (snapshot == null)
				{
					PublishSessionStatus(GatewayConnectionState.Offline, "Offline", "Remote TwinCAT snapshot endpoint returned no content.");
					return;
				}

				lock (_stateLock)
				{
					_movers = MapMovers(snapshot.Movers);
					_machines = MapMachines(snapshot.Machines);
					_robots = MapRobots(snapshot.Robots);
					_totalPartsProduced = snapshot.TotalPartsProduced;
					_goodPartsCount = snapshot.GoodPartsCount;
					_badPartsCount = snapshot.BadPartsCount;
					_primeMoverEnteredCount = snapshot.PrimeMoverEnteredCount;
					_primeMoverExitedCount = snapshot.PrimeMoverExitedCount;
					_isRunning = snapshot.IsRunning;
					_entryZoneBlink = snapshot.EntryZoneBlink;
					_exitZoneBlink = snapshot.ExitZoneBlink;
				}

				PublishSessionStatus(GatewayConnectionState.Connected, "Connected", BuildConnectedDetail());
				StateChanged?.Invoke(this, EventArgs.Empty);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				string message = $"Remote TwinCAT snapshot poll failed: {ex.Message}";
				PublishSessionStatus(GatewayConnectionState.Offline, "Offline", message);
				_errorHandler.ReportException(ErrorCategory.Gateway, "RemoteHttpGateway.PollSnapshotAsync", ex, wasRecovered: true);
			}
		}

		private async Task<T?> GetFromJsonAsync<T>(string relativePath, CancellationToken cancellationToken)
		{
			try
			{
				T? result = await _httpClient.GetFromJsonAsync<T>(relativePath, _jsonOptions, cancellationToken).ConfigureAwait(false);
				PublishSessionStatus(GatewayConnectionState.Connected, "Connected", BuildConnectedDetail());
				return result;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				PublishSessionStatus(GatewayConnectionState.Offline, "Offline", $"Remote request to '{relativePath}' failed: {ex.Message}");
				_errorHandler.ReportException(ErrorCategory.Gateway, $"RemoteHttpGateway.{relativePath}", ex);
				return default;
			}
		}

		private async Task SendCommandAsync(string relativePath, object? payload, string successLogMessage)
		{
			PublishSessionStatus(GatewayConnectionState.Reconnecting, "Reconnecting", $"Sending remote command '{relativePath}'.");

			try
			{
				using HttpResponseMessage response = payload == null
					? await _httpClient.PostAsync(relativePath, content: null).ConfigureAwait(false)
					: await _httpClient.PostAsJsonAsync(relativePath, payload, _jsonOptions).ConfigureAwait(false);

				if (!response.IsSuccessStatusCode)
				{
					PublishSessionStatus(GatewayConnectionState.Offline, "Offline", $"Remote command '{relativePath}' failed with HTTP {(int)response.StatusCode}.");
					return;
				}

				PublishSessionStatus(GatewayConnectionState.Connected, "Connected", BuildConnectedDetail());
				LogGenerated?.Invoke(this, successLogMessage);
				await PollSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				PublishSessionStatus(GatewayConnectionState.Offline, "Offline", $"Remote command '{relativePath}' failed: {ex.Message}");
				_errorHandler.ReportException(ErrorCategory.Gateway, $"RemoteHttpGateway.{relativePath}", ex);
			}
		}

		private GatewaySessionStatus CreateSessionStatus(GatewayConnectionState state, string summary, string detail)
		{
			return new GatewaySessionStatus(state, summary, detail, DateTime.UtcNow, IsRemote: true);
		}

		private void PublishSessionStatus(GatewayConnectionState state, string summary, string detail)
		{
			GatewaySessionStatus next = CreateSessionStatus(state, summary, detail);
			if (_sessionStatus == next)
			{
				return;
			}

			_sessionStatus = next;
			SessionStatusChanged?.Invoke(this, next);
		}

		private string BuildConnectedDetail()
		{
			return $"Remote TwinCAT HTTP gateway connected to {_httpClient.BaseAddress}.";
		}

		private static Uri CreateBaseUri(string baseAddress)
		{
			string normalized = baseAddress.EndsWith("/", StringComparison.Ordinal) ? baseAddress : baseAddress + "/";
			return new Uri(normalized, UriKind.Absolute);
		}

		private static List<Mover> MapMovers(List<RemoteMoverDto>? movers)
		{
			var result = new List<Mover>();
			if (movers == null)
			{
				return result;
			}

			foreach (var dto in movers)
			{
				var mover = new Mover(dto.MoverId)
				{
					Position = dto.Position,
					Velocity = dto.Velocity,
					State = dto.State,
					CurrentPart = MapPart(dto.CurrentPart),
					TargetStation = dto.TargetStation
				};
				result.Add(mover);
			}

			return result;
		}

		private static List<Machine> MapMachines(List<RemoteMachineDto>? machines)
		{
			var result = new List<Machine>();
			if (machines == null)
			{
				return result;
			}

			foreach (var dto in machines)
			{
				var machine = new Machine(dto.MachineId, dto.Name ?? string.Empty, dto.Type, dto.LoadAngle)
				{
					CurrentStationIndex = dto.CurrentStationIndex,
					IsOperational = dto.IsOperational,
					PartsEnteredCount = dto.PartsEnteredCount,
					PartsExitedCount = dto.PartsExitedCount,
					SequencerState = dto.SequencerState,
					IsIndexing = dto.IsIndexing,
					FaultActive = dto.FaultActive,
					FaultMessage = dto.FaultMessage ?? string.Empty,
					RotaryAngle = dto.RotaryAngle,
					Stations = MapStations(dto.Stations)
				};
				result.Add(machine);
			}

			return result;
		}

		private static List<Station> MapStations(List<RemoteStationDto>? stations)
		{
			var result = new List<Station>();
			if (stations == null)
			{
				return result;
			}

			foreach (var dto in stations)
			{
				var station = new Station(dto.StationId, dto.Name ?? string.Empty, dto.Type, dto.ProcessTime, dto.DefectRate)
				{
					Status = dto.Status,
					CurrentPart = MapPart(dto.CurrentPart),
					ElapsedTime = dto.ElapsedTime
				};
				result.Add(station);
			}

			return result;
		}

		private static List<Robot> MapRobots(List<RemoteRobotDto>? robots)
		{
			var result = new List<Robot>();
			if (robots == null)
			{
				return result;
			}

			foreach (var dto in robots)
			{
				var robot = new Robot(dto.RobotId, dto.AssignedMachineId)
				{
					Name = dto.Name ?? string.Empty,
					State = dto.State,
					HeldPart = MapPart(dto.HeldPart),
					ActionProgress = dto.ActionProgress,
					ActionTime = dto.ActionTime
				};
				result.Add(robot);
			}

			return result;
		}

		private static Part? MapPart(RemotePartDto? dto)
		{
			if (dto == null)
			{
				return null;
			}

			return new Part
			{
				PartId = dto.PartId,
				TrackingNumber = dto.TrackingNumber ?? string.Empty,
				Status = dto.Status,
				CreatedAt = dto.CreatedAt,
				EnteredPrimeMoverAt = dto.EnteredPrimeMoverAt,
				ExitedPrimeMoverAt = dto.ExitedPrimeMoverAt,
				ProcessStep = dto.ProcessStep,
				ProcessHistory = dto.ProcessHistory ?? Array.Empty<string>(),
				HasDefect = dto.HasDefect,
				NextMachineIndex = dto.NextMachineIndex,
				CompletedStations = dto.CompletedStations,
				CurrentLocation = dto.CurrentLocation ?? string.Empty
			};
		}

		private sealed class RemoteMachineSnapshotDto
		{
			public List<RemoteMoverDto>? Movers { get; set; }
			public List<RemoteMachineDto>? Machines { get; set; }
			public List<RemoteRobotDto>? Robots { get; set; }
			public int TotalPartsProduced { get; set; }
			public int GoodPartsCount { get; set; }
			public int BadPartsCount { get; set; }
			public int PrimeMoverEnteredCount { get; set; }
			public int PrimeMoverExitedCount { get; set; }
			public bool IsRunning { get; set; }
			public bool EntryZoneBlink { get; set; }
			public bool ExitZoneBlink { get; set; }
		}

		private sealed class RemoteMoverDto
		{
			public int MoverId { get; set; }
			public double Position { get; set; }
			public double Velocity { get; set; }
			public MoverState State { get; set; }
			public RemotePartDto? CurrentPart { get; set; }
			public int TargetStation { get; set; }
		}

		private sealed class RemoteMachineDto
		{
			public int MachineId { get; set; }
			public string? Name { get; set; }
			public MachineType Type { get; set; }
			public List<RemoteStationDto>? Stations { get; set; }
			public int CurrentStationIndex { get; set; }
			public double LoadAngle { get; set; }
			public bool IsOperational { get; set; }
			public int PartsEnteredCount { get; set; }
			public int PartsExitedCount { get; set; }
			public PlcSequencerState SequencerState { get; set; }
			public bool IsIndexing { get; set; }
			public bool FaultActive { get; set; }
			public string? FaultMessage { get; set; }
			public double RotaryAngle { get; set; }
		}

		private sealed class RemoteStationDto
		{
			public int StationId { get; set; }
			public string? Name { get; set; }
			public StationType Type { get; set; }
			public StationStatus Status { get; set; }
			public RemotePartDto? CurrentPart { get; set; }
			public double ProcessTime { get; set; }
			public double ElapsedTime { get; set; }
			public double DefectRate { get; set; }
		}

		private sealed class RemoteRobotDto
		{
			public int RobotId { get; set; }
			public string? Name { get; set; }
			public RobotState State { get; set; }
			public RemotePartDto? HeldPart { get; set; }
			public int AssignedMachineId { get; set; }
			public double ActionProgress { get; set; }
			public double ActionTime { get; set; }
		}

		private sealed class RemotePartDto
		{
			public Guid PartId { get; set; }
			public string? TrackingNumber { get; set; }
			public PartStatus Status { get; set; }
			public DateTime CreatedAt { get; set; }
			public DateTime? EnteredPrimeMoverAt { get; set; }
			public DateTime? ExitedPrimeMoverAt { get; set; }
			public int ProcessStep { get; set; }
			public string[]? ProcessHistory { get; set; }
			public bool HasDefect { get; set; }
			public int NextMachineIndex { get; set; }
			public int CompletedStations { get; set; }
			public string? CurrentLocation { get; set; }
		}
	}
}
