using Microsoft.AspNetCore.SignalR;
using PrimeMoverApi.Services;
using System.Text.Json;
using XtsContracts.Dtos;

namespace PrimeMoverApi.Hubs;

public class SystemHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "system");
        await base.OnConnectedAsync();
    }
}

public class SystemStatusBroadcaster : BackgroundService
{
    private readonly IHubContext<SystemHub> _hub;
    private readonly IHttpClientFactory _factory;
    private readonly MachineAggregatorService _aggregator;
    private readonly ILogger<SystemStatusBroadcaster> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public SystemStatusBroadcaster(
        IHubContext<SystemHub> hub,
        IHttpClientFactory factory,
        MachineAggregatorService aggregator,
        ILogger<SystemStatusBroadcaster> logger)
    {
        _hub = hub;
        _factory = factory;
        _aggregator = aggregator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pmClient = _factory.CreateClient("PrimeMoverService");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Fetch track status from PrimeMoverService
                var trackJson = await pmClient.GetStringAsync("/api/track/status", stoppingToken);
                var track = JsonSerializer.Deserialize<TrackStatusPayload>(trackJson, _jsonOptions);

                // Fetch all machine statuses
                var machines = await _aggregator.GetAllMachineStatusesAsync();

                var statusDto = new SystemStatusDto(
                    IsRunning: track?.IsRunning ?? false,
                    TotalPartsProduced: track?.TotalParts ?? 0,
                    GoodPartsCount: track?.GoodParts ?? 0,
                    BadPartsCount: track?.BadParts ?? 0,
                    PrimeMoverEnteredCount: track?.Entered ?? 0,
                    PrimeMoverExitedCount: (track?.GoodParts ?? 0) + (track?.BadParts ?? 0),
                    Movers: Array.Empty<MoverDto>(),
                    Machines: machines,
                    Robots: Array.Empty<RobotDto>(),
                    Timestamp: DateTime.UtcNow);

                await _hub.Clients.Group("system").SendAsync("SystemStatusUpdate", statusDto, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("System broadcast error: {Error}", ex.Message);
            }

            await Task.Delay(100, stoppingToken);
        }
    }

    private sealed class TrackStatusPayload
    {
        public bool IsRunning { get; set; }
        public int TotalParts { get; set; }
        public int GoodParts { get; set; }
        public int BadParts { get; set; }
        public int Entered { get; set; }
    }
}
