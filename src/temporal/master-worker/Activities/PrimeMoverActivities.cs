using Temporalio.Activities;
using XtsContracts.Dtos;
using System.Net.Http.Json;

namespace MasterWorker.Activities;

public record TrackTickResult(
    bool NewPartEntered,
    Guid? NewPartId,
    string? NewPartTrackingNumber,
    IReadOnlyList<int>? MachineRouteIds,
    bool PartExited,
    bool ExitedPartGood,
    IReadOnlyList<MoverDto>? UpdatedMovers
);

public class PrimeMoverActivities
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PrimeMoverActivities> _logger;

    public PrimeMoverActivities(IHttpClientFactory httpFactory, ILogger<PrimeMoverActivities> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [Activity]
    public async Task<TrackTickResult> TickTrackEngineAsync(double speedFactor)
    {
        var client = _httpFactory.CreateClient("PrimeMoverApi");
        var response = await client.PostAsync($"/api/track/tick?speedFactor={speedFactor}", null);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApplicationException($"Track tick failed: {response.StatusCode}");
        }
        var result = await response.Content.ReadFromJsonAsync<TrackTickResult>()
            ?? throw new InvalidOperationException("Null tick result");
        return result;
    }

    [Activity]
    public async Task AssignMoverForPickupAsync(int machineId, string partTrackingNumber)
    {
        var client = _httpFactory.CreateClient("PrimeMoverApi");
        var response = await client.PostAsync($"/api/movers/assign-pickup?machineId={machineId}&tracking={partTrackingNumber}", null);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Mover assigned for pickup from Machine {MachineId}, Part {Tracking}", machineId, partTrackingNumber);
    }

    [Activity]
    public async Task RobotPickFromMoverAsync(int robotId, int moverId, string partTracking)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        _logger.LogInformation("Robot {Robot} picked from Mover {Mover}, Part={Part}", robotId, moverId, partTracking);
    }

    [Activity]
    public async Task RobotMoveToMachineAsync(int robotId, int machineId)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        _logger.LogInformation("Robot {Robot} moving to Machine {Machine}", robotId, machineId);
    }

    [Activity]
    public async Task RobotPlaceInMachineAsync(int robotId, int machineId, string partTracking)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        _logger.LogInformation("Robot {Robot} placed Part {Part} in Machine {Machine}", robotId, partTracking, machineId);
    }

    [Activity]
    public async Task RobotPickFromMachineAsync(int robotId, int machineId, string partTracking)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        _logger.LogInformation("Robot {Robot} picked Part {Part} from Machine {Machine}", robotId, partTracking, machineId);
    }

    [Activity]
    public async Task RobotMoveToMoverAsync(int robotId, int moverId)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(600));
        _logger.LogInformation("Robot {Robot} moving to Mover {Mover}", robotId, moverId);
    }

    [Activity]
    public async Task RobotPlaceOnMoverAsync(int robotId, int moverId, string partTracking)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        _logger.LogInformation("Robot {Robot} placed Part {Part} on Mover {Mover}", robotId, partTracking, moverId);
    }
}
