using Microsoft.AspNetCore.SignalR;

namespace MesApi.Hubs;

/// <summary>Real-time MES dashboard updates via SignalR.</summary>
public class MesHub : Hub
{
    public async Task JoinDashboard() => await Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");
    public async Task LeaveDashboard() => await Groups.RemoveFromGroupAsync(Context.ConnectionId, "dashboard");
}

/// <summary>Pushes periodic MES KPI snapshots to connected HMI clients.</summary>
public class MesDashboardBroadcaster : BackgroundService
{
    private readonly IHubContext<MesHub> _hub;

    public MesDashboardBroadcaster(IHubContext<MesHub> hub) => _hub = hub;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _hub.Clients.Group("dashboard")
                .SendAsync("DashboardPing", new { timestamp = DateTime.UtcNow }, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
