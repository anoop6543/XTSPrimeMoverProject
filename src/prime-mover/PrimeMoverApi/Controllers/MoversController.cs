using Microsoft.AspNetCore.Mvc;
using Temporalio.Client;
using XtsContracts.Workflows;

namespace PrimeMoverApi.Controllers;

[ApiController]
[Route("api/movers")]
public class MoversController : ControllerBase
{
    private readonly ITemporalClient _temporal;
    private readonly IHttpClientFactory _factory;

    public MoversController(ITemporalClient temporal, IHttpClientFactory factory)
    {
        _temporal = temporal;
        _factory = factory;
    }

    [HttpGet]
    public async Task<IActionResult> GetMovers()
    {
        var handle = _temporal.GetWorkflowHandle<IXTSPrimeMoverWorkflow>("xts-prime-mover-main");
        var movers = await handle.QueryAsync(w => w.GetMoverPositions());
        return Ok(movers);
    }

    [HttpPost("assign-pickup")]
    public async Task<IActionResult> AssignPickup([FromQuery] int machineId, [FromQuery] string tracking)
    {
        // Signal prime mover to dispatch a mover for pickup
        var handle = _temporal.GetWorkflowHandle<IXTSPrimeMoverWorkflow>("xts-prime-mover-main");
        await handle.SignalAsync(w => w.SignalMachinePartReadyAsync(
            new PartReadyFromMachineSignal(machineId, tracking, false, "Processed")));
        return Ok(new { dispatched = true });
    }
}
