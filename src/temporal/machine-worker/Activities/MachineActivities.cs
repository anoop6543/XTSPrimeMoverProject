using Temporalio.Activities;
using System.Net.Http.Json;

namespace MachineWorker.Activities;

public record StationSequenceResult(bool HasDefect, string FinalStatus, IReadOnlyList<string> StationLog);

public class MachineActivities
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MachineActivities> _logger;

    public MachineActivities(IHttpClientFactory httpFactory, ILogger<MachineActivities> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    [Activity]
    public async Task<StationSequenceResult> ExecuteStationSequenceAsync(
        int machineId, string partId, string trackingNumber, bool initialHasDefect)
    {
        var client = _httpFactory.CreateClient("MachineService");

        // Load part into machine
        var loadResp = await client.PostAsJsonAsync($"/api/machine/{machineId}/load", new
        {
            PartId = partId,
            TrackingNumber = trackingNumber,
            HasDefect = initialHasDefect
        });
        loadResp.EnsureSuccessStatusCode();

        // Poll until machine completes (station sequence driven by machine service)
        var timeout = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < timeout)
        {
            var status = await client.GetFromJsonAsync<MachineCompletionStatus>(
                $"/api/machine/{machineId}/completion-status");

            if (status?.IsComplete == true)
            {
                _logger.LogInformation("Machine {Id} completed part {Tracking}", machineId, trackingNumber);
                return new StationSequenceResult(status.HasDefect, status.FinalPartStatus, status.StationLog);
            }

            if (status?.IsFaulted == true)
            {
                throw new ApplicationException($"Machine {machineId} faulted: {status.FaultMessage}");
            }

            // Heartbeat so Temporal knows we're still alive (prevents timeout)
            ActivityExecutionContext.Current.Heartbeat($"machine-{machineId} polling for part {trackingNumber}");
            await Task.Delay(500);
        }

        throw new TimeoutException($"Machine {machineId} did not complete part {trackingNumber} within timeout");
    }
}

public record MachineCompletionStatus(bool IsComplete, bool IsFaulted, string FaultMessage, bool HasDefect, string FinalPartStatus, IReadOnlyList<string> StationLog);
