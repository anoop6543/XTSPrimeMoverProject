using Microsoft.AspNetCore.Mvc;
using MesService.Services;
using System.Text.Json;

namespace MesApi.Controllers;

[ApiController]
[Route("api/quality")]
public class QualityController : ControllerBase
{
    private readonly QualityService _service;

    public QualityController(QualityService service) => _service = service;

    [HttpGet("ncr")]
    public async Task<IActionResult> GetNcrs([FromQuery] string? status) =>
        Ok(await _service.GetAllAsync(status));

    [HttpGet("ncr/{id:guid}")]
    public async Task<IActionResult> GetNcr(Guid id)
    {
        var r = await _service.GetByIdAsync(id);
        return r == null ? NotFound() : Ok(_service.ToDto(r));
    }

    [HttpPost("ncr")]
    public async Task<IActionResult> CreateNcr([FromBody] CreateNcrRequest req)
    {
        var ncr = await _service.CreateAsync(
            req.PartTrackingNumber, req.MachineId,
            req.DefectCategory, req.Decision, req.WorkOrderId);
        return CreatedAtAction(nameof(GetNcr), new { id = ncr.Id },
            new { id = ncr.Id, ncrNumber = ncr.NcrNumber });
    }

    [HttpPatch("ncr/{id:guid}")]
    public async Task<IActionResult> UpdateNcr(Guid id, [FromBody] UpdateNcrRequest req)
    {
        await _service.UpdateAsync(id, req.AssignedTo, req.RootCause, req.CorrectiveAction);
        return NoContent();
    }

    [HttpPost("ncr/{id:guid}/close")]
    public async Task<IActionResult> CloseNcr(Guid id)
    {
        await _service.CloseAsync(id);
        return Ok(new { message = "NCR closed." });
    }

    [HttpGet("defect-summary")]
    public async Task<IActionResult> DefectSummary([FromQuery] int days = 7) =>
        Ok(await _service.GetDefectSummaryAsync(DateTime.UtcNow.AddDays(-days)));

    [HttpPost("decisions")]
    public async Task<IActionResult> RecordDecision([FromBody] JsonElement body)
    {
        // Passthrough endpoint for orchestration-worker activity
        return Ok(new { recorded = true });
    }
}

public record CreateNcrRequest(
    string PartTrackingNumber, int MachineId, string DefectCategory,
    string Decision, Guid? WorkOrderId);

public record UpdateNcrRequest(string? AssignedTo, string? RootCause, string? CorrectiveAction);
