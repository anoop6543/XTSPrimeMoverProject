using Microsoft.AspNetCore.Mvc;
using PrimeMoverApi.Services;
using Temporalio.Client;
using XtsContracts.Dtos;
using XtsContracts.Workflows;

namespace PrimeMoverApi.Controllers;

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly IHttpClientFactory _factory;
    private readonly ITemporalClient _temporal;
    private readonly MachineAggregatorService _aggregator;

    public SystemController(IHttpClientFactory factory, ITemporalClient temporal, MachineAggregatorService aggregator)
    {
        _factory = factory;
        _temporal = temporal;
        _aggregator = aggregator;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var pmClient = _factory.CreateClient("PrimeMoverService");
        var trackStatus = await pmClient.GetFromJsonAsync<object>("/api/track/status");
        var machines = await _aggregator.GetAllMachineStatusesAsync();
        return Ok(new { Track = trackStatus, Machines = machines, Timestamp = DateTime.UtcNow });
    }

    [HttpGet("status/live")]
    public async Task<IActionResult> GetLiveStatus()
    {
        var handle = _temporal.GetWorkflowHandle<IXTSPrimeMoverWorkflow>("xts-prime-mover-main");
        var status = await handle.QueryAsync(w => w.GetSystemStatus());
        return Ok(status);
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start()
    {
        var pmClient = _factory.CreateClient("PrimeMoverService");
        await pmClient.PostAsync("/api/track/start", null);

        var handle = _temporal.GetWorkflowHandle<IXTSPrimeMoverWorkflow>("xts-prime-mover-main");
        await handle.SignalAsync(w => w.SignalStartProductionAsync());
        return Ok(new { success = true });
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop()
    {
        var pmClient = _factory.CreateClient("PrimeMoverService");
        await pmClient.PostAsync("/api/track/stop", null);

        var handle = _temporal.GetWorkflowHandle<IXTSPrimeMoverWorkflow>("xts-prime-mover-main");
        await handle.SignalAsync(w => w.SignalStopProductionAsync());
        return Ok(new { success = true });
    }

    [HttpPost("set-speed")]
    public async Task<IActionResult> SetSpeed([FromQuery] double factor)
    {
        var handle = _temporal.GetWorkflowHandle<IXTSPrimeMoverWorkflow>("xts-prime-mover-main");
        await handle.SignalAsync(w => w.SignalSetSpeedAsync(factor));
        return Ok(new { success = true });
    }
}

/// <summary>
/// Proxy controller that forwards track-engine tick calls from the Temporal master-worker
/// to PrimeMoverService running in the same pod. The master-worker only holds the
/// PrimeMoverApi base URL, so tick requests arrive here and are forwarded over the
/// pod-local loopback to the track engine.
/// </summary>
[ApiController]
[Route("api/track")]
public class TrackProxyController : ControllerBase
{
    private readonly IHttpClientFactory _factory;

    public TrackProxyController(IHttpClientFactory factory) => _factory = factory;

    /// <summary>Advance the XTS track engine by one simulation step.</summary>
    [HttpPost("tick")]
    public async Task<IActionResult> Tick([FromQuery] double speedFactor = 1.0)
    {
        var client = _factory.CreateClient("PrimeMoverService");
        var response = await client.PostAsync($"/api/track/tick?speedFactor={speedFactor:F4}", null);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    /// <summary>Get raw track status (complements /api/system/status).</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var client = _factory.CreateClient("PrimeMoverService");
        var response = await client.GetAsync("/api/track/status");
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }
}
