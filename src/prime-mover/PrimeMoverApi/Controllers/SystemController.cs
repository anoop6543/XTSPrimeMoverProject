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
