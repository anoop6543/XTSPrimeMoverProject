using Microsoft.AspNetCore.SignalR;
using XtsContracts.Dtos;

namespace MachineApi.Hubs;

public class MachineHub : Hub
{
    public async Task JoinMachineGroup(int machineId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"machine-{machineId}");
    }

    public async Task LeaveMachineGroup(int machineId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"machine-{machineId}");
    }
}

/// <summary>
/// Background service that polls the MachineService and broadcasts updates via SignalR.
/// </summary>
public class MachineStatusBroadcaster : BackgroundService
{
    private readonly IHubContext<MachineHub> _hub;
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<MachineStatusBroadcaster> _logger;

    public MachineStatusBroadcaster(IHubContext<MachineHub> hub, IHttpClientFactory factory, ILogger<MachineStatusBroadcaster> logger)
    {
        _hub = hub;
        _factory = factory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = _factory.CreateClient("MachineService");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var machineId = 0; // set from config in real deployment
                var status = await client.GetFromJsonAsync<MachineDto>($"/api/machine/{machineId}/status", stoppingToken);
                if (status != null)
                {
                    await _hub.Clients.Group($"machine-{machineId}").SendAsync("MachineStatusUpdate", status, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Status broadcast error: {Error}", ex.Message);
            }

            await Task.Delay(100, stoppingToken);
        }
    }
}
