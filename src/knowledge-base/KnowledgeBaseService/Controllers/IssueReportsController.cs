using Microsoft.AspNetCore.Mvc;
using KnowledgeBaseService.Services;

namespace KnowledgeBaseService.Controllers;

[ApiController]
[Route("api/issues")]
public class IssueReportsController : ControllerBase
{
    private readonly IssueReportingService _service;

    public IssueReportsController(IssueReportingService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] int? machineId) =>
        Ok(await _service.GetAllAsync(status, machineId));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _service.GetByIdAsync(id);
        return r == null ? NotFound() : Ok(_service.ToDto(r));
    }

    /// <summary>
    /// One-touch report from machine terminal. Auto-context injected from payload.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitIssueReportRequest req)
    {
        var report = await _service.SubmitReportAsync(req);
        return CreatedAtAction(nameof(GetById), new { id = report.Id }, _service.ToDto(report));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIssueReportRequest req)
    {
        await _service.UpdateStatusAsync(id, req.Status, req.AssignedTo, req.ResolutionNotes);
        return NoContent();
    }

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveIssueRequest req)
    {
        await _service.UpdateStatusAsync(id, "Resolved", req.ResolvedBy, req.ResolutionNotes);
        return Ok(new { message = "Issue resolved." });
    }
}

public record UpdateIssueReportRequest(string Status, string? AssignedTo, string? ResolutionNotes);
public record ResolveIssueRequest(string ResolvedBy, string ResolutionNotes);
