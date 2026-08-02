using Microsoft.AspNetCore.Mvc;
using MesService.Services;
using MesService.Domain;
using XtsContracts.Dtos;
using Temporalio.Client;
using XtsContracts.Workflows;

namespace MesApi.Controllers;

[ApiController]
[Route("api/work-orders")]
public class WorkOrdersController : ControllerBase
{
    private readonly WorkOrderService _service;
    private readonly ITemporalClient _temporal;
    private const string OrchestrationQueue = "xts-orchestration-queue";

    public WorkOrdersController(WorkOrderService service, ITemporalClient temporal)
    {
        _service = service;
        _temporal = temporal;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status) =>
        Ok(await _service.GetAllAsync(status));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var wo = await _service.GetByIdAsync(id);
        return wo == null ? NotFound() : Ok(_service.ToDto(wo));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkOrderRequest req)
    {
        var wo = await _service.CreateAsync(new WorkOrder
        {
            ProductId = req.ProductId,
            RecipeId = req.RecipeId,
            QuantityOrdered = req.QuantityOrdered,
            Priority = req.Priority,
            DueDate = req.DueDate,
            CreatedBy = req.CreatedBy
        });
        return CreatedAtAction(nameof(GetById), new { id = wo.Id }, _service.ToDto(wo));
    }

    [HttpPost("{id:guid}/release")]
    public async Task<IActionResult> Release(Guid id)
    {
        var wo = await _service.GetByIdAsync(id);
        if (wo == null) return NotFound();
        if (wo.Status != WorkOrderStatus.Pending)
            return BadRequest(new { message = "Work order must be in Pending status to release." });

        // Trigger ProductionOrderWorkflow in Temporal orchestration-worker
        var workflowId = $"production-order-{wo.Id}";
        await _temporal.StartWorkflowAsync(
            (IProductionOrderWorkflow w) => w.RunAsync(new ProductionOrderInput(
                wo.Id, wo.OrderNumber, wo.ProductId, wo.RecipeId,
                wo.QuantityOrdered, wo.Priority, wo.DueDate)),
            new WorkflowOptions
            {
                Id = workflowId,
                TaskQueue = OrchestrationQueue
            });

        await _service.SetTemporalWorkflowIdAsync(id, workflowId);
        await _service.UpdateStatusAsync(id, "Released", DateTime.UtcNow);

        return Ok(new { workflowId, message = "Work order released to production." });
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest req)
    {
        await _service.UpdateStatusAsync(id, req.Status, req.Timestamp ?? DateTime.UtcNow);
        return NoContent();
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id, [FromBody] PauseRequest req)
    {
        var wo = await _service.GetByIdAsync(id);
        if (wo?.TemporalWorkflowId == null) return NotFound();

        var handle = _temporal.GetWorkflowHandle<IProductionOrderWorkflow>(wo.TemporalWorkflowId);
        await handle.SignalAsync(w => w.SignalPauseAsync(req.Reason, req.RequestedBy));
        return Ok(new { message = "Production paused." });
    }

    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, [FromBody] ResumeRequest req)
    {
        var wo = await _service.GetByIdAsync(id);
        if (wo?.TemporalWorkflowId == null) return NotFound();

        var handle = _temporal.GetWorkflowHandle<IProductionOrderWorkflow>(wo.TemporalWorkflowId);
        await handle.SignalAsync(w => w.SignalResumeAsync(req.AuthorizedBy));
        return Ok(new { message = "Production resumed." });
    }

    [HttpGet("{id:guid}/progress")]
    public async Task<IActionResult> GetProgress(Guid id)
    {
        var wo = await _service.GetByIdAsync(id);
        if (wo?.TemporalWorkflowId == null) return NotFound();

        var handle = _temporal.GetWorkflowHandle<IProductionOrderWorkflow>(wo.TemporalWorkflowId);
        var progress = await handle.QueryAsync(w => w.GetProgress());
        return Ok(progress);
    }
}

public record CreateWorkOrderRequest(
    string ProductId, Guid RecipeId, int QuantityOrdered,
    int Priority, DateTime DueDate, string? CreatedBy);

public record UpdateStatusRequest(string Status, DateTime? Timestamp);
public record PauseRequest(string Reason, string RequestedBy);
public record ResumeRequest(string AuthorizedBy);
