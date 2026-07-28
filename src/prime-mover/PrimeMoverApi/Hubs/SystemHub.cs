using Microsoft.AspNetCore.SignalR;
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
    private readonly ILogger<SystemStatusBroadcaster> _logger;

    public SystemStatusBroadcaster(IHubContext<SystemHub> hub, IHttpClientFactory factory, ILogger<SystemStatusBroadcaster> logger)
    {
        _hub = hub;
        _factory = factory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = _factory.CreateClient("PrimeMoverService");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status = await client.GetFromJsonAsync<object>("/api/track/status", stoppingToken);
                await _hub.Clients.Group("system").SendAsync("SystemStatusUpdate", status, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("System broadcast error: {Error}", ex.Message);
            }

            await Task.Delay(100, stoppingToken);
        }
    }
}
