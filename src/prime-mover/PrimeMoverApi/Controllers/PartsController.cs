using Microsoft.AspNetCore.Mvc;
using Temporalio.Client;
using XtsContracts.Workflows;

namespace PrimeMoverApi.Controllers;

[ApiController]
[Route("api/parts")]
public class PartsController : ControllerBase
{
    private readonly ITemporalClient _temporal;

    public PartsController(ITemporalClient temporal) => _temporal = temporal;

    [HttpGet("{trackingNumber}")]
    public async Task<IActionResult> GetPart(string trackingNumber)
    {
        try
        {
            var handle = _temporal.GetWorkflowHandle<IPartLifecycleWorkflow>($"part-{trackingNumber}");
            var status = await handle.QueryAsync(w => w.GetCurrentStatus());
            var history = await handle.QueryAsync(w => w.GetStationHistory());
            return Ok(new { Part = status, StationHistory = history });
        }
        catch
        {
            return NotFound(new { message = $"Part {trackingNumber} not found in active workflows" });
        }
    }
}
